using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.Machine;
using SplitOS.RuntimeHost.ModeRuntime;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class PowerModeActionRecoveryTests
{
    private static readonly Guid Balanced = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid Performance = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    private const string Session = "session:power-recovery";

    [TestMethod]
    public async Task RollbackAlreadyAtDurableSourceIsVerifiedWithoutMutation()
    {
        var action = Action(PersistedModeActionState.RollingBack, Balanced);
        var query = new SequenceQuery(new[] { Balanced });
        var setter = new RecordingSetter();
        var handler = new PowerModeActionRollbackHandler(query, setter, new FixedIdentity(Session));

        var result = await handler.RollbackAsync(Command(action), action);

        Assert.AreEqual("VERIFIED", result.Disposition);
        Assert.AreEqual("MODE_POWER_ROLLBACK_ALREADY_VERIFIED", result.ProductCode);
        Assert.AreEqual(1, query.Calls);
        Assert.AreEqual(0, setter.Calls);
    }

    [TestMethod]
    public async Task RollbackRestoresExactDurableSourceAndRequiresFreshReadBack()
    {
        var action = Action(PersistedModeActionState.RollingBack, Balanced);
        var query = new SequenceQuery(new[] { Performance, Balanced });
        var setter = new RecordingSetter();
        var handler = new PowerModeActionRollbackHandler(query, setter, new FixedIdentity(Session));

        var result = await handler.RollbackAsync(Command(action), action);

        Assert.AreEqual("VERIFIED", result.Disposition);
        Assert.AreEqual("MODE_POWER_ROLLBACK_VERIFIED", result.ProductCode);
        Assert.AreEqual(2, query.Calls);
        Assert.AreEqual(1, setter.Calls);
        Assert.AreEqual(Balanced, setter.LastSchemeId);
    }

    [TestMethod]
    public async Task RollbackTrustsExactReadBackTruthEvenIfSetReturnedError()
    {
        var action = Action(PersistedModeActionState.RollingBack, Balanced);
        var query = new SequenceQuery(new[] { Performance, Balanced });
        var setter = new RecordingSetter(errorCode: 87);
        var handler = new PowerModeActionRollbackHandler(query, setter, new FixedIdentity(Session));

        var result = await handler.RollbackAsync(Command(action), action);

        Assert.AreEqual("VERIFIED", result.Disposition);
        Assert.AreEqual("MODE_POWER_ROLLBACK_VERIFIED_AFTER_SET_REJECTED", result.ProductCode);
        Assert.AreEqual(2, query.Calls);
        Assert.AreEqual(1, setter.Calls);
    }

    [TestMethod]
    public async Task RollbackMismatchRequiresReconciliation()
    {
        var action = Action(PersistedModeActionState.RollingBack, Balanced);
        var query = new SequenceQuery(new[] { Performance, Performance });
        var setter = new RecordingSetter();
        var handler = new PowerModeActionRollbackHandler(query, setter, new FixedIdentity(Session));

        var result = await handler.RollbackAsync(Command(action), action);

        Assert.AreEqual("RECONCILIATION_REQUIRED", result.Disposition);
        Assert.AreEqual("MODE_POWER_ROLLBACK_MISMATCH", result.ProductCode);
        Assert.AreEqual(2, query.Calls);
        Assert.AreEqual(1, setter.Calls);
    }

    [TestMethod]
    public async Task RollbackRejectsStaleControlAuthorityBeforeMutation()
    {
        var action = Action(PersistedModeActionState.RollingBack, Balanced);
        var query = new SequenceQuery(Array.Empty<Guid>());
        var setter = new RecordingSetter();
        var handler = new PowerModeActionRollbackHandler(query, setter, new FixedIdentity("other-session"));

        var result = await handler.RollbackAsync(Command(action), action);

        Assert.AreEqual("RECONCILIATION_REQUIRED", result.Disposition);
        Assert.AreEqual("MODE_POWER_CONTROL_CONTEXT_STALE", result.ProductCode);
        Assert.AreEqual(0, query.Calls);
        Assert.AreEqual(0, setter.Calls);
    }

    [TestMethod]
    public async Task SourceVerificationIndependentlyProvesDurableSourceGuid()
    {
        var transitionId = Guid.NewGuid();
        var action = Action(PersistedModeActionState.RolledBack, Balanced, transitionId: transitionId, rollbackResult: "ROLLED_BACK");
        var plan = Plan(transitionId, action);
        var handler = new PowerModeSourceVerificationHandler(
            new FakePlanReader(plan),
            new FakeActionReader(action),
            new SequenceQuery(new[] { Balanced }),
            new FixedIdentity(Session));

        var result = await handler.VerifySourceAsync(SourceCommand(transitionId));

        Assert.AreEqual("VERIFIED", result.Disposition);
        Assert.AreEqual("MODE_SOURCE_POWER_VERIFIED", result.ProductCode);
    }

    [TestMethod]
    public async Task SourceVerificationReportsMismatchInsteadOfTrustingRollbackCode()
    {
        var transitionId = Guid.NewGuid();
        var action = Action(PersistedModeActionState.RolledBack, Balanced, transitionId: transitionId, rollbackResult: "ROLLED_BACK");
        var handler = new PowerModeSourceVerificationHandler(
            new FakePlanReader(Plan(transitionId, action)),
            new FakeActionReader(action),
            new SequenceQuery(new[] { Performance }),
            new FixedIdentity(Session));

        var result = await handler.VerifySourceAsync(SourceCommand(transitionId));

        Assert.AreEqual("MISMATCH", result.Disposition);
        Assert.AreEqual("MODE_SOURCE_POWER_SCHEME_MISMATCH", result.ProductCode);
    }

    [TestMethod]
    public async Task SourceVerificationRequiresDurableRollbackEvidenceForAppliedMutation()
    {
        var transitionId = Guid.NewGuid();
        var action = Action(PersistedModeActionState.Applied, Balanced, transitionId: transitionId);
        var query = new SequenceQuery(Array.Empty<Guid>());
        var handler = new PowerModeSourceVerificationHandler(
            new FakePlanReader(Plan(transitionId, action)),
            new FakeActionReader(action),
            query,
            new FixedIdentity(Session));

        var result = await handler.VerifySourceAsync(SourceCommand(transitionId));

        Assert.AreEqual("RECONCILIATION_REQUIRED", result.Disposition);
        Assert.AreEqual("MODE_SOURCE_POWER_ROLLBACK_EVIDENCE_REQUIRED", result.ProductCode);
        Assert.AreEqual(0, query.Calls);
    }

    [TestMethod]
    public async Task SourceVerificationTreatsPlanWithNoPowerMutationBoundaryAsVerifiedNoMutation()
    {
        var transitionId = Guid.NewGuid();
        var action = Action(
            PersistedModeActionState.Planned,
            preState: null,
            transitionId: transitionId,
            applyResult: null,
            verifyResult: null,
            rollbackResult: null);
        var query = new SequenceQuery(Array.Empty<Guid>());
        var handler = new PowerModeSourceVerificationHandler(
            new FakePlanReader(Plan(transitionId, action)),
            new FakeActionReader(action),
            query,
            new FixedIdentity(Session));

        var result = await handler.VerifySourceAsync(SourceCommand(transitionId));

        Assert.AreEqual("VERIFIED", result.Disposition);
        Assert.AreEqual("MODE_SOURCE_POWER_NO_MUTATION", result.ProductCode);
        Assert.AreEqual(0, query.Calls);
    }

    private static ModeActionRollbackCommand Command(PersistedModeActionRecord action)
        => new(
            action.TransitionId,
            action.ActionId,
            action.Revision,
            Guid.NewGuid(),
            17,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Session);

    private static ModeSourceVerificationCommand SourceCommand(Guid transitionId)
        => new(
            transitionId,
            Guid.NewGuid(),
            17,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Session,
            7);

    private static ModeTransitionActionPlan Plan(Guid transitionId, params PersistedModeActionRecord[] actions)
        => new(
            transitionId,
            1,
            new string('b', 64),
            actions.Length,
            DateTimeOffset.UtcNow,
            7,
            actions);

    private static PersistedModeActionRecord Action(
        PersistedModeActionState state,
        Guid? preState,
        Guid? transitionId = null,
        string? applyResult = "APPLIED",
        string? verifyResult = "VERIFIED",
        string? rollbackResult = null)
    {
        var desired = new PowerPolicyDesiredState("GAME_PERFORMANCE");
        var desiredJson = PowerModeActionContract.SerializeDesired(desired);
        string? preJson = null;
        string? preDigest = null;
        if (preState.HasValue)
        {
            var evidence = PowerModeActionContract.CapturePreState(preState.Value);
            preJson = PowerModeActionContract.SerializePreState(evidence);
            preDigest = PowerModeActionContract.ComputePreStateDigest(evidence);
        }

        return new PersistedModeActionRecord(
            Guid.NewGuid(),
            transitionId ?? Guid.NewGuid(),
            1,
            PowerModeActionContract.OwningModule,
            PowerModeActionContract.ActionType,
            PowerModeActionContract.TargetRef,
            PowerModeActionContract.DesiredSchemaVersion,
            desiredJson,
            PowerModeActionContract.ComputeDesiredDigest(desired),
            true,
            PowerModeActionContract.RollbackClass,
            PowerModeActionContract.VerificationClass,
            state,
            preJson,
            preDigest,
            applyResult,
            verifyResult,
            rollbackResult,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            5);
    }

    private sealed class SequenceQuery(IEnumerable<Guid> values) : IPowerSchemeQuery
    {
        private readonly Queue<Guid> _values = new(values);
        public int Calls { get; private set; }

        public Guid QueryActiveScheme()
        {
            Calls++;
            if (_values.Count == 0)
                throw new AssertFailedException("Unexpected power scheme query.");
            return _values.Dequeue();
        }
    }

    private sealed class RecordingSetter(int errorCode = 0) : IPowerSchemeSetter
    {
        public int Calls { get; private set; }
        public Guid? LastSchemeId { get; private set; }

        public PowerSetSchemeAttempt SetActiveScheme(Guid schemeId)
        {
            Calls++;
            LastSchemeId = schemeId;
            return new(errorCode);
        }
    }

    private sealed class FixedIdentity(string value) : IControlSessionIdentity
    {
        public string GetCurrentKey() => value;
    }

    private sealed class FakePlanReader(ModeTransitionActionPlan plan) : IModeActionPlanReader
    {
        public Task<ModeTransitionActionPlan?> GetAsync(Guid transitionId, CancellationToken cancellationToken = default)
            => Task.FromResult<ModeTransitionActionPlan?>(plan);
    }

    private sealed class FakeActionReader(params PersistedModeActionRecord[] records) : IModeActionRecordReader
    {
        private readonly IReadOnlyDictionary<Guid, PersistedModeActionRecord> _records = records.ToDictionary(static item => item.ActionId);

        public Task InitializeAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<PersistedModeActionRecord?> GetAsync(Guid actionId, CancellationToken cancellationToken = default)
            => Task.FromResult(_records.TryGetValue(actionId, out var action) ? action : null);
    }
}
