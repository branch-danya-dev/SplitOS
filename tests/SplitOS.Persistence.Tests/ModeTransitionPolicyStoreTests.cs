using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.Machine;

namespace SplitOS.Persistence.Tests;

[TestClass]
public sealed class ModeTransitionPolicyStoreTests
{
    private static readonly DateTimeOffset StartUtc =
        new(2026, 9, 9, 8, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task BindPersistsImmutableResolvedPolicyAndReplaysIdempotently()
    {
        using var storage = new TestStorage();
        var context = await CreateResolvingContextAsync(storage, "NONE", "WORK");
        var identity = Identity('a');
        var fallbacks = new[]
        {
            new PersistedModePolicyFallbackSelection(
                "display.primary",
                PersistedModePolicyFallbackClass.ApprovedAlternate,
                "display.secondary")
        };

        var bound = await context.Policies.BindResolvedPolicyAsync(
            context.TransitionId,
            context.TransitionRevision,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId,
            identity,
            PersistedModePolicyTarget.Work,
            Digest('b'),
            fallbacks);

        Assert.AreEqual(ModeTransitionPolicyBindDisposition.Bound, bound.Disposition, bound.Detail);
        Assert.AreEqual(context.TransitionRevision + 1, bound.Binding?.TransitionRevision);
        Assert.AreEqual(PersistedModePolicyTarget.Work, bound.Binding?.Target);
        Assert.AreEqual(1, bound.Binding?.SelectedFallbacks.Count);

        var replay = await context.Policies.BindResolvedPolicyAsync(
            context.TransitionId,
            context.TransitionRevision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            identity,
            PersistedModePolicyTarget.Work,
            Digest('b'),
            fallbacks);
        Assert.AreEqual(ModeTransitionPolicyBindDisposition.Replayed, replay.Disposition);
        AssertBindingEquivalent(bound.Binding!, replay.Binding);

        SqliteConnection.ClearAllPools();
        var reopened = new ModeTransitionPolicyStore(
            context.DatabasePath,
            context.MarkerPath,
            context.QuarantineMarkerPath,
            context.Time);
        await reopened.InitializeAsync();
        var restored = await reopened.GetAsync(context.TransitionId);
        AssertBindingEquivalent(bound.Binding!, restored);
    }

    [TestMethod]
    public async Task ExistingBindingCannotBeReplacedByDifferentResolvedSnapshot()
    {
        using var storage = new TestStorage();
        var context = await CreateResolvingContextAsync(storage, "NONE", "GAME");

        var first = await context.Policies.BindResolvedPolicyAsync(
            context.TransitionId,
            context.TransitionRevision,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId,
            Identity('a'),
            PersistedModePolicyTarget.Game,
            Digest('b'));
        Assert.AreEqual(ModeTransitionPolicyBindDisposition.Bound, first.Disposition);

        var conflict = await context.Policies.BindResolvedPolicyAsync(
            context.TransitionId,
            context.TransitionRevision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            Identity('a'),
            PersistedModePolicyTarget.Game,
            Digest('c'));

        Assert.AreEqual(ModeTransitionPolicyBindDisposition.BindingConflict, conflict.Disposition);
        Assert.AreEqual("MODE_POLICY_BINDING_CONFLICT", conflict.ProductCode);
        Assert.AreEqual(Digest('b'), (await context.Policies.GetAsync(context.TransitionId))?.ResolvedDigest);
    }

    [TestMethod]
    public async Task BindRejectsTargetMismatchStaleFenceAndExpiredLease()
    {
        using var mismatchStorage = new TestStorage();
        var mismatch = await CreateResolvingContextAsync(mismatchStorage, "NONE", "WORK");
        var wrongTarget = await mismatch.Policies.BindResolvedPolicyAsync(
            mismatch.TransitionId,
            mismatch.TransitionRevision,
            mismatch.Lease.LeaseId!.Value,
            mismatch.Lease.FenceToken,
            mismatch.OperationId,
            Identity('a'),
            PersistedModePolicyTarget.Game,
            Digest('b'));
        Assert.AreEqual(ModeTransitionPolicyBindDisposition.TargetMismatch, wrongTarget.Disposition);
        Assert.IsNull(await mismatch.Policies.GetAsync(mismatch.TransitionId));

        using var fenceStorage = new TestStorage();
        var stale = await CreateResolvingContextAsync(fenceStorage, "NONE", "WORK");
        var staleFence = await stale.Policies.BindResolvedPolicyAsync(
            stale.TransitionId,
            stale.TransitionRevision,
            stale.Lease.LeaseId!.Value,
            stale.Lease.FenceToken + 1,
            stale.OperationId,
            Identity('a'),
            PersistedModePolicyTarget.Work,
            Digest('b'));
        Assert.AreEqual(ModeTransitionPolicyBindDisposition.LeaseConflict, staleFence.Disposition);
        Assert.AreEqual("MUTATION_LEASE_STALE_FENCE", staleFence.ProductCode);

        using var expiredStorage = new TestStorage();
        var expired = await CreateResolvingContextAsync(
            expiredStorage,
            "NONE",
            "WORK",
            TimeSpan.FromSeconds(30));
        expired.Time.Advance(TimeSpan.FromSeconds(31));
        var expiredLease = await expired.Policies.BindResolvedPolicyAsync(
            expired.TransitionId,
            expired.TransitionRevision,
            expired.Lease.LeaseId!.Value,
            expired.Lease.FenceToken,
            expired.OperationId,
            Identity('a'),
            PersistedModePolicyTarget.Work,
            Digest('b'));
        Assert.AreEqual(ModeTransitionPolicyBindDisposition.ReconciliationRequired, expiredLease.Disposition);
        Assert.AreEqual("MUTATION_LEASE_RECONCILIATION_REQUIRED", expiredLease.ProductCode);
    }

    [TestMethod]
    public async Task ActionPlanReadyRequiresDurablePolicyBinding()
    {
        using var storage = new TestStorage();
        var context = await CreateResolvingContextAsync(storage, "NONE", "WORK");

        var denied = await context.Transitions.AdvanceAsync(
            context.TransitionId,
            context.TransitionRevision,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeTransitionState.Resolving,
            PersistedModeTransitionStage.ActionPlanReady,
            mandatoryVerified: false);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.InvalidLifecycle, denied.Disposition);
        StringAssert.Contains(denied.Detail, "Durable resolved policy binding");

        var bound = await context.Policies.BindResolvedPolicyAsync(
            context.TransitionId,
            context.TransitionRevision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            Identity('a'),
            PersistedModePolicyTarget.Work,
            Digest('b'));
        Assert.AreEqual(ModeTransitionPolicyBindDisposition.Bound, bound.Disposition);

        var missingPlan = await context.Transitions.AdvanceAsync(
            context.TransitionId,
            bound.Binding!.TransitionRevision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeTransitionState.Resolving,
            PersistedModeTransitionStage.ActionPlanReady,
            mandatoryVerified: false);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.InvalidLifecycle, missingPlan.Disposition);
        StringAssert.Contains(missingPlan.Detail, "action plan");

        var plans = new ModeTransitionActionPlanStore(
            context.DatabasePath,
            context.MarkerPath,
            context.QuarantineMarkerPath,
            context.Time);
        await plans.InitializeAsync();
        var persisted = await plans.PersistActionPlanAsync(
            context.TransitionId,
            bound.Binding.TransitionRevision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            [new PersistedModeActionDefinition(
                Guid.NewGuid(), 100, "test", "noop.prepare", null, 1, "{}",
                "44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a",
                true, "no_mutation", "test.ready")]);
        Assert.AreEqual(ModeTransitionActionPlanPersistDisposition.Persisted, persisted.Disposition, persisted.Detail);

        var ready = await context.Transitions.AdvanceAsync(
            context.TransitionId,
            persisted.Plan!.TransitionRevision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            PersistedModeTransitionState.Resolving,
            PersistedModeTransitionStage.ActionPlanReady,
            mandatoryVerified: false);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, ready.Disposition, ready.Detail);
    }

    [TestMethod]
    public async Task DeactivateToNoneRequiresBasePolicyTarget()
    {
        using var storage = new TestStorage();
        var context = await CreateResolvingContextAsync(storage, "WORK", "NONE");

        var wrong = await context.Policies.BindResolvedPolicyAsync(
            context.TransitionId,
            context.TransitionRevision,
            context.Lease.LeaseId!.Value,
            context.Lease.FenceToken,
            context.OperationId,
            Identity('a'),
            PersistedModePolicyTarget.Work,
            Digest('b'));
        Assert.AreEqual(ModeTransitionPolicyBindDisposition.TargetMismatch, wrong.Disposition);

        var correct = await context.Policies.BindResolvedPolicyAsync(
            context.TransitionId,
            context.TransitionRevision,
            context.Lease.LeaseId.Value,
            context.Lease.FenceToken,
            context.OperationId,
            Identity('a'),
            PersistedModePolicyTarget.Base,
            Digest('c'));
        Assert.AreEqual(ModeTransitionPolicyBindDisposition.Bound, correct.Disposition, correct.Detail);
    }

    private static void AssertBindingEquivalent(
        ModeTransitionPolicyBinding expected,
        ModeTransitionPolicyBinding? actual)
    {
        Assert.IsNotNull(actual);
        Assert.AreEqual(expected.TransitionId, actual.TransitionId);
        Assert.AreEqual(expected.Identity, actual.Identity);
        Assert.AreEqual(expected.Target, actual.Target);
        Assert.AreEqual(expected.ResolvedDigest, actual.ResolvedDigest);
        Assert.AreEqual(expected.BoundUtc, actual.BoundUtc);
        Assert.AreEqual(expected.TransitionRevision, actual.TransitionRevision);
        CollectionAssert.AreEqual(
            expected.SelectedFallbacks.ToArray(),
            actual.SelectedFallbacks.ToArray());
    }

    private static PersistedModePolicyIdentity Identity(char digestCharacter)
        => new("mode-policy.v1", 7, "development", Digest(digestCharacter));

    private static string Digest(char character) => new(character, 64);

    private static async Task<TestContext> CreateResolvingContextAsync(
        TestStorage storage,
        string sourceMode,
        string targetMode,
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

        const string controlSessionKey = "console-session-policy-binding";
        if (sourceMode != "NONE")
        {
            await SeedManagedSourceAsync(
                db,
                sourceMode,
                controlSessionKey,
                Guid.NewGuid());
        }

        var source = await machine.GetOperationalModeAsync();
        Assert.AreEqual(sourceMode, source.CommittedMode);
        if (sourceMode != "NONE")
        {
            Assert.AreEqual(controlSessionKey, source.ControlSessionKey);
            Assert.IsNotNull(source.ActivationEpochId);
            Assert.IsNotNull(source.PolicyIdentity);
            Assert.IsNotNull(source.ResolvedPolicyDigest);
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
        var kind = (sourceMode, targetMode) switch
        {
            ("NONE", "WORK" or "GAME") => PersistedModeOperationKind.Activate,
            ("WORK", "GAME") or ("GAME", "WORK") => PersistedModeOperationKind.Switch,
            ("WORK" or "GAME", "NONE") => PersistedModeOperationKind.Deactivate,
            _ => throw new InvalidOperationException("Unsupported test transition tuple.")
        };
        var transitionId = Guid.NewGuid();
        var created = await transitions.CreateAsync(
            transitionId,
            operationId,
            correlationId,
            kind,
            sourceMode,
            targetMode,
            source.Revision,
            controlSessionKey,
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
            mandatoryVerified: false);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, inspecting.Disposition, inspecting.Detail);

        var resolving = await transitions.AdvanceAsync(
            transitionId,
            inspecting.Transition!.Revision,
            acquired.Lease.LeaseId.Value,
            acquired.Lease.FenceToken,
            operationId,
            PersistedModeTransitionState.Resolving,
            PersistedModeTransitionStage.ResolutionStarted,
            mandatoryVerified: false);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, resolving.Disposition, resolving.Detail);

        var policies = new ModeTransitionPolicyStore(db, marker, quarantineMarker, time);
        await policies.InitializeAsync();
        return new TestContext(
            db,
            marker,
            quarantineMarker,
            time,
            transitions,
            policies,
            acquired.Lease,
            operationId,
            transitionId,
            resolving.Transition!.Revision);
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

    private sealed record TestContext(
        string DatabasePath,
        string MarkerPath,
        string QuarantineMarkerPath,
        ManualTimeProvider Time,
        ModeTransitionStore Transitions,
        ModeTransitionPolicyStore Policies,
        MachineMutationLeaseRecord Lease,
        Guid OperationId,
        Guid TransitionId,
        int TransitionRevision);

    private sealed class ManualTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtc;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan amount) => _utcNow = _utcNow.Add(amount);
    }
}
