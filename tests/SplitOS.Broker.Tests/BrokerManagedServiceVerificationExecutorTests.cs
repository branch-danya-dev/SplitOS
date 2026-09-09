using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Broker.Service;
using SplitOS.Contracts.Protocol;
using SplitOS.Persistence.Machine;

namespace SplitOS.Broker.Tests;

[TestClass]
public sealed class BrokerManagedServiceVerificationExecutorTests
{
    private string? _root;

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (_root is not null && Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public async Task MatchingDurableVerifyActionReadsScmAndReturnsVerifiedWithoutMutation()
    {
        var fixture = await CreateFixtureAsync();
        var adapter = new FakeAdapter(static _ => ManagedServiceObservedState.Stopped);
        var executor = await CreateExecutorAsync(fixture, adapter);

        var result = await executor.ExecuteAsync(
            fixture.OperationId,
            fixture.CorrelationId,
            fixture.Request);

        Assert.AreEqual("VERIFIED", result.Disposition);
        Assert.AreEqual("SERVICE_POLICY_VERIFIED", result.ProductCode);
        Assert.AreEqual(1, adapter.Queries);
        Assert.AreEqual(0, adapter.ApplyCalls);
        var entry = result.Entries.Single();
        Assert.AreEqual("SEARCH_INDEXER", entry.ManagedServiceId);
        Assert.AreEqual("STOPPED", entry.DesiredState);
        Assert.AreEqual("STOPPED", entry.ActualStateObserved);
        Assert.AreEqual("VERIFIED", entry.VerificationStatus);
        Assert.IsNull(entry.ErrorCode);
    }

    [TestMethod]
    public async Task StableMismatchReturnsMismatchWithoutMutation()
    {
        var fixture = await CreateFixtureAsync();
        var adapter = new FakeAdapter(static _ => ManagedServiceObservedState.Running);
        var executor = await CreateExecutorAsync(fixture, adapter);

        var result = await executor.ExecuteAsync(
            fixture.OperationId,
            fixture.CorrelationId,
            fixture.Request);

        Assert.AreEqual("MISMATCH", result.Disposition);
        Assert.AreEqual("SERVICE_POLICY_VERIFY_MISMATCH", result.ProductCode);
        Assert.AreEqual(1, adapter.Queries);
        Assert.AreEqual(0, adapter.ApplyCalls);
        var entry = result.Entries.Single();
        Assert.AreEqual("RUNNING", entry.ActualStateObserved);
        Assert.AreEqual("MISMATCH", entry.VerificationStatus);
        Assert.AreEqual("STATE_MISMATCH", entry.ErrorCode);
    }

    [TestMethod]
    public async Task TransitionalObservationReturnsUnknownWithoutMutation()
    {
        var fixture = await CreateFixtureAsync();
        var adapter = new FakeAdapter(static _ => ManagedServiceObservedState.StartPending);
        var executor = await CreateExecutorAsync(fixture, adapter);

        var result = await executor.ExecuteAsync(
            fixture.OperationId,
            fixture.CorrelationId,
            fixture.Request);

        Assert.AreEqual("UNKNOWN", result.Disposition);
        Assert.AreEqual("SERVICE_POLICY_VERIFY_UNKNOWN", result.ProductCode);
        Assert.AreEqual(1, adapter.Queries);
        Assert.AreEqual(0, adapter.ApplyCalls);
        var entry = result.Entries.Single();
        Assert.AreEqual("UNKNOWN", entry.ActualStateObserved);
        Assert.AreEqual("UNKNOWN", entry.VerificationStatus);
        Assert.AreEqual("STATE_NOT_STABLE", entry.ErrorCode);
    }

    [TestMethod]
    public async Task StaleFenceRejectsBeforeAnyScmRead()
    {
        var fixture = await CreateFixtureAsync();
        var adapter = new FakeAdapter(static _ => ManagedServiceObservedState.Stopped);
        var executor = await CreateExecutorAsync(fixture, adapter);
        var stale = fixture.Request with { FenceToken = fixture.Request.FenceToken + 1 };

        var result = await executor.ExecuteAsync(
            fixture.OperationId,
            fixture.CorrelationId,
            stale);

        Assert.AreEqual("REJECTED", result.Disposition);
        Assert.AreEqual("MUTATION_LEASE_STALE_FENCE", result.ProductCode);
        Assert.AreEqual(0, adapter.Queries);
        Assert.AreEqual(0, adapter.ApplyCalls);
    }

    [TestMethod]
    public async Task StaleActionRevisionRejectsBeforeAnyScmRead()
    {
        var fixture = await CreateFixtureAsync();
        var adapter = new FakeAdapter(static _ => ManagedServiceObservedState.Stopped);
        var executor = await CreateExecutorAsync(fixture, adapter);
        var stale = fixture.Request with { ExpectedActionRevision = fixture.Request.ExpectedActionRevision + 1 };

        var result = await executor.ExecuteAsync(
            fixture.OperationId,
            fixture.CorrelationId,
            stale);

        Assert.AreEqual("REJECTED", result.Disposition);
        Assert.AreEqual("MODE_ACTION_REVISION_CONFLICT", result.ProductCode);
        Assert.AreEqual(0, adapter.Queries);
        Assert.AreEqual(0, adapter.ApplyCalls);
    }

    [TestMethod]
    public async Task WireCapabilityReturnsReadOnlyVerificationResult()
    {
        var fixture = await CreateFixtureAsync();
        var adapter = new FakeAdapter(static _ => ManagedServiceObservedState.Stopped);
        var executor = await CreateExecutorAsync(fixture, adapter);
        var handler = new BrokerMessageHandler(
            fixture.Machine,
            managedServiceVerificationExecutor: executor);
        var wire = WireMessage.Create(
            MessageTypes.MachineServicePolicyVerifyRequest,
            fixture.Request,
            Capabilities.MachineServicePolicyVerify,
            fixture.OperationId,
            fixture.CorrelationId);

        var response = await handler.HandleAsync(wire, CancellationToken.None);
        var result = response.ReadPayload<MachineServicePolicyVerifyResult>();

        Assert.AreEqual(MessageTypes.MachineServicePolicyVerifyResult, response.MessageType);
        Assert.AreEqual("VERIFIED", result.Disposition);
        Assert.AreEqual(1, adapter.Queries);
        Assert.AreEqual(0, adapter.ApplyCalls);
    }

    private static async Task<BrokerManagedServiceVerificationExecutor> CreateExecutorAsync(
        Fixture fixture,
        IManagedServiceAdapter adapter)
    {
        var evidence = new ModeVerificationEvidenceStore(
            fixture.DatabasePath,
            fixture.MarkerPath,
            fixture.QuarantineMarkerPath,
            fixture.Time);
        await evidence.InitializeAsync();
        return new BrokerManagedServiceVerificationExecutor(
            evidence,
            new TestCatalog("SEARCH_INDEXER"),
            adapter);
    }

    private async Task<Fixture> CreateFixtureAsync()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "SplitOS.Broker.ServiceVerify.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var db = Path.Combine(_root, "machine.db");
        var marker = Path.Combine(_root, "machine-store.initialized");
        var quarantineMarker = Path.Combine(_root, "machine-store.quarantined.json");
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 9, 10, 1, 0, 0, TimeSpan.Zero));
        var machine = new MachineStateStore(
            db,
            marker,
            Path.Combine(_root, "maintenance", "backups"),
            Path.Combine(_root, "maintenance", "quarantine"),
            quarantineMarker);
        await machine.InitializeAsync();

        var operationId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        const string controlSessionKey = "broker-managed-service-verify-session";

        var leases = new MachineMutationLeaseStore(db, marker, quarantineMarker, time);
        await leases.InitializeAsync();
        var acquired = await leases.TryAcquireAsync(
            MachineMutationType.Mode,
            operationId,
            correlationId,
            controlSessionKey,
            TimeSpan.FromMinutes(5));
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
        var transitionRevision = created.Transition!.Revision;

        transitionRevision = await AdvanceAsync(
            transitions,
            transitionId,
            transitionRevision,
            lease,
            operationId,
            PersistedModeTransitionState.Inspecting,
            PersistedModeTransitionStage.InspectionComplete);
        transitionRevision = await AdvanceAsync(
            transitions,
            transitionId,
            transitionRevision,
            lease,
            operationId,
            PersistedModeTransitionState.Resolving,
            PersistedModeTransitionStage.ResolutionStarted);

        var policyStore = new ModeTransitionPolicyStore(db, marker, quarantineMarker, time);
        await policyStore.InitializeAsync();
        var bound = await policyStore.BindResolvedPolicyAsync(
            transitionId,
            transitionRevision,
            lease.LeaseId.Value,
            lease.FenceToken,
            operationId,
            new PersistedModePolicyIdentity(
                "mode-policy.broker-managed-service-verify",
                1,
                "development",
                new string('a', 64)),
            PersistedModePolicyTarget.Work,
            new string('b', 64));
        Assert.AreEqual(ModeTransitionPolicyBindDisposition.Bound, bound.Disposition, bound.Detail);
        transitionRevision = bound.Binding!.TransitionRevision;

        var desired = new[] { new ManagedServicePolicyEntry("SEARCH_INDEXER", "STOPPED") };
        var desiredJson = ManagedServicePolicyActionContract.SerializeDesiredState(desired);
        var desiredDigest = ManagedServicePolicyActionContract.ComputeDesiredStateDigest(desired);
        var actionId = Guid.NewGuid();
        var planStore = new ModeTransitionActionPlanStore(db, marker, quarantineMarker, time);
        await planStore.InitializeAsync();
        var plan = await planStore.PersistActionPlanAsync(
            transitionId,
            transitionRevision,
            lease.LeaseId.Value,
            lease.FenceToken,
            operationId,
            [new PersistedModeActionDefinition(
                actionId,
                100,
                ManagedServicePolicyActionContract.OwningModule,
                ManagedServicePolicyActionContract.ActionType,
                ManagedServicePolicyActionContract.TargetRef,
                ManagedServicePolicyActionContract.DesiredSchemaVersion,
                desiredJson,
                desiredDigest,
                true,
                "restore_pre_state",
                "service.actual-state")]);
        Assert.AreEqual(ModeTransitionActionPlanPersistDisposition.Persisted, plan.Disposition, plan.Detail);
        transitionRevision = plan.Plan!.TransitionRevision;

        transitionRevision = await AdvanceAsync(
            transitions,
            transitionId,
            transitionRevision,
            lease,
            operationId,
            PersistedModeTransitionState.Resolving,
            PersistedModeTransitionStage.ActionPlanReady);
        transitionRevision = await AdvanceAsync(
            transitions,
            transitionId,
            transitionRevision,
            lease,
            operationId,
            PersistedModeTransitionState.Applying,
            PersistedModeTransitionStage.ApplyStarted);

        var journal = new ModeTransitionActionJournalStore(db, marker, quarantineMarker, time);
        await journal.InitializeAsync();
        var preState = new[] { new ManagedServicePreStateEntry("SEARCH_INDEXER", "RUNNING") };
        var applying = await journal.BeginApplyAsync(
            transitionId,
            actionId,
            1,
            lease.LeaseId.Value,
            lease.FenceToken,
            operationId,
            ManagedServicePolicyActionContract.SerializePreState(preState),
            ManagedServicePolicyActionContract.ComputePreStateDigest(preState));
        Assert.AreEqual(ModeActionAdvanceDisposition.Advanced, applying.Disposition, applying.Detail);

        var applied = await journal.RecordApplyResultAsync(
            transitionId,
            actionId,
            applying.Action!.Revision,
            lease.LeaseId.Value,
            lease.FenceToken,
            operationId,
            PersistedModeApplyResult.Applied);
        Assert.AreEqual(ModeActionAdvanceDisposition.Advanced, applied.Disposition, applied.Detail);
        Assert.AreEqual(3, applied.Action!.Revision);

        transitionRevision = await AdvanceAsync(
            transitions,
            transitionId,
            transitionRevision,
            lease,
            operationId,
            PersistedModeTransitionState.Applying,
            PersistedModeTransitionStage.ApplyComplete);
        _ = await AdvanceAsync(
            transitions,
            transitionId,
            transitionRevision,
            lease,
            operationId,
            PersistedModeTransitionState.Verifying,
            PersistedModeTransitionStage.VerifyStarted);

        var verifying = await journal.BeginVerifyAsync(
            transitionId,
            actionId,
            applied.Action.Revision,
            lease.LeaseId.Value,
            lease.FenceToken,
            operationId);
        Assert.AreEqual(ModeActionAdvanceDisposition.Advanced, verifying.Disposition, verifying.Detail);
        Assert.AreEqual(4, verifying.Action!.Revision);

        var request = new MachineServicePolicyVerifyRequest(
            transitionId,
            actionId,
            lease.LeaseId.Value,
            lease.FenceToken,
            controlSessionKey,
            verifying.Action.Revision,
            desired);

        return new Fixture(
            machine,
            db,
            marker,
            quarantineMarker,
            time,
            operationId,
            correlationId,
            request);
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
        FixedTimeProvider Time,
        Guid OperationId,
        Guid CorrelationId,
        MachineServicePolicyVerifyRequest Request);

    private sealed class TestCatalog(params string[] ids) : IManagedServiceCatalog
    {
        private readonly IReadOnlyDictionary<string, ManagedServiceCatalogEntry> _entries = ids.ToDictionary(
            static id => id,
            static id => new ManagedServiceCatalogEntry(
                id,
                $"fixture-{id}",
                new HashSet<ManagedServiceDesiredState>
                {
                    ManagedServiceDesiredState.Running,
                    ManagedServiceDesiredState.Stopped
                },
                TimeSpan.FromSeconds(1),
                0),
            StringComparer.Ordinal);

        public bool TryResolve(string managedServiceId, out ManagedServiceCatalogEntry entry)
            => _entries.TryGetValue(managedServiceId, out entry!);
    }

    private sealed class FakeAdapter(Func<ManagedServiceCatalogEntry, ManagedServiceObservedState> query)
        : IManagedServiceAdapter
    {
        public int Queries { get; private set; }
        public int ApplyCalls { get; private set; }

        public ValueTask<ManagedServiceObservation> QueryAsync(
            ManagedServiceCatalogEntry entry,
            CancellationToken cancellationToken = default)
        {
            Queries++;
            return ValueTask.FromResult(new ManagedServiceObservation(query(entry)));
        }

        public ValueTask<ManagedServiceTechnicalResult> ApplyAsync(
            ManagedServiceCatalogEntry entry,
            ManagedServiceDesiredState desiredState,
            CancellationToken cancellationToken = default)
        {
            ApplyCalls++;
            throw new AssertFailedException("Verification boundary must never invoke ApplyAsync.");
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
