using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.Machine;

namespace SplitOS.Persistence.Tests;

[TestClass]
public sealed class ModeTransitionRollbackStoreTests
{
    private static readonly DateTimeOffset StartUtc =
        new(2026, 9, 9, 13, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task RollbackRunsInReverseOrderAndSurvivesRestart()
    {
        using var storage = new TestStorage();
        var context = await CreateRollbackContextAsync(storage);
        var first = context.Actions[0];
        var second = context.Actions[1];

        var candidate = await context.Rollback.GetNextRollbackCandidateAsync(context.TransitionId);
        Assert.AreEqual(second.ActionId, candidate?.ActionId);

        var outOfOrder = await context.Rollback.BeginRollbackAsync(
            context.TransitionId,
            first.ActionId,
            3,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId);
        Assert.AreEqual(ModeRollbackAdvanceDisposition.InvalidLifecycle, outOfOrder.Disposition, outOfOrder.Detail);
        Assert.AreEqual("MODE_ACTION_ROLLBACK_ORDER_BLOCKED", outOfOrder.ProductCode);

        var secondStarted = await context.Rollback.BeginRollbackAsync(
            context.TransitionId,
            second.ActionId,
            3,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId);
        Assert.AreEqual(ModeRollbackAdvanceDisposition.Advanced, secondStarted.Disposition, secondStarted.Detail);
        Assert.AreEqual(PersistedModeActionState.RollingBack, secondStarted.Action?.State);
        Assert.AreEqual(4, secondStarted.Action?.Revision);

        var replay = await context.Rollback.BeginRollbackAsync(
            context.TransitionId,
            second.ActionId,
            3,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId);
        Assert.AreEqual(ModeRollbackAdvanceDisposition.Replayed, replay.Disposition, replay.Detail);

        var secondDone = await context.Rollback.RecordRollbackResultAsync(
            context.TransitionId,
            second.ActionId,
            secondStarted.Action!.Revision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeRollbackResult.RolledBack);
        Assert.AreEqual(ModeRollbackAdvanceDisposition.Advanced, secondDone.Disposition, secondDone.Detail);
        Assert.AreEqual(PersistedModeActionState.RolledBack, secondDone.Action?.State);
        Assert.AreEqual("ROLLED_BACK", secondDone.Action?.RollbackResultCode);

        candidate = await context.Rollback.GetNextRollbackCandidateAsync(context.TransitionId);
        Assert.AreEqual(first.ActionId, candidate?.ActionId);

        var firstStarted = await context.Rollback.BeginRollbackAsync(
            context.TransitionId,
            first.ActionId,
            3,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId);
        var firstDone = await context.Rollback.RecordRollbackResultAsync(
            context.TransitionId,
            first.ActionId,
            firstStarted.Action!.Revision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeRollbackResult.RolledBack);
        Assert.AreEqual(ModeRollbackAdvanceDisposition.Advanced, firstDone.Disposition, firstDone.Detail);
        Assert.IsNull(await context.Rollback.GetNextRollbackCandidateAsync(context.TransitionId));

        var rollbackVerify = await context.Transitions.AdvanceAsync(
            context.TransitionId,
            context.TransitionRevision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeTransitionState.RollingBack,
            PersistedModeTransitionStage.RollbackVerify,
            false);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, rollbackVerify.Disposition, rollbackVerify.Detail);

        var cancelled = await context.Transitions.AdvanceAsync(
            context.TransitionId,
            rollbackVerify.Transition!.Revision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeTransitionState.Cancelled,
            PersistedModeTransitionStage.Terminal,
            false,
            "CANCELLED");
        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, cancelled.Disposition, cancelled.Detail);

        var reopened = new ModeTransitionRollbackStore(
            context.DatabasePath,
            context.MarkerPath,
            context.QuarantineMarkerPath,
            context.Time);
        await reopened.InitializeAsync();
        Assert.IsNull(await reopened.GetNextRollbackCandidateAsync(context.TransitionId));
    }

    [TestMethod]
    public async Task RollbackFailureBlocksEarlierCompensationAndRollbackVerify()
    {
        using var storage = new TestStorage();
        var context = await CreateRollbackContextAsync(storage);
        var first = context.Actions[0];
        var second = context.Actions[1];

        var started = await context.Rollback.BeginRollbackAsync(
            context.TransitionId,
            second.ActionId,
            3,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId);
        var failed = await context.Rollback.RecordRollbackResultAsync(
            context.TransitionId,
            second.ActionId,
            started.Action!.Revision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeRollbackResult.Unknown);
        Assert.AreEqual(ModeRollbackAdvanceDisposition.Advanced, failed.Disposition, failed.Detail);
        Assert.AreEqual(PersistedModeActionState.RollbackFailed, failed.Action?.State);
        Assert.AreEqual("UNKNOWN", failed.Action?.RollbackResultCode);

        var blocked = await context.Rollback.BeginRollbackAsync(
            context.TransitionId,
            first.ActionId,
            3,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId);
        Assert.AreEqual(ModeRollbackAdvanceDisposition.ReconciliationRequired, blocked.Disposition, blocked.Detail);
        Assert.AreEqual("MODE_ROLLBACK_HIGHER_ACTION_FAILED", blocked.ProductCode);

        var verifyBlocked = await context.Transitions.AdvanceAsync(
            context.TransitionId,
            context.TransitionRevision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeTransitionState.RollingBack,
            PersistedModeTransitionStage.RollbackVerify,
            false);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.InvalidLifecycle, verifyBlocked.Disposition, verifyBlocked.Detail);
    }

    [TestMethod]
    public async Task UnknownApplyOutcomeRemainsRollbackCandidate()
    {
        using var storage = new TestStorage();
        var context = await CreateApplyingContextAsync(storage, secondMandatory: false);
        var first = context.Actions[0];
        var second = context.Actions[1];

        await ApplyAsync(context, first, PersistedModeApplyResult.Applied);
        await ApplyAsync(context, second, PersistedModeApplyResult.Unknown);

        var rollingBack = await context.Transitions.AdvanceAsync(
            context.TransitionId,
            context.TransitionRevision,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeTransitionState.RollingBack,
            PersistedModeTransitionStage.RollbackStarted,
            false);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, rollingBack.Disposition, rollingBack.Detail);

        var candidate = await context.Rollback.GetNextRollbackCandidateAsync(context.TransitionId);
        Assert.AreEqual(second.ActionId, candidate?.ActionId);
        Assert.AreEqual("UNKNOWN", candidate?.ApplyResultCode);

        var started = await context.Rollback.BeginRollbackAsync(
            context.TransitionId,
            second.ActionId,
            3,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId);
        Assert.AreEqual(ModeRollbackAdvanceDisposition.Advanced, started.Disposition, started.Detail);
    }

    [TestMethod]
    public async Task ExpiredLeaseStopsRollbackForReconciliation()
    {
        using var storage = new TestStorage();
        var context = await CreateRollbackContextAsync(storage, TimeSpan.FromSeconds(30));
        context.Time.Advance(TimeSpan.FromSeconds(31));
        var second = context.Actions[1];

        var blocked = await context.Rollback.BeginRollbackAsync(
            context.TransitionId,
            second.ActionId,
            3,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId);
        Assert.AreEqual(ModeRollbackAdvanceDisposition.ReconciliationRequired, blocked.Disposition, blocked.Detail);
        Assert.AreEqual("MUTATION_LEASE_RECONCILIATION_REQUIRED", blocked.ProductCode);
    }

    [TestMethod]
    public async Task TransitionStagesAreDerivedFromDurableActionEvidence()
    {
        using var storage = new TestStorage();
        var context = await CreateApplyingContextAsync(storage, secondMandatory: false);
        var first = context.Actions[0];
        var second = context.Actions[1];

        var fakeApplyComplete = await context.Transitions.AdvanceAsync(
            context.TransitionId,
            context.TransitionRevision,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeTransitionState.Applying,
            PersistedModeTransitionStage.ApplyComplete,
            false);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.InvalidLifecycle, fakeApplyComplete.Disposition, fakeApplyComplete.Detail);

        await ApplyAsync(context, first, PersistedModeApplyResult.Applied);
        var skipped = await context.Journal.SkipOptionalAsync(
            context.TransitionId,
            second.ActionId,
            1,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId);
        Assert.AreEqual(ModeActionAdvanceDisposition.Advanced, skipped.Disposition, skipped.Detail);

        var applyComplete = await context.Transitions.AdvanceAsync(
            context.TransitionId,
            context.TransitionRevision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeTransitionState.Applying,
            PersistedModeTransitionStage.ApplyComplete,
            false);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, applyComplete.Disposition, applyComplete.Detail);

        var verifying = await context.Transitions.AdvanceAsync(
            context.TransitionId,
            applyComplete.Transition!.Revision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeTransitionState.Verifying,
            PersistedModeTransitionStage.VerifyStarted,
            false);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, verifying.Disposition, verifying.Detail);

        var fakeVerifyComplete = await context.Transitions.AdvanceAsync(
            context.TransitionId,
            verifying.Transition!.Revision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeTransitionState.Verifying,
            PersistedModeTransitionStage.VerifyComplete,
            false);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.InvalidLifecycle, fakeVerifyComplete.Disposition, fakeVerifyComplete.Detail);

        var currentFirst = await context.Journal.GetAsync(first.ActionId);
        var verifyStarted = await context.Journal.BeginVerifyAsync(
            context.TransitionId,
            first.ActionId,
            currentFirst!.Revision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId);
        var verified = await context.Journal.RecordVerifyResultAsync(
            context.TransitionId,
            first.ActionId,
            verifyStarted.Action!.Revision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeVerifyResult.Verified);
        Assert.AreEqual(ModeActionAdvanceDisposition.Advanced, verified.Disposition, verified.Detail);

        var verifyComplete = await context.Transitions.AdvanceAsync(
            context.TransitionId,
            verifying.Transition.Revision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeTransitionState.Verifying,
            PersistedModeTransitionStage.VerifyComplete,
            false);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, verifyComplete.Disposition, verifyComplete.Detail);
        Assert.IsTrue(verifyComplete.Transition!.MandatoryVerified);

        var committing = await context.Transitions.AdvanceAsync(
            context.TransitionId,
            verifyComplete.Transition.Revision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeTransitionState.Committing,
            PersistedModeTransitionStage.CommitStarted,
            true);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, committing.Disposition, committing.Detail);
    }

    private static async Task<TestContext> CreateRollbackContextAsync(
        TestStorage storage,
        TimeSpan? leaseLifetime = null)
    {
        var context = await CreateApplyingContextAsync(storage, secondMandatory: true, leaseLifetime);
        await ApplyAsync(context, context.Actions[0], PersistedModeApplyResult.Applied);
        await ApplyAsync(context, context.Actions[1], PersistedModeApplyResult.Applied);

        var applyComplete = await context.Transitions.AdvanceAsync(
            context.TransitionId,
            context.TransitionRevision,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeTransitionState.Applying,
            PersistedModeTransitionStage.ApplyComplete,
            false);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, applyComplete.Disposition, applyComplete.Detail);

        var rollingBack = await context.Transitions.AdvanceAsync(
            context.TransitionId,
            applyComplete.Transition!.Revision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeTransitionState.RollingBack,
            PersistedModeTransitionStage.RollbackStarted,
            false);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, rollingBack.Disposition, rollingBack.Detail);
        return context with { TransitionRevision = rollingBack.Transition!.Revision };
    }

    private static async Task<ModeActionAdvanceOutcome> ApplyAsync(
        TestContext context,
        PersistedModeActionDefinition action,
        PersistedModeApplyResult result)
    {
        var preState = $"{{\"source\":\"{action.ActionId:D}\"}}";
        var started = await context.Journal.BeginApplyAsync(
            context.TransitionId,
            action.ActionId,
            1,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId,
            preState,
            Digest(preState));
        Assert.AreEqual(ModeActionAdvanceDisposition.Advanced, started.Disposition, started.Detail);

        var recorded = await context.Journal.RecordApplyResultAsync(
            context.TransitionId,
            action.ActionId,
            started.Action!.Revision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            result);
        Assert.AreEqual(ModeActionAdvanceDisposition.Advanced, recorded.Disposition, recorded.Detail);
        return recorded;
    }

    private static async Task<TestContext> CreateApplyingContextAsync(
        TestStorage storage,
        bool secondMandatory,
        TimeSpan? leaseLifetime = null)
    {
        var db = storage.PathFor("machine.db");
        var marker = storage.PathFor("machine-store.initialized");
        var quarantineMarker = storage.PathFor("machine-store.quarantined.json");
        var time = new ManualTimeProvider(StartUtc);
        var machine = new MachineStateStore(
            db,
            marker,
            storage.PathFor("maintenance", "backups"),
            storage.PathFor("maintenance", "quarantine"),
            quarantineMarker);
        await machine.InitializeAsync();

        var leases = new MachineMutationLeaseStore(db, marker, quarantineMarker, time);
        await leases.InitializeAsync();
        var operationId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        const string session = "console-session-rollback";
        var acquired = await leases.TryAcquireAsync(
            MachineMutationType.Mode,
            operationId,
            correlationId,
            session,
            leaseLifetime ?? TimeSpan.FromMinutes(2));
        Assert.AreEqual(MachineMutationLeaseAcquireDisposition.Acquired, acquired.Disposition);

        var transitions = new ModeTransitionStore(db, marker, quarantineMarker, time);
        await transitions.InitializeAsync();
        var transitionId = Guid.NewGuid();
        var created = await transitions.CreateAsync(
            transitionId,
            operationId,
            correlationId,
            PersistedModeOperationKind.Activate,
            "NONE",
            "WORK",
            1,
            session,
            acquired.Lease.LeaseId!.Value,
            acquired.Lease.FenceToken);
        Assert.AreEqual(ModeTransitionCreateDisposition.Created, created.Disposition);

        var inspecting = await transitions.AdvanceAsync(
            transitionId,
            1,
            acquired.Lease.LeaseId.Value,
            acquired.Lease.FenceToken,
            operationId,
            PersistedModeTransitionState.Inspecting,
            PersistedModeTransitionStage.InspectionComplete,
            false);
        var resolving = await transitions.AdvanceAsync(
            transitionId,
            inspecting.Transition!.Revision,
            acquired.Lease.LeaseId.Value,
            acquired.Lease.FenceToken,
            operationId,
            PersistedModeTransitionState.Resolving,
            PersistedModeTransitionStage.ResolutionStarted,
            false);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, resolving.Disposition, resolving.Detail);

        var policies = new ModeTransitionPolicyStore(db, marker, quarantineMarker, time);
        await policies.InitializeAsync();
        var bound = await policies.BindResolvedPolicyAsync(
            transitionId,
            resolving.Transition!.Revision,
            acquired.Lease.LeaseId.Value,
            acquired.Lease.FenceToken,
            operationId,
            new PersistedModePolicyIdentity("mode-policy.rollback", 1, "development", new string('a', 64)),
            PersistedModePolicyTarget.Work,
            new string('b', 64));
        Assert.AreEqual(ModeTransitionPolicyBindDisposition.Bound, bound.Disposition, bound.Detail);

        var actions = TestActions(secondMandatory);
        var plans = new ModeTransitionActionPlanStore(db, marker, quarantineMarker, time);
        await plans.InitializeAsync();
        var persisted = await plans.PersistActionPlanAsync(
            transitionId,
            bound.Binding!.TransitionRevision,
            acquired.Lease.LeaseId.Value,
            acquired.Lease.FenceToken,
            operationId,
            actions);
        Assert.AreEqual(ModeTransitionActionPlanPersistDisposition.Persisted, persisted.Disposition, persisted.Detail);

        var ready = await transitions.AdvanceAsync(
            transitionId,
            persisted.Plan!.TransitionRevision,
            acquired.Lease.LeaseId.Value,
            acquired.Lease.FenceToken,
            operationId,
            PersistedModeTransitionState.Resolving,
            PersistedModeTransitionStage.ActionPlanReady,
            false);
        var applying = await transitions.AdvanceAsync(
            transitionId,
            ready.Transition!.Revision,
            acquired.Lease.LeaseId.Value,
            acquired.Lease.FenceToken,
            operationId,
            PersistedModeTransitionState.Applying,
            PersistedModeTransitionStage.ApplyStarted,
            false);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, applying.Disposition, applying.Detail);

        var journal = new ModeTransitionActionJournalStore(db, marker, quarantineMarker, time);
        await journal.InitializeAsync();
        var rollback = new ModeTransitionRollbackStore(db, marker, quarantineMarker, time);
        await rollback.InitializeAsync();

        return new TestContext(
            db,
            marker,
            quarantineMarker,
            time,
            transitions,
            journal,
            rollback,
            acquired.Lease,
            operationId,
            correlationId,
            session,
            transitionId,
            applying.Transition!.Revision,
            actions);
    }

    private static PersistedModeActionDefinition[] TestActions(bool secondMandatory)
    {
        const string firstJson = "{\"powerMode\":\"target\"}";
        const string secondJson = "{\"serviceMode\":\"target\"}";
        return
        [
            new PersistedModeActionDefinition(
                Guid.NewGuid(), 100, "power", "context.apply", "power/default", 1,
                firstJson, Digest(firstJson), true, "restore.pre_state", "power.confirmed"),
            new PersistedModeActionDefinition(
                Guid.NewGuid(), 200, "service", "managed.apply", "service/test", 1,
                secondJson, Digest(secondJson), secondMandatory, "restore.pre_state", "service.confirmed")
        ];
    }

    private static string Digest(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record TestContext(
        string DatabasePath,
        string MarkerPath,
        string QuarantineMarkerPath,
        ManualTimeProvider Time,
        ModeTransitionStore Transitions,
        ModeTransitionActionJournalStore Journal,
        ModeTransitionRollbackStore Rollback,
        MachineMutationLeaseRecord Lease,
        Guid OperationId,
        Guid CorrelationId,
        string ControlSessionKey,
        Guid TransitionId,
        int TransitionRevision,
        PersistedModeActionDefinition[] Actions);

    private sealed class ManualTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtc;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan amount) => _utcNow = _utcNow.Add(amount);
    }
}
