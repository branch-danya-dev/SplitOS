using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class DisplayExtendTopologyRollbackCoordinatorTests
{
    [TestMethod]
    public void VerifiedExtendTopologyCanRollbackToExactBaselineConnections()
    {
        var generation = GenerationAt(2);
        var baselinePath = Path(10, 1, 20, 7);
        var extendedTarget = Path(30, 2, 40, 8);
        var baseline = Snapshot(1, baselinePath);
        var extended = Snapshot(2, baselinePath, extendedTarget);
        var beforeRollback = Snapshot(2, baselinePath, extendedTarget);
        var afterRollback = Snapshot(3, baselinePath);
        var native = new FakeNativeRollbackApplier();
        var coordinator = new DisplayExtendTopologyRollbackCoordinator(
            new QueueSnapshotReader(beforeRollback, afterRollback), generation, native);

        var result = coordinator.Rollback(new DisplayExtendTopologyRollbackRequest(
            baseline, extended, extendedTarget));

        Assert.AreEqual(DisplayExtendTopologyRollbackDisposition.RolledBackVerified, result.Disposition, result.Detail);
        Assert.AreEqual("DISPLAY_EXTEND_ROLLED_BACK_VERIFIED", result.ProductCode);
        Assert.AreEqual(1, native.Calls);
        Assert.AreEqual(3L, generation.CurrentGeneration);
        Assert.AreEqual(afterRollback, result.AfterRollback);
        Assert.AreEqual(1, native.LastRollback!.BaselineActivePaths.Count);
        Assert.AreEqual(extendedTarget.TargetKey, native.LastRollback.ExtendedTarget.TargetKey);
    }

    [TestMethod]
    public void RollbackMayCompensateAfterModeGenerationAdvanceWhenConnectionsRemainStable()
    {
        var generation = GenerationAt(3);
        var baselinePath = Path(10, 1, 20, 7);
        var extendedTarget = Path(30, 2, 40, 8);
        var baseline = Snapshot(1, baselinePath);
        var extended = Snapshot(2, baselinePath, extendedTarget);
        var modeStageSnapshot = Snapshot(3, baselinePath, extendedTarget);
        var afterRollback = Snapshot(4, baselinePath);
        var native = new FakeNativeRollbackApplier();
        var coordinator = new DisplayExtendTopologyRollbackCoordinator(
            new QueueSnapshotReader(modeStageSnapshot, afterRollback), generation, native);

        var result = coordinator.Rollback(new DisplayExtendTopologyRollbackRequest(
            baseline,
            extended,
            extendedTarget,
            ExpectedCurrentGeneration: 3));

        Assert.AreEqual(DisplayExtendTopologyRollbackDisposition.RolledBackVerified, result.Disposition, result.Detail);
        Assert.AreEqual(1, native.Calls);
        Assert.AreEqual(3L, native.LastRollback!.SnapshotGeneration);
        Assert.AreEqual(4L, generation.CurrentGeneration);
    }

    [TestMethod]
    public void GenerationChangeBeforeRollbackNeverReachesNativeMutation()
    {
        var generation = GenerationAt(3);
        var baselinePath = Path(10, 1, 20, 7);
        var extendedTarget = Path(30, 2, 40, 8);
        var baseline = Snapshot(1, baselinePath);
        var extended = Snapshot(2, baselinePath, extendedTarget);
        var native = new FakeNativeRollbackApplier();
        var coordinator = new DisplayExtendTopologyRollbackCoordinator(
            new QueueSnapshotReader(Snapshot(3, baselinePath, extendedTarget)), generation, native);

        var result = coordinator.Rollback(new DisplayExtendTopologyRollbackRequest(
            baseline, extended, extendedTarget));

        Assert.AreEqual(DisplayExtendTopologyRollbackDisposition.StaleSnapshot, result.Disposition);
        Assert.AreEqual(0, native.Calls);
    }

    [TestMethod]
    public void ExternalTopologyDriftBlocksRollbackInsteadOfOverwritingIt()
    {
        var generation = GenerationAt(2);
        var baselinePath = Path(10, 1, 20, 7);
        var extendedTarget = Path(30, 2, 40, 8);
        var unexpected = Path(50, 3, 60, 9);
        var baseline = Snapshot(1, baselinePath);
        var extended = Snapshot(2, baselinePath, extendedTarget);
        var native = new FakeNativeRollbackApplier();
        var coordinator = new DisplayExtendTopologyRollbackCoordinator(
            new QueueSnapshotReader(Snapshot(2, baselinePath, extendedTarget, unexpected)), generation, native);

        var result = coordinator.Rollback(new DisplayExtendTopologyRollbackRequest(
            baseline, extended, extendedTarget));

        Assert.AreEqual(DisplayExtendTopologyRollbackDisposition.TopologyDrift, result.Disposition);
        Assert.AreEqual("DISPLAY_ROLLBACK_TOPOLOGY_DRIFT", result.ProductCode);
        Assert.AreEqual(0, native.Calls);
    }

    [TestMethod]
    public void NativeValidationRejectionDoesNotAdvanceGeneration()
    {
        var generation = GenerationAt(2);
        var baselinePath = Path(10, 1, 20, 7);
        var extendedTarget = Path(30, 2, 40, 8);
        var baseline = Snapshot(1, baselinePath);
        var extended = Snapshot(2, baselinePath, extendedTarget);
        var native = new FakeNativeRollbackApplier
        {
            Outcome = new DisplayNativeMutationOutcome(
                DisplayNativeMutationDisposition.ValidationRejected,
                "DISPLAY_ROLLBACK_OPERATION_REJECTED",
                87)
        };
        var coordinator = new DisplayExtendTopologyRollbackCoordinator(
            new QueueSnapshotReader(Snapshot(2, baselinePath, extendedTarget)), generation, native);

        var result = coordinator.Rollback(new DisplayExtendTopologyRollbackRequest(
            baseline, extended, extendedTarget));

        Assert.AreEqual(DisplayExtendTopologyRollbackDisposition.OperationRejected, result.Disposition);
        Assert.AreEqual(87, result.NativeErrorCode);
        Assert.AreEqual(2L, generation.CurrentGeneration);
        Assert.AreEqual(1, native.Calls);
    }

    [TestMethod]
    public void SuccessfulNativeRollbackWithWrongBaselineReadBackIsVerificationFailure()
    {
        var generation = GenerationAt(2);
        var baselinePath = Path(10, 1, 20, 7);
        var extendedTarget = Path(30, 2, 40, 8);
        var baseline = Snapshot(1, baselinePath);
        var extended = Snapshot(2, baselinePath, extendedTarget);
        var wrongSource = Path(10, 99, 20, 7);
        var coordinator = new DisplayExtendTopologyRollbackCoordinator(
            new QueueSnapshotReader(
                Snapshot(2, baselinePath, extendedTarget),
                Snapshot(3, wrongSource)),
            generation,
            new FakeNativeRollbackApplier());

        var result = coordinator.Rollback(new DisplayExtendTopologyRollbackRequest(
            baseline, extended, extendedTarget));

        Assert.AreEqual(DisplayExtendTopologyRollbackDisposition.VerificationFailed, result.Disposition);
        Assert.AreEqual("DISPLAY_ROLLBACK_VERIFICATION_FAILED", result.ProductCode);
        Assert.AreEqual(3L, generation.CurrentGeneration);
    }

    [TestMethod]
    public void ConcurrentDisplayChangeDuringRollbackReturnsStaleAfterReadBack()
    {
        var generation = GenerationAt(2);
        var baselinePath = Path(10, 1, 20, 7);
        var extendedTarget = Path(30, 2, 40, 8);
        var baseline = Snapshot(1, baselinePath);
        var extended = Snapshot(2, baselinePath, extendedTarget);
        var native = new FakeNativeRollbackApplier
        {
            OnApply = () => generation.Invalidate("WM_DISPLAYCHANGE")
        };
        var coordinator = new DisplayExtendTopologyRollbackCoordinator(
            new QueueSnapshotReader(
                Snapshot(2, baselinePath, extendedTarget),
                Snapshot(4, baselinePath)),
            generation,
            native);

        var result = coordinator.Rollback(new DisplayExtendTopologyRollbackRequest(
            baseline, extended, extendedTarget));

        Assert.AreEqual(DisplayExtendTopologyRollbackDisposition.StaleSnapshot, result.Disposition);
        Assert.AreEqual(4L, generation.CurrentGeneration);
        Assert.AreEqual(1, native.Calls);
    }

    private static DisplayGenerationTracker GenerationAt(long generation)
    {
        var tracker = new DisplayGenerationTracker();
        while (tracker.CurrentGeneration < generation)
            tracker.Invalidate("test");
        return tracker;
    }

    private static DisplayPathEvidence Path(
        long sourceAdapter,
        uint sourceId,
        long targetAdapter,
        uint targetId) => new(
            SourceAdapterLuid: sourceAdapter,
            SourceId: sourceId,
            TargetKey: new DisplayPathKey(targetAdapter, targetId),
            Active: true,
            TargetAvailable: true,
            OutputTechnology: 5,
            Rotation: 1,
            Scaling: 1,
            RefreshRate: new DisplayRational(60, 1),
            SourceResolution: new DisplayPixelSize(1920, 1080),
            SourcePosition: new DisplayDesktopPoint(0, 0));

    private static DisplaySnapshot Snapshot(long generation, params DisplayPathEvidence[] paths) =>
        new(generation, DateTimeOffset.UtcNow, paths);

    private sealed class QueueSnapshotReader(params DisplaySnapshot[] snapshots) : IDisplaySnapshotReader
    {
        private readonly Queue<DisplaySnapshot> _snapshots = new(snapshots);

        public DisplaySnapshot Read()
        {
            Assert.IsTrue(_snapshots.Count > 0, "Unexpected extra display snapshot read.");
            return _snapshots.Dequeue();
        }
    }

    private sealed class FakeNativeRollbackApplier : IDisplayNativeTopologyRollbackApplier
    {
        public int Calls { get; private set; }
        public ResolvedDisplayTopologyRollback? LastRollback { get; private set; }
        public Action? OnApply { get; init; }
        public DisplayNativeMutationOutcome Outcome { get; init; } = new(
            DisplayNativeMutationDisposition.Applied,
            "DISPLAY_ROLLBACK_NATIVE_APPLIED");

        public DisplayNativeMutationOutcome ValidateAndApply(ResolvedDisplayTopologyRollback rollback)
        {
            Calls++;
            LastRollback = rollback;
            OnApply?.Invoke();
            return Outcome;
        }
    }
}
