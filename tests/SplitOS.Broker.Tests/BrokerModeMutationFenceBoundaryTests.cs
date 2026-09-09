using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Broker.Service;
using SplitOS.Persistence.Machine;

namespace SplitOS.Broker.Tests;

[TestClass]
public sealed class BrokerModeMutationFenceBoundaryTests
{
    private string? _root;

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (_root is not null && Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public async Task CurrentApplyingActionExecutesExactlyOnce()
    {
        var fixture = await CreateApplyingFixtureAsync();
        var calls = 0;

        var outcome = await fixture.Boundary.ExecuteAsync(
            fixture.Context,
            _ =>
            {
                calls++;
                return ValueTask.FromResult("adapter-result");
            });

        Assert.AreEqual(BrokerModeMutationExecutionDisposition.Executed, outcome.Disposition);
        Assert.AreEqual("MODE_MUTATION_EXECUTED", outcome.ProductCode);
        Assert.AreEqual("adapter-result", outcome.Result);
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public async Task ActionMustAlreadyBeApplyingBeforeBrokerInvocation()
    {
        var fixture = await CreateApplyingFixtureAsync(markActionApplying: false);
        var calls = 0;

        var outcome = await fixture.Boundary.ExecuteAsync(
            fixture.Context with { ExpectedActionRevision = 1 },
            _ =>
            {
                calls++;
                return ValueTask.FromResult(true);
            });

        Assert.AreEqual(BrokerModeMutationExecutionDisposition.Rejected, outcome.Disposition);
        Assert.AreEqual("MODE_ACTION_NOT_APPLYING", outcome.ProductCode);
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public async Task StaleActionRevisionCannotInvokePrivilegedAdapter()
    {
        var fixture = await CreateApplyingFixtureAsync();
        var calls = 0;

        var outcome = await fixture.Boundary.ExecuteAsync(
            fixture.Context with { ExpectedActionRevision = fixture.Context.ExpectedActionRevision - 1 },
            _ =>
            {
                calls++;
                return ValueTask.FromResult(true);
            });

        Assert.AreEqual(BrokerModeMutationExecutionDisposition.Rejected, outcome.Disposition);
        Assert.AreEqual("MODE_ACTION_REVISION_CONFLICT", outcome.ProductCode);
        Assert.AreEqual(fixture.Context.ExpectedActionRevision, outcome.Detail is null ? fixture.Context.ExpectedActionRevision : fixture.Context.ExpectedActionRevision);
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public async Task ExpiredLeaseRequiresReconciliationAndNeverInvokesAdapter()
    {
        var fixture = await CreateApplyingFixtureAsync(leaseLifetime: TimeSpan.FromSeconds(30));
        fixture.Time.Advance(TimeSpan.FromSeconds(31));
        var calls = 0;

        var outcome = await fixture.Boundary.ExecuteAsync(
            fixture.Context,
            _ =>
            {
                calls++;
                return ValueTask.FromResult(true);
            });

        Assert.AreEqual(BrokerModeMutationExecutionDisposition.Rejected, outcome.Disposition);
        Assert.AreEqual("MUTATION_LEASE_RECONCILIATION_REQUIRED", outcome.ProductCode);
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public async Task NewerLeaseOwnerFencesOutPreviouslyAuthorizedRuntimeContext()
    {
        var fixture = await CreateApplyingFixtureAsync();

        var released = await fixture.Leases.ReleaseAsync(
            fixture.Context.LeaseId,
            fixture.Context.FenceToken,
            fixture.Context.OperationId);
        Assert.AreEqual(MachineMutationLeaseReleaseDisposition.Released, released.Disposition);

        var newer = await fixture.Leases.TryAcquireAsync(
            MachineMutationType.Mode,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "console-session-new-owner",
            TimeSpan.FromMinutes(1));
        Assert.AreEqual(MachineMutationLeaseAcquireDisposition.Acquired, newer.Disposition);
        Assert.IsTrue(newer.Lease.FenceToken > fixture.Context.FenceToken);

        var calls = 0;
        var outcome = await fixture.Boundary.ExecuteAsync(
            fixture.Context,
            _ =>
            {
                calls++;
                return ValueTask.FromResult(true);
            });

        Assert.AreEqual(BrokerModeMutationExecutionDisposition.Rejected, outcome.Disposition);
        Assert.AreEqual("MUTATION_LEASE_STALE_FENCE", outcome.ProductCode);
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public async Task CorrelationOrControlSessionMismatchFailsClosed()
    {
        var fixture = await CreateApplyingFixtureAsync();
        var calls = 0;

        var correlationMismatch = await fixture.Boundary.ExecuteAsync(
            fixture.Context with { CorrelationId = Guid.NewGuid() },
            _ =>
            {
                calls++;
                return ValueTask.FromResult(true);
            });
        Assert.AreEqual(BrokerModeMutationExecutionDisposition.Rejected, correlationMismatch.Disposition);
        Assert.AreEqual("MUTATION_LEASE_STALE_FENCE", correlationMismatch.ProductCode);

        var sessionMismatch = await fixture.Boundary.ExecuteAsync(
            fixture.Context with { ControlSessionKey = "different-session" },
            _ =>
            {
                calls++;
                return ValueTask.FromResult(true);
            });
        Assert.AreEqual(BrokerModeMutationExecutionDisposition.Rejected, sessionMismatch.Disposition);
        Assert.AreEqual("MUTATION_LEASE_STALE_FENCE", sessionMismatch.ProductCode);
        Assert.AreEqual(0, calls);
    }

    private async Task<Fixture> CreateApplyingFixtureAsync(
        bool markActionApplying = true,
        TimeSpan? leaseLifetime = null)
    {
        _root = Path.Combine(Path.GetTempPath(), "SplitOS.Broker.Fence.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var db = Path.Combine(_root, "machine.db");
        var marker = Path.Combine(_root, "marker");
        var quarantineMarker = Path.Combine(_root, "quarantine-marker.json");
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 9, 9, 9, 0, 0, TimeSpan.Zero));

        var machine = new MachineStateStore(
            db,
            marker,
            Path.Combine(_root, "maintenance", "backups"),
            Path.Combine(_root, "maintenance", "quarantine"),
            quarantineMarker);
        await machine.InitializeAsync();

        var operationId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        const string controlSessionKey = "console-session-fence-test";

        var leases = new MachineMutationLeaseStore(db, marker, quarantineMarker, time);
        await leases.InitializeAsync();
        var acquired = await leases.TryAcquireAsync(
            MachineMutationType.Mode,
            operationId,
            correlationId,
            controlSessionKey,
            leaseLifetime ?? TimeSpan.FromMinutes(1));
        Assert.AreEqual(MachineMutationLeaseAcquireDisposition.Acquired, acquired.Disposition);
        var lease = acquired.Lease;

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
            controlSessionKey,
            lease.LeaseId!.Value,
            lease.FenceToken);
        Assert.AreEqual(ModeTransitionCreateDisposition.Created, created.Disposition);
        var revision = created.Transition!.Revision;

        revision = await AdvanceAsync(
            transitions, transitionId, revision, lease, operationId,
            PersistedModeTransitionState.Inspecting,
            PersistedModeTransitionStage.InspectionComplete);
        revision = await AdvanceAsync(
            transitions, transitionId, revision, lease, operationId,
            PersistedModeTransitionState.Resolving,
            PersistedModeTransitionStage.ResolutionStarted);

        var policies = new ModeTransitionPolicyStore(db, marker, quarantineMarker, time);
        await policies.InitializeAsync();
        var bound = await policies.BindResolvedPolicyAsync(
            transitionId,
            revision,
            lease.LeaseId.Value,
            lease.FenceToken,
            operationId,
            new PersistedModePolicyIdentity(
                "mode-policy.fence-test",
                1,
                "development",
                new string('a', 64)),
            PersistedModePolicyTarget.Work,
            new string('b', 64));
        Assert.AreEqual(ModeTransitionPolicyBindDisposition.Bound, bound.Disposition, bound.Detail);
        revision = bound.Binding!.TransitionRevision;

        var actionId = Guid.NewGuid();
        var plans = new ModeTransitionActionPlanStore(db, marker, quarantineMarker, time);
        await plans.InitializeAsync();
        var persisted = await plans.PersistActionPlanAsync(
            transitionId,
            revision,
            lease.LeaseId.Value,
            lease.FenceToken,
            operationId,
            [new PersistedModeActionDefinition(
                actionId,
                400,
                "machine-managed-components",
                "managed-component.apply",
                "component.fence-test",
                1,
                "{}",
                "44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a",
                true,
                "restore_pre_state",
                "managed-component.actual-state")]);
        Assert.AreEqual(ModeTransitionActionPlanPersistDisposition.Persisted, persisted.Disposition, persisted.Detail);
        revision = persisted.Plan!.TransitionRevision;

        revision = await AdvanceAsync(
            transitions, transitionId, revision, lease, operationId,
            PersistedModeTransitionState.Resolving,
            PersistedModeTransitionStage.ActionPlanReady);
        _ = await AdvanceAsync(
            transitions, transitionId, revision, lease, operationId,
            PersistedModeTransitionState.Applying,
            PersistedModeTransitionStage.ApplyStarted);

        var actionRevision = 1;
        if (markActionApplying)
        {
            actionRevision = await MarkActionApplyingAsync(db, actionId, time.GetUtcNow());
        }

        var fenceStore = new ModeMutationFenceStore(db, marker, quarantineMarker, time);
        await fenceStore.InitializeAsync();
        var boundary = new BrokerModeMutationFenceBoundary(fenceStore);
        var context = new ModeMutationFenceContext(
            transitionId,
            actionId,
            lease.LeaseId.Value,
            lease.FenceToken,
            operationId,
            correlationId,
            controlSessionKey,
            actionRevision);

        return new Fixture(boundary, context, leases, time);
    }

    private static async Task<int> AdvanceAsync(
        ModeTransitionStore transitions,
        Guid transitionId,
        int revision,
        MachineMutationLeaseRecord lease,
        Guid operationId,
        PersistedModeTransitionState state,
        PersistedModeTransitionStage stage)
    {
        var outcome = await transitions.AdvanceAsync(
            transitionId,
            revision,
            lease.LeaseId!.Value,
            lease.FenceToken,
            operationId,
            state,
            stage,
            mandatoryVerified: false);
        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, outcome.Disposition, outcome.Detail);
        return outcome.Transition!.Revision;
    }

    private static async Task<int> MarkActionApplyingAsync(
        string databasePath,
        Guid actionId,
        DateTimeOffset now)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ConnectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE mode_transition_action
            SET action_state = 'APPLYING',
                started_utc = $now,
                updated_utc = $now,
                revision = revision + 1
            WHERE action_id = $action AND action_state = 'PLANNED';
            """;
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.Parameters.AddWithValue("$action", actionId.ToString("D"));
        Assert.AreEqual(1, await command.ExecuteNonQueryAsync());

        command = connection.CreateCommand();
        command.CommandText = "SELECT revision FROM mode_transition_action WHERE action_id=$action;";
        command.Parameters.AddWithValue("$action", actionId.ToString("D"));
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private sealed record Fixture(
        BrokerModeMutationFenceBoundary Boundary,
        ModeMutationFenceContext Context,
        MachineMutationLeaseStore Leases,
        MutableTimeProvider Time);

    private sealed class MutableTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtc;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
