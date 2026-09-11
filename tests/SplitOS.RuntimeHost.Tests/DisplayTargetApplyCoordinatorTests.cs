using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class DisplayTargetApplyCoordinatorTests
{
    [TestMethod]
    public void AppliedTargetRequiresFreshMatchingReadBack()
    {
        var generation = new DisplayGenerationTracker();
        var key = new DisplayPathKey(20, 7);
        var target = Target(key, 1);
        var before = Snapshot(1, Path(key, new DisplayPixelSize(1920, 1080), new DisplayRational(60, 1), 1));
        var after = Snapshot(2, Path(key, target.Resolution, target.RefreshRate, target.Rotation));
        var reader = new QueueSnapshotReader(before, after);
        var native = new FakeNativeApplier();
        var coordinator = new DisplayTargetApplyCoordinator(reader, generation, native);

        var result = coordinator.Apply(target);

        Assert.AreEqual(DisplayTargetApplyDisposition.AppliedVerified, result.Disposition, result.Detail);
        Assert.AreEqual("DISPLAY_APPLIED_VERIFIED", result.ProductCode);
        Assert.AreEqual(1, native.Calls);
        Assert.AreEqual(2L, generation.CurrentGeneration);
        Assert.AreEqual(after, result.After);
    }

    [TestMethod]
    public void EquivalentRefreshRationalsVerifyWithoutIntegerRounding()
    {
        var generation = new DisplayGenerationTracker();
        var key = new DisplayPathKey(20, 7);
        var target = Target(key, 1) with { RefreshRate = new DisplayRational(60000, 1001) };
        var before = Snapshot(1, Path(key, target.Resolution, new DisplayRational(60, 1), target.Rotation));
        var after = Snapshot(2, Path(key, target.Resolution, new DisplayRational(120000, 2002), target.Rotation));
        var coordinator = new DisplayTargetApplyCoordinator(
            new QueueSnapshotReader(before, after), generation, new FakeNativeApplier());

        Assert.AreEqual(DisplayTargetApplyDisposition.AppliedVerified, coordinator.Apply(target).Disposition);
    }

    [TestMethod]
    public void StaleResolvedGenerationNeverReachesNativeMutation()
    {
        var generation = new DisplayGenerationTracker();
        generation.Invalidate("hotplug");
        var key = new DisplayPathKey(20, 7);
        var snapshot = Snapshot(2, Path(key));
        var native = new FakeNativeApplier();
        var coordinator = new DisplayTargetApplyCoordinator(new QueueSnapshotReader(snapshot), generation, native);

        var result = coordinator.Apply(Target(key, 1));

        Assert.AreEqual(DisplayTargetApplyDisposition.StaleSnapshot, result.Disposition);
        Assert.AreEqual(0, native.Calls);
    }

    [TestMethod]
    public void NativeValidationRejectionMapsToOperationRejectedWithoutAdvancingGeneration()
    {
        var generation = new DisplayGenerationTracker();
        var key = new DisplayPathKey(20, 7);
        var snapshot = Snapshot(1, Path(key));
        var native = new FakeNativeApplier
        {
            Outcome = new DisplayNativeMutationOutcome(
                DisplayNativeMutationDisposition.ValidationRejected,
                "DISPLAY_OPERATION_REJECTED",
                1610)
        };
        var coordinator = new DisplayTargetApplyCoordinator(new QueueSnapshotReader(snapshot), generation, native);

        var result = coordinator.Apply(Target(key, 1));

        Assert.AreEqual(DisplayTargetApplyDisposition.OperationRejected, result.Disposition);
        Assert.AreEqual(1610, result.NativeErrorCode);
        Assert.AreEqual(1L, generation.CurrentGeneration);
    }

    [TestMethod]
    public void SuccessfulNativeApplyWithDifferentReadBackIsVerificationFailure()
    {
        var generation = new DisplayGenerationTracker();
        var key = new DisplayPathKey(20, 7);
        var target = Target(key, 1);
        var before = Snapshot(1, Path(key));
        var after = Snapshot(2, Path(key, new DisplayPixelSize(2560, 1440), new DisplayRational(60, 1), target.Rotation));
        var coordinator = new DisplayTargetApplyCoordinator(
            new QueueSnapshotReader(before, after), generation, new FakeNativeApplier());

        var result = coordinator.Apply(target);

        Assert.AreEqual(DisplayTargetApplyDisposition.VerificationFailed, result.Disposition);
        Assert.AreEqual("DISPLAY_VERIFICATION_FAILED", result.ProductCode);
        Assert.AreEqual(after, result.After);
    }

    [TestMethod]
    public void ConcurrentGenerationChangeDuringNativeMutationReturnsStaleAfterReadBack()
    {
        var generation = new DisplayGenerationTracker();
        var key = new DisplayPathKey(20, 7);
        var target = Target(key, 1);
        var before = Snapshot(1, Path(key));
        var after = Snapshot(3, Path(key, target.Resolution, target.RefreshRate, target.Rotation));
        var native = new FakeNativeApplier
        {
            OnApply = () => generation.Invalidate("WM_DISPLAYCHANGE")
        };
        var coordinator = new DisplayTargetApplyCoordinator(new QueueSnapshotReader(before, after), generation, native);

        var result = coordinator.Apply(target);

        Assert.AreEqual(DisplayTargetApplyDisposition.StaleSnapshot, result.Disposition);
        Assert.AreEqual(3L, generation.CurrentGeneration);
        Assert.AreEqual(after, result.After);
    }

    [TestMethod]
    public void DynamicRefreshPathIsFailClosedUntilVersionedMutationIsValidated()
    {
        var generation = new DisplayGenerationTracker();
        var key = new DisplayPathKey(20, 7);
        var snapshot = Snapshot(1, Path(key) with { BoostRefreshRate = true });
        var native = new FakeNativeApplier();
        var coordinator = new DisplayTargetApplyCoordinator(new QueueSnapshotReader(snapshot), generation, native);

        var result = coordinator.Apply(Target(key, 1));

        Assert.AreEqual(DisplayTargetApplyDisposition.UnsupportedCapability, result.Disposition);
        Assert.AreEqual("DISPLAY_DYNAMIC_REFRESH_MUTATION_UNSUPPORTED", result.ProductCode);
        Assert.AreEqual(0, native.Calls);
    }

    [TestMethod]
    public void TopologyChangingIntentIsNotSilentlyDowngradedToPreserveTopology()
    {
        var generation = new DisplayGenerationTracker();
        var key = new DisplayPathKey(20, 7);
        var snapshot = Snapshot(1, Path(key));
        var native = new FakeNativeApplier();
        var coordinator = new DisplayTargetApplyCoordinator(new QueueSnapshotReader(snapshot), generation, native);
        var target = Target(key, 1) with { TopologyIntent = DisplayTopologyIntent.SelectedOnly };

        var result = coordinator.Apply(target);

        Assert.AreEqual(DisplayTargetApplyDisposition.UnsupportedCapability, result.Disposition);
        Assert.AreEqual("DISPLAY_TOPOLOGY_MUTATION_NOT_IMPLEMENTED", result.ProductCode);
        Assert.AreEqual(0, native.Calls);
    }

    [TestMethod]
    public void MissingOrUnavailableCurrentPathIsRejectedBeforeMutation()
    {
        var key = new DisplayPathKey(20, 7);
        var missingNative = new FakeNativeApplier();
        var missing = new DisplayTargetApplyCoordinator(
            new QueueSnapshotReader(Snapshot(1, Path(new DisplayPathKey(30, 8)))),
            new DisplayGenerationTracker(),
            missingNative).Apply(Target(key, 1));
        Assert.AreEqual(DisplayTargetApplyDisposition.TargetNotFound, missing.Disposition);
        Assert.AreEqual(0, missingNative.Calls);

        var unavailableNative = new FakeNativeApplier();
        var unavailable = new DisplayTargetApplyCoordinator(
            new QueueSnapshotReader(Snapshot(1, Path(key) with { TargetAvailable = false })),
            new DisplayGenerationTracker(),
            unavailableNative).Apply(Target(key, 1));
        Assert.AreEqual(DisplayTargetApplyDisposition.TargetUnavailable, unavailable.Disposition);
        Assert.AreEqual(0, unavailableNative.Calls);
    }

    private static ResolvedDisplayTarget Target(DisplayPathKey key, long generation) => new(
        key,
        generation,
        new DisplayPixelSize(3840, 2160),
        new DisplayRational(120, 1),
        Rotation: 1);

    private static DisplayPathEvidence Path(
        DisplayPathKey key,
        DisplayPixelSize? resolution = null,
        DisplayRational? refresh = null,
        uint rotation = 1) => new(
            SourceAdapterLuid: 10,
            SourceId: 3,
            TargetKey: key,
            Active: true,
            TargetAvailable: true,
            OutputTechnology: 5,
            Rotation: rotation,
            Scaling: 1,
            RefreshRate: refresh ?? new DisplayRational(60, 1),
            SourceResolution: resolution ?? new DisplayPixelSize(1920, 1080),
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

    private sealed class FakeNativeApplier : IDisplayNativeTargetApplier
    {
        public int Calls { get; private set; }
        public Action? OnApply { get; init; }
        public DisplayNativeMutationOutcome Outcome { get; init; } = new(
            DisplayNativeMutationDisposition.Applied,
            "DISPLAY_NATIVE_APPLIED");

        public DisplayNativeMutationOutcome ValidateAndApply(ResolvedDisplayTarget target)
        {
            Calls++;
            OnApply?.Invoke();
            return Outcome;
        }
    }
}
