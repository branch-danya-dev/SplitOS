using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.Machine;

namespace SplitOS.Persistence.Tests;

[TestClass]
public sealed class ModeTransitionStoreTests
{
    private static readonly DateTimeOffset StartUtc =
        new(2026, 9, 9, 6, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task CreatePersistsRequestedAcceptedAndReplaysIdempotently()
    {
        using var storage = new TestStorage();
        var context = await CreateContextAsync(storage);
        var transitionId = Guid.NewGuid();

        var created = await context.Transitions.CreateAsync(
            transitionId,
            context.OperationId,
            context.CorrelationId,
            PersistedModeOperationKind.Activate,
            "NONE",
            "WORK",
            1,
            context.ControlSessionKey,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken);

        Assert.AreEqual(ModeTransitionCreateDisposition.Created, created.Disposition);
        Assert.AreEqual(PersistedModeTransitionState.Requested, created.Transition?.TransitionState);
        Assert.AreEqual(PersistedModeTransitionStage.Accepted, created.Transition?.Stage);
        Assert.AreEqual(1, created.Transition?.Revision);
        Assert.IsFalse(created.Transition?.MandatoryVerified ?? true);
        Assert.IsFalse(created.Transition?.CommitDurable ?? true);

        var replay = await context.Transitions.CreateAsync(
            transitionId,
            context.OperationId,
            context.CorrelationId,
            PersistedModeOperationKind.Activate,
            "NONE",
            "WORK",
            1,
            context.ControlSessionKey,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken);
        Assert.AreEqual(ModeTransitionCreateDisposition.Replayed, replay.Disposition);
        Assert.AreEqual(created.Transition, replay.Transition);

        var loaded = await context.Transitions.GetAsync(transitionId);
        Assert.AreEqual(created.Transition, loaded);
        Assert.AreEqual(1, (await context.Transitions.GetIncompleteAsync()).Count);
    }

    [TestMethod]
    public async Task CreateFailsClosedOnSourceOrFenceConflict()
    {
        using var sourceStorage = new TestStorage();
        var sourceContext = await CreateContextAsync(sourceStorage);
        var sourceConflict = await sourceContext.Transitions.CreateAsync(
            Guid.NewGuid(),
            sourceContext.OperationId,
            sourceContext.CorrelationId,
            PersistedModeOperationKind.Activate,
            "NONE",
            "GAME",
            2,
            sourceContext.ControlSessionKey,
            sourceContext.Lease.LeaseId!.Value,
            sourceContext.Lease.FenceToken);
        Assert.AreEqual(ModeTransitionCreateDisposition.SourceConflict, sourceConflict.Disposition);
        Assert.AreEqual("MODE_SOURCE_REVISION_CONFLICT", sourceConflict.ProductCode);
        Assert.AreEqual(0, (await sourceContext.Transitions.GetIncompleteAsync()).Count);

        using var fenceStorage = new TestStorage();
        var fenceContext = await CreateContextAsync(fenceStorage);
        var fenceConflict = await fenceContext.Transitions.CreateAsync(
            Guid.NewGuid(),
            fenceContext.OperationId,
            fenceContext.CorrelationId,
            PersistedModeOperationKind.Activate,
            "NONE",
            "WORK",
            1,
            fenceContext.ControlSessionKey,
            fenceContext.Lease.LeaseId!.Value,
            fenceContext.Lease.FenceToken + 1);
        Assert.AreEqual(ModeTransitionCreateDisposition.LeaseConflict, fenceConflict.Disposition);
        Assert.AreEqual("MUTATION_LEASE_STALE_FENCE", fenceConflict.ProductCode);
        Assert.AreEqual(0, (await fenceContext.Transitions.GetIncompleteAsync()).Count);
    }

    [TestMethod]
    public async Task ExistingIncompleteTransitionRequiresReconciliationBeforeNewOperation()
    {
        using var storage = new TestStorage();
        var context = await CreateContextAsync(storage);
        var firstTransition = Guid.NewGuid();
        var created = await context.Transitions.CreateAsync(
            firstTransition,
            context.OperationId,
            context.CorrelationId,
            PersistedModeOperationKind.Activate,
            "NONE",
            "WORK",
            1,
            context.ControlSessionKey,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken);
        Assert.AreEqual(ModeTransitionCreateDisposition.Created, created.Disposition);

        var released = await context.Leases.ReleaseAsync(
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId);
        Assert.AreEqual(MachineMutationLeaseReleaseDisposition.Released, released.Disposition);

        var secondOperation = Guid.NewGuid();
        var secondCorrelation = Guid.NewGuid();
        var secondLease = await context.Leases.TryAcquireAsync(
            MachineMutationType.Mode,
            secondOperation,
            secondCorrelation,
            context.ControlSessionKey,
            TimeSpan.FromMinutes(2));
        Assert.AreEqual(MachineMutationLeaseAcquireDisposition.Acquired, secondLease.Disposition);

        var blocked = await context.Transitions.CreateAsync(
            Guid.NewGuid(),
            secondOperation,
            secondCorrelation,
            PersistedModeOperationKind.Activate,
            "NONE",
            "GAME",
            1,
            context.ControlSessionKey,
            secondLease.Lease.LeaseId!.Value,
            secondLease.Lease.FenceToken);

        Assert.AreEqual(ModeTransitionCreateDisposition.ReconciliationRequired, blocked.Disposition);
        Assert.AreEqual("MODE_RECONCILIATION_REQUIRED", blocked.ProductCode);
        Assert.AreEqual(firstTransition, blocked.Transition?.TransitionId);
        Assert.AreEqual(1, (await context.Transitions.GetIncompleteAsync()).Count);
    }

    [TestMethod]
    public async Task AdvanceRequiresCurrentRevisionFenceAndLifecycleEdge()
    {
        using var storage = new TestStorage();
        var context = await CreateContextAsync(storage);
        var transitionId = await CreateActivateTransitionAsync(context);

        var advanced = await context.Transitions.AdvanceAsync(
            transitionId,
            1,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeTransitionState.Inspecting,
            PersistedModeTransitionStage.InspectionStarted,
            mandatoryVerified: false);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, advanced.Disposition);
        Assert.AreEqual(2, advanced.Transition?.Revision);

        var staleRevision = await context.Transitions.AdvanceAsync(
            transitionId,
            1,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeTransitionState.Inspecting,
            PersistedModeTransitionStage.InspectionComplete,
            mandatoryVerified: false);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.RevisionConflict, staleRevision.Disposition);
        Assert.AreEqual(2, staleRevision.ActualRevision);

        var invalidJump = await context.Transitions.AdvanceAsync(
            transitionId,
            2,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeTransitionState.Applying,
            PersistedModeTransitionStage.ApplyStarted,
            mandatoryVerified: false);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.InvalidLifecycle, invalidJump.Disposition);

        var staleFence = await context.Transitions.AdvanceAsync(
            transitionId,
            2,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken + 1,
            context.OperationId,
            PersistedModeTransitionState.Inspecting,
            PersistedModeTransitionStage.InspectionComplete,
            mandatoryVerified: false);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.LeaseConflict, staleFence.Disposition);
        Assert.AreEqual("MUTATION_LEASE_STALE_FENCE", staleFence.ProductCode);
    }

    [TestMethod]
    public async Task CommittingRequiresPersistedMandatoryVerificationAndCannotFakeDurableCommit()
    {
        using var storage = new TestStorage();
        var context = await CreateContextAsync(storage);
        var transitionId = await CreateActivateTransitionAsync(context);
        var revision = 1;

        revision = await AdvanceAsync(context, transitionId, revision,
            PersistedModeTransitionState.Inspecting,
            PersistedModeTransitionStage.InspectionComplete,
            false);
        revision = await AdvanceAsync(context, transitionId, revision,
            PersistedModeTransitionState.Resolving,
            PersistedModeTransitionStage.ActionPlanReady,
            false);
        revision = await AdvanceAsync(context, transitionId, revision,
            PersistedModeTransitionState.Applying,
            PersistedModeTransitionStage.ApplyComplete,
            false);
        revision = await AdvanceAsync(context, transitionId, revision,
            PersistedModeTransitionState.Verifying,
            PersistedModeTransitionStage.VerifyComplete,
            false);

        var denied = await context.Transitions.AdvanceAsync(
            transitionId,
            revision,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeTransitionState.Committing,
            PersistedModeTransitionStage.CommitStarted,
            mandatoryVerified: false);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.InvalidLifecycle, denied.Disposition);

        var committing = await context.Transitions.AdvanceAsync(
            transitionId,
            revision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeTransitionState.Committing,
            PersistedModeTransitionStage.CommitStarted,
            mandatoryVerified: true);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, committing.Disposition);
        Assert.IsTrue(committing.Transition?.MandatoryVerified);
        revision = committing.Transition!.Revision;

        var fakeDurable = await context.Transitions.AdvanceAsync(
            transitionId,
            revision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeTransitionState.Committing,
            PersistedModeTransitionStage.CommitDurable,
            mandatoryVerified: true);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.InvalidLifecycle, fakeDurable.Disposition);

        var fakeCompleted = await context.Transitions.AdvanceAsync(
            transitionId,
            revision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeTransitionState.Completed,
            PersistedModeTransitionStage.Terminal,
            mandatoryVerified: true,
            terminalOutcome: "COMPLETED");
        Assert.AreEqual(ModeTransitionAdvanceDisposition.InvalidLifecycle, fakeCompleted.Disposition);
    }

    [TestMethod]
    public async Task CancelledTransitionIsDurableAndNoLongerIncomplete()
    {
        using var storage = new TestStorage();
        var context = await CreateContextAsync(storage);
        var transitionId = await CreateActivateTransitionAsync(context);
        var revision = 1;

        revision = await AdvanceAsync(context, transitionId, revision,
            PersistedModeTransitionState.Inspecting,
            PersistedModeTransitionStage.InspectionComplete,
            false);
        revision = await AdvanceAsync(context, transitionId, revision,
            PersistedModeTransitionState.Blocked,
            PersistedModeTransitionStage.InspectionComplete,
            false);

        var cancelled = await context.Transitions.AdvanceAsync(
            transitionId,
            revision,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeTransitionState.Cancelled,
            PersistedModeTransitionStage.Terminal,
            mandatoryVerified: false,
            terminalOutcome: "CANCELLED");
        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, cancelled.Disposition);
        Assert.AreEqual(PersistedModeTransitionState.Cancelled, cancelled.Transition?.TransitionState);
        Assert.AreEqual(0, (await context.Transitions.GetIncompleteAsync()).Count);

        var reopened = new ModeTransitionStore(
            context.DatabasePath,
            context.MarkerPath,
            context.QuarantineMarkerPath,
            context.Time);
        await reopened.InitializeAsync();
        var restored = await reopened.GetAsync(transitionId);
        Assert.AreEqual(PersistedModeTransitionState.Cancelled, restored?.TransitionState);
        Assert.AreEqual("CANCELLED", restored?.TerminalOutcome);
    }

    [TestMethod]
    public async Task ExpiredLeaseBlocksFurtherTransitionAdvanceForReconciliation()
    {
        using var storage = new TestStorage();
        var context = await CreateContextAsync(storage, TimeSpan.FromSeconds(30));
        var transitionId = await CreateActivateTransitionAsync(context);
        context.Time.Advance(TimeSpan.FromSeconds(31));

        var blocked = await context.Transitions.AdvanceAsync(
            transitionId,
            1,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeTransitionState.Inspecting,
            PersistedModeTransitionStage.InspectionStarted,
            mandatoryVerified: false);

        Assert.AreEqual(ModeTransitionAdvanceDisposition.LeaseConflict, blocked.Disposition);
        Assert.AreEqual("MUTATION_LEASE_RECONCILIATION_REQUIRED", blocked.ProductCode);
        Assert.AreEqual(PersistedModeTransitionState.Requested, (await context.Transitions.GetAsync(transitionId))?.TransitionState);
    }

    private static async Task<Guid> CreateActivateTransitionAsync(TestContext context)
    {
        var transitionId = Guid.NewGuid();
        var created = await context.Transitions.CreateAsync(
            transitionId,
            context.OperationId,
            context.CorrelationId,
            PersistedModeOperationKind.Activate,
            "NONE",
            "WORK",
            1,
            context.ControlSessionKey,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken);
        Assert.AreEqual(ModeTransitionCreateDisposition.Created, created.Disposition);
        return transitionId;
    }

    private static async Task<int> AdvanceAsync(
        TestContext context,
        Guid transitionId,
        int revision,
        PersistedModeTransitionState state,
        PersistedModeTransitionStage stage,
        bool mandatoryVerified)
    {
        if (state == PersistedModeTransitionState.Resolving &&
            stage == PersistedModeTransitionStage.ActionPlanReady)
        {
            var resolving = await context.Transitions.AdvanceAsync(
                transitionId,
                revision,
                context.Lease.LeaseId!.Value,
                context.Lease.FenceToken,
                context.OperationId,
                PersistedModeTransitionState.Resolving,
                PersistedModeTransitionStage.ResolutionStarted,
                mandatoryVerified: false);
            Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, resolving.Disposition, resolving.Detail);
            revision = resolving.Transition!.Revision;

            var policies = new ModeTransitionPolicyStore(
                context.DatabasePath,
                context.MarkerPath,
                context.QuarantineMarkerPath,
                context.Time);
            await policies.InitializeAsync();
            var bound = await policies.BindResolvedPolicyAsync(
                transitionId,
                revision,
                context.Lease.LeaseId.Value,
                context.Lease.FenceToken,
                context.OperationId,
                new PersistedModePolicyIdentity("mode-policy.test", 1, "development", new string('a', 64)),
                PersistedModePolicyTarget.Work,
                new string('b', 64));
            Assert.AreEqual(ModeTransitionPolicyBindDisposition.Bound, bound.Disposition, bound.Detail);
            revision = bound.Binding!.TransitionRevision;

            var plans = new ModeTransitionActionPlanStore(
                context.DatabasePath,
                context.MarkerPath,
                context.QuarantineMarkerPath,
                context.Time);
            await plans.InitializeAsync();
            var persisted = await plans.PersistActionPlanAsync(
                transitionId,
                revision,
                context.Lease.LeaseId.Value,
                context.Lease.FenceToken,
                context.OperationId,
                [new PersistedModeActionDefinition(
                    Guid.NewGuid(), 100, "test", "noop.prepare", null, 1, "{}",
                    "44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a",
                    true, "no_mutation", "test.ready")]);
            Assert.AreEqual(ModeTransitionActionPlanPersistDisposition.Persisted, persisted.Disposition, persisted.Detail);
            revision = persisted.Plan!.TransitionRevision;
        }

        var outcome = await context.Transitions.AdvanceAsync(
            transitionId,
            revision,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId,
            state,
            stage,
            mandatoryVerified);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, outcome.Disposition, outcome.Detail);
        return outcome.Transition!.Revision;
    }

    private static async Task<TestContext> CreateContextAsync(
        TestStorage storage,
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
        const string controlSessionKey = "console-session-1";
        var acquired = await leases.TryAcquireAsync(
            MachineMutationType.Mode,
            operationId,
            correlationId,
            controlSessionKey,
            leaseLifetime ?? TimeSpan.FromMinutes(2));
        Assert.AreEqual(MachineMutationLeaseAcquireDisposition.Acquired, acquired.Disposition);

        var transitions = new ModeTransitionStore(db, marker, quarantineMarker, time);
        await transitions.InitializeAsync();
        return new TestContext(
            db,
            marker,
            quarantineMarker,
            time,
            leases,
            transitions,
            acquired.Lease,
            operationId,
            correlationId,
            controlSessionKey);
    }

    private sealed record TestContext(
        string DatabasePath,
        string MarkerPath,
        string QuarantineMarkerPath,
        ManualTimeProvider Time,
        MachineMutationLeaseStore Leases,
        ModeTransitionStore Transitions,
        MachineMutationLeaseRecord Lease,
        Guid OperationId,
        Guid CorrelationId,
        string ControlSessionKey);

    private sealed class ManualTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtc;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan amount)
        {
            if (amount < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(amount));
            _utcNow = _utcNow.Add(amount);
        }
    }
}
