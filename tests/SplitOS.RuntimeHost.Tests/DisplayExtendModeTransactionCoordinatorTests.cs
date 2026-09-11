using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class DisplayExtendModeTransactionCoordinatorTests
{
    [TestMethod]
    public void VerifiedExtendAndModeCompleteAsOneVerifiedTransaction()
    {
        var generation = new DisplayGenerationTracker();
        var fixture = Fixture();
        var topology = new FakeTopologyStage(fixture.ExtendSuccess)
        {
            OnApply = () => generation.Invalidate("extend")
        };
        var mode = new FakeModeStage(fixture.ModeSuccess);
        var rollback = new FakeRollbackStage(fixture.RollbackSuccess);
        var coordinator = new DisplayExtendModeTransactionCoordinator(topology, mode, rollback, generation);

        var result = coordinator.Apply(Request());

        Assert.AreEqual(DisplayExtendModeDisposition.AppliedVerified, result.Disposition, result.Detail);
        Assert.AreEqual("DISPLAY_EXTEND_MODE_APPLIED_VERIFIED", result.ProductCode);
        Assert.AreEqual(1, topology.Calls);
        Assert.AreEqual(1, mode.Calls);
        Assert.AreEqual(0, rollback.Calls);
        Assert.AreEqual(fixture.ExtendedTarget.TargetKey, mode.LastTarget!.TargetKey);
        Assert.AreEqual(2L, mode.LastTarget.SnapshotGeneration);
        Assert.AreEqual(DisplayTopologyIntent.PreserveActiveTopology, mode.LastTarget.TopologyIntent);
        Assert.AreEqual(new DisplayPixelSize(3840, 2160), mode.LastTarget.Resolution);
        Assert.AreEqual(new DisplayRational(120, 1), mode.LastTarget.RefreshRate);
    }

    [TestMethod]
    public void ExtendFailureBeforeNativeMutationDoesNotRunModeOrRollback()
    {
        var fixture = Fixture();
        var topology = new FakeTopologyStage(new DisplayExtendApplyOutcome(
            DisplayExtendApplyDisposition.TargetNotFound,
            "DISPLAY_SELECTOR_NOT_FOUND",
            fixture.Baseline,
            fixture.Candidates));
        var mode = new FakeModeStage(fixture.ModeSuccess);
        var rollback = new FakeRollbackStage(fixture.RollbackSuccess);
        var coordinator = new DisplayExtendModeTransactionCoordinator(
            topology, mode, rollback, new DisplayGenerationTracker());

        var result = coordinator.Apply(Request());

        Assert.AreEqual(DisplayExtendModeDisposition.ExtendFailed, result.Disposition);
        Assert.AreEqual("DISPLAY_SELECTOR_NOT_FOUND", result.ProductCode);
        Assert.AreEqual(0, mode.Calls);
        Assert.AreEqual(0, rollback.Calls);
    }

    [TestMethod]
    public void UnverifiedExtendAfterNativeMutationRequiresRecoveryWithoutGuessingCompensation()
    {
        var fixture = Fixture();
        var topology = new FakeTopologyStage(new DisplayExtendApplyOutcome(
            DisplayExtendApplyDisposition.VerificationFailed,
            "DISPLAY_EXTEND_VERIFICATION_FAILED",
            fixture.Baseline,
            fixture.Candidates,
            fixture.Extended,
            fixture.ExtendedTarget));
        var mode = new FakeModeStage(fixture.ModeSuccess);
        var rollback = new FakeRollbackStage(fixture.RollbackSuccess);
        var coordinator = new DisplayExtendModeTransactionCoordinator(
            topology, mode, rollback, new DisplayGenerationTracker());

        var result = coordinator.Apply(Request());

        Assert.AreEqual(DisplayExtendModeDisposition.RecoveryRequired, result.Disposition);
        Assert.AreEqual("DISPLAY_EXTEND_MODE_RECOVERY_REQUIRED", result.ProductCode);
        Assert.AreEqual(0, mode.Calls);
        Assert.AreEqual(0, rollback.Calls);
    }

    [TestMethod]
    public void ModeValidationFailureRollsBackVerifiedExtendTopology()
    {
        var generation = new DisplayGenerationTracker();
        var fixture = Fixture();
        var topology = new FakeTopologyStage(fixture.ExtendSuccess)
        {
            OnApply = () => generation.Invalidate("extend")
        };
        var modeFailure = new DisplayTargetApplyOutcome(
            DisplayTargetApplyDisposition.OperationRejected,
            "DISPLAY_OPERATION_REJECTED",
            fixture.Extended,
            ObservedTarget: fixture.ExtendedTarget,
            NativeErrorCode: 87);
        var mode = new FakeModeStage(modeFailure);
        var rollback = new FakeRollbackStage(fixture.RollbackSuccess);
        var coordinator = new DisplayExtendModeTransactionCoordinator(topology, mode, rollback, generation);

        var result = coordinator.Apply(Request());

        Assert.AreEqual(DisplayExtendModeDisposition.ModeFailedRolledBack, result.Disposition, result.Detail);
        Assert.AreEqual("DISPLAY_EXTEND_MODE_FAILED_ROLLED_BACK", result.ProductCode);
        Assert.AreEqual(1, rollback.Calls);
        Assert.AreEqual(2L, rollback.LastRequest!.ExpectedCurrentGeneration);
        Assert.AreEqual(fixture.Baseline, rollback.LastRequest.Baseline);
        Assert.AreEqual(fixture.Extended, rollback.LastRequest.Extended);
    }

    [TestMethod]
    public void ModeVerificationFailureAfterGenerationAdvanceUsesLatestGenerationForCompensation()
    {
        var generation = new DisplayGenerationTracker();
        var fixture = Fixture();
        var topology = new FakeTopologyStage(fixture.ExtendSuccess)
        {
            OnApply = () => generation.Invalidate("extend")
        };
        var modeAfter = Snapshot(3, fixture.BaselinePath, fixture.ConfiguredExtendedTarget);
        var modeFailure = new DisplayTargetApplyOutcome(
            DisplayTargetApplyDisposition.VerificationFailed,
            "DISPLAY_VERIFICATION_FAILED",
            fixture.Extended,
            modeAfter,
            fixture.ConfiguredExtendedTarget);
        var mode = new FakeModeStage(modeFailure)
        {
            OnApply = () => generation.Invalidate("mode")
        };
        var rollback = new FakeRollbackStage(fixture.RollbackSuccess);
        var coordinator = new DisplayExtendModeTransactionCoordinator(topology, mode, rollback, generation);

        var result = coordinator.Apply(Request());

        Assert.AreEqual(DisplayExtendModeDisposition.ModeFailedRolledBack, result.Disposition, result.Detail);
        Assert.AreEqual(3L, rollback.LastRequest!.ExpectedCurrentGeneration);
        Assert.AreEqual(1, rollback.Calls);
    }

    [TestMethod]
    public void FailedCompensationMakesTransactionRecoveryRequired()
    {
        var generation = new DisplayGenerationTracker();
        var fixture = Fixture();
        var topology = new FakeTopologyStage(fixture.ExtendSuccess)
        {
            OnApply = () => generation.Invalidate("extend")
        };
        var modeFailure = new DisplayTargetApplyOutcome(
            DisplayTargetApplyDisposition.UnsupportedCapability,
            "DISPLAY_DYNAMIC_REFRESH_MUTATION_UNSUPPORTED",
            fixture.Extended,
            ObservedTarget: fixture.ExtendedTarget);
        var mode = new FakeModeStage(modeFailure);
        var rollbackFailure = new DisplayExtendTopologyRollbackOutcome(
            DisplayExtendTopologyRollbackDisposition.TopologyDrift,
            "DISPLAY_ROLLBACK_TOPOLOGY_DRIFT",
            fixture.Baseline,
            fixture.Extended,
            Detail: "external topology changed");
        var rollback = new FakeRollbackStage(rollbackFailure);
        var coordinator = new DisplayExtendModeTransactionCoordinator(topology, mode, rollback, generation);

        var result = coordinator.Apply(Request());

        Assert.AreEqual(DisplayExtendModeDisposition.RecoveryRequired, result.Disposition);
        Assert.AreEqual("DISPLAY_EXTEND_MODE_RECOVERY_REQUIRED", result.ProductCode);
        Assert.AreEqual(modeFailure, result.ModeOutcome);
        Assert.AreEqual(rollbackFailure, result.RollbackOutcome);
    }

    private static DisplayExtendModeRequest Request() => new(
        new PersistentDisplaySelector(PnpDeviceInstanceId: "DISPLAY\\TV\\INSTANCE"),
        SnapshotGeneration: 1,
        Resolution: new DisplayPixelSize(3840, 2160),
        RefreshRate: new DisplayRational(120, 1),
        Rotation: 1);

    private static TransactionFixture Fixture()
    {
        var baselinePath = Path(10, 1, 20, 7, new DisplayPixelSize(1920, 1080), new DisplayRational(60, 1));
        var extendedTarget = Path(30, 2, 40, 8, new DisplayPixelSize(1920, 1080), new DisplayRational(60, 1));
        var configuredExtendedTarget = Path(30, 2, 40, 8, new DisplayPixelSize(3840, 2160), new DisplayRational(120, 1));
        var baseline = Snapshot(1, baselinePath);
        var extended = Snapshot(2, baselinePath, extendedTarget);
        var modeAfter = Snapshot(3, baselinePath, configuredExtendedTarget);
        var candidates = new DisplayConnectionCandidateSnapshot(
            1,
            DateTimeOffset.UtcNow,
            Array.Empty<DisplayConnectionCandidate>());
        var extendSuccess = new DisplayExtendApplyOutcome(
            DisplayExtendApplyDisposition.AppliedVerified,
            "DISPLAY_EXTEND_APPLIED_VERIFIED",
            baseline,
            candidates,
            extended,
            extendedTarget);
        var modeSuccess = new DisplayTargetApplyOutcome(
            DisplayTargetApplyDisposition.AppliedVerified,
            "DISPLAY_APPLIED_VERIFIED",
            extended,
            modeAfter,
            configuredExtendedTarget);
        var rollbackSuccess = new DisplayExtendTopologyRollbackOutcome(
            DisplayExtendTopologyRollbackDisposition.RolledBackVerified,
            "DISPLAY_EXTEND_ROLLED_BACK_VERIFIED",
            baseline,
            extended,
            Snapshot(4, baselinePath));

        return new TransactionFixture(
            baselinePath,
            extendedTarget,
            configuredExtendedTarget,
            baseline,
            extended,
            candidates,
            extendSuccess,
            modeSuccess,
            rollbackSuccess);
    }

    private static DisplayPathEvidence Path(
        long sourceAdapter,
        uint sourceId,
        long targetAdapter,
        uint targetId,
        DisplayPixelSize resolution,
        DisplayRational refresh) => new(
            SourceAdapterLuid: sourceAdapter,
            SourceId: sourceId,
            TargetKey: new DisplayPathKey(targetAdapter, targetId),
            Active: true,
            TargetAvailable: true,
            OutputTechnology: 5,
            Rotation: 1,
            Scaling: 1,
            RefreshRate: refresh,
            SourceResolution: resolution,
            SourcePosition: new DisplayDesktopPoint(0, 0));

    private static DisplaySnapshot Snapshot(long generation, params DisplayPathEvidence[] paths) =>
        new(generation, DateTimeOffset.UtcNow, paths);

    private sealed record TransactionFixture(
        DisplayPathEvidence BaselinePath,
        DisplayPathEvidence ExtendedTarget,
        DisplayPathEvidence ConfiguredExtendedTarget,
        DisplaySnapshot Baseline,
        DisplaySnapshot Extended,
        DisplayConnectionCandidateSnapshot Candidates,
        DisplayExtendApplyOutcome ExtendSuccess,
        DisplayTargetApplyOutcome ModeSuccess,
        DisplayExtendTopologyRollbackOutcome RollbackSuccess);

    private sealed class FakeTopologyStage(DisplayExtendApplyOutcome outcome)
        : IDisplayExtendTransactionTopologyStage
    {
        public int Calls { get; private set; }
        public DisplayExtendRequest? LastRequest { get; private set; }
        public Action? OnApply { get; init; }

        public DisplayExtendApplyOutcome Apply(DisplayExtendRequest request)
        {
            Calls++;
            LastRequest = request;
            OnApply?.Invoke();
            return outcome;
        }
    }

    private sealed class FakeModeStage(DisplayTargetApplyOutcome outcome)
        : IDisplayExtendTransactionModeStage
    {
        public int Calls { get; private set; }
        public ResolvedDisplayTarget? LastTarget { get; private set; }
        public Action? OnApply { get; init; }

        public DisplayTargetApplyOutcome Apply(ResolvedDisplayTarget target)
        {
            Calls++;
            LastTarget = target;
            OnApply?.Invoke();
            return outcome;
        }
    }

    private sealed class FakeRollbackStage(DisplayExtendTopologyRollbackOutcome outcome)
        : IDisplayExtendTransactionRollbackStage
    {
        public int Calls { get; private set; }
        public DisplayExtendTopologyRollbackRequest? LastRequest { get; private set; }

        public DisplayExtendTopologyRollbackOutcome Rollback(DisplayExtendTopologyRollbackRequest request)
        {
            Calls++;
            LastRequest = request;
            return outcome;
        }
    }
}
