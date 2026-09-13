using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Contracts.Protocol;
using SplitOS.Runtime.Client;
using SplitOS.RuntimeHost;
using SplitOS.RuntimeHost.GameRuntime;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class LauncherRuntimeLifecycleTests
{
    private static readonly Guid OperationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CorrelationId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [TestMethod]
    public void ReadinessAcceptsOnlyRuntimeExpectedOperationAndIsIdempotent()
    {
        var state = new LauncherReadinessState();
        state.Arm(OperationId, CorrelationId);

        var stale = state.ReportReady(Guid.NewGuid(), CorrelationId);
        Assert.AreEqual("REJECTED", stale.Disposition);
        Assert.AreEqual(LauncherReadinessProductCodes.OperationMismatch, stale.ProductCode);
        Assert.IsFalse(state.Snapshot.IsReady);

        var accepted = state.ReportReady(OperationId, CorrelationId);
        Assert.AreEqual("ACCEPTED", accepted.Disposition);
        Assert.AreEqual(LauncherReadinessProductCodes.ReadyAccepted, accepted.ProductCode);
        Assert.IsTrue(state.Snapshot.IsReady);

        var replay = state.ReportReady(OperationId, CorrelationId);
        Assert.AreEqual("NO_OP", replay.Disposition);
        Assert.AreEqual(LauncherReadinessProductCodes.ReadyAlreadyRecorded, replay.ProductCode);
        Assert.AreEqual(accepted.ReadinessRevision, replay.ReadinessRevision);
    }

    [TestMethod]
    public void ReadinessCannotBeRearmedByConflictingOperation()
    {
        var state = new LauncherReadinessState();
        state.Arm(OperationId, CorrelationId);

        try
        {
            state.Arm(Guid.NewGuid(), Guid.NewGuid());
            Assert.Fail("Conflicting Launcher readiness expectation must be rejected.");
        }
        catch (InvalidOperationException)
        {
            // Expected fail-closed behavior.
        }
    }

    [TestMethod]
    public void SnapshotVersionChangesOnlyWhenAuthoritativeSemanticStateChanges()
    {
        var runtimeState = ReadyRuntimeState("WORK");
        var gameSession = new GameSessionStateMachine();
        var readiness = new LauncherReadinessState();
        var provider = new LauncherRuntimeSnapshotProvider(runtimeState, gameSession, readiness);

        var first = provider.Read();
        var reread = provider.Read();
        Assert.AreEqual(first.SnapshotVersion, reread.SnapshotVersion);

        readiness.Arm(OperationId, CorrelationId);
        var armed = provider.Read();
        Assert.IsTrue(armed.SnapshotVersion > first.SnapshotVersion);
        Assert.IsTrue(armed.ExpectedGameModeOperationId == OperationId);
        Assert.IsTrue(armed.ExpectedGameModeCorrelationId == CorrelationId);

        _ = readiness.ReportReady(OperationId, CorrelationId);
        var ready = provider.Read();
        Assert.IsTrue(ready.SnapshotVersion > armed.SnapshotVersion);
        Assert.IsTrue(ready.ReadinessRevision > armed.ReadinessRevision);
    }

    [TestMethod]
    public void BindingRequiresFreshSnapshotAfterTransportConnectBeforeReadyPrecommit()
    {
        var controller = new LauncherRuntimeBindingController();
        Assert.AreEqual(LauncherLifecycleState.Starting, controller.BeginStart().State);
        Assert.AreEqual(LauncherLifecycleState.Connecting, controller.BeginConnecting().State);

        var connected = controller.ReportTransportConnected();
        Assert.AreEqual(LauncherLifecycleState.Preparing, connected.State);
        Assert.IsFalse(connected.HasFreshRuntimeSnapshot);
        Assert.IsFalse(connected.CanIssueMutatingRequests);

        _ = controller.ReportPresentationSubsystemReady();
        var bound = controller.ApplyFreshRuntimeSnapshot(
            Snapshot("WORK", "INACTIVE", expectedOperation: OperationId, expectedCorrelation: CorrelationId),
            LauncherPresentationState.Inactive);

        Assert.AreEqual(LauncherLifecycleState.ReadyPrecommit, bound.State);
        Assert.IsTrue(bound.HasFreshRuntimeSnapshot);
        Assert.IsTrue(bound.CanReportGameModeReady);
        Assert.IsFalse(bound.CanIssueMutatingRequests);
    }

    [TestMethod]
    public void DisconnectRevokesAuthorityAndReconnectCannotReuseCachedRuntimeTruth()
    {
        var controller = ReadyPrecommitController();

        var disconnected = controller.ReportRuntimeDisconnected();
        Assert.AreEqual(LauncherLifecycleState.DegradedDisconnected, disconnected.State);
        Assert.IsFalse(disconnected.HasFreshRuntimeSnapshot);
        Assert.IsFalse(disconnected.CanReportGameModeReady);
        Assert.IsFalse(disconnected.CanIssueMutatingRequests);

        Assert.AreEqual(LauncherLifecycleState.Connecting, controller.BeginConnecting().State);
        var connected = controller.ReportTransportConnected();
        Assert.AreEqual(LauncherLifecycleState.Preparing, connected.State);
        Assert.IsFalse(connected.HasFreshRuntimeSnapshot);

        var currentTruth = controller.ApplyFreshRuntimeSnapshot(
            Snapshot("GAME", "LAUNCHER", expectedOperation: null, expectedCorrelation: null, version: 2),
            LauncherPresentationState.Active);
        Assert.AreEqual(LauncherLifecycleState.Active, currentTruth.State);
        Assert.IsTrue(currentTruth.CanIssueMutatingRequests);
    }

    [TestMethod]
    public void CommittedGameSnapshotComposesWithPresentationOwner()
    {
        var controller = ConnectedController();
        _ = controller.ReportPresentationSubsystemReady();

        var active = controller.ApplyFreshRuntimeSnapshot(
            Snapshot("GAME", "LAUNCHER"),
            LauncherPresentationState.Active);
        Assert.AreEqual(LauncherLifecycleState.Active, active.State);
        Assert.IsTrue(active.CanIssueMutatingRequests);

        var background = controller.ApplyFreshRuntimeSnapshot(
            Snapshot("GAME", "GAME_RUNNING", version: 2),
            LauncherPresentationState.BackgroundGameRunning);
        Assert.AreEqual(LauncherLifecycleState.BackgroundGameRunning, background.State);
        Assert.IsFalse(background.CanIssueMutatingRequests);

        var restoring = controller.ApplyFreshRuntimeSnapshot(
            Snapshot("GAME", "RETURNING_TO_LAUNCHER", version: 3),
            LauncherPresentationState.Restoring);
        Assert.AreEqual(LauncherLifecycleState.Restoring, restoring.State);
    }

    [TestMethod]
    public void ContradictoryCommittedGameAndInactivePresentationIsRejected()
    {
        var controller = ConnectedController();
        var result = controller.ApplyFreshRuntimeSnapshot(
            Snapshot("GAME", "LAUNCHER"),
            LauncherPresentationState.Inactive);

        Assert.AreEqual(LauncherBindingDisposition.Rejected, result.Disposition);
        Assert.AreEqual(LauncherBindingReasonCodes.RuntimeSnapshotInconsistent, result.ReasonCode);
        Assert.IsFalse(result.HasFreshRuntimeSnapshot);
    }

    [TestMethod]
    public void TrustedExistingLauncherIsAttachedWithoutStartingAnother()
    {
        var platform = new FakeLauncherPlatform();
        platform.Processes.Add(platform.Trusted(42));
        var supervisor = new LauncherProcessSupervisor(platform);

        var decision = supervisor.EnsureRunning(DateTimeOffset.UtcNow, required: true);

        Assert.AreEqual(LauncherProcessSupervisionState.Running, decision.State);
        Assert.AreEqual(42, decision.ProcessId);
        Assert.AreEqual(0, platform.StartCount);
    }

    [TestMethod]
    public void SameNameWrongPathFailsClosedAndIsNeverKilledOrAttached()
    {
        var platform = new FakeLauncherPlatform();
        platform.Processes.Add(new LauncherProcessObservation(
            50,
            platform.CurrentSessionId,
            @"C:\Temp\SplitOS.GameLauncher.exe",
            DateTimeOffset.UtcNow));
        var supervisor = new LauncherProcessSupervisor(platform);

        var decision = supervisor.EnsureRunning(DateTimeOffset.UtcNow, required: true);

        Assert.AreEqual(LauncherProcessSupervisionState.Degraded, decision.State);
        Assert.AreEqual(LauncherProcessSupervisionReasonCodes.UntrustedNameCollision, decision.ReasonCode);
        Assert.AreEqual(0, platform.StartCount);
    }

    [TestMethod]
    public void MultipleTrustedInstancesFailClosedInsteadOfChoosingArbitrarily()
    {
        var platform = new FakeLauncherPlatform();
        platform.Processes.Add(platform.Trusted(60));
        platform.Processes.Add(platform.Trusted(61));
        var supervisor = new LauncherProcessSupervisor(platform);

        var decision = supervisor.EnsureRunning(DateTimeOffset.UtcNow, required: true);

        Assert.AreEqual(LauncherProcessSupervisionState.Degraded, decision.State);
        Assert.AreEqual(LauncherProcessSupervisionReasonCodes.MultipleTrustedInstances, decision.ReasonCode);
        Assert.IsNull(decision.ProcessId);
    }

    [TestMethod]
    public void RepeatedCrashesHitBoundedRestartBudget()
    {
        var platform = new FakeLauncherPlatform();
        var supervisor = new LauncherProcessSupervisor(
            platform,
            maxRestartAttempts: 2,
            restartWindow: TimeSpan.FromMinutes(1),
            minimumRestartDelay: TimeSpan.Zero);
        var now = DateTimeOffset.UtcNow;

        var first = supervisor.EnsureRunning(now, required: true);
        Assert.AreEqual(LauncherProcessSupervisionReasonCodes.StartIssued, first.ReasonCode);
        platform.Processes.Clear();

        var second = supervisor.EnsureRunning(now.AddSeconds(1), required: true);
        Assert.AreEqual(LauncherProcessSupervisionReasonCodes.StartIssued, second.ReasonCode);
        platform.Processes.Clear();

        var exhausted = supervisor.EnsureRunning(now.AddSeconds(2), required: true);
        Assert.AreEqual(LauncherProcessSupervisionState.Degraded, exhausted.State);
        Assert.AreEqual(LauncherProcessSupervisionReasonCodes.RestartBudgetExhausted, exhausted.ReasonCode);
        Assert.AreEqual(2, platform.StartCount);
    }

    [TestMethod]
    public void LauncherIsNotStartedWhenNeitherGameNorPrecommitReadinessRequiresIt()
    {
        var platform = new FakeLauncherPlatform();
        var supervisor = new LauncherProcessSupervisor(platform);

        var decision = supervisor.EnsureRunning(DateTimeOffset.UtcNow, required: false);

        Assert.AreEqual(LauncherProcessSupervisionState.Stopped, decision.State);
        Assert.AreEqual(LauncherProcessSupervisionReasonCodes.NotRequired, decision.ReasonCode);
        Assert.AreEqual(0, platform.StartCount);
    }

    private static RuntimeStateState ReadyRuntimeState(string committedMode)
    {
        var state = new RuntimeStateState();
        state.Report(new RuntimeStateReadResult(
            "READY",
            "ENABLED",
            committedMode,
            "ASSOCIATED",
            1,
            1,
            1,
            DateTimeOffset.UtcNow));
        return state;
    }

    private static LauncherRuntimeBindingController ConnectedController()
    {
        var controller = new LauncherRuntimeBindingController();
        _ = controller.BeginStart();
        _ = controller.BeginConnecting();
        _ = controller.ReportTransportConnected();
        return controller;
    }

    private static LauncherRuntimeBindingController ReadyPrecommitController()
    {
        var controller = ConnectedController();
        _ = controller.ReportPresentationSubsystemReady();
        _ = controller.ApplyFreshRuntimeSnapshot(
            Snapshot("WORK", "INACTIVE", expectedOperation: OperationId, expectedCorrelation: CorrelationId),
            LauncherPresentationState.Inactive);
        return controller;
    }

    private static LauncherRuntimeSnapshotResult Snapshot(
        string committedMode,
        string gameSessionState,
        Guid? expectedOperation = null,
        Guid? expectedCorrelation = null,
        long version = 1)
        => new(
            "READY",
            "ENABLED",
            committedMode,
            gameSessionState,
            GameSessionRevision: 1,
            ActiveLaunchOperationId: null,
            ActiveLaunchCorrelationId: null,
            ActiveGameId: null,
            ExpectedGameModeOperationId: expectedOperation,
            ExpectedGameModeCorrelationId: expectedCorrelation,
            ReadinessRevision: expectedOperation.HasValue ? 1 : 0,
            SnapshotVersion: version,
            ObservedAtUtc: DateTimeOffset.UtcNow);

    private sealed class FakeLauncherPlatform : ILauncherProcessPlatform
    {
        public int CurrentSessionId { get; } = 7;
        public string TrustedLauncherPath { get; } = @"C:\SplitOS\GameLauncher\SplitOS.GameLauncher.exe";
        public List<LauncherProcessObservation> Processes { get; } = [];
        public int StartCount { get; private set; }

        public IReadOnlyList<LauncherProcessObservation> EnumerateLauncherProcesses()
            => Processes.ToArray();

        public LauncherProcessObservation StartTrustedLauncher()
        {
            StartCount++;
            var process = Trusted(100 + StartCount);
            Processes.Add(process);
            return process;
        }

        public LauncherProcessObservation Trusted(int processId)
            => new(
                processId,
                CurrentSessionId,
                TrustedLauncherPath,
                DateTimeOffset.UtcNow);
    }
}
