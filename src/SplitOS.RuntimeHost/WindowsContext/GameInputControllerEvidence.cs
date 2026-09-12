using Microsoft.Extensions.Hosting;

namespace SplitOS.RuntimeHost.WindowsContext;

[Flags]
public enum SplitOSInputKinds
{
    None = 0x00000000,
    Controller = 0x0000000E,
    Keyboard = 0x00000010,
    Mouse = 0x00000020,
    ArcadeStick = 0x00010000,
    FlightStick = 0x00020000,
    Gamepad = 0x00040000,
    RacingWheel = 0x00080000,
}

public sealed record GameInputDeviceEvidence(
    string GameInputDeviceId,
    bool Connected,
    SplitOSInputKinds SupportedKinds,
    ushort VendorId,
    ushort ProductId,
    int DeviceFamily,
    int NativeStatus,
    DateTimeOffset ObservedUtc);

public sealed record GameInputDeviceChange(
    GameInputDeviceEvidence? Device,
    int NativeCurrentStatus,
    int NativePreviousStatus,
    ulong NativeTimestamp,
    DateTimeOffset ObservedUtc);

public sealed record InputSnapshot(
    long Generation,
    DateTimeOffset ObservedUtc,
    IReadOnlyList<GameInputDeviceEvidence> Devices);

public interface IInputGenerationTracker
{
    long CurrentGeneration { get; }
}

public interface IInputSnapshotReader
{
    InputSnapshot Read();
}

/// <summary>
/// Process-local GameInput evidence. Device callbacks invalidate the generation and update the
/// observed device projection under one lock, so readers never combine a generation with device
/// rows from a different callback state.
/// </summary>
public sealed class GameInputEvidenceState(
    TimeProvider? timeProvider = null) : IInputGenerationTracker, IInputSnapshotReader
{
    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly Dictionary<string, GameInputDeviceEvidence> _devices =
        new(StringComparer.Ordinal);
    private long _generation = 1;

    public long CurrentGeneration
    {
        get
        {
            lock (_gate)
                return _generation;
        }
    }

    public void Apply(GameInputDeviceChange change)
    {
        lock (_gate)
        {
            _generation = checked(_generation + 1);

            if (change.Device is null)
                return;

            if (string.IsNullOrWhiteSpace(change.Device.GameInputDeviceId))
                throw new InvalidDataException("GameInput device evidence must carry an opaque device ID.");

            _devices[change.Device.GameInputDeviceId] = change.Device;
        }
    }

    public InputSnapshot Read()
    {
        lock (_gate)
        {
            return new InputSnapshot(
                _generation,
                _timeProvider.GetUtcNow(),
                _devices.Values
                    .OrderBy(static device => device.GameInputDeviceId, StringComparer.Ordinal)
                    .ToArray());
        }
    }
}

public interface IGameInputSession : IDisposable
{
    bool IsStarted { get; }
    void Start(Action<GameInputDeviceChange> callback);
    void Stop();
}

public interface IGameInputSessionFactory
{
    IGameInputSession Create();
}

/// <summary>
/// Owns exactly one GameInput session for the RuntimeHost process lifetime. The native callback is
/// evidence-only: it updates GameInputEvidenceState and never performs mode switching, launching,
/// termination, synthetic input, rumble or force-feedback mutation.
/// </summary>
public sealed class GameInputControllerMonitor(
    IGameInputSessionFactory sessionFactory,
    GameInputEvidenceState evidenceState) : IHostedService, IDisposable
{
    private readonly object _gate = new();
    private IGameInputSession? _session;

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
                session.Start(evidenceState.Apply);
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
        _ = cancellationToken;

        lock (_gate)
        {
            if (_session is null)
                return Task.CompletedTask;

            var session = _session;
            _session = null;
            try
            {
                session.Stop();
            }
            finally
            {
                session.Dispose();
            }
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_session is null)
                return;

            var session = _session;
            _session = null;
            try
            {
                session.Stop();
            }
            finally
            {
                session.Dispose();
            }
        }
    }
}
