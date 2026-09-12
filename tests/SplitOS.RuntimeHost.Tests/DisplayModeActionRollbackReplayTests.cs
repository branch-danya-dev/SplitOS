using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.Machine;
using SplitOS.RuntimeHost.ModeRuntime;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class DisplayModeActionRollbackReplayTests
{
    [TestMethod]
    public async Task TopologyRollbackAcceptsAlreadyRestoredPhysicalBaselineWithoutNativeMutation()
    {
        var tracker = new DisplayGenerationTracker();
        var baseline = Snapshot(1, Path(1, "DISPLAY\\A\\0"));
        var preState = DisplayModeActionPreStateContract.CaptureTopology(baseline);
        var definition = DisplayModeActionContract.CreateTopologyExtendDefinition(
            Guid.NewGuid(), 10, Selector("DISPLAY\\B\\0", 2));
        var action = RollingBack(
            definition,
            DisplayModeActionPreStateContract.SerializeTopology(preState),
            DisplayModeActionPreStateContract.ComputeTopologyDigest(preState));
        var native = new CountingTopologyApplier();
        var handler = Handler(new QueueSnapshotReader(baseline), tracker, native);

        var outcome = await handler.RollbackAsync(Command(action), action);

        Assert.AreEqual("VERIFIED", outcome.Disposition);
        Assert.AreEqual("MODE_DISPLAY_TOPOLOGY_ROLLBACK_ALREADY_VERIFIED", outcome.ProductCode);
        Assert.AreEqual(0, native.Calls);
        Assert.AreEqual(1L, tracker.CurrentGeneration);
    }

    [TestMethod]
    public async Task TargetModeRollbackAcceptsAlreadyRestoredExactRationalModeWithoutNativeMutation()
    {
        var tracker = new DisplayGenerationTracker();
        var oldPath = Path(1, "DISPLAY\\A\\0", 1920, 1080, 60000, 1001);
        var baseline = Snapshot(1, oldPath);
        var preState = DisplayModeActionPreStateContract.CaptureTargetMode(baseline, oldPath);
        var desired = new DisplayTargetModeDesiredState(
            Selector("DISPLAY\\A\\0", 1), 2560, 1440, 144, 1, 1);
        var definition = DisplayModeActionContract.CreateTargetModeDefinition(Guid.NewGuid(), 20, desired);
        var action = RollingBack(
            definition,
            DisplayModeActionPreStateContract.SerializeTargetMode(preState),
            DisplayModeActionPreStateContract.ComputeTargetModeDigest(preState));
        var native = new CountingTopologyApplier();
        var targetNative = new CountingTargetApplier();
        var targetCoordinator = new DisplayTargetApplyCoordinator(
            new QueueSnapshotReader(baseline), tracker, targetNative);
        var handler = new DisplayModeActionRollbackHandler(
            new QueueSnapshotReader(baseline), tracker,
            new PersistentDisplaySelectorResolver(), native, targetCoordinator,
            new FixedControlSessionIdentity("session:test"));

        var outcome = await handler.RollbackAsync(Command(action), action);

        Assert.AreEqual("VERIFIED", outcome.Disposition);
        Assert.AreEqual("MODE_DISPLAY_MODE_ROLLBACK_ALREADY_VERIFIED", outcome.ProductCode);
        Assert.AreEqual(0, native.Calls);
        Assert.AreEqual(0, targetNative.Calls);
        Assert.AreEqual(1L, tracker.CurrentGeneration);
    }

    private static DisplayModeActionRollbackHandler Handler(
        IDisplaySnapshotReader reader,
        IDisplayGenerationTracker tracker,
        IDisplayNativeTopologyRollbackApplier topologyNative)
        => new(
            reader,
            tracker,
            new PersistentDisplaySelectorResolver(),
            topologyNative,
            new DisplayTargetApplyCoordinator(
                new QueueSnapshotReader(Snapshot(1, Path(99, "DISPLAY\\UNUSED\\0"))),
                tracker,
                new CountingTargetApplier()),
            new FixedControlSessionIdentity("session:test"));

    private static PersistedModeActionRecord RollingBack(
        PersistedModeActionDefinition definition,
        string preStateJson,
        string preStateDigest)
        => new(
            definition.ActionId, Guid.NewGuid(), definition.SequenceNo,
            definition.OwningModule, definition.ActionType, definition.TargetRef,
            definition.DesiredSchemaVersion, definition.DesiredStateJson, definition.DesiredStateDigest,
            definition.Mandatory, definition.RollbackClass, definition.VerificationClass,
            PersistedModeActionState.RollingBack,
            preStateJson, preStateDigest, "APPLIED", null, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow, 4);

    private static ModeActionRollbackCommand Command(PersistedModeActionRecord action)
        => new(action.TransitionId, action.ActionId, action.Revision,
            Guid.NewGuid(), 7, Guid.NewGuid(), Guid.NewGuid(), "session:test");

    private static DisplaySnapshot Snapshot(long generation, params DisplayPathEvidence[] paths)
        => new(generation, DateTimeOffset.UtcNow, paths);

    private static DisplayPathEvidence Path(
        uint targetId,
        string pnp,
        uint width = 1920,
        uint height = 1080,
        uint refreshNumerator = 60,
        uint refreshDenominator = 1)
        => new(
            10, targetId, new DisplayPathKey(20, targetId),
            true, true, 5, 1, 128,
            new DisplayRational(refreshNumerator, refreshDenominator),
            new DisplayPixelSize(width, height),
            new DisplayDesktopPoint((int)targetId * 100, 0),
            true, false, Identity(pnp, targetId));

    private static DisplayTargetIdentityEvidence Identity(string pnp, uint connector)
        => new(
            $"\\\\?\\DISPLAY#{connector}", $"Panel {connector}",
            1234, (ushort)(5000 + connector), connector, 5, 20, pnp);

    private static PersistentDisplaySelector Selector(string pnp, uint connector)
        => DisplayModeActionPreStateContract.SelectorFromIdentity(Identity(pnp, connector));

    private sealed class QueueSnapshotReader(params DisplaySnapshot[] snapshots) : IDisplaySnapshotReader
    {
        private readonly Queue<DisplaySnapshot> _queue = new(snapshots);
        public DisplaySnapshot Read() => _queue.Count > 0
            ? _queue.Dequeue()
            : throw new InvalidOperationException("No fake display snapshot remains.");
    }

    private sealed class CountingTopologyApplier : IDisplayNativeTopologyRollbackApplier
    {
        public int Calls { get; private set; }
        public DisplayNativeMutationOutcome ValidateAndApply(ResolvedDisplayTopologyRollback rollback)
        {
            Calls++;
            return new(DisplayNativeMutationDisposition.Applied, "MUST_NOT_RUN");
        }
    }

    private sealed class CountingTargetApplier : IDisplayNativeTargetApplier
    {
        public int Calls { get; private set; }
        public DisplayNativeMutationOutcome ValidateAndApply(ResolvedDisplayTarget target)
        {
            Calls++;
            return new(DisplayNativeMutationDisposition.Applied, "MUST_NOT_RUN");
        }
    }

    private sealed class FixedControlSessionIdentity(string key) : IControlSessionIdentity
    {
        public string GetCurrentKey() => key;
    }
}
