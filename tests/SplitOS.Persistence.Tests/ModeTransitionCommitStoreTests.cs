using Microsoft.Data.Sqlite;
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
            activationEpochId: context.ActivationEpochId,
            currentPolicyIdentity: context.BoundPolicyIdentity);

        Assert.AreEqual(ModeTransitionCommitDisposition.Committed, outcome.Disposition);
        Assert.AreEqual("MODE_COMMIT_DURABLE", outcome.ProductCode);
        Assert.AreEqual("WORK", outcome.OperationalMode?.CommittedMode);
        Assert.AreEqual(context.SourceModeRevision + 1, outcome.OperationalMode?.Revision);
        Assert.AreEqual(context.OperationId.ToString("D"), outcome.OperationalMode?.CommittedByOperationId);
        Assert.AreEqual(context.ControlSessionKey, outcome.OperationalMode?.ControlSessionKey);
        Assert.AreEqual(context.ActivationEpochId, outcome.OperationalMode?.ActivationEpochId);
        Assert.AreEqual(context.BoundPolicyIdentity, outcome.OperationalMode?.PolicyIdentity);
        Assert.AreEqual(PersistedModePolicyTarget.Work, outcome.OperationalMode?.PolicyTarget);
        Assert.AreEqual(context.BoundResolvedPolicyDigest, outcome.OperationalMode?.ResolvedPolicyDigest);
        Assert.AreEqual(PersistedModeTransitionStage.CommitDurable, outcome.Transition?.Stage);
        Assert.IsTrue(outcome.Transition?.CommitDurable == true);
        Assert.AreEqual(context.TransitionRevision + 1, outcome.Transition?.Revision);

        var persistedMode = await context.Machine.GetOperationalModeAsync();
        var persistedTransition = await context.Transitions.GetAsync(context.TransitionId);
        Assert.AreEqual("WORK", persistedMode.CommittedMode);
        Assert.AreEqual(context.SourceModeRevision + 1, persistedMode.Revision);
        Assert.AreEqual(context.ControlSessionKey, persistedMode.ControlSessionKey);
        Assert.AreEqual(context.ActivationEpochId, persistedMode.ActivationEpochId);
        Assert.AreEqual(context.BoundPolicyIdentity, persistedMode.PolicyIdentity);
        Assert.AreEqual(context.BoundResolvedPolicyDigest, persistedMode.ResolvedPolicyDigest);
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
            activationEpochId: context.ActivationEpochId,
            currentPolicyIdentity: context.BoundPolicyIdentity);

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
    public async Task StaleCurrentPolicyIdentityDeniesPremiumCommit()
    {
        using var storage = new TestStorage();
        var context = await CreateCommitReadyContextAsync(storage, "NONE", "WORK");
        var staleIdentity = context.BoundPolicyIdentity with { CatalogDigest = new string('c', 64) };

        var denied = await context.CommitStore.CommitTransitionAndModeAsync(
            context.TransitionId,
            context.TransitionRevision,
            context.SourceModeRevision,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId,
            runtimeAccessPermitsTarget: true,
            activationEpochId: context.ActivationEpochId,
            currentPolicyIdentity: staleIdentity);

        Assert.AreEqual(ModeTransitionCommitDisposition.AuthorityDenied, denied.Disposition);
        Assert.AreEqual("MODE_POLICY_IDENTITY_STALE", denied.ProductCode);
        await AssertStillCommitStartedAsync(context);
    }

    [TestMethod]
    public async Task SwitchCannotCrossCanonicalActivationEpoch()
    {
        using var storage = new TestStorage();
        var context = await CreateCommitReadyContextAsync(storage, "WORK", "GAME");

        var denied = await context.CommitStore.CommitTransitionAndModeAsync(
            context.TransitionId,
            context.TransitionRevision,
            context.SourceModeRevision,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId,
            runtimeAccessPermitsTarget: true,
            activationEpochId: Guid.NewGuid(),
            currentPolicyIdentity: context.BoundPolicyIdentity);

        Assert.AreEqual(ModeTransitionCommitDisposition.ConcurrencyConflict, denied.Disposition);
        Assert.AreEqual("MODE_SOURCE_ACTIVATION_IDENTITY_CONFLICT", denied.ProductCode);
        await AssertStillCommitStartedAsync(context);
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
            activationEpochId: context.ActivationEpochId,
            currentPolicyIdentity: context.BoundPolicyIdentity);

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
            activationEpochId: context.ActivationEpochId,
            currentPolicyIdentity: context.BoundPolicyIdentity);

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
            activationEpochId: context.ActivationEpochId,
            currentPolicyIdentity: context.BoundPolicyIdentity);

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
            activationEpochId: context.ActivationEpochId,
            currentPolicyIdentity: context.BoundPolicyIdentity);
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
            activationEpochId: context.ActivationEpochId,
            currentPolicyIdentity: null);

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
            activationEpochId: null,
            currentPolicyIdentity: null);

        Assert.AreEqual(ModeTransitionCommitDisposition.Committed, outcome.Disposition);
        Assert.AreEqual("NONE", outcome.OperationalMode?.CommittedMode);
        Assert.IsNull(outcome.OperationalMode?.ControlSessionKey);
        Assert.IsNull(outcome.OperationalMode?.ActivationEpochId);
        Assert.IsNull(outcome.OperationalMode?.PolicyIdentity);
        Assert.IsNull(outcome.OperationalMode?.PolicyTarget);
        Assert.IsNull(outcome.OperationalMode?.ResolvedPolicyDigest);
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
            activationEpochId: Guid.NewGuid(),
            currentPolicyIdentity: null);

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
            PersistedModeTransitionStage.ResolutionStarted,
            false);

        var policies = new ModeTransitionPolicyStore(
            context.DatabasePath,
            context.MarkerPath,
            context.QuarantineMarkerPath,
            context.Time);
        await policies.InitializeAsync();
        var policyTarget = targetMode switch
        {
            "NONE" => PersistedModePolicyTarget.Base,
            "WORK" => PersistedModePolicyTarget.Work,
            "GAME" => PersistedModePolicyTarget.Game,
            _ => throw new InvalidOperationException("Unsupported policy target.")
        };
        var targetPolicyIdentity = new PersistedModePolicyIdentity(
            "mode-policy.test", 1, "development", new string('a', 64));
        var targetResolvedPolicyDigest = new string('b', 64);
        var bound = await policies.BindResolvedPolicyAsync(
            transitionId,
            revision,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId,
            targetPolicyIdentity,
            policyTarget,
            targetResolvedPolicyDigest);
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
            revision,
            context.ActivationEpochId,
            targetPolicyIdentity,
            targetResolvedPolicyDigest);
    }

    private static async Task<int> AdvanceAsync(
        BaseContext context,
        Guid transitionId,
        int revision,
        PersistedModeTransitionState state,
        PersistedModeTransitionStage stage,
        bool mandatoryVerified)
    {
        if (state == PersistedModeTransitionState.Applying &&
            stage == PersistedModeTransitionStage.ApplyComplete)
        {
            var currentTransition = await context.Transitions.GetAsync(transitionId);
            if (currentTransition!.Stage != PersistedModeTransitionStage.ApplyStarted)
            {
                var applying = await context.Transitions.AdvanceAsync(
                    transitionId, revision, context.Lease.LeaseId!.Value, context.Lease.FenceToken, context.OperationId,
                    PersistedModeTransitionState.Applying, PersistedModeTransitionStage.ApplyStarted, false);
                Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, applying.Disposition, applying.Detail);
                revision = applying.Transition!.Revision;
            }

            var plans = new ModeTransitionActionPlanStore(context.DatabasePath, context.MarkerPath, context.QuarantineMarkerPath, context.Time);
            await plans.InitializeAsync();
            var plan = await plans.GetAsync(transitionId);
            var journal = new ModeTransitionActionJournalStore(context.DatabasePath, context.MarkerPath, context.QuarantineMarkerPath, context.Time);
            await journal.InitializeAsync();
            foreach (var action in plan!.Actions.OrderBy(item => item.SequenceNo))
            {
                if (action.State != PersistedModeActionState.Planned) continue;
                var started = await journal.BeginApplyAsync(transitionId, action.ActionId, action.Revision, context.Lease.LeaseId.Value, context.Lease.FenceToken, context.OperationId);
                Assert.AreEqual(ModeActionAdvanceDisposition.Advanced, started.Disposition, started.Detail);
                var applied = await journal.RecordApplyResultAsync(transitionId, action.ActionId, started.Action!.Revision, context.Lease.LeaseId.Value, context.Lease.FenceToken, context.OperationId, PersistedModeApplyResult.Applied);
                Assert.AreEqual(ModeActionAdvanceDisposition.Advanced, applied.Disposition, applied.Detail);
            }
        }

        if (state == PersistedModeTransitionState.Verifying &&
            stage == PersistedModeTransitionStage.VerifyComplete)
        {
            var currentTransition = await context.Transitions.GetAsync(transitionId);
            if (currentTransition!.Stage != PersistedModeTransitionStage.VerifyStarted)
            {
                var verifying = await context.Transitions.AdvanceAsync(
                    transitionId, revision, context.Lease.LeaseId!.Value, context.Lease.FenceToken, context.OperationId,
                    PersistedModeTransitionState.Verifying, PersistedModeTransitionStage.VerifyStarted, false);
                Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, verifying.Disposition, verifying.Detail);
                revision = verifying.Transition!.Revision;
            }

            var plans = new ModeTransitionActionPlanStore(context.DatabasePath, context.MarkerPath, context.QuarantineMarkerPath, context.Time);
            await plans.InitializeAsync();
            var plan = await plans.GetAsync(transitionId);
            var journal = new ModeTransitionActionJournalStore(context.DatabasePath, context.MarkerPath, context.QuarantineMarkerPath, context.Time);
            await journal.InitializeAsync();
            foreach (var action in plan!.Actions.OrderBy(item => item.SequenceNo))
            {
                if (action.State != PersistedModeActionState.Applied) continue;
                var started = await journal.BeginVerifyAsync(transitionId, action.ActionId, action.Revision, context.Lease.LeaseId.Value, context.Lease.FenceToken, context.OperationId);
                Assert.AreEqual(ModeActionAdvanceDisposition.Advanced, started.Disposition, started.Detail);
                var verified = await journal.RecordVerifyResultAsync(transitionId, action.ActionId, started.Action!.Revision, context.Lease.LeaseId.Value, context.Lease.FenceToken, context.OperationId, PersistedModeVerifyResult.Verified);
                Assert.AreEqual(ModeActionAdvanceDisposition.Advanced, verified.Disposition, verified.Detail);
            }
            mandatoryVerified = true;
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

        const string controlSessionKey = "console-session-atomic-commit";
        var activationEpochId = Guid.NewGuid();
        if (sourceMode != "NONE")
        {
            await SeedManagedSourceAsync(db, sourceMode, controlSessionKey, activationEpochId);
        }

        var source = await machine.GetOperationalModeAsync();
        Assert.AreEqual(sourceMode, source.CommittedMode);
        if (sourceMode != "NONE")
        {
            Assert.AreEqual(controlSessionKey, source.ControlSessionKey);
            Assert.AreEqual(activationEpochId, source.ActivationEpochId);
            Assert.IsNotNull(source.PolicyIdentity);
        }

        var leases = new MachineMutationLeaseStore(db, marker, quarantineMarker, time);
        await leases.InitializeAsync();
        var operationId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
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
            source.Revision,
            activationEpochId);
    }

    private static async Task SeedManagedSourceAsync(
        string databasePath,
        string sourceMode,
        string controlSessionKey,
        Guid activationEpochId)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ConnectionString);
        await connection.OpenAsync();
        var now = StartUtc.ToString("O");
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE operational_mode_state
            SET committed_mode = $mode,
                committed_utc = $now,
                committed_by_operation_id = $operation,
                correlation_id = $correlation,
                revision = 2,
                updated_utc = $now,
                control_session_key = $session,
                activation_epoch_id = $epoch,
                policy_catalog_id = 'mode-policy.source',
                policy_version = 1,
                policy_release_id = 'development',
                policy_catalog_digest = $catalog_digest,
                policy_target = $mode,
                resolved_policy_digest = $resolved_digest
            WHERE singleton_id = 1;
            """;
        command.Parameters.AddWithValue("$mode", sourceMode);
        command.Parameters.AddWithValue("$now", now);
        command.Parameters.AddWithValue("$operation", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("$correlation", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("$session", controlSessionKey);
        command.Parameters.AddWithValue("$epoch", activationEpochId.ToString("D"));
        command.Parameters.AddWithValue("$catalog_digest", new string('c', 64));
        command.Parameters.AddWithValue("$resolved_digest", new string('d', 64));
        Assert.AreEqual(1, await command.ExecuteNonQueryAsync());
        await connection.DisposeAsync();
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
        int SourceModeRevision,
        Guid ActivationEpochId);

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
        int TransitionRevision,
        Guid ActivationEpochId,
        PersistedModePolicyIdentity BoundPolicyIdentity,
        string BoundResolvedPolicyDigest)
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
            SourceModeRevision,
            ActivationEpochId);

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
