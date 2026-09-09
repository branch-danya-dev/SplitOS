using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Broker.Service;
using SplitOS.Contracts.Protocol;
using SplitOS.Persistence.Machine;

namespace SplitOS.Broker.Tests;

[TestClass]
public sealed class BrokerManagedServiceSnapshotExecutorTests
{
    private string? _root;

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (_root is not null && Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public async Task StableSnapshotFeedsExactBeginApplyPreState()
    {
        var fixture = await CreateFixtureAsync();
        var adapter = new FakeAdapter(ManagedServiceObservedState.Running);
        var executor = CreateExecutor(fixture, adapter);

        var result = await executor.ExecuteAsync(
            fixture.OperationId,
            fixture.CorrelationId,
            fixture.Request);

        Assert.AreEqual("CAPTURED", result.Disposition);
        Assert.AreEqual("SERVICE_PRESTATE_CAPTURED", result.ProductCode);
        Assert.AreEqual(1, adapter.QueryCalls);
        Assert.IsNotNull(result.PreStateJson);
        Assert.IsNotNull(result.PreStateDigest);
        Assert.AreEqual("RUNNING", result.Entries.Single().ActualState);
        Assert.AreEqual(
            ManagedServicePolicyActionContract.ComputePreStateDigest(result.Entries),
            result.PreStateDigest);

        var journal = new ModeTransitionActionJournalStore(
            fixture.DatabasePath,
            fixture.MarkerPath,
            fixture.QuarantineMarkerPath,
            fixture.Time);
        await journal.InitializeAsync();
        var applying = await journal.BeginApplyAsync(
            fixture.Request.TransitionId,
            fixture.Request.ActionId,
            fixture.Request.ExpectedActionRevision,
            fixture.Request.LeaseId,
            fixture.Request.FenceToken,
            fixture.OperationId,
            result.PreStateJson,
            result.PreStateDigest);

        Assert.AreEqual(ModeActionAdvanceDisposition.Advanced, applying.Disposition, applying.Detail);
        Assert.AreEqual(PersistedModeActionState.Applying, applying.Action?.State);
        Assert.AreEqual(result.PreStateJson, applying.Action?.PreStateJson);
        Assert.AreEqual(result.PreStateDigest, applying.Action?.PreStateDigest);
    }

    [TestMethod]
    public async Task DifferentDesiredStateCannotBorrowPlannedActionForSnapshot()
    {
        var fixture = await CreateFixtureAsync();
        var adapter = new FakeAdapter(ManagedServiceObservedState.Running);
        var executor = CreateExecutor(fixture, adapter);
        var changed = fixture.Request with
        {
            Entries = [new ManagedServicePolicyEntry("SEARCH_INDEXER", "RUNNING")]
        };

        var result = await executor.ExecuteAsync(
            fixture.OperationId,
            fixture.CorrelationId,
            changed);

        Assert.AreEqual("REJECTED", result.Disposition);
        Assert.AreEqual("MODE_PRESTATE_ACTION_SEMANTICS_MISMATCH", result.ProductCode);
        Assert.AreEqual(0, adapter.QueryCalls);
        Assert.IsNull(result.PreStateJson);
    }

    [TestMethod]
    public async Task StaleFenceNeverReadsScmState()
    {
        var fixture = await CreateFixtureAsync();
        var adapter = new FakeAdapter(ManagedServiceObservedState.Running);
        var executor = CreateExecutor(fixture, adapter);
        var stale = fixture.Request with { FenceToken = fixture.Request.FenceToken + 1 };

        var result = await executor.ExecuteAsync(
            fixture.OperationId,
            fixture.CorrelationId,
            stale);

        Assert.AreEqual("REJECTED", result.Disposition);
        Assert.AreEqual("MUTATION_LEASE_STALE_FENCE", result.ProductCode);
        Assert.AreEqual(0, adapter.QueryCalls);
    }

    [TestMethod]
    public async Task TransitionalOrUnknownStateCannotBecomeRollbackPreState()
    {
        var fixture = await CreateFixtureAsync();
        var adapter = new FakeAdapter(ManagedServiceObservedState.StopPending);
        var executor = CreateExecutor(fixture, adapter);

        var result = await executor.ExecuteAsync(
            fixture.OperationId,
            fixture.CorrelationId,
            fixture.Request);

        Assert.AreEqual("FAILED", result.Disposition);
        Assert.AreEqual("SERVICE_PRESTATE_NOT_STABLE", result.ProductCode);
        Assert.AreEqual(1, adapter.QueryCalls);
        Assert.IsNull(result.PreStateJson);
        Assert.IsNull(result.PreStateDigest);
    }

    [TestMethod]
    public async Task SnapshotIsRejectedAfterBeginApply()
    {
        var fixture = await CreateFixtureAsync();
        var adapter = new FakeAdapter(ManagedServiceObservedState.Running);
        var executor = CreateExecutor(fixture, adapter);
        var initial = await executor.ExecuteAsync(
            fixture.OperationId,
            fixture.CorrelationId,
            fixture.Request);
        Assert.AreEqual("CAPTURED", initial.Disposition);

        var journal = new ModeTransitionActionJournalStore(
            fixture.DatabasePath,
            fixture.MarkerPath,
            fixture.QuarantineMarkerPath,
            fixture.Time);
        await journal.InitializeAsync();
        var applying = await journal.BeginApplyAsync(
            fixture.Request.TransitionId,
            fixture.Request.ActionId,
            1,
            fixture.Request.LeaseId,
            fixture.Request.FenceToken,
            fixture.OperationId,
            initial.PreStateJson,
            initial.PreStateDigest);
        Assert.AreEqual(ModeActionAdvanceDisposition.Advanced, applying.Disposition);

        var replaySnapshotRequest = fixture.Request with { ExpectedActionRevision = applying.Action!.Revision };
        var denied = await executor.ExecuteAsync(
            fixture.OperationId,
            fixture.CorrelationId,
            replaySnapshotRequest);

        Assert.AreEqual("REJECTED", denied.Disposition);
        Assert.AreEqual("MODE_PRESTATE_ACTION_NOT_PLANNED", denied.ProductCode);
        Assert.AreEqual(1, adapter.QueryCalls, "No second SCM read should occur after BeginApply.");
    }

    [TestMethod]
    public async Task WireCapabilityReturnsCanonicalPreState()
    {
        var fixture = await CreateFixtureAsync();
        var adapter = new FakeAdapter(ManagedServiceObservedState.Running);
        var snapshot = CreateExecutor(fixture, adapter);
        var handler = new BrokerMessageHandler(fixture.Machine, null, snapshot);
        var wire = WireMessage.Create(
            MessageTypes.MachineServicePolicySnapshotRequest,
            fixture.Request,
            Capabilities.MachineServicePolicySnapshot,
            fixture.OperationId,
            fixture.CorrelationId);

        var response = await handler.HandleAsync(wire, CancellationToken.None);
        var result = response.ReadPayload<MachineServicePolicySnapshotResult>();

        Assert.AreEqual(MessageTypes.MachineServicePolicySnapshotResult, response.MessageType);
        Assert.AreEqual("CAPTURED", result.Disposition);
        Assert.IsNotNull(result.PreStateJson);
        Assert.IsNotNull(result.PreStateDigest);
    }

    private BrokerManagedServiceSnapshotExecutor CreateExecutor(Fixture fixture, IManagedServiceAdapter adapter)
    {
        var evidence = new ModePreMutationEvidenceStore(
            fixture.DatabasePath,
            fixture.MarkerPath,
            fixture.QuarantineMarkerPath,
            fixture.Time);
        evidence.InitializeAsync().GetAwaiter().GetResult();
        return new BrokerManagedServiceSnapshotExecutor(
            evidence,
            new TestCatalog(),
            adapter);
    }

    private async Task<Fixture> CreateFixtureAsync()
    {
        _root = Path.Combine(Path.GetTempPath(), "SplitOS.Broker.ServicePreState.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var db = Path.Combine(_root, "machine.db");
        var marker = Path.Combine(_root, "machine-store.initialized");
        var quarantineMarker = Path.Combine(_root, "machine-store.quarantined.json");
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero));
        var machine = new MachineStateStore(
            db,
            marker,
            Path.Combine(_root, "maintenance", "backups"),
            Path.Combine(_root, "maintenance", "quarantine"),
            quarantineMarker);
        await machine.InitializeAsync();

        var operationId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        const string session = "console-session-service-prestate";
        var leases = new MachineMutationLeaseStore(db, marker, quarantineMarker, time);
        await leases.InitializeAsync();
        var acquired = await leases.TryAcquireAsync(
            MachineMutationType.Mode,
            operationId,
            correlationId,
            session,
            TimeSpan.FromMinutes(2));
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
            session,
            lease.LeaseId!.Value,
            lease.FenceToken);
        Assert.AreEqual(ModeTransitionCreateDisposition.Created, created.Disposition);
        var revision = created.Transition!.Revision;

        revision = await AdvanceAsync(transitions, transitionId, revision, lease, operationId,
            PersistedModeTransitionState.Inspecting, PersistedModeTransitionStage.InspectionComplete);
        revision = await AdvanceAsync(transitions, transitionId, revision, lease, operationId,
            PersistedModeTransitionState.Resolving, PersistedModeTransitionStage.ResolutionStarted);

        var policies = new ModeTransitionPolicyStore(db, marker, quarantineMarker, time);
        await policies.InitializeAsync();
        var bound = await policies.BindResolvedPolicyAsync(
            transitionId,
            revision,
            lease.LeaseId.Value,
            lease.FenceToken,
            operationId,
            new PersistedModePolicyIdentity("mode-policy.service-prestate-test", 1, "development", new string('a', 64)),
            PersistedModePolicyTarget.Work,
            new string('b', 64));
        Assert.AreEqual(ModeTransitionPolicyBindDisposition.Bound, bound.Disposition, bound.Detail);
        revision = bound.Binding!.TransitionRevision;

        var entries = new[] { new ManagedServicePolicyEntry("SEARCH_INDEXER", "STOPPED") };
        var desiredJson = ManagedServicePolicyActionContract.SerializeDesiredState(entries);
        var desiredDigest = ManagedServicePolicyActionContract.ComputeDesiredStateDigest(entries);
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
                ManagedServicePolicyActionContract.OwningModule,
                ManagedServicePolicyActionContract.ActionType,
                ManagedServicePolicyActionContract.TargetRef,
                ManagedServicePolicyActionContract.DesiredSchemaVersion,
                desiredJson,
                desiredDigest,
                true,
                "restore_pre_state",
                "service.actual-state")]);
        Assert.AreEqual(ModeTransitionActionPlanPersistDisposition.Persisted, persisted.Disposition, persisted.Detail);
        revision = persisted.Plan!.TransitionRevision;

        revision = await AdvanceAsync(transitions, transitionId, revision, lease, operationId,
            PersistedModeTransitionState.Resolving, PersistedModeTransitionStage.ActionPlanReady);
        _ = await AdvanceAsync(transitions, transitionId, revision, lease, operationId,
            PersistedModeTransitionState.Applying, PersistedModeTransitionStage.ApplyStarted);

        var request = new MachineServicePolicySnapshotRequest(
            transitionId,
            actionId,
            lease.LeaseId.Value,
            lease.FenceToken,
            session,
            1,
            entries);

        return new Fixture(machine, db, marker, quarantineMarker, time, operationId, correlationId, request);
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

    private sealed record Fixture(
        MachineStateStore Machine,
        string DatabasePath,
        string MarkerPath,
        string QuarantineMarkerPath,
        MutableTimeProvider Time,
        Guid OperationId,
        Guid CorrelationId,
        MachineServicePolicySnapshotRequest Request);

    private sealed class TestCatalog : IManagedServiceCatalog
    {
        private static readonly ManagedServiceCatalogEntry Entry = new(
            "SEARCH_INDEXER",
            "fixture-SEARCH_INDEXER",
            new HashSet<ManagedServiceDesiredState>
            {
                ManagedServiceDesiredState.Running,
                ManagedServiceDesiredState.Stopped
            },
            TimeSpan.FromSeconds(1),
            0);

        public bool TryResolve(string managedServiceId, out ManagedServiceCatalogEntry entry)
        {
            if (string.Equals(managedServiceId, Entry.ManagedServiceId, StringComparison.Ordinal))
            {
                entry = Entry;
                return true;
            }
            entry = null!;
            return false;
        }
    }

    private sealed class FakeAdapter(ManagedServiceObservedState state) : IManagedServiceAdapter
    {
        public int QueryCalls { get; private set; }

        public ValueTask<ManagedServiceObservation> QueryAsync(
            ManagedServiceCatalogEntry entry,
            CancellationToken cancellationToken = default)
        {
            QueryCalls++;
            return ValueTask.FromResult(new ManagedServiceObservation(state));
        }

        public ValueTask<ManagedServiceTechnicalResult> ApplyAsync(
            ManagedServiceCatalogEntry entry,
            ManagedServiceDesiredState desiredState,
            CancellationToken cancellationToken = default)
            => throw new AssertFailedException("Snapshot executor must never invoke ApplyAsync.");
    }

    private sealed class MutableTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private readonly DateTimeOffset _utcNow = initialUtc;
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
