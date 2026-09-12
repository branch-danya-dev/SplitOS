using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.Machine;
using SplitOS.RuntimeHost.ModeRuntime;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class DisplayModeActionRollbackHandlerTests
{
    [TestMethod]
    public async Task TopologyRollbackFreshResolvesAndRestoresDurablePhysicalBaseline()
    {
        var tracker = new DisplayGenerationTracker();
        var baseline = Snapshot(1, Path(1, "DISPLAY\\A\\0"));
        var extended = Snapshot(1, Path(1, "DISPLAY\\A\\0"), Path(2, "DISPLAY\\B\\0"));
        var restored = Snapshot(2, Path(1, "DISPLAY\\A\\0"));
        var reader = new QueueSnapshotReader(extended, restored);
        var native = new FakeTopologyRollbackApplier(new(
            DisplayNativeMutationDisposition.Applied,
            "DISPLAY_ROLLBACK_NATIVE_APPLIED"));
        var action = TopologyAction(baseline, Selector("DISPLAY\\B\\0", 2));
        var handler = CreateHandler(reader, tracker, native);

        var outcome = await handler.RollbackAsync(Command(action), action);

        Assert.AreEqual("VERIFIED", outcome.Disposition);
        Assert.AreEqual("MODE_DISPLAY_TOPOLOGY_ROLLBACK_VERIFIED", outcome.ProductCode);
        Assert.AreEqual(1, native.Calls);
        Assert.IsNotNull(native.LastRequest);
        Assert.AreEqual(1, native.LastRequest!.BaselineActivePaths.Count);
        Assert.AreEqual(2u, native.LastRequest.ExtendedTarget.TargetKey.TargetId);
        Assert.AreEqual(2L, tracker.CurrentGeneration);
    }

    [TestMethod]
    public async Task TopologyRollbackRefusesUnrelatedPhysicalTopologyDriftBeforeNativeMutation()
    {
        var tracker = new DisplayGenerationTracker();
        var baseline = Snapshot(1, Path(1, "DISPLAY\\A\\0"));
        var drifted = Snapshot(1,
            Path(1, "DISPLAY\\A\\0"),
            Path(2, "DISPLAY\\B\\0"),
            Path(3, "DISPLAY\\C\\0"));
        var reader = new QueueSnapshotReader(drifted);
        var native = new FakeTopologyRollbackApplier(new(
            DisplayNativeMutationDisposition.Applied,
            "MUST_NOT_RUN"));
        var action = TopologyAction(baseline, Selector("DISPLAY\\B\\0", 2));
        var handler = CreateHandler(reader, tracker, native);

        var outcome = await handler.RollbackAsync(Command(action), action);

        Assert.AreEqual("RECONCILIATION_REQUIRED", outcome.Disposition);
        Assert.AreEqual("MODE_DISPLAY_ROLLBACK_TOPOLOGY_DRIFT", outcome.ProductCode);
        Assert.AreEqual(0, native.Calls);
        Assert.AreEqual(1L, tracker.CurrentGeneration);
    }

    [TestMethod]
    public async Task TargetModeRollbackRestoresExactRationalModeWithoutChangingPhysicalTopology()
    {
        var tracker = new DisplayGenerationTracker();
        var oldPath = Path(1, "DISPLAY\\A\\0", 1920, 1080, 60000, 1001);
        var changedPath = Path(1, "DISPLAY\\A\\0", 2560, 1440, 144, 1);
        var oldSnapshot = Snapshot(1, oldPath);
        var changedSnapshot = Snapshot(1, changedPath);
        var restoredSnapshot = Snapshot(2, oldPath);
        var handlerReader = new QueueSnapshotReader(changedSnapshot, restoredSnapshot);
        var targetReader = new QueueSnapshotReader(changedSnapshot, restoredSnapshot);
        var nativeTarget = new FakeTargetApplier(new(
            DisplayNativeMutationDisposition.Applied,
            "DISPLAY_NATIVE_APPLIED"));
        var targetCoordinator = new DisplayTargetApplyCoordinator(targetReader, tracker, nativeTarget);
        var topologyNative = new FakeTopologyRollbackApplier(new(
            DisplayNativeMutationDisposition.Applied,
            "MUST_NOT_RUN"));
        var action = TargetModeAction(oldSnapshot, oldPath);
        var handler = new DisplayModeActionRollbackHandler(
            handlerReader,
            tracker,
            new PersistentDisplaySelectorResolver(),
            topologyNative,
            targetCoordinator,
            new FixedControlSessionIdentity("session:test"));

        var outcome = await handler.RollbackAsync(Command(action), action);

        Assert.AreEqual("VERIFIED", outcome.Disposition);
        Assert.AreEqual("MODE_DISPLAY_MODE_ROLLBACK_VERIFIED", outcome.ProductCode);
        Assert.AreEqual(1, nativeTarget.Calls);
        Assert.IsNotNull(nativeTarget.LastTarget);
        Assert.AreEqual(1920u, nativeTarget.LastTarget!.Resolution.Width);
        Assert.AreEqual(1080u, nativeTarget.LastTarget.Resolution.Height);
        Assert.AreEqual(60000u, nativeTarget.LastTarget.RefreshRate.Numerator);
        Assert.AreEqual(1001u, nativeTarget.LastTarget.RefreshRate.Denominator);
        Assert.AreEqual(DisplayTopologyIntent.PreserveActiveTopology, nativeTarget.LastTarget.TopologyIntent);
        Assert.AreEqual(0, topologyNative.Calls);
    }

    private static DisplayModeActionRollbackHandler CreateHandler(
        IDisplaySnapshotReader reader,
        IDisplayGenerationTracker tracker,
        IDisplayNativeTopologyRollbackApplier native)
    {
        var unusedTarget = new DisplayTargetApplyCoordinator(
            new QueueSnapshotReader(Snapshot(1, Path(99, "DISPLAY\\UNUSED\\0"))),
            tracker,
            new FakeTargetApplier(new(DisplayNativeMutationDisposition.ApplyFailed, "MUST_NOT_RUN")));
        return new DisplayModeActionRollbackHandler(
            reader,
            tracker,
            new PersistentDisplaySelectorResolver(),
            native,
            unusedTarget,
            new FixedControlSessionIdentity("session:test"));
    }

    private static PersistedModeActionRecord TopologyAction(
        DisplaySnapshot baseline,
        PersistentDisplaySelector selector)
    {
        var definition = DisplayModeActionContract.CreateTopologyExtendDefinition(Guid.NewGuid(), 10, selector);
        var preState = DisplayModeActionPreStateContract.CaptureTopology(baseline);
        return ToRollingBack(
            definition,
            DisplayModeActionPreStateContract.SerializeTopology(preState),
            DisplayModeActionPreStateContract.ComputeTopologyDigest(preState));
    }

    private static PersistedModeActionRecord TargetModeAction(
        DisplaySnapshot baseline,
        DisplayPathEvidence path)
    {
        var desired = new DisplayTargetModeDesiredState(
            DisplayModeActionPreStateContract.SelectorFromIdentity(path.Identity!),
            2560,
            1440,
            144,
            1,
            1);
        var definition = DisplayModeActionContract.CreateTargetModeDefinition(Guid.NewGuid(), 20, desired);
        var preState = DisplayModeActionPreStateContract.CaptureTargetMode(baseline, path);
        return ToRollingBack(
            definition,
            DisplayModeActionPreStateContract.SerializeTargetMode(preState),
            DisplayModeActionPreStateContract.ComputeTargetModeDigest(preState));
    }

    private static PersistedModeActionRecord ToRollingBack(
        PersistedModeActionDefinition definition,
        string preStateJson,
        string preStateDigest)
        => new(
            definition.ActionId,
            Guid.NewGuid(),
            definition.SequenceNo,
            definition.OwningModule,
            definition.ActionType,
            definition.TargetRef,
            definition.DesiredSchemaVersion,
            definition.DesiredStateJson,
            definition.DesiredStateDigest,
            definition.Mandatory,
            definition.RollbackClass,
            definition.VerificationClass,
            PersistedModeActionState.RollingBack,
            preStateJson,
            preStateDigest,
            "APPLIED",
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            DateTimeOffset.UtcNow,
            4);

    private static ModeActionRollbackCommand Command(PersistedModeActionRecord action)
        => new(
            action.TransitionId,
            action.ActionId,
            action.Revision,
            Guid.NewGuid(),
            7,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "session:test");

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
            SourceAdapterLuid: 10,
            SourceId: targetId,
            TargetKey: new DisplayPathKey(20, targetId),
            Active: true,
            TargetAvailable: true,
            OutputTechnology: 5,
            Rotation: 1,
            Scaling: 128,
            RefreshRate: new DisplayRational(refreshNumerator, refreshDenominator),
            SourceResolution: new DisplayPixelSize(width, height),
            SourcePosition: new DisplayDesktopPoint((int)targetId * 100, 0),
            SupportsVirtualMode: true,
            BoostRefreshRate: false,
            Identity: Identity(pnp, targetId));

    private static DisplayTargetIdentityEvidence Identity(string pnp, uint connector)
        => new(
            MonitorDevicePath: $"\\\\?\\DISPLAY#{connector}",
            FriendlyMonitorName: $"Panel {connector}",
            EdidManufactureId: 1234,
            EdidProductCodeId: (ushort)(5000 + connector),
            ConnectorInstance: connector,
            OutputTechnology: 5,
            AdapterLuidHint: 20,
            PnpDeviceInstanceId: pnp);

    private static PersistentDisplaySelector Selector(string pnp, uint connector)
        => DisplayModeActionPreStateContract.SelectorFromIdentity(Identity(pnp, connector));

    private sealed class QueueSnapshotReader(params DisplaySnapshot[] snapshots) : IDisplaySnapshotReader
    {
        private readonly Queue<DisplaySnapshot> _snapshots = new(snapshots);

        public DisplaySnapshot Read()
        {
            if (_snapshots.Count == 0) throw new InvalidOperationException("No fake display snapshot remains.");
            return _snapshots.Dequeue();
        }
    }

    private sealed class FakeTopologyRollbackApplier(DisplayNativeMutationOutcome outcome)
        : IDisplayNativeTopologyRollbackApplier
    {
        public int Calls { get; private set; }
        public ResolvedDisplayTopologyRollback? LastRequest { get; private set; }

        public DisplayNativeMutationOutcome ValidateAndApply(ResolvedDisplayTopologyRollback rollback)
        {
            Calls++;
            LastRequest = rollback;
            return outcome;
        }
    }

    private sealed class FakeTargetApplier(DisplayNativeMutationOutcome outcome) : IDisplayNativeTargetApplier
    {
        public int Calls { get; private set; }
        public ResolvedDisplayTarget? LastTarget { get; private set; }

        public DisplayNativeMutationOutcome ValidateAndApply(ResolvedDisplayTarget target)
        {
            Calls++;
            LastTarget = target;
            return outcome;
        }
    }

    private sealed class FixedControlSessionIdentity(string key) : IControlSessionIdentity
    {
        public string GetCurrentKey() => key;
    }
}
