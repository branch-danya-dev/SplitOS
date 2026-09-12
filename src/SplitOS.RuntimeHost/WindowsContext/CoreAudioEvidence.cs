using Microsoft.Extensions.Hosting;

namespace SplitOS.RuntimeHost.WindowsContext;

public enum AudioEndpointFlow
{
    Render = 0,
    Capture = 1,
}

public enum AudioEndpointRole
{
    Console = 0,
    Multimedia = 1,
    Communications = 2,
}

[Flags]
public enum AudioEndpointState : uint
{
    None = 0,
    Active = 0x00000001,
    Disabled = 0x00000002,
    NotPresent = 0x00000004,
    Unplugged = 0x00000008,
}

public enum AudioInvalidationKind
{
    DeviceAdded,
    DeviceRemoved,
    DeviceStateChanged,
    DefaultDeviceChanged,
    PropertyChanged,
}

public sealed record AudioEndpointEvidence(
    AudioEndpointFlow Flow,
    AudioEndpointState State,
    string EndpointId,
    string? StableId,
    string? FriendlyName,
    DateTimeOffset ObservedUtc);

public sealed record AudioDefaultEndpointEvidence(
    AudioEndpointFlow Flow,
    AudioEndpointRole Role,
    string? EndpointId,
    string? StableId,
    DateTimeOffset ObservedUtc);

public sealed record AudioObservedState(
    IReadOnlyList<AudioEndpointEvidence> RenderEndpoints,
    IReadOnlyList<AudioEndpointEvidence> CaptureEndpoints,
    IReadOnlyList<AudioDefaultEndpointEvidence> Defaults);

public sealed record AudioSnapshot(
    long Generation,
    DateTimeOffset ObservedUtc,
    IReadOnlyList<AudioEndpointEvidence> RenderEndpoints,
    IReadOnlyList<AudioEndpointEvidence> CaptureEndpoints,
    IReadOnlyList<AudioDefaultEndpointEvidence> Defaults);

public sealed record AudioGenerationChange(
    long Generation,
    AudioInvalidationKind Kind,
    string? EndpointId,
    AudioEndpointFlow? Flow,
    AudioEndpointRole? Role,
    DateTimeOffset ObservedUtc);

public sealed record AudioNotificationChange(
    AudioInvalidationKind Kind,
    string? EndpointId,
    AudioEndpointFlow? Flow,
    AudioEndpointRole? Role,
    DateTimeOffset ObservedUtc);

public interface IAudioGenerationTracker
{
    long CurrentGeneration { get; }
    AudioGenerationChange? LastChange { get; }
    AudioGenerationChange Invalidate(AudioNotificationChange change);
}

/// <summary>
/// Process-local freshness fence for Core Audio evidence. It is never persisted: RuntimeHost must
/// re-observe the live MMDevice graph after each process start.
/// </summary>
public sealed class AudioGenerationTracker : IAudioGenerationTracker
{
    private readonly object _gate = new();
    private long _generation = 1;
    private AudioGenerationChange? _lastChange;

    public long CurrentGeneration
    {
        get
        {
            lock (_gate)
                return _generation;
        }
    }

    public AudioGenerationChange? LastChange
    {
        get
        {
            lock (_gate)
                return _lastChange;
        }
    }

    public AudioGenerationChange Invalidate(AudioNotificationChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        lock (_gate)
        {
            var invalidation = new AudioGenerationChange(
                checked(++_generation),
                change.Kind,
                change.EndpointId,
                change.Flow,
                change.Role,
                change.ObservedUtc);
            _lastChange = invalidation;
            return invalidation;
        }
    }
}

public interface ICoreAudioSnapshotQuery
{
    AudioObservedState Query();
}

public interface IAudioSnapshotReader
{
    AudioSnapshot Read();
}

/// <summary>
/// Publishes a snapshot only when no Core Audio notification changed the generation while the
/// native endpoint/default queries were in flight.
/// </summary>
public sealed class AudioSnapshotReader(
    ICoreAudioSnapshotQuery snapshotQuery,
    IAudioGenerationTracker generationTracker,
    TimeProvider? timeProvider = null) : IAudioSnapshotReader
{
    private const int MaxStableReadAttempts = 4;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public AudioSnapshot Read()
    {
        for (var attempt = 0; attempt < MaxStableReadAttempts; attempt++)
        {
            var before = generationTracker.CurrentGeneration;
            var observed = snapshotQuery.Query();
            var after = generationTracker.CurrentGeneration;

            if (before != after)
                continue;

            ValidateObservedState(observed);
            return new AudioSnapshot(
                after,
                _timeProvider.GetUtcNow(),
                observed.RenderEndpoints.ToArray(),
                observed.CaptureEndpoints.ToArray(),
                observed.Defaults.ToArray());
        }

        throw new InvalidOperationException(
            "Core Audio topology/defaults changed repeatedly while building a snapshot; stale evidence was not published.");
    }

    private static void ValidateObservedState(AudioObservedState observed)
    {
        ArgumentNullException.ThrowIfNull(observed);

        foreach (var endpoint in observed.RenderEndpoints)
        {
            if (endpoint.Flow != AudioEndpointFlow.Render)
                throw new InvalidDataException("Render endpoint collection contains non-render evidence.");
            ValidateEndpoint(endpoint);
        }

        foreach (var endpoint in observed.CaptureEndpoints)
        {
            if (endpoint.Flow != AudioEndpointFlow.Capture)
                throw new InvalidDataException("Capture endpoint collection contains non-capture evidence.");
            ValidateEndpoint(endpoint);
        }

        var expectedDefaults = new HashSet<(AudioEndpointFlow Flow, AudioEndpointRole Role)>(
            from flow in Enum.GetValues<AudioEndpointFlow>()
            from role in Enum.GetValues<AudioEndpointRole>()
            select (flow, role));

        foreach (var defaultEndpoint in observed.Defaults)
        {
            if (!expectedDefaults.Remove((defaultEndpoint.Flow, defaultEndpoint.Role)))
            {
                throw new InvalidDataException(
                    $"Core Audio defaults contain a duplicate or unknown slot: {defaultEndpoint.Flow}/{defaultEndpoint.Role}.");
            }

            if (defaultEndpoint.EndpointId is { Length: 0 })
                throw new InvalidDataException("Core Audio default endpoint ID must be null or non-empty.");
        }

        if (expectedDefaults.Count != 0)
            throw new InvalidDataException("Core Audio snapshot must represent all render/capture role defaults explicitly.");
    }

    private static void ValidateEndpoint(AudioEndpointEvidence endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint.EndpointId))
            throw new InvalidDataException("Core Audio endpoint evidence must carry an opaque endpoint ID.");
        if (endpoint.StableId is { Length: 0 })
            throw new InvalidDataException("Core Audio StableId must be null or non-empty.");
    }
}

public interface ICoreAudioNotificationSession : IDisposable
{
    bool IsStarted { get; }
    void Start(Action<AudioNotificationChange> callback);
    void StopObservation();
}

public interface ICoreAudioNotificationSessionFactory
{
    ICoreAudioNotificationSession Create();
}

/// <summary>
/// Owns exactly one MMDevice notification registration for the RuntimeHost process lifetime.
/// Notifications only invalidate evidence. They never query COM, switch defaults, change volume,
/// route applications or commit an operational mode.
/// </summary>
public sealed class CoreAudioEndpointMonitor(
    ICoreAudioNotificationSessionFactory sessionFactory,
    IAudioGenerationTracker generationTracker) : IHostedService, IDisposable
{
    private readonly object _gate = new();
    private ICoreAudioNotificationSession? _session;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_session is not null)
                return Task.CompletedTask;

            var session = sessionFactory.Create();
            try
            {
                session.Start(change => _ = generationTracker.Invalidate(change));
                _session = session;
            }
            catch
            {
                session.Dispose();
                throw;
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Unregistering the native callback is part of correctness and is not skipped merely because
        // the host shutdown token is already cancelled.
        _ = cancellationToken;

        lock (_gate)
        {
            StopSessionUnderLock();
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            StopSessionUnderLock();
        }
    }

    private void StopSessionUnderLock()
    {
        if (_session is null)
            return;

        var session = _session;
        _session = null;
        try
        {
            session.StopObservation();
        }
        finally
        {
            session.Dispose();
        }
    }
}
