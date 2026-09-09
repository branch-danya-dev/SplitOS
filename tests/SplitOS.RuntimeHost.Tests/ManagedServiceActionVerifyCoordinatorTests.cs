using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Contracts.Protocol;
using SplitOS.Persistence.Machine;
using SplitOS.RuntimeHost.ModeRuntime;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class ManagedServiceActionVerifyCoordinatorTests
{
    private string? _root;

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (_root is not null && Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public async Task VerifiedEvidenceAdvancesActionFromAppliedThroughVerifyingToVerified()
    {
        var fixture = await CreateFixtureAsync();
        var broker = new FakeBrokerClient(_ => Task.FromResult(VerifiedResult("STOPPED")));
        var coordinator = new ManagedServiceActionVerifyCoordinator(fixture.Journal, broker);

        var outcome = await coordinator.VerifyAsync(fixture.Command);

        Assert.AreEqual(ManagedServiceActionVerifyDisposition.Verified, outcome.Disposition);
        Assert.IsTrue(outcome.IsVerified);
        Assert.AreEqual("SERVICE_POLICY_VERIFIED", outcome.ProductCode);
        Assert.AreEqual(1, broker.Calls);
        Assert.AreEqual(4, broker.LastRequest!.ExpectedActionRevision);
        Assert.AreEqual("STOPPED", broker.LastRequest.Entries.Single().DesiredState);

        var persisted = await fixture.Journal.GetAsync(fixture.ActionId);
        Assert.IsNotNull(persisted);
        Assert.AreEqual(PersistedModeActionState.Verified, persisted.State);
        Assert.AreEqual("VERIFIED", persisted.VerifyResultCode);
        Assert.AreEqual(5, persisted.Revision);
    }

    [TestMethod]
    public async Task MismatchIsDurablyFailedAndCannotBeReportedVerified()
    {
        var fixture = await CreateFixtureAsync();
        var broker = new FakeBrokerClient(_ => Task.FromResult(new MachineServicePolicyVerifyResult(
            "MISMATCH",
            "SERVICE_POLICY_VERIFY_MISMATCH",
            [new ManagedServicePolicyVerificationEntryResult(
                "SEARCH_INDEXER",
                "STOPPED",
                "RUNNING",
                "MISMATCH",
                "STATE_MISMATCH")])));
        var coordinator = new ManagedServiceActionVerifyCoordinator(fixture.Journal, broker);

        var outcome = await coordinator.VerifyAsync(fixture.Command);

        Assert.AreEqual(ManagedServiceActionVerifyDisposition.BrokerRejected, outcome.Disposition);
        Assert.AreEqual("SERVICE_POLICY_VERIFY_MISMATCH", outcome.ProductCode);
        Assert.IsFalse(outcome.IsVerified);
        var persisted = await fixture.Journal.GetAsync(fixture.ActionId);
        Assert.IsNotNull(persisted);
        Assert.AreEqual(PersistedModeActionState.Failed, persisted.State);
        Assert.AreEqual("MISMATCH", persisted.VerifyResultCode);
        Assert.AreEqual(5, persisted.Revision);
    }

    [TestMethod]
    public async Task LostVerificationResponsePersistsUnknown()
    {
        var fixture = await CreateFixtureAsync();
        var broker = new FakeBrokerClient(_ =>
            Task.FromException<MachineServicePolicyVerifyResult>(new IOException("pipe closed after verification request")));
        var coordinator = new ManagedServiceActionVerifyCoordinator(fixture.Journal, broker);

        var outcome = await coordinator.VerifyAsync(fixture.Command);

        Assert.AreEqual(ManagedServiceActionVerifyDisposition.BrokerUnavailable, outcome.Disposition);
        Assert.AreEqual("MODE_SERVICE_VERIFY_RESPONSE_UNKNOWN", outcome.ProductCode);
        var persisted = await fixture.Journal.GetAsync(fixture.ActionId);
        Assert.IsNotNull(persisted);
        Assert.AreEqual(PersistedModeActionState.Failed, persisted.State);
        Assert.AreEqual("UNKNOWN", persisted.VerifyResultCode);
        Assert.AreEqual(5, persisted.Revision);
    }

    [TestMethod]
    public async Task InconsistentVerifiedAggregateIsPersistedUnknown()
    {
        var fixture = await CreateFixtureAsync();
        var broker = new FakeBrokerClient(_ => Task.FromResult(new MachineServicePolicyVerifyResult(
            "VERIFIED",
            "SERVICE_POLICY_VERIFIED",
            [new ManagedServicePolicyVerificationEntryResult(
                "SEARCH_INDEXER",
                "STOPPED",
                "RUNNING",
                "VERIFIED",
                null)])));
        var coordinator = new ManagedServiceActionVerifyCoordinator(fixture.Journal, broker);

        var outcome = await coordinator.VerifyAsync(fixture.Command);

        Assert.AreEqual(ManagedServiceActionVerifyDisposition.BrokerRejected, outcome.Disposition);
        Assert.AreEqual("MODE_SERVICE_VERIFY_EVIDENCE_INVALID", outcome.ProductCode);
        var persisted = await fixture.Journal.GetAsync(fixture.ActionId);
        Assert.IsNotNull(persisted);
        Assert.AreEqual(PersistedModeActionState.Failed, persisted.State);
        Assert.AreEqual("UNKNOWN", persisted.VerifyResultCode);
        Assert.AreEqual(5, persisted.Revision);
    }

    [TestMethod]
    public async Task StaleExpectedActionRevisionIsRejectedBeforeBeginVerifyOrBrokerRead()
    {
        var fixture = await CreateFixtureAsync();
        var broker = new FakeBrokerClient(_ =>
            throw new AssertFailedException("Broker must not be called for a stale action revision."));
        var coordinator = new ManagedServiceActionVerifyCoordinator(fixture.Journal, broker);
        var stale = fixture.Command with { ExpectedActionRevision = 4 };

        var outcome = await coordinator.VerifyAsync(stale);

        Assert.AreEqual(ManagedServiceActionVerifyDisposition.ActionRejected, outcome.Disposition);
        Assert.AreEqual("MODE_SERVICE_ACTION_REVISION_MISMATCH", outcome.ProductCode);
        Assert.AreEqual(0, broker.Calls);
        var persisted = await fixture.Journal.GetAsync(fixture.ActionId);
        Assert.IsNotNull(persisted);
        Assert.AreEqual(PersistedModeActionState.Applied, persisted.State);
        Assert.AreEqual(3, persisted.Revision);
    }

    private async Task<Fixture> CreateFixtureAsync()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "SplitOS.RuntimeHost.ManagedServiceVerify.Tests",
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
        const string controlSessionKey = "runtime-managed-service-verify-session";

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
                "mode-policy.runtime-managed-service-verify",
                1,
                "development",
                new string('a', 64)),
            PersistedModePolicyTarget.Work,
            new string('b', 64));
        Assert.AreEqual(ModeTransitionPolicyBindDisposition.Bound, bound.Disposition, bound.Detail);
        transitionRevision = bound.Binding!.TransitionRevision;

        var desiredEntries = new[]
        {
            new ManagedServicePolicyEntry("SEARCH_INDEXER", "STOPPED")
        };
        var desiredJson = ManagedServicePolicyActionContract.SerializeDesiredState(desiredEntries);
        var desiredDigest = ManagedServicePolicyActionContract.ComputeDesiredStateDigest(desiredEntries);
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
        Assert.AreEqual(2, applying.Action!.Revision);

        var applied = await journal.RecordApplyResultAsync(
            transitionId,
            actionId,
            applying.Action.Revision,
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

        var command = new ManagedServiceActionVerifyCommand(
            transitionId,
            actionId,
            3,
            lease.LeaseId.Value,
            lease.FenceToken,
            operationId,
            correlationId,
            controlSessionKey);
        return new Fixture(journal, command, actionId);
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

    private static MachineServicePolicyVerifyResult VerifiedResult(string desiredState)
        => new(
            "VERIFIED",
            "SERVICE_POLICY_VERIFIED",
            [new ManagedServicePolicyVerificationEntryResult(
                "SEARCH_INDEXER",
                desiredState,
                desiredState,
                "VERIFIED",
                null)]);

    private sealed record Fixture(
        ModeTransitionActionJournalStore Journal,
        ManagedServiceActionVerifyCommand Command,
        Guid ActionId);

    private sealed class FakeBrokerClient(
        Func<MachineServicePolicyVerifyRequest, Task<MachineServicePolicyVerifyResult>> verify)
        : IManagedServiceActionVerificationBrokerClient
    {
        public int Calls { get; private set; }
        public MachineServicePolicyVerifyRequest? LastRequest { get; private set; }

        public Task<MachineServicePolicyVerifyResult> VerifyAsync(
            Guid operationId,
            Guid correlationId,
            MachineServicePolicyVerifyRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastRequest = request;
            return verify(request);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
