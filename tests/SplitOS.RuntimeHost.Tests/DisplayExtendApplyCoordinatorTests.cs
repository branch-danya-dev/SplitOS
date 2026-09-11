using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class DisplayExtendApplyCoordinatorTests
{
    [TestMethod]
    public void AppliedExtendRequiresExactConnectionAndTopologyReadBack()
    {
        var generation = new DisplayGenerationTracker();
        var selectedIdentity = Identity("DISPLAY\\SELECTED\\UID", outputTechnology: 5);
        var before = Snapshot(1, ActivePath(1, 50, Identity("DISPLAY\\ACTIVE\\UID")));
        var candidates = CandidateSnapshot(1,
            Candidate(0, 1, 50, true, Identity("DISPLAY\\ACTIVE\\UID")),
            Candidate(2, 2, 99, false, selectedIdentity));
        var after = Snapshot(2,
            ActivePath(1, 50, Identity("DISPLAY\\ACTIVE\\UID")),
            ActivePath(2, 99, selectedIdentity));
        var native = new FakeNativeExtendApplier();
        var coordinator = Coordinator(generation, new QueueSnapshotReader(before, after), candidates, native);

        var result = coordinator.Apply(new DisplayExtendRequest(
            new PersistentDisplaySelector(PnpDeviceInstanceId: selectedIdentity.PnpDeviceInstanceId),
            SnapshotGeneration: 1));

        Assert.AreEqual(DisplayExtendApplyDisposition.AppliedVerified, result.Disposition, result.Detail);
        Assert.AreEqual("DISPLAY_EXTEND_APPLIED_VERIFIED", result.ProductCode);
        Assert.AreEqual(1, native.Calls);
        Assert.IsNotNull(native.LastConnection);
        Assert.AreEqual(2u, native.LastConnection.SourceId);
        Assert.AreEqual(new DisplayPathKey(100, 99), native.LastConnection.TargetKey);
        Assert.AreEqual(2, native.LastConnection.PriorityOrdinal);
        Assert.AreEqual(5, native.LastConnection.OutputTechnology);
        Assert.AreEqual(2L, generation.CurrentGeneration);
    }

    [TestMethod]
    public void NativeValidationFailureDoesNotAdvanceDisplayGeneration()
    {
        var generation = new DisplayGenerationTracker();
        var identity = Identity("DISPLAY\\SELECTED\\UID");
        var before = Snapshot(1);
        var candidates = CandidateSnapshot(1, Candidate(0, 2, 99, false, identity));
        var native = new FakeNativeExtendApplier
        {
            Outcome = new DisplayNativeMutationOutcome(
                DisplayNativeMutationDisposition.ValidationRejected,
                "DISPLAY_EXTEND_OPERATION_REJECTED",
                1610)
        };
        var coordinator = Coordinator(generation, new QueueSnapshotReader(before), candidates, native);

        var result = coordinator.Apply(Request(identity, 1));

        Assert.AreEqual(DisplayExtendApplyDisposition.OperationRejected, result.Disposition);
        Assert.AreEqual(1610, result.NativeErrorCode);
        Assert.AreEqual(1L, generation.CurrentGeneration);
        Assert.AreEqual(1, native.Calls);
    }

    [TestMethod]
    public void SuccessfulNativeApplyWithExtraActiveTargetFailsVerification()
    {
        var generation = new DisplayGenerationTracker();
        var identity = Identity("DISPLAY\\SELECTED\\UID");
        var before = Snapshot(1, ActivePath(1, 50, Identity("DISPLAY\\ACTIVE\\UID")));
        var candidates = CandidateSnapshot(1, Candidate(1, 2, 99, false, identity));
        var after = Snapshot(2,
            ActivePath(1, 50, Identity("DISPLAY\\ACTIVE\\UID")),
            ActivePath(2, 99, identity),
            ActivePath(3, 77, Identity("DISPLAY\\UNEXPECTED\\UID")));
        var coordinator = Coordinator(
            generation,
            new QueueSnapshotReader(before, after),
            candidates,
            new FakeNativeExtendApplier());

        var result = coordinator.Apply(Request(identity, 1));

        Assert.AreEqual(DisplayExtendApplyDisposition.VerificationFailed, result.Disposition);
        Assert.AreEqual("DISPLAY_EXTEND_VERIFICATION_FAILED", result.ProductCode);
    }

    [TestMethod]
    public void SuccessfulNativeApplyOnDifferentSourceFailsVerification()
    {
        var generation = new DisplayGenerationTracker();
        var identity = Identity("DISPLAY\\SELECTED\\UID");
        var before = Snapshot(1);
        var candidates = CandidateSnapshot(1, Candidate(0, 2, 99, false, identity));
        var after = Snapshot(2, ActivePath(3, 99, identity));
        var coordinator = Coordinator(
            generation,
            new QueueSnapshotReader(before, after),
            candidates,
            new FakeNativeExtendApplier());

        var result = coordinator.Apply(Request(identity, 1));

        Assert.AreEqual(DisplayExtendApplyDisposition.VerificationFailed, result.Disposition);
    }

    [TestMethod]
    public void ConcurrentDisplayChangeDuringNativeMutationReturnsStaleAfterReadBack()
    {
        var generation = new DisplayGenerationTracker();
        var identity = Identity("DISPLAY\\SELECTED\\UID");
        var before = Snapshot(1);
        var candidates = CandidateSnapshot(1, Candidate(0, 2, 99, false, identity));
        var after = Snapshot(3, ActivePath(2, 99, identity));
        var native = new FakeNativeExtendApplier
        {
            OnApply = () => generation.Invalidate("WM_DISPLAYCHANGE")
        };
        var coordinator = Coordinator(generation, new QueueSnapshotReader(before, after), candidates, native);

        var result = coordinator.Apply(Request(identity, 1));

        Assert.AreEqual(DisplayExtendApplyDisposition.StaleSnapshot, result.Disposition);
        Assert.AreEqual(3L, generation.CurrentGeneration);
    }

    [TestMethod]
    public void AmbiguousConnectionNeverReachesNativeMutation()
    {
        var generation = new DisplayGenerationTracker();
        var identity = Identity("DISPLAY\\SELECTED\\UID");
        var before = Snapshot(1);
        var candidates = CandidateSnapshot(1,
            Candidate(1, 2, 99, false, identity),
            Candidate(2, 3, 99, false, identity));
        var native = new FakeNativeExtendApplier();
        var coordinator = Coordinator(generation, new QueueSnapshotReader(before), candidates, native);

        var result = coordinator.Apply(Request(identity, 1));

        Assert.AreEqual(DisplayExtendApplyDisposition.ConnectionAmbiguous, result.Disposition);
        Assert.AreEqual(0, native.Calls);
        Assert.AreEqual(1L, generation.CurrentGeneration);
    }

    private static DisplayExtendApplyCoordinator Coordinator(
        IDisplayGenerationTracker generation,
        IDisplaySnapshotReader snapshots,
        DisplayConnectionCandidateSnapshot candidates,
        IDisplayNativeExtendApplier native) => new(
        snapshots,
        new FixedCandidateReader(candidates),
        generation,
        new DisplayExtendCandidateResolver(new PersistentDisplaySelectorResolver()),
        native);

    private static DisplayExtendRequest Request(DisplayTargetIdentityEvidence identity, long generation) => new(
        new PersistentDisplaySelector(PnpDeviceInstanceId: identity.PnpDeviceInstanceId),
        generation);

    private static DisplayTargetIdentityEvidence Identity(
        string pnpId,
        int outputTechnology = 10) => new(
        MonitorDevicePath: $"MONITOR#{pnpId.Length}",
        FriendlyMonitorName: "Monitor",
        EdidManufactureId: 1,
        EdidProductCodeId: 2,
        ConnectorInstance: 1,
        OutputTechnology: outputTechnology,
        AdapterLuidHint: 100,
        PnpDeviceInstanceId: pnpId);

    private static DisplayPathEvidence ActivePath(
        uint sourceId,
        uint targetId,
        DisplayTargetIdentityEvidence identity) => new(
        SourceAdapterLuid: 100,
        SourceId: sourceId,
        TargetKey: new DisplayPathKey(100, targetId),
        Active: true,
        TargetAvailable: true,
        OutputTechnology: identity.OutputTechnology,
        Rotation: 1,
        Scaling: 1,
        RefreshRate: new DisplayRational(60, 1),
        SourceResolution: new DisplayPixelSize(1920, 1080),
        SourcePosition: new DisplayDesktopPoint(0, 0),
        Identity: identity);

    private static DisplayConnectionCandidate Candidate(
        int priority,
        uint sourceId,
        uint targetId,
        bool active,
        DisplayTargetIdentityEvidence identity) => new(
        PriorityOrdinal: priority,
        SourceAdapterLuid: 100,
        SourceId: sourceId,
        TargetKey: new DisplayPathKey(100, targetId),
        Active: active,
        Identity: identity);

    private static DisplaySnapshot Snapshot(long generation, params DisplayPathEvidence[] paths) =>
        new(generation, DateTimeOffset.UtcNow, paths);

    private static DisplayConnectionCandidateSnapshot CandidateSnapshot(
        long generation,
        params DisplayConnectionCandidate[] candidates) =>
        new(generation, DateTimeOffset.UtcNow, candidates);

    private sealed class QueueSnapshotReader(params DisplaySnapshot[] snapshots) : IDisplaySnapshotReader
    {
        private readonly Queue<DisplaySnapshot> _snapshots = new(snapshots);

        public DisplaySnapshot Read()
        {
            Assert.IsTrue(_snapshots.Count > 0, "Unexpected extra display snapshot read.");
            return _snapshots.Dequeue();
        }
    }

    private sealed class FixedCandidateReader(DisplayConnectionCandidateSnapshot snapshot) : IDisplayConnectionCandidateReader
    {
        public DisplayConnectionCandidateSnapshot Read() => snapshot;
    }

    private sealed class FakeNativeExtendApplier : IDisplayNativeExtendApplier
    {
        public int Calls { get; private set; }
        public ResolvedDisplayExtendConnection? LastConnection { get; private set; }
        public Action? OnApply { get; init; }
        public DisplayNativeMutationOutcome Outcome { get; init; } = new(
            DisplayNativeMutationDisposition.Applied,
            "DISPLAY_EXTEND_NATIVE_APPLIED");

        public DisplayNativeMutationOutcome ValidateAndApply(ResolvedDisplayExtendConnection connection)
        {
            Calls++;
            LastConnection = connection;
            OnApply?.Invoke();
            return Outcome;
        }
    }
}
