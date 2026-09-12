using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.Machine;
using SplitOS.RuntimeHost.ModeRuntime;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class DisplayModeSourceVerificationHandlerTests
{
    [TestMethod]
    public async Task VerifiesExactSourceFromTopologyPreStateAfterRollback()
    {
        var transitionId = Guid.NewGuid();
        var baseline = Snapshot(1,
            Path(1, "DISPLAY\\A\\0", 2560, 1440, 60000, 1001),
            Path(3, "DISPLAY\\C\\0", 1920, 1080, 60, 1));
        var topology = TopologyRecord(
            transitionId,
            10,
            baseline,
            Selector("DISPLAY\\B\\0", 2));
        var handler = Handler(Plan(transitionId, topology), new[] { topology }, baseline);

        var result = await handler.VerifySourceAsync(Command(transitionId));

        Assert.AreEqual("VERIFIED", result.Disposition);
        Assert.AreEqual("MODE_SOURCE_DISPLAY_VERIFIED", result.ProductCode);
    }

    [TestMethod]
    public async Task ReconstructsEarliestModesAcrossTargetModeThenExtendChain()
    {
        var transitionId = Guid.NewGuid();
        var aOld = Path(1, "DISPLAY\\A\\0", 1920, 1080, 60000, 1001);
        var aChanged = Path(1, "DISPLAY\\A\\0", 2560, 1440, 144, 1);
        var c = Path(3, "DISPLAY\\C\\0", 1920, 1080, 60, 1);
        var original = Snapshot(1, aOld, c);
        var afterFirstAction = Snapshot(2, aChanged, c);

        var targetMode = TargetModeRecord(transitionId, 10, original, aOld);
        var topology = TopologyRecord(
            transitionId,
            20,
            afterFirstAction,
            Selector("DISPLAY\\B\\0", 2));
        var handler = Handler(
            Plan(transitionId, targetMode, topology),
            new[] { targetMode, topology },
            original);

        var result = await handler.VerifySourceAsync(Command(transitionId));

        Assert.AreEqual("VERIFIED", result.Disposition);
        Assert.AreEqual("MODE_SOURCE_DISPLAY_VERIFIED", result.ProductCode);
    }

    [TestMethod]
    public async Task FreshModeMismatchFailsEvenWhenRollbackJournalSaysRolledBack()
    {
        var transitionId = Guid.NewGuid();
        var baseline = Snapshot(1, Path(1, "DISPLAY\\A\\0", 1920, 1080, 60000, 1001));
        var wrong = Snapshot(1, Path(1, "DISPLAY\\A\\0", 2560, 1440, 144, 1));
        var topology = TopologyRecord(
            transitionId,
            10,
            baseline,
            Selector("DISPLAY\\B\\0", 2));
        var handler = Handler(Plan(transitionId, topology), new[] { topology }, wrong);

        var result = await handler.VerifySourceAsync(Command(transitionId));

        Assert.AreEqual("MISMATCH", result.Disposition);
        Assert.AreEqual("MODE_SOURCE_DISPLAY_MODE_MISMATCH", result.ProductCode);
    }

    [TestMethod]
    public async Task UnknownMutationMustHaveDurableRollbackEvidenceBeforeSourceVerification()
    {
        var transitionId = Guid.NewGuid();
        var baseline = Snapshot(1, Path(1, "DISPLAY\\A\\0"));
        var topology = TopologyRecord(
            transitionId,
            10,
            baseline,
            Selector("DISPLAY\\B\\0", 2),
            PersistedModeActionState.Failed,
            "UNKNOWN",
            null);
        var snapshotReader = new CountingSnapshotReader(baseline);
        var handler = Handler(Plan(transitionId, topology), new[] { topology }, snapshotReader);

        var result = await handler.VerifySourceAsync(Command(transitionId));

        Assert.AreEqual("RECONCILIATION_REQUIRED", result.Disposition);
        Assert.AreEqual("MODE_SOURCE_DISPLAY_ROLLBACK_EVIDENCE_REQUIRED", result.ProductCode);
        Assert.AreEqual(0, snapshotReader.Calls);
    }

    private static DisplayModeSourceVerificationHandler Handler(
        ModeTransitionActionPlan plan,
        IReadOnlyList<PersistedModeActionRecord> records,
        DisplaySnapshot snapshot)
        => Handler(plan, records, new CountingSnapshotReader(snapshot));

    private static DisplayModeSourceVerificationHandler Handler(
        ModeTransitionActionPlan plan,
        IReadOnlyList<PersistedModeActionRecord> records,
        IDisplaySnapshotReader snapshots)
        => new(
            new FakePlanReader(plan),
            new FakeActionReader(records),
            snapshots,
            new FixedGenerationTracker(1),
            new PersistentDisplaySelectorResolver(),
            new FixedControlSessionIdentity("session:test"));

    private static ModeSourceVerificationCommand Command(Guid transitionId)
        => new(
            transitionId,
            Guid.NewGuid(),
            7,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "session:test",
            4);

    private static ModeTransitionActionPlan Plan(
        Guid transitionId,
        params PersistedModeActionRecord[] actions)
        => new(
            transitionId,
            1,
            new string('b', 64),
            actions.Length,
            DateTimeOffset.UtcNow,
            4,
            actions.OrderBy(static action => action.SequenceNo).ToArray());

    private static PersistedModeActionRecord TopologyRecord(
        Guid transitionId,
        int sequence,
        DisplaySnapshot baseline,
        PersistentDisplaySelector addedSelector,
        PersistedModeActionState state = PersistedModeActionState.RolledBack,
        string? applyResult = "APPLIED",
        string? rollbackResult = "ROLLED_BACK")
    {
        var definition = DisplayModeActionContract.CreateTopologyExtendDefinition(Guid.NewGuid(), sequence, addedSelector);
        var preState = DisplayModeActionPreStateContract.CaptureTopology(baseline);
        return Record(
            transitionId,
            definition,
            state,
            DisplayModeActionPreStateContract.SerializeTopology(preState),
            DisplayModeActionPreStateContract.ComputeTopologyDigest(preState),
            applyResult,
            rollbackResult);
    }

    private static PersistedModeActionRecord TargetModeRecord(
        Guid transitionId,
        int sequence,
        DisplaySnapshot baseline,
        DisplayPathEvidence target)
    {
        var desired = new DisplayTargetModeDesiredState(
            DisplayModeActionPreStateContract.SelectorFromIdentity(target.Identity!),
            2560,
            1440,
            144,
            1,
            1);
        var definition = DisplayModeActionContract.CreateTargetModeDefinition(Guid.NewGuid(), sequence, desired);
        var preState = DisplayModeActionPreStateContract.CaptureTargetMode(baseline, target);
        return Record(
            transitionId,
            definition,
            PersistedModeActionState.RolledBack,
            DisplayModeActionPreStateContract.SerializeTargetMode(preState),
            DisplayModeActionPreStateContract.ComputeTargetModeDigest(preState),
            "APPLIED",
            "ROLLED_BACK");
    }

    private static PersistedModeActionRecord Record(
        Guid transitionId,
        PersistedModeActionDefinition definition,
        PersistedModeActionState state,
        string preStateJson,
        string preStateDigest,
        string? applyResult,
        string? rollbackResult)
        => new(
            definition.ActionId,
            transitionId,
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
            state,
            preStateJson,
            preStateDigest,
            applyResult,
            applyResult == "APPLIED" ? "VERIFIED" : null,
            rollbackResult,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            5);

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
            10,
            targetId,
            new DisplayPathKey(20, targetId),
            true,
            true,
            5,
            1,
            128,
            new DisplayRational(refreshNumerator, refreshDenominator),
            new DisplayPixelSize(width, height),
            new DisplayDesktopPoint((int)targetId * 100, 0),
            true,
            false,
            Identity(pnp, targetId));

    private static DisplayTargetIdentityEvidence Identity(string pnp, uint connector)
        => new(
            $"\\\\?\\DISPLAY#{connector}",
            $"Panel {connector}",
            1234,
            (ushort)(5000 + connector),
            connector,
            5,
            20,
            pnp);

    private static PersistentDisplaySelector Selector(string pnp, uint connector)
        => DisplayModeActionPreStateContract.SelectorFromIdentity(Identity(pnp, connector));

    private sealed class FakePlanReader(ModeTransitionActionPlan plan) : IModeActionPlanReader
    {
        public Task<ModeTransitionActionPlan?> GetAsync(Guid transitionId, CancellationToken cancellationToken = default)
            => Task.FromResult<ModeTransitionActionPlan?>(plan);
    }

    private sealed class FakeActionReader(IReadOnlyList<PersistedModeActionRecord> records) : IModeActionRecordReader
    {
        private readonly Dictionary<Guid, PersistedModeActionRecord> _records = records.ToDictionary(static record => record.ActionId);

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<PersistedModeActionRecord?> GetAsync(Guid actionId, CancellationToken cancellationToken = default)
            => Task.FromResult(_records.TryGetValue(actionId, out var record) ? record : null);
    }

    private sealed class CountingSnapshotReader(DisplaySnapshot snapshot) : IDisplaySnapshotReader
    {
        public int Calls { get; private set; }
        public DisplaySnapshot Read()
        {
            Calls++;
            return snapshot;
        }
    }

    private sealed class FixedGenerationTracker(long generation) : IDisplayGenerationTracker
    {
        public long CurrentGeneration => generation;
        public long Invalidate(string reason) => throw new InvalidOperationException("Source verification must not mutate display generation.");
    }

    private sealed class FixedControlSessionIdentity(string key) : IControlSessionIdentity
    {
        public string GetCurrentKey() => key;
    }
}
