using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class GameInputControllerEvidenceTests
{
    [TestMethod]
    public void EvidenceStateTracksOpaqueIdentityConnectionAndGeneration()
    {
        var now = new DateTimeOffset(2026, 9, 12, 10, 30, 0, TimeSpan.Zero);
        var state = new GameInputEvidenceState(new FixedTimeProvider(now));
        var deviceId = new string('A', 64);

        state.Apply(new GameInputDeviceChange(
            new GameInputDeviceEvidence(deviceId, true,
                SplitOSInputKinds.Gamepad | SplitOSInputKinds.Controller,
                0x045E, 0x0B13, 1, 1, now),
            1, 0, 100, now));

        var snapshot = state.Read();
        Assert.AreEqual(2L, snapshot.Generation);
        Assert.AreEqual(now, snapshot.ObservedUtc);
        Assert.AreEqual(1, snapshot.Devices.Count);
        Assert.AreEqual(deviceId, snapshot.Devices[0].GameInputDeviceId);
        Assert.IsTrue(snapshot.Devices[0].Connected);
        Assert.IsTrue(snapshot.Devices[0].SupportedKinds.HasFlag(SplitOSInputKinds.Gamepad));
        Assert.AreEqual((ushort)0x045E, snapshot.Devices[0].VendorId);
        Assert.AreEqual((ushort)0x0B13, snapshot.Devices[0].ProductId);
    }

    [TestMethod]
    public void DisconnectUpdatesSameOpaqueDeviceInsteadOfInventingReplacement()
    {
        var now = new DateTimeOffset(2026, 9, 12, 10, 40, 0, TimeSpan.Zero);
        var state = new GameInputEvidenceState(new FixedTimeProvider(now));
        var deviceId = new string('B', 64);

        state.Apply(Change(deviceId, true, now));
        state.Apply(Change(deviceId, false, now.AddSeconds(1)));

        var snapshot = state.Read();
        Assert.AreEqual(3L, snapshot.Generation);
        Assert.AreEqual(1, snapshot.Devices.Count);
        Assert.IsFalse(snapshot.Devices[0].Connected);
        Assert.AreEqual(deviceId, snapshot.Devices[0].GameInputDeviceId);
    }

    [TestMethod]
    public void MetadataFailureStillInvalidatesInputGeneration()
    {
        var now = new DateTimeOffset(2026, 9, 12, 10, 50, 0, TimeSpan.Zero);
        var state = new GameInputEvidenceState(new FixedTimeProvider(now));
        state.Apply(new GameInputDeviceChange(null, 1, 0, 123, now));

        var snapshot = state.Read();
        Assert.AreEqual(2L, snapshot.Generation);
        Assert.AreEqual(0, snapshot.Devices.Count);
    }

    [TestMethod]
    public async Task MonitorRetainsOneSessionAndUnregistersDuringShutdownEvenWhenCancelled()
    {
        var session = new FakeSession();
        var monitor = new GameInputControllerMonitor(new FakeSessionFactory(session), new GameInputEvidenceState());
        await monitor.StartAsync(CancellationToken.None);
        await monitor.StartAsync(CancellationToken.None);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await monitor.StopAsync(cancelled.Token);

        Assert.AreEqual(1, session.StartCount);
        Assert.AreEqual(1, session.StopObservationCount);
        Assert.AreEqual(1, session.DisposeCount);
    }

    [TestMethod]
    public void MicrosoftGameInputPackageCanCreateAndRegisterReadOnlySessionOnWindows()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var session = new WindowsGameInputSessionFactory().Create();
        session.Start(_ => { });
        Assert.IsTrue(session.IsStarted);
        session.StopObservation();
        Assert.IsFalse(session.IsStarted);
    }

    private static GameInputDeviceChange Change(string deviceId, bool connected, DateTimeOffset observedUtc)
    {
        var status = connected ? 1 : 0;
        return new GameInputDeviceChange(
            new GameInputDeviceEvidence(deviceId, connected, SplitOSInputKinds.Gamepad,
                1, 2, 3, status, observedUtc),
            status, connected ? 0 : 1, 10, observedUtc);
    }

    private sealed class FakeSessionFactory(FakeSession session) : IGameInputSessionFactory
    {
        public IGameInputSession Create() => session;
    }

    private sealed class FakeSession : IGameInputSession
    {
        public int StartCount { get; private set; }
        public int StopObservationCount { get; private set; }
        public int DisposeCount { get; private set; }
        public bool IsStarted { get; private set; }

        public void Start(Action<GameInputDeviceChange> callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            if (IsStarted)
                return;
            StartCount++;
            IsStarted = true;
        }

        public void StopObservation()
        {
            if (!IsStarted)
                return;
            StopObservationCount++;
            IsStarted = false;
        }

        public void Dispose() => DisposeCount++;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
