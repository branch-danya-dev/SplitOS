using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class CoreAudioEvidenceTests
{
    [TestMethod]
    public void GenerationInvalidationIsMonotonicAndRetainsNotificationContext()
    {
        var observed = new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero);
        var tracker = new AudioGenerationTracker();

        var first = tracker.Invalidate(new AudioNotificationChange(
            AudioInvalidationKind.DeviceAdded,
            "opaque-endpoint-A",
            null,
            null,
            observed));
        var second = tracker.Invalidate(new AudioNotificationChange(
            AudioInvalidationKind.DefaultDeviceChanged,
            "opaque-endpoint-B",
            AudioEndpointFlow.Render,
            AudioEndpointRole.Console,
            observed.AddSeconds(1)));

        Assert.AreEqual(2L, first.Generation);
        Assert.AreEqual(3L, second.Generation);
        Assert.AreEqual(AudioInvalidationKind.DefaultDeviceChanged, second.Kind);
        Assert.AreEqual("opaque-endpoint-B", second.EndpointId);
        Assert.AreEqual(AudioEndpointFlow.Render, second.Flow);
        Assert.AreEqual(AudioEndpointRole.Console, second.Role);
        Assert.AreEqual(3L, tracker.CurrentGeneration);
        Assert.AreEqual(second, tracker.LastChange);
    }

    [TestMethod]
    public void SnapshotRetriesWhenNotificationInvalidatesAnInFlightRead()
    {
        var observed = new DateTimeOffset(2026, 9, 13, 0, 5, 0, TimeSpan.Zero);
        var tracker = new AudioGenerationTracker();
        var query = new FakeSnapshotQuery(() =>
        {
            if (queryCallCount++ == 0)
            {
                tracker.Invalidate(new AudioNotificationChange(
                    AudioInvalidationKind.PropertyChanged,
                    "endpoint-render",
                    null,
                    null,
                    observed));
            }

            return CompleteObservedState(observed);
        });
        var reader = new AudioSnapshotReader(query, tracker, new FixedTimeProvider(observed.AddSeconds(2)));
        var queryCallCount = 0;

        var snapshot = reader.Read();

        Assert.AreEqual(2, query.CallCount);
        Assert.AreEqual(2L, snapshot.Generation);
        Assert.AreEqual(observed.AddSeconds(2), snapshot.ObservedUtc);
        Assert.AreEqual(1, snapshot.RenderEndpoints.Count);
        Assert.AreEqual("Stable/Case/Sensitive", snapshot.RenderEndpoints[0].StableId);
        Assert.AreEqual(1, snapshot.CaptureEndpoints.Count);
        Assert.AreEqual(6, snapshot.Defaults.Count);
    }

    [TestMethod]
    public void SnapshotFailsClosedWhenGenerationNeverStabilizes()
    {
        var observed = new DateTimeOffset(2026, 9, 13, 0, 10, 0, TimeSpan.Zero);
        var tracker = new AudioGenerationTracker();
        var query = new FakeSnapshotQuery(() =>
        {
            tracker.Invalidate(new AudioNotificationChange(
                AudioInvalidationKind.DeviceStateChanged,
                "endpoint-render",
                null,
                null,
                observed));
            return CompleteObservedState(observed);
        });
        var reader = new AudioSnapshotReader(query, tracker);

        AssertThrows<InvalidOperationException>(() => reader.Read());
        Assert.AreEqual(4, query.CallCount);
    }

    [TestMethod]
    public void SnapshotRequiresAllSixRoleSpecificDefaultSlots()
    {
        var observed = new DateTimeOffset(2026, 9, 13, 0, 15, 0, TimeSpan.Zero);
        var state = CompleteObservedState(observed);
        var incomplete = state with { Defaults = state.Defaults.Take(5).ToArray() };
        var reader = new AudioSnapshotReader(
            new FakeSnapshotQuery(() => incomplete),
            new AudioGenerationTracker());

        AssertThrows<InvalidDataException>(() => reader.Read());
    }

    [TestMethod]
    public void StableIdPropertyKeyMatchesCurrentWindowsSdkContract()
    {
        Assert.AreEqual(
            new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"),
            CoreAudioPropertyKeys.AudioEndpointStableId.FormatId);
        Assert.AreEqual(12u, CoreAudioPropertyKeys.AudioEndpointStableId.PropertyId);
    }

    [TestMethod]
    public async Task MonitorOwnsOneSessionAndUnregistersEvenWithCancelledShutdownToken()
    {
        var tracker = new AudioGenerationTracker();
        var session = new FakeNotificationSession();
        var monitor = new CoreAudioEndpointMonitor(new FakeNotificationSessionFactory(session), tracker);

        await monitor.StartAsync(CancellationToken.None);
        await monitor.StartAsync(CancellationToken.None);
        session.Raise(new AudioNotificationChange(
            AudioInvalidationKind.DeviceRemoved,
            "endpoint-X",
            null,
            null,
            DateTimeOffset.UtcNow));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await monitor.StopAsync(cancellation.Token);

        Assert.AreEqual(1, session.StartCalls);
        Assert.AreEqual(1, session.StopCalls);
        Assert.AreEqual(2L, tracker.CurrentGeneration);
        Assert.IsFalse(session.IsStarted);
    }

    [TestMethod]
    public void NativeCoreAudioSnapshotAndNotificationRegistrationSmoke()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("Core Audio native smoke requires Windows.");

        var query = new WindowsCoreAudioSnapshotQuery();
        var observed = query.Query();

        Assert.IsNotNull(observed.RenderEndpoints);
        Assert.IsNotNull(observed.CaptureEndpoints);
        Assert.AreEqual(6, observed.Defaults.Count);

        using var session = new WindowsCoreAudioNotificationSession();
        session.Start(_ => { });
        Assert.IsTrue(session.IsStarted);
        session.StopObservation();
        Assert.IsFalse(session.IsStarted);
    }

    private static AudioObservedState CompleteObservedState(DateTimeOffset observed)
    {
        var render = new[]
        {
            new AudioEndpointEvidence(
                AudioEndpointFlow.Render,
                AudioEndpointState.Active,
                "endpoint-render",
                "Stable/Case/Sensitive",
                "Speakers",
                observed),
        };
        var capture = new[]
        {
            new AudioEndpointEvidence(
                AudioEndpointFlow.Capture,
                AudioEndpointState.Active,
                "endpoint-capture",
                null,
                "Microphone",
                observed),
        };
        var defaults = (
            from flow in Enum.GetValues<AudioEndpointFlow>()
            from role in Enum.GetValues<AudioEndpointRole>()
            select new AudioDefaultEndpointEvidence(
                flow,
                role,
                flow == AudioEndpointFlow.Render ? "endpoint-render" : "endpoint-capture",
                flow == AudioEndpointFlow.Render ? "Stable/Case/Sensitive" : null,
                observed))
            .ToArray();

        return new AudioObservedState(render, capture, defaults);
    }

    private sealed class FakeSnapshotQuery(Func<AudioObservedState> read) : ICoreAudioSnapshotQuery
    {
        public int CallCount { get; private set; }

        public AudioObservedState Query()
        {
            CallCount++;
            return read();
        }
    }

    private sealed class FakeNotificationSessionFactory(FakeNotificationSession session)
        : ICoreAudioNotificationSessionFactory
    {
        public ICoreAudioNotificationSession Create() => session;
    }

    private sealed class FakeNotificationSession : ICoreAudioNotificationSession
    {
        private Action<AudioNotificationChange>? _callback;

        public bool IsStarted { get; private set; }
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }

        public void Start(Action<AudioNotificationChange> callback)
        {
            StartCalls++;
            _callback = callback;
            IsStarted = true;
        }

        public void StopObservation()
        {
            if (!IsStarted)
                return;

            StopCalls++;
            IsStarted = false;
        }

        public void Raise(AudioNotificationChange change)
            => (_callback ?? throw new InvalidOperationException("Core Audio callback is not registered."))(change);

        public void Dispose() => StopObservation();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private static T AssertThrows<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T exception)
        {
            return exception;
        }
        catch (Exception exception)
        {
            Assert.Fail($"Expected {typeof(T).Name}, got {exception.GetType().Name}: {exception.Message}");
            throw;
        }

        Assert.Fail($"Expected {typeof(T).Name}.");
        throw new InvalidOperationException();
    }
}
