using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class DisplayExtendCandidateResolverTests
{
    private readonly DisplayExtendCandidateResolver _resolver = new(new PersistentDisplaySelectorResolver());

    [TestMethod]
    public void RepeatedTargetIdentityAcrossConnectionPathsIsResolvedAsOnePhysicalMonitor()
    {
        var selectedIdentity = Identity("DISPLAY\\SELECTED\\UID");
        var active = ActiveSnapshot(
            generation: 7,
            ActivePath(sourceId: 1, targetId: 50, Identity("DISPLAY\\ACTIVE\\UID")));
        var all = CandidateSnapshot(
            generation: 7,
            Candidate(priority: 0, sourceId: 1, targetId: 50, active: true, Identity("DISPLAY\\ACTIVE\\UID")),
            Candidate(priority: 1, sourceId: 1, targetId: 99, active: false, selectedIdentity),
            Candidate(priority: 2, sourceId: 2, targetId: 99, active: false, selectedIdentity));

        var result = _resolver.Resolve(
            new PersistentDisplaySelector(PnpDeviceInstanceId: "display\\selected\\uid"),
            active,
            all);

        Assert.AreEqual(DisplayExtendCandidateDisposition.Resolved, result.Disposition);
        Assert.AreEqual(new DisplayPathKey(100, 99), result.TargetKey);
        Assert.IsNotNull(result.Candidate);
        Assert.AreEqual(2u, result.Candidate.SourceId);
        Assert.AreEqual(2, result.Candidate.PriorityOrdinal);
    }

    [TestMethod]
    public void MultipleFreeConnectionsRemainAmbiguousEvenWhenWindowsProvidesPriorityOrder()
    {
        var identity = Identity("DISPLAY\\SELECTED\\UID");
        var result = _resolver.Resolve(
            new PersistentDisplaySelector(PnpDeviceInstanceId: identity.PnpDeviceInstanceId),
            ActiveSnapshot(generation: 3),
            CandidateSnapshot(
                generation: 3,
                Candidate(4, 2, 99, false, identity),
                Candidate(8, 3, 99, false, identity)));

        Assert.AreEqual(DisplayExtendCandidateDisposition.CandidateAmbiguous, result.Disposition);
        Assert.AreEqual("DISPLAY_EXTEND_CONNECTION_AMBIGUOUS", result.ProductCode);
        Assert.IsNull(result.Candidate);
    }

    [TestMethod]
    public void DuplicatePhysicalIdentityAcrossDifferentTargetsIsAmbiguousBeforeConnectionSelection()
    {
        var identity = Identity("DISPLAY\\DUPLICATE\\UID");
        var result = _resolver.Resolve(
            new PersistentDisplaySelector(PnpDeviceInstanceId: identity.PnpDeviceInstanceId),
            ActiveSnapshot(generation: 4),
            CandidateSnapshot(
                generation: 4,
                Candidate(1, 2, 80, false, identity),
                Candidate(2, 3, 81, false, identity)));

        Assert.AreEqual(DisplayExtendCandidateDisposition.TargetAmbiguous, result.Disposition);
        Assert.AreEqual("DISPLAY_SELECTOR_PNP_INSTANCE_AMBIGUOUS", result.ProductCode);
    }

    [TestMethod]
    public void AlreadyActivePhysicalTargetIsNotExtendedAgain()
    {
        var identity = Identity("DISPLAY\\ACTIVE\\UID");
        var result = _resolver.Resolve(
            new PersistentDisplaySelector(PnpDeviceInstanceId: identity.PnpDeviceInstanceId),
            ActiveSnapshot(generation: 5, ActivePath(1, 90, identity)),
            CandidateSnapshot(
                generation: 5,
                Candidate(0, 1, 90, true, identity),
                Candidate(1, 2, 90, false, identity)));

        Assert.AreEqual(DisplayExtendCandidateDisposition.TargetAlreadyActive, result.Disposition);
        Assert.AreEqual("DISPLAY_EXTEND_TARGET_ALREADY_ACTIVE", result.ProductCode);
    }

    [TestMethod]
    public void DifferentSnapshotGenerationsFailBeforeSelectorOrConnectionEvaluation()
    {
        var identity = Identity("DISPLAY\\SELECTED\\UID");
        var result = _resolver.Resolve(
            new PersistentDisplaySelector(PnpDeviceInstanceId: identity.PnpDeviceInstanceId),
            ActiveSnapshot(generation: 10),
            CandidateSnapshot(generation: 11, Candidate(0, 2, 99, false, identity)));

        Assert.AreEqual(DisplayExtendCandidateDisposition.StaleTopology, result.Disposition);
        Assert.AreEqual("DISPLAY_EXTEND_TOPOLOGY_CHANGED", result.ProductCode);
        Assert.IsNull(result.Candidate);
    }

    [TestMethod]
    public void ReusingOnlyActiveSourceLeavesNoEligibleConnection()
    {
        var identity = Identity("DISPLAY\\SELECTED\\UID");
        var result = _resolver.Resolve(
            new PersistentDisplaySelector(PnpDeviceInstanceId: identity.PnpDeviceInstanceId),
            ActiveSnapshot(generation: 12, ActivePath(1, 50, Identity("DISPLAY\\ACTIVE\\UID"))),
            CandidateSnapshot(generation: 12, Candidate(1, 1, 99, false, identity)));

        Assert.AreEqual(DisplayExtendCandidateDisposition.CandidateNotFound, result.Disposition);
        Assert.AreEqual("DISPLAY_EXTEND_CONNECTION_NOT_FOUND", result.ProductCode);
    }

    private static DisplayTargetIdentityEvidence Identity(string pnpId) => new(
        MonitorDevicePath: $"MONITOR#{pnpId.GetHashCode():X}",
        FriendlyMonitorName: "Monitor",
        EdidManufactureId: 1,
        EdidProductCodeId: 2,
        ConnectorInstance: 1,
        OutputTechnology: 5,
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

    private static DisplaySnapshot ActiveSnapshot(long generation, params DisplayPathEvidence[] paths) =>
        new(generation, DateTimeOffset.UtcNow, paths);

    private static DisplayConnectionCandidateSnapshot CandidateSnapshot(
        long generation,
        params DisplayConnectionCandidate[] candidates) =>
        new(generation, DateTimeOffset.UtcNow, candidates);
}
