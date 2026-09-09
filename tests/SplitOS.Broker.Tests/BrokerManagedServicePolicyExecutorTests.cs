using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Broker.Service;
using SplitOS.Contracts.Protocol;
using SplitOS.Persistence.Machine;

namespace SplitOS.Broker.Tests;

[TestClass]
public sealed class BrokerManagedServicePolicyExecutorTests
{
    private string? _root;

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (_root is not null && Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public async Task MatchingDurableActionExecutesAllowlistedAdapter()
    {
        var entries = new[] { new ManagedServicePolicyEntry("SEARCH_INDEXER", "STOPPED") };
        var fixture = await CreateFixtureAsync(entries);
        var adapter = new FakeAdapter(static (_, _) => Verified(ManagedServiceObservedState.Stopped));
        var executor = CreateExecutor(fixture, adapter, new TestCatalog("SEARCH_INDEXER"));

        var result = await executor.ExecuteAsync(
            fixture.OperationId,
            fixture.CorrelationId,
            fixture.Request);

        Assert.AreEqual("SUCCEEDED", result.Disposition);
        Assert.AreEqual("SERVICE_POLICY_APPLIED_VERIFIED", result.ProductCode);
        Assert.AreEqual(1, adapter.Calls);
        Assert.AreEqual("fixture-SEARCH_INDEXER", adapter.LastTarget?.WindowsServiceName);
        Assert.AreEqual(ManagedServiceDesiredState.Stopped, adapter.LastDesiredState);
        Assert.AreEqual("STOPPED", result.Entries.Single().ActualStateObserved);
    }

    [TestMethod]
    public async Task DifferentDesiredStateCannotBorrowApplyingActionFence()
    {
        var persisted = new[] { new ManagedServicePolicyEntry("SEARCH_INDEXER", "STOPPED") };
        var fixture = await CreateFixtureAsync(persisted);
        var adapter = new FakeAdapter(static (_, _) => Verified(ManagedServiceObservedState.Running));
        var executor = CreateExecutor(fixture, adapter, new TestCatalog("SEARCH_INDEXER"));
        var changedRequest = fixture.Request with
        {
            Entries = [new ManagedServicePolicyEntry("SEARCH_INDEXER", "RUNNING")]
        };

        var result = await executor.ExecuteAsync(
            fixture.OperationId,
            fixture.CorrelationId,
            changedRequest);

        Assert.AreEqual("REJECTED", result.Disposition);
        Assert.AreEqual("MODE_MUTATION_ACTION_SEMANTICS_MISMATCH", result.ProductCode);
        Assert.AreEqual(0, adapter.Calls);
    }

    [TestMethod]
    public async Task UnknownManagedServiceIdFailsBeforeAnyAdapterInvocation()
    {
        var entries = new[] { new ManagedServicePolicyEntry("UNKNOWN_TARGET", "STOPPED") };
        var fixture = await CreateFixtureAsync(entries);
        var adapter = new FakeAdapter(static (_, _) => Verified(ManagedServiceObservedState.Stopped));
        var executor = CreateExecutor(fixture, adapter, new TestCatalog("SEARCH_INDEXER"));

        var result = await executor.ExecuteAsync(
            fixture.OperationId,
            fixture.CorrelationId,
            fixture.Request);

        Assert.AreEqual("FAILED", result.Disposition);
        Assert.AreEqual("SERVICE_POLICY_TARGET_INVALID", result.ProductCode);
        Assert.AreEqual("TARGET_NOT_FOUND", result.Entries.Single().ErrorCode);
        Assert.AreEqual(0, adapter.Calls);
    }

    [TestMethod]
    public async Task StaleFenceNeverInvokesManagedServiceAdapter()
    {
        var entries = new[] { new ManagedServicePolicyEntry("SEARCH_INDEXER", "STOPPED") };
        var fixture = await CreateFixtureAsync(entries);
        var adapter = new FakeAdapter(static (_, _) => Verified(ManagedServiceObservedState.Stopped));
        var executor = CreateExecutor(fixture, adapter, new TestCatalog("SEARCH_INDEXER"));
        var stale = fixture.Request with { FenceToken = fixture.Request.FenceToken + 1 };

        var result = await executor.ExecuteAsync(
            fixture.OperationId,
            fixture.CorrelationId,
            stale);

        Assert.AreEqual("REJECTED", result.Disposition);
        Assert.AreEqual("MUTATION_LEASE_STALE_FENCE", result.ProductCode);
        Assert.AreEqual(0, adapter.Calls);
    }

    [TestMethod]
    public async Task WireCapabilityReturnsTechnicalVerificationResult()
    {
        var entries = new[] { new ManagedServicePolicyEntry("SEARCH_INDEXER", "STOPPED") };
        var fixture = await CreateFixtureAsync(entries);
        var adapter = new FakeAdapter(static (_, _) => Verified(ManagedServiceObservedState.Stopped));
        var executor = CreateExecutor(fixture, adapter, new TestCatalog("SEARCH_INDEXER"));
        var handler = new BrokerMessageHandler(fixture.Machine, executor);
        var wire = WireMessage.Create(
            MessageTypes.MachineServicePolicyApplyRequest,
            fixture.Request,
            Capabilities.MachineServicePolicyApply,
            fixture.OperationId,
            fixture.CorrelationId);

        var response = await handler.HandleAsync(wire, CancellationToken.None);
        var result = response.ReadPayload<MachineServicePolicyApplyResult>();

        Assert.AreEqual(MessageTypes.MachineServicePolicyApplyResult, response.MessageType);
        Assert.AreEqual("SUCCEEDED", result.Disposition);
        Assert.AreEqual(1, adapter.Calls);
    }

    private BrokerManagedServicePolicyExecutor CreateExecutor(
        Fixture fixture,
        IManagedServiceAdapter adapter,
        IManagedServiceCatalog catalog)
    {
        var fenceStore = new ModeMutationFenceStore(
            fixture.DatabasePath,
            fixture.MarkerPath,
            fixture.QuarantineMarkerPath,
            fixture.Time);
        fenceStore.InitializeAsync().GetAwaiter().GetResult();
        return new BrokerManagedServicePolicyExecutor(
            new BrokerModeMutationFenceBoundary(fenceStore),
            catalog,
            adapter);
    }

    private async Task<Fixture> CreateFixtureAsync(
        IReadOnlyCollection<ManagedServicePolicyEntry> entries)
    {
        _root = Path.Combine(Path.GetTempPath(), "SplitOS.Broker.ServicePolicy.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var db = Path.Combine(_root, "machine.db");
        var marker = Path.Combine(_root, "machine-store.initialized");
        var quarantineMarker = Path.Combine(_root, "machine-store.quarantined.json");
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 9, 9, 14, 0, 0, TimeSpan.Zero));
        var machine = new MachineStateStore(
            db,
            marker,
            Path.Combine(_root, "maintenance", "backups"),
            Path.Combine(_root, "maintenance", "quarantine"),
            quarantineMarker);
        await machine.InitializeAsync();

        var operationId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        const string session = "console-session-service-policy";
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

        revision = await AdvanceAsync(
            transitions,
            transitionId,
            revision,
            lease,
            operationId,
            PersistedModeTransitionState.Inspecting,
            PersistedModeTransitionStage.InspectionComplete);
        revision = await AdvanceAsync(
            transitions,
            transitionId,
            revision,
            lease,
            operationId,
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
                "mode-policy.service-policy-test",
                1,
                "development",
                new string('a', 64)),
            PersistedModePolicyTarget.Work,
            new string('b', 64));
        Assert.AreEqual(ModeTransitionPolicyBindDisposition.Bound, bound.Disposition, bound.Detail);
        revision = bound.Binding!.TransitionRevision;

        var normalized = ManagedServicePolicyActionContract.NormalizeEntries(entries);
        var desiredJson = ManagedServicePolicyActionContract.SerializeDesiredState(normalized);
        var desiredDigest = ManagedServicePolicyActionContract.ComputeDesiredStateDigest(normalized);
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

        revision = await AdvanceAsync(
            transitions,
            transitionId,
            revision,
            lease,
            operationId,
            PersistedModeTransitionState.Resolving,
            PersistedModeTransitionStage.ActionPlanReady);
        revision = await AdvanceAsync(
            transitions,
            transitionId,
            revision,
            lease,
            operationId,
            PersistedModeTransitionState.Applying,
            PersistedModeTransitionStage.ApplyStarted);

        var journal = new ModeTransitionActionJournalStore(db, marker, quarantineMarker, time);
        await journal.InitializeAsync();
        const string preState = "{\"SEARCH_INDEXER\":\"RUNNING\"}";
        var applying = await journal.BeginApplyAsync(
            transitionId,
            actionId,
            1,
            lease.LeaseId.Value,
            lease.FenceToken,
            operationId,
            preState,
            Digest(preState));
        Assert.AreEqual(ModeActionAdvanceDisposition.Advanced, applying.Disposition, applying.Detail);

        var request = new MachineServicePolicyApplyRequest(
            transitionId,
            actionId,
            lease.LeaseId.Value,
            lease.FenceToken,
            session,
            applying.Action!.Revision,
            normalized);

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

    private static ManagedServiceTechnicalResult Verified(ManagedServiceObservedState state)
        => new(
            ManagedServiceApplyDisposition.AppliedVerified,
            true,
            "SCM_OPERATION_ACCEPTED",
            state,
            "VERIFIED");

    private static string Digest(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record Fixture(
        MachineStateStore Machine,
        string DatabasePath,
        string MarkerPath,
        string QuarantineMarkerPath,
        MutableTimeProvider Time,
        Guid OperationId,
        Guid CorrelationId,
        MachineServicePolicyApplyRequest Request);

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

    private sealed class FakeAdapter(
        Func<ManagedServiceCatalogEntry, ManagedServiceDesiredState, ManagedServiceTechnicalResult> resultFactory)
        : IManagedServiceAdapter
    {
        public int Calls { get; private set; }
        public ManagedServiceCatalogEntry? LastTarget { get; private set; }
        public ManagedServiceDesiredState? LastDesiredState { get; private set; }

        public ValueTask<ManagedServiceObservation> QueryAsync(
            ManagedServiceCatalogEntry entry,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new ManagedServiceObservation(ManagedServiceObservedState.Running));

        public ValueTask<ManagedServiceTechnicalResult> ApplyAsync(
            ManagedServiceCatalogEntry entry,
            ManagedServiceDesiredState desiredState,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastTarget = entry;
            LastDesiredState = desiredState;
            return ValueTask.FromResult(resultFactory(entry, desiredState));
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtc;
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
