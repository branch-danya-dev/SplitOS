using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.Machine;

namespace SplitOS.Persistence.Tests;

[TestClass]
public sealed class ModeTransitionReconciliationStoreTests
{
    private static readonly DateTimeOffset StartUtc =
        new(2026, 9, 9, 14, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task CrashDuringApplyingRequiresActualStateThenCanRefenceExpiredLease()
    {
        using var storage = new TestStorage();
        var context = await CreateApplyingContextAsync(storage, TimeSpan.FromSeconds(30));
        var action = context.Actions[0];
        var preState = "{\"source\":\"before-crash\"}";
        var applying = await context.Journal.BeginApplyAsync(
            context.TransitionId,
            action.ActionId,
            1,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId,
            preState,
            Digest(preState));
        Assert.AreEqual(ModeActionAdvanceDisposition.Advanced, applying.Disposition, applying.Detail);

        context.Time.Advance(TimeSpan.FromSeconds(31));
        var reopened = Reconciliation(context);
        await reopened.InitializeAsync();

        var snapshot = await reopened.InspectAsync(context.ControlSessionKey);
        Assert.AreEqual(ModeCrashReconciliationAction.ReconcileApplyingOutcome, snapshot.Action);
        Assert.AreEqual(ModeCrashReconciliationLeaseState.TakeoverRequired, snapshot.LeaseState);
        Assert.AreEqual(action.ActionId, snapshot.FocusAction?.ActionId);
        Assert.AreEqual("NONE", snapshot.CanonicalMode.CommittedMode);
        Assert.AreEqual(1, snapshot.CanonicalMode.Revision);

        var takeover = await reopened.TakeOverAsync(
            context.TransitionId,
            context.TransitionRevision,
            context.ControlSessionKey,
            TimeSpan.FromMinutes(2));
        Assert.AreEqual(ModeReconciliationTakeoverDisposition.Acquired, takeover.Disposition, takeover.Detail);
        Assert.AreNotEqual(context.Lease.LeaseId, takeover.Lease.LeaseId);
        Assert.AreEqual(context.Lease.FenceToken + 1, takeover.Lease.FenceToken);
        Assert.AreEqual(context.TransitionRevision + 1, takeover.Transition?.Revision);

        var journal = new ModeTransitionActionJournalStore(
            context.DatabasePath,
            context.MarkerPath,
            context.QuarantineMarkerPath,
            context.Time);
        await journal.InitializeAsync();
        var reconciled = await journal.RecordApplyResultAsync(
            context.TransitionId,
            action.ActionId,
            applying.Action!.Revision,
            takeover.Lease.LeaseId!.Value,
            takeover.Lease.FenceToken,
            context.OperationId,
            PersistedModeApplyResult.Unknown);
        Assert.AreEqual(ModeActionAdvanceDisposition.Advanced, reconciled.Disposition, reconciled.Detail);
        Assert.AreEqual(PersistedModeActionState.Failed, reconciled.Action?.State);
        Assert.AreEqual("UNKNOWN", reconciled.Action?.ApplyResultCode);

        var after = await reopened.InspectAsync(context.ControlSessionKey);
        Assert.AreEqual(ModeCrashReconciliationAction.RollbackSource, after.Action);
        Assert.AreEqual(ModeCrashReconciliationLeaseState.Current, after.LeaseState);
        Assert.AreEqual(action.ActionId, after.FocusAction?.ActionId);
    }

    [TestMethod]
    public async Task CrashDuringRollbackReconcilesCompensationBeforeEarlierAction()
    {
        using var storage = new TestStorage();
        var context = await CreateApplyingContextAsync(storage, TimeSpan.FromSeconds(30));
        await ApplyAsync(context, context.Actions[0]);
        await ApplyAsync(context, context.Actions[1]);

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
        var rollbackStarted = await context.Transitions.AdvanceAsync(
            context.TransitionId,
            applyComplete.Transition!.Revision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeTransitionState.RollingBack,
            PersistedModeTransitionStage.RollbackStarted,
            false);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, rollbackStarted.Disposition, rollbackStarted.Detail);

        var second = context.Actions[1];
        var rolling = await context.Rollback.BeginRollbackAsync(
            context.TransitionId,
            second.ActionId,
            3,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId);
        Assert.AreEqual(ModeRollbackAdvanceDisposition.Advanced, rolling.Disposition, rolling.Detail);

        context.Time.Advance(TimeSpan.FromSeconds(31));
        var reopened = Reconciliation(context);
        await reopened.InitializeAsync();
        var snapshot = await reopened.InspectAsync(context.ControlSessionKey);
        Assert.AreEqual(ModeCrashReconciliationAction.ReconcileRollbackOutcome, snapshot.Action);
        Assert.AreEqual(ModeCrashReconciliationLeaseState.TakeoverRequired, snapshot.LeaseState);
        Assert.AreEqual(second.ActionId, snapshot.FocusAction?.ActionId);

        var takeover = await reopened.TakeOverAsync(
            context.TransitionId,
            rollbackStarted.Transition!.Revision,
            context.ControlSessionKey,
            TimeSpan.FromMinutes(2));
        Assert.AreEqual(ModeReconciliationTakeoverDisposition.Acquired, takeover.Disposition, takeover.Detail);

        var rollback = new ModeTransitionRollbackStore(
            context.DatabasePath,
            context.MarkerPath,
            context.QuarantineMarkerPath,
            context.Time);
        await rollback.InitializeAsync();
        var settled = await rollback.RecordRollbackResultAsync(
            context.TransitionId,
            second.ActionId,
            rolling.Action!.Revision,
            takeover.Lease.LeaseId!.Value,
            takeover.Lease.FenceToken,
            context.OperationId,
            PersistedModeRollbackResult.RolledBack);
        Assert.AreEqual(ModeRollbackAdvanceDisposition.Advanced, settled.Disposition, settled.Detail);

        var after = await reopened.InspectAsync(context.ControlSessionKey);
        Assert.AreEqual(ModeCrashReconciliationAction.RollbackSource, after.Action);
        Assert.AreEqual(context.Actions[0].ActionId, after.FocusAction?.ActionId);
    }

    [TestMethod]
    public async Task CrashBeforeDurableCommitKeepsSourceCanonicalAndRollsBack()
    {
        using var storage = new TestStorage();
        var context = await CreateApplyingContextAsync(storage, TimeSpan.FromSeconds(30));
        var commitReady = await AdvanceToCommitStartedAsync(context);
        context.Time.Advance(TimeSpan.FromSeconds(31));

        var reopened = Reconciliation(context);
        await reopened.InitializeAsync();
        var snapshot = await reopened.InspectAsync(context.ControlSessionKey);

        Assert.AreEqual(ModeCrashReconciliationAction.RollbackSource, snapshot.Action);
        Assert.AreEqual(ModeCrashReconciliationLeaseState.TakeoverRequired, snapshot.LeaseState);
        Assert.IsFalse(snapshot.Transition!.CommitDurable);
        Assert.AreEqual(PersistedModeTransitionStage.CommitStarted, snapshot.Transition.Stage);
        Assert.AreEqual("NONE", snapshot.CanonicalMode.CommittedMode);
        Assert.AreEqual(1, snapshot.CanonicalMode.Revision);
        Assert.AreEqual(commitReady.TransitionRevision, snapshot.Transition.Revision);
    }

    [TestMethod]
    public async Task CrashAfterDurableCommitTreatsTargetAsCanonicalTruth()
    {
        using var storage = new TestStorage();
        var context = await CreateApplyingContextAsync(storage, TimeSpan.FromSeconds(30));
        var commitReady = await AdvanceToCommitStartedAsync(context);
        var committed = await context.Commit.CommitTransitionAndModeAsync(
            context.TransitionId,
            commitReady.TransitionRevision,
            1,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId,
            true,
            Guid.NewGuid(),
            new PersistedModePolicyIdentity("mode-policy.reconciliation", 1, "development", new string('a', 64)));
        Assert.AreEqual(ModeTransitionCommitDisposition.Committed, committed.Disposition, committed.Detail);

        context.Time.Advance(TimeSpan.FromSeconds(31));
        var reopened = Reconciliation(context);
        await reopened.InitializeAsync();
        var snapshot = await reopened.InspectAsync(context.ControlSessionKey);

        Assert.AreEqual(ModeCrashReconciliationAction.VerifyCommittedTarget, snapshot.Action);
        Assert.AreEqual(ModeCrashReconciliationLeaseState.TakeoverRequired, snapshot.LeaseState);
        Assert.IsTrue(snapshot.Transition!.CommitDurable);
        Assert.AreEqual(PersistedModeTransitionStage.CommitDurable, snapshot.Transition.Stage);
        Assert.AreEqual("WORK", snapshot.CanonicalMode.CommittedMode);
        Assert.AreEqual(2, snapshot.CanonicalMode.Revision);

        var takeover = await reopened.TakeOverAsync(
            context.TransitionId,
            committed.Transition!.Revision,
            context.ControlSessionKey,
            TimeSpan.FromMinutes(2));
        Assert.AreEqual(ModeReconciliationTakeoverDisposition.Acquired, takeover.Disposition, takeover.Detail);
        Assert.AreEqual(context.Lease.FenceToken + 1, takeover.Lease.FenceToken);
    }

    [TestMethod]
    public async Task FreshControlSessionNeverAdoptsPriorCommittedTransition()
    {
        using var storage = new TestStorage();
        var context = await CreateApplyingContextAsync(storage, TimeSpan.FromMinutes(2));
        var commitReady = await AdvanceToCommitStartedAsync(context);
        var committed = await context.Commit.CommitTransitionAndModeAsync(
            context.TransitionId,
            commitReady.TransitionRevision,
            1,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId,
            true,
            Guid.NewGuid(),
            new PersistedModePolicyIdentity("mode-policy.reconciliation", 1, "development", new string('a', 64)));
        Assert.AreEqual(ModeTransitionCommitDisposition.Committed, committed.Disposition, committed.Detail);

        var reopened = Reconciliation(context);
        await reopened.InitializeAsync();
        const string freshSession = "console-session-reconciliation-fresh";
        var snapshot = await reopened.InspectAsync(freshSession);
        Assert.AreEqual(ModeCrashReconciliationAction.ConvergeBaseForFreshSession, snapshot.Action);
        Assert.AreEqual(ModeCrashReconciliationLeaseState.SessionMismatch, snapshot.LeaseState);
        Assert.AreEqual("WORK", snapshot.CanonicalMode.CommittedMode);

        var denied = await reopened.TakeOverAsync(
            context.TransitionId,
            committed.Transition!.Revision,
            freshSession,
            TimeSpan.FromMinutes(2));
        Assert.AreEqual(ModeReconciliationTakeoverDisposition.SessionConflict, denied.Disposition, denied.Detail);
    }

    [TestMethod]
    public async Task CompletedManagedModeWithoutIncompleteTransitionStillReconcilesSessionIdentity()
    {
        using var storage = new TestStorage();
        var context = await CreateApplyingContextAsync(storage, TimeSpan.FromMinutes(2));
        var commitReady = await AdvanceToCommitStartedAsync(context);
        var committed = await context.Commit.CommitTransitionAndModeAsync(
            context.TransitionId,
            commitReady.TransitionRevision,
            1,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId,
            true,
            Guid.NewGuid(),
            new PersistedModePolicyIdentity("mode-policy.reconciliation", 1, "development", new string('a', 64)));
        Assert.AreEqual(ModeTransitionCommitDisposition.Committed, committed.Disposition, committed.Detail);

        var completed = await context.Transitions.AdvanceAsync(
            context.TransitionId,
            committed.Transition!.Revision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeTransitionState.Completed,
            PersistedModeTransitionStage.Terminal,
            true,
            "COMPLETED");
        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, completed.Disposition, completed.Detail);

        var leases = new MachineMutationLeaseStore(
            context.DatabasePath,
            context.MarkerPath,
            context.QuarantineMarkerPath,
            context.Time);
        await leases.InitializeAsync();
        var released = await leases.ReleaseAsync(
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId);
        Assert.AreEqual(MachineMutationLeaseReleaseDisposition.Released, released.Disposition);

        var reopened = Reconciliation(context);
        await reopened.InitializeAsync();
        var sameSession = await reopened.InspectAsync(context.ControlSessionKey);
        Assert.AreEqual(ModeCrashReconciliationAction.VerifyCommittedTarget, sameSession.Action);
        Assert.AreEqual(ModeCrashReconciliationLeaseState.None, sameSession.LeaseState);
        Assert.AreEqual(PersistedModeTransitionState.Completed, sameSession.Transition?.TransitionState);
        Assert.AreEqual("WORK", sameSession.CanonicalMode.CommittedMode);

        var freshSession = await reopened.InspectAsync("console-session-reconciliation-next");
        Assert.AreEqual(ModeCrashReconciliationAction.ConvergeBaseForFreshSession, freshSession.Action);
        Assert.AreEqual(ModeCrashReconciliationLeaseState.SessionMismatch, freshSession.LeaseState);
        Assert.AreEqual("WORK", freshSession.CanonicalMode.CommittedMode);
    }

    [TestMethod]
    public async Task UpdateOwnershipTakesPrecedenceOverFreshSessionModeConvergence()
    {
        using var storage = new TestStorage();
        var context = await CreateApplyingContextAsync(storage, TimeSpan.FromMinutes(2));
        var commitReady = await AdvanceToCommitStartedAsync(context);
        var committed = await context.Commit.CommitTransitionAndModeAsync(
            context.TransitionId,
            commitReady.TransitionRevision,
            1,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId,
            true,
            Guid.NewGuid(),
            new PersistedModePolicyIdentity("mode-policy.reconciliation", 1, "development", new string('a', 64)));
        var completed = await context.Transitions.AdvanceAsync(
            context.TransitionId,
            committed.Transition!.Revision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeTransitionState.Completed,
            PersistedModeTransitionStage.Terminal,
            true,
            "COMPLETED");
        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, completed.Disposition, completed.Detail);

        var leases = new MachineMutationLeaseStore(
            context.DatabasePath,
            context.MarkerPath,
            context.QuarantineMarkerPath,
            context.Time);
        await leases.InitializeAsync();
        var released = await leases.ReleaseAsync(context.Lease.LeaseId.Value, context.Lease.FenceToken, context.OperationId);
        Assert.AreEqual(MachineMutationLeaseReleaseDisposition.Released, released.Disposition);

        var updateOperation = Guid.NewGuid();
        var updateCorrelation = Guid.NewGuid();
        const string freshSession = "console-session-reconciliation-next";
        var updateLease = await leases.TryAcquireAsync(
            MachineMutationType.Update,
            updateOperation,
            updateCorrelation,
            freshSession,
            TimeSpan.FromMinutes(2));
        Assert.AreEqual(MachineMutationLeaseAcquireDisposition.Acquired, updateLease.Disposition);

        var reopened = Reconciliation(context);
        await reopened.InitializeAsync();
        var snapshot = await reopened.InspectAsync(freshSession);
        Assert.AreEqual(ModeCrashReconciliationAction.WaitForMutationOwner, snapshot.Action);
        Assert.AreEqual(ModeCrashReconciliationLeaseState.Busy, snapshot.LeaseState);
        Assert.AreEqual("BUSY_UPDATE", snapshot.ProductCode);
    }

    [TestMethod]
    public async Task TerminalTransitionCanReleaseExpiredResidualLeaseAfterRestart()
    {
        using var storage = new TestStorage();
        var context = await CreateApplyingContextAsync(storage, TimeSpan.FromSeconds(30));
        var commitReady = await AdvanceToCommitStartedAsync(context);
        var committed = await context.Commit.CommitTransitionAndModeAsync(
            context.TransitionId,
            commitReady.TransitionRevision,
            1,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId,
            true,
            Guid.NewGuid(),
            new PersistedModePolicyIdentity("mode-policy.reconciliation", 1, "development", new string('a', 64)));
        Assert.AreEqual(ModeTransitionCommitDisposition.Committed, committed.Disposition, committed.Detail);

        var completed = await context.Transitions.AdvanceAsync(
            context.TransitionId,
            committed.Transition!.Revision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeTransitionState.Completed,
            PersistedModeTransitionStage.Terminal,
            true,
            "COMPLETED");
        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, completed.Disposition, completed.Detail);

        context.Time.Advance(TimeSpan.FromSeconds(31));
        var reopened = Reconciliation(context);
        await reopened.InitializeAsync();
        var snapshot = await reopened.InspectAsync(context.ControlSessionKey);
        Assert.AreEqual(ModeCrashReconciliationAction.ReleaseTerminalLease, snapshot.Action);

        var released = await reopened.ReleaseTerminalLeaseAsync();
        Assert.AreEqual(ModeTerminalLeaseCleanupDisposition.Released, released.Disposition, released.Detail);
        Assert.IsFalse(released.Lease.IsHeld);

        var leases = new MachineMutationLeaseStore(
            context.DatabasePath,
            context.MarkerPath,
            context.QuarantineMarkerPath,
            context.Time);
        await leases.InitializeAsync();
        Assert.IsFalse((await leases.GetAsync()).IsHeld);
    }

    private static async Task<CommitReadyContext> AdvanceToCommitStartedAsync(TestContext context)
    {
        await ApplyAsync(context, context.Actions[0]);
        await ApplyAsync(context, context.Actions[1]);

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

        await VerifyAsync(context, context.Actions[0]);
        await VerifyAsync(context, context.Actions[1]);

        var verified = await context.Transitions.AdvanceAsync(
            context.TransitionId,
            verifying.Transition!.Revision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeTransitionState.Verifying,
            PersistedModeTransitionStage.VerifyComplete,
            false);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, verified.Disposition, verified.Detail);
        Assert.IsTrue(verified.Transition!.MandatoryVerified);

        var committing = await context.Transitions.AdvanceAsync(
            context.TransitionId,
            verified.Transition.Revision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeTransitionState.Committing,
            PersistedModeTransitionStage.CommitStarted,
            true);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, committing.Disposition, committing.Detail);
        return new CommitReadyContext(committing.Transition!.Revision);
    }

    private static async Task ApplyAsync(TestContext context, PersistedModeActionDefinition action)
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
        var applied = await context.Journal.RecordApplyResultAsync(
            context.TransitionId,
            action.ActionId,
            started.Action!.Revision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeApplyResult.Applied);
        Assert.AreEqual(ModeActionAdvanceDisposition.Advanced, applied.Disposition, applied.Detail);
    }

    private static async Task VerifyAsync(TestContext context, PersistedModeActionDefinition action)
    {
        var current = await context.Journal.GetAsync(action.ActionId);
        Assert.IsNotNull(current);
        var started = await context.Journal.BeginVerifyAsync(
            context.TransitionId,
            action.ActionId,
            current.Revision,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId);
        Assert.AreEqual(ModeActionAdvanceDisposition.Advanced, started.Disposition, started.Detail);
        var verified = await context.Journal.RecordVerifyResultAsync(
            context.TransitionId,
            action.ActionId,
            started.Action!.Revision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeVerifyResult.Verified);
        Assert.AreEqual(ModeActionAdvanceDisposition.Advanced, verified.Disposition, verified.Detail);
    }

    private static async Task<TestContext> CreateApplyingContextAsync(
        TestStorage storage,
        TimeSpan leaseLifetime)
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
        const string session = "console-session-reconciliation";
        var acquired = await leases.TryAcquireAsync(
            MachineMutationType.Mode,
            operationId,
            correlationId,
            session,
            leaseLifetime);
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
        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, inspecting.Disposition, inspecting.Detail);
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
            new PersistedModePolicyIdentity("mode-policy.reconciliation", 1, "development", new string('a', 64)),
            PersistedModePolicyTarget.Work,
            new string('b', 64));
        Assert.AreEqual(ModeTransitionPolicyBindDisposition.Bound, bound.Disposition, bound.Detail);

        var actions = TestActions();
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
        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, ready.Disposition, ready.Detail);
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
        var commit = new ModeTransitionCommitStore(db, marker, quarantineMarker, time);

        return new TestContext(
            db,
            marker,
            quarantineMarker,
            time,
            transitions,
            journal,
            rollback,
            commit,
            acquired.Lease,
            operationId,
            correlationId,
            session,
            transitionId,
            applying.Transition!.Revision,
            actions);
    }

    private static PersistedModeActionDefinition[] TestActions()
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
                secondJson, Digest(secondJson), true, "restore.pre_state", "service.confirmed")
        ];
    }

    private static ModeTransitionReconciliationStore Reconciliation(TestContext context)
        => new(
            context.DatabasePath,
            context.MarkerPath,
            context.QuarantineMarkerPath,
            context.Time);

    private static string Digest(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record CommitReadyContext(int TransitionRevision);

    private sealed record TestContext(
        string DatabasePath,
        string MarkerPath,
        string QuarantineMarkerPath,
        ManualTimeProvider Time,
        ModeTransitionStore Transitions,
        ModeTransitionActionJournalStore Journal,
        ModeTransitionRollbackStore Rollback,
        ModeTransitionCommitStore Commit,
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