using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.Machine;

namespace SplitOS.Persistence.Tests;

[TestClass]
public sealed class ModeTransitionCommitStoreTests
{
    private static readonly DateTimeOffset StartUtc =
        new(2026, 9, 9, 7, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task PremiumTargetCommitUpdatesModeAndTransitionAtOneDurableBoundary()
    {
        using var storage = new TestStorage();
        var context = await CreateCommitReadyContextAsync(storage, "NONE", "WORK");

        var outcome = await context.CommitStore.CommitTransitionAndModeAsync(
            context.TransitionId,
            context.TransitionRevision,
            context.SourceModeRevision,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId,
            runtimeAccessPermitsTarget: true,
            policyIdentityCompatible: true);

        Assert.AreEqual(ModeTransitionCommitDisposition.Committed, outcome.Disposition);
        Assert.AreEqual("MODE_COMMIT_DURABLE", outcome.ProductCode);
        Assert.AreEqual("WORK", outcome.OperationalMode?.CommittedMode);
        Assert.AreEqual(context.SourceModeRevision + 1, outcome.OperationalMode?.Revision);
        Assert.AreEqual(context.OperationId.ToString("D"), outcome.OperationalMode?.CommittedByOperationId);
        Assert.AreEqual(PersistedModeTransitionStage.CommitDurable, outcome.Transition?.Stage);
        Assert.IsTrue(outcome.Transition?.CommitDurable == true);
        Assert.AreEqual(context.TransitionRevision + 1, outcome.Transition?.Revision);

        var persistedMode = await context.Machine.GetOperationalModeAsync();
        var persistedTransition = await context.Transitions.GetAsync(context.TransitionId);
        Assert.AreEqual("WORK", persistedMode.CommittedMode);
        Assert.AreEqual(context.SourceModeRevision + 1, persistedMode.Revision);
        Assert.AreEqual(PersistedModeTransitionStage.CommitDurable, persistedTransition?.Stage);
        Assert.IsTrue(persistedTransition?.CommitDurable == true);
    }

    [TestMethod]
    public async Task DeniedPremiumAuthorityLeavesBothCanonicalRowsUnchanged()
    {
        using var storage = new TestStorage();
        var context = await CreateCommitReadyContextAsync(storage, "NONE", "GAME");

        var denied = await context.CommitStore.CommitTransitionAndModeAsync(
            context.TransitionId,
            context.TransitionRevision,
            context.SourceModeRevision,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId,
            runtimeAccessPermitsTarget: false,
            policyIdentityCompatible: true);

        Assert.AreEqual(ModeTransitionCommitDisposition.AuthorityDenied, denied.Disposition);
        Assert.AreEqual("MODE_TARGET_AUTHORITY_DENIED", denied.ProductCode);

        var mode = await context.Machine.GetOperationalModeAsync();
        var transition = await context.Transitions.GetAsync(context.TransitionId);
        Assert.AreEqual("NONE", mode.CommittedMode);
        Assert.AreEqual(context.SourceModeRevision, mode.Revision);
        Assert.AreEqual(PersistedModeTransitionStage.CommitStarted, transition?.Stage);
        Assert.IsFalse(transition?.CommitDurable ?? true);
        Assert.AreEqual(context.TransitionRevision, transition?.Revision);
    }

    [TestMethod]
    public async Task StaleFenceLeavesModeAndTransitionUncommitted()
    {
        using var storage = new TestStorage();
        var context = await CreateCommitReadyContextAsync(storage, "NONE", "WORK");

        var stale = await context.CommitStore.CommitTransitionAndModeAsync(
            context.TransitionId,
            context.TransitionRevision,
            context.SourceModeRevision,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken + 1,
            context.OperationId,
            runtimeAccessPermitsTarget: true,
            policyIdentityCompatible: true);

        Assert.AreEqual(ModeTransitionCommitDisposition.LeaseConflict, stale.Disposition);
        Assert.AreEqual("MUTATION_LEASE_STALE_FENCE", stale.ProductCode);
        await AssertStillCommitStartedAsync(context);
    }

    [TestMethod]
    public async Task ExpiredLeaseRequiresReconciliationAndDoesNotCommit()
    {
        using var storage = new TestStorage();
        var context = await CreateCommitReadyContextAsync(
            storage,
            "NONE",
            "WORK",
            leaseLifetime: TimeSpan.FromSeconds(30));
        context.Time.Advance(TimeSpan.FromSeconds(31));

        var blocked = await context.CommitStore.CommitTransitionAndModeAsync(
            context.TransitionId,
            context.TransitionRevision,
            context.SourceModeRevision,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId,
            runtimeAccessPermitsTarget: true,
            policyIdentityCompatible: true);

        Assert.AreEqual(ModeTransitionCommitDisposition.ReconciliationRequired, blocked.Disposition);
        Assert.AreEqual("MUTATION_LEASE_RECONCILIATION_REQUIRED", blocked.ProductCode);
        await AssertStillCommitStartedAsync(context);
    }

    [TestMethod]
    public async Task SourceModeRevisionConflictDoesNotCommitTarget()
    {
        using var storage = new TestStorage();
        var context = await CreateCommitReadyContextAsync(storage, "NONE", "WORK");

        var conflict = await context.CommitStore.CommitTransitionAndModeAsync(
            context.TransitionId,
            context.TransitionRevision,
            context.SourceModeRevision + 1,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId,
            runtimeAccessPermitsTarget: true,
            policyIdentityCompatible: true);

        Assert.AreEqual(ModeTransitionCommitDisposition.ModeRevisionConflict, conflict.Disposition);
        Assert.AreEqual("MODE_SOURCE_REVISION_CONFLICT", conflict.ProductCode);
        await AssertStillCommitStartedAsync(context);
    }

    [TestMethod]
    public async Task CommitIsReplaySafeAfterLeaseRelease()
    {
        using var storage = new TestStorage();
        var context = await CreateCommitReadyContextAsync(storage, "NONE", "WORK");

        var first = await context.CommitStore.CommitTransitionAndModeAsync(
            context.TransitionId,
            context.TransitionRevision,
            context.SourceModeRevision,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId,
            runtimeAccessPermitsTarget: true,
            policyIdentityCompatible: true);
        Assert.AreEqual(ModeTransitionCommitDisposition.Committed, first.Disposition);

        var released = await context.Leases.ReleaseAsync(
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId);
        Assert.AreEqual(MachineMutationLeaseReleaseDisposition.Released, released.Disposition);

        var replay = await context.CommitStore.CommitTransitionAndModeAsync(
            context.TransitionId,
            context.TransitionRevision,
            context.SourceModeRevision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            runtimeAccessPermitsTarget: false,
            policyIdentityCompatible: false);

        Assert.AreEqual(ModeTransitionCommitDisposition.Replayed, replay.Disposition);
        Assert.AreEqual("MODE_COMMIT_REPLAYED", replay.ProductCode);
        Assert.AreEqual("WORK", replay.OperationalMode?.CommittedMode);
        Assert.IsTrue(replay.Transition?.CommitDurable == true);
    }

    [TestMethod]
    public async Task DeactivateToNoneCanCommitAfterPremiumAuthorityLoss()
    {
        using var storage = new TestStorage();
        var context = await CreateCommitReadyContextAsync(storage, "WORK", "NONE");

        var outcome = await context.CommitStore.CommitTransitionAndModeAsync(
            context.TransitionId,
            context.TransitionRevision,
            context.SourceModeRevision,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId,
            runtimeAccessPermitsTarget: false,
            policyIdentityCompatible: false);

        Assert.AreEqual(ModeTransitionCommitDisposition.Committed, outcome.Disposition);
        Assert.AreEqual("NONE", outcome.OperationalMode?.CommittedMode);
        Assert.IsTrue(outcome.Transition?.CommitDurable == true);
    }

    [TestMethod]
    public async Task CommitRequiresDurablyVerifiedCommitStartedTransition()
    {
        using var storage = new TestStorage();
        var context = await CreateContextAsync(storage, "NONE");
        var transitionId = Guid.NewGuid();
        var created = await context.Transitions.CreateAsync(
            transitionId,
            context.OperationId,
            context.CorrelationId,
            PersistedModeOperationKind.Activate,
            "NONE",
            "WORK",
            context.SourceModeRevision,
            context.ControlSessionKey,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken);
        Assert.AreEqual(ModeTransitionCreateDisposition.Created, created.Disposition);

        var denied = await context.CommitStore.CommitTransitionAndModeAsync(
            transitionId,
            1,
            context.SourceModeRevision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            runtimeAccessPermitsTarget: true,
            policyIdentityCompatible: true);

        Assert.AreEqual(ModeTransitionCommitDisposition.InvalidTransition, denied.Disposition);
        Assert.AreEqual("MODE_TRANSITION_NOT_COMMIT_READY", denied.ProductCode);
        Assert.AreEqual("NONE", (await context.Machine.GetOperationalModeAsync()).CommittedMode);
        Assert.IsFalse((await context.Transitions.GetAsync(transitionId))?.CommitDurable ?? true);
    }

    private static async Task AssertStillCommitStartedAsync(CommitReadyContext context)
    {
        var mode = await context.Machine.GetOperationalModeAsync();
        var transition = await context.Transitions.GetAsync(context.TransitionId);
        Assert.AreEqual(context.SourceMode, mode.CommittedMode);
        Assert.AreEqual(context.SourceModeRevision, mode.Revision);
        Assert.AreEqual(PersistedModeTransitionStage.CommitStarted, transition?.Stage);
        Assert.IsFalse(transition?.CommitDurable ?? true);
        Assert.AreEqual(context.TransitionRevision, transition?.Revision);
    }

    private static async Task<CommitReadyContext> CreateCommitReadyContextAsync(
        TestStorage storage,
        string sourceMode,
        string targetMode,
        TimeSpan? leaseLifetime = null)
    {
        var context = await CreateContextAsync(storage, sourceMode, leaseLifetime);
        var transitionId = Guid.NewGuid();
        var kind = (sourceMode, targetMode) switch
        {
            ("NONE", "WORK" or "GAME") => PersistedModeOperationKind.Activate,
            ("WORK", "GAME") or ("GAME", "WORK") => PersistedModeOperationKind.Switch,
            ("WORK" or "GAME", "NONE") => PersistedModeOperationKind.Deactivate,
            _ => throw new InvalidOperationException("Unsupported test tuple.")
        };

        var created = await context.Transitions.CreateAsync(
            transitionId,
            context.OperationId,
            context.CorrelationId,
            kind,
            sourceMode,
            targetMode,
            context.SourceModeRevision,
            context.ControlSessionKey,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken);
        Assert.AreEqual(ModeTransitionCreateDisposition.Created, created.Disposition);

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
        revision = await AdvanceAsync(context, transitionId, revision,
            PersistedModeTransitionState.Committing,
            PersistedModeTransitionStage.CommitStarted,
            true);

        return new CommitReadyContext(
            context.DatabasePath,
            context.MarkerPath,
            context.QuarantineMarkerPath,
            context.Time,
            context.Machine,
            context.Leases,
            context.Transitions,
            context.CommitStore,
            context.Lease,
            context.OperationId,
            context.CorrelationId,
            context.ControlSessionKey,
            context.SourceModeRevision,
            sourceMode,
            transitionId,
            revision);
    }

    private static async Task<int> AdvanceAsync(
        BaseContext context,
        Guid transitionId,
        int revision,
        PersistedModeTransitionState state,
        PersistedModeTransitionStage stage,
        bool mandatoryVerified)
    {
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

    private static async Task<BaseContext> CreateContextAsync(
        TestStorage storage,
        string sourceMode,
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

        if (sourceMode != "NONE")
        {
            var seeded = await machine.WriteOperationalModeAsync(
                sourceMode,
                1,
                Guid.NewGuid(),
                Guid.NewGuid());
            Assert.AreEqual(OperationalModeWriteDisposition.Applied, seeded.Disposition);
        }

        var source = await machine.GetOperationalModeAsync();
        Assert.AreEqual(sourceMode, source.CommittedMode);

        var leases = new MachineMutationLeaseStore(db, marker, quarantineMarker, time);
        await leases.InitializeAsync();
        var operationId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        const string controlSessionKey = "console-session-atomic-commit";
        var acquired = await leases.TryAcquireAsync(
            MachineMutationType.Mode,
            operationId,
            correlationId,
            controlSessionKey,
            leaseLifetime ?? TimeSpan.FromMinutes(2));
        Assert.AreEqual(MachineMutationLeaseAcquireDisposition.Acquired, acquired.Disposition);

        var transitions = new ModeTransitionStore(db, marker, quarantineMarker, time);
        await transitions.InitializeAsync();
        var commitStore = new ModeTransitionCommitStore(db, marker, quarantineMarker, time);

        return new BaseContext(
            db,
            marker,
            quarantineMarker,
            time,
            machine,
            leases,
            transitions,
            commitStore,
            acquired.Lease,
            operationId,
            correlationId,
            controlSessionKey,
            source.Revision);
    }

    private record BaseContext(
        string DatabasePath,
        string MarkerPath,
        string QuarantineMarkerPath,
        ManualTimeProvider Time,
        MachineStateStore Machine,
        MachineMutationLeaseStore Leases,
        ModeTransitionStore Transitions,
        ModeTransitionCommitStore CommitStore,
        MachineMutationLeaseRecord Lease,
        Guid OperationId,
        Guid CorrelationId,
        string ControlSessionKey,
        int SourceModeRevision);

    private sealed record CommitReadyContext(
        string DatabasePath,
        string MarkerPath,
        string QuarantineMarkerPath,
        ManualTimeProvider Time,
        MachineStateStore Machine,
        MachineMutationLeaseStore Leases,
        ModeTransitionStore Transitions,
        ModeTransitionCommitStore CommitStore,
        MachineMutationLeaseRecord Lease,
        Guid OperationId,
        Guid CorrelationId,
        string ControlSessionKey,
        int SourceModeRevision,
        string SourceMode,
        Guid TransitionId,
        int TransitionRevision)
        : BaseContext(
            DatabasePath,
            MarkerPath,
            QuarantineMarkerPath,
            Time,
            Machine,
            Leases,
            Transitions,
            CommitStore,
            Lease,
            OperationId,
            CorrelationId,
            ControlSessionKey,
            SourceModeRevision);

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
