using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Contracts.Protocol;
using SplitOS.Persistence.Machine;
using SplitOS.RuntimeHost.ModeRuntime;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class ManagedServiceActionApplyCoordinatorTests
{
    private string? _root;

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (_root is not null && Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public async Task DurableIntentDrivesSnapshotBeginApplyBrokerApplyAndJournalResult()
    {
        var fixture = await CreateFixtureAsync();
        var snapshot = CapturedSnapshot("SEARCH_INDEXER", "RUNNING");
        var broker = new FakeBrokerClient(
            _ => Task.FromResult(snapshot),
            _ => Task.FromResult(VerifiedApply("SEARCH_INDEXER", "STOPPED")));
        var coordinator = new ManagedServiceActionApplyCoordinator(fixture.Journal, broker);

        var outcome = await coordinator.ApplyAsync(fixture.Command);

        Assert.AreEqual(ManagedServiceActionApplyDisposition.Applied, outcome.Disposition);
        Assert.IsTrue(outcome.IsApplied);
        Assert.AreEqual("SERVICE_POLICY_APPLIED_VERIFIED", outcome.ProductCode);
        Assert.AreEqual(1, broker.SnapshotCalls);
        Assert.AreEqual(1, broker.ApplyCalls);
        Assert.AreEqual(1, broker.LastSnapshotRequest!.ExpectedActionRevision);
        Assert.AreEqual(2, broker.LastApplyRequest!.ExpectedActionRevision);
        Assert.AreEqual("STOPPED", broker.LastSnapshotRequest.Entries.Single().DesiredState);
        Assert.AreEqual("STOPPED", broker.LastApplyRequest.Entries.Single().DesiredState);

        var persisted = await fixture.Journal.GetAsync(fixture.ActionId);
        Assert.IsNotNull(persisted);
        Assert.AreEqual(PersistedModeActionState.Applied, persisted.State);
        Assert.AreEqual("APPLIED", persisted.ApplyResultCode);
        Assert.AreEqual(snapshot.PreStateJson, persisted.PreStateJson);
        Assert.AreEqual(snapshot.PreStateDigest, persisted.PreStateDigest);
        Assert.AreEqual(3, persisted.Revision);
    }

    [TestMethod]
    public async Task SnapshotRejectionLeavesActionPlannedAndDoesNotCallApply()
    {
        var fixture = await CreateFixtureAsync();
        var broker = new FakeBrokerClient(
            _ => Task.FromResult(new MachineServicePolicySnapshotResult(
                "FAILED",
                "SERVICE_PRESTATE_NOT_STABLE",
                [new ManagedServicePreStateEntry("SEARCH_INDEXER", "UNKNOWN")],
                null,
                null)),
            _ => throw new AssertFailedException("Apply must not be called after snapshot rejection."));
        var coordinator = new ManagedServiceActionApplyCoordinator(fixture.Journal, broker);

        var outcome = await coordinator.ApplyAsync(fixture.Command);

        Assert.AreEqual(ManagedServiceActionApplyDisposition.SnapshotRejected, outcome.Disposition);
        Assert.AreEqual("SERVICE_PRESTATE_NOT_STABLE", outcome.ProductCode);
        Assert.AreEqual(1, broker.SnapshotCalls);
        Assert.AreEqual(0, broker.ApplyCalls);
        var persisted = await fixture.Journal.GetAsync(fixture.ActionId);
        Assert.IsNotNull(persisted);
        Assert.AreEqual(PersistedModeActionState.Planned, persisted.State);
        Assert.AreEqual(1, persisted.Revision);
        Assert.IsNull(persisted.PreStateJson);
    }

    [TestMethod]
    public async Task InvalidCapturedEvidenceNeverBeginsApply()
    {
        var fixture = await CreateFixtureAsync();
        var other = CapturedSnapshot("OTHER_TARGET", "RUNNING");
        var broker = new FakeBrokerClient(
            _ => Task.FromResult(other),
            _ => throw new AssertFailedException("Apply must not be called for mismatched snapshot evidence."));
        var coordinator = new ManagedServiceActionApplyCoordinator(fixture.Journal, broker);

        var outcome = await coordinator.ApplyAsync(fixture.Command);

        Assert.AreEqual(ManagedServiceActionApplyDisposition.SnapshotRejected, outcome.Disposition);
        Assert.AreEqual("MODE_SERVICE_SNAPSHOT_EVIDENCE_INVALID", outcome.ProductCode);
        Assert.AreEqual(0, broker.ApplyCalls);
        var persisted = await fixture.Journal.GetAsync(fixture.ActionId);
        Assert.IsNotNull(persisted);
        Assert.AreEqual(PersistedModeActionState.Planned, persisted.State);
    }

    [TestMethod]
    public async Task ExplicitBrokerDriftAfterBeginApplyIsDurablyFailed()
    {
        var fixture = await CreateFixtureAsync();
        var broker = new FakeBrokerClient(
            _ => Task.FromResult(CapturedSnapshot("SEARCH_INDEXER", "RUNNING")),
            _ => Task.FromResult(new MachineServicePolicyApplyResult(
                "REJECTED",
                "SERVICE_PRESTATE_DRIFT_DETECTED",
                [new ManagedServicePolicyEntryResult(
                    "SEARCH_INDEXER",
                    "STOPPED",
                    false,
                    "PRESTATE_DRIFT_DETECTED",
                    "STOPPED",
                    "NOT_VERIFIED",
                    "PRESTATE_DRIFT_DETECTED")])));
        var coordinator = new ManagedServiceActionApplyCoordinator(fixture.Journal, broker);

        var outcome = await coordinator.ApplyAsync(fixture.Command);

        Assert.AreEqual(ManagedServiceActionApplyDisposition.BrokerRejected, outcome.Disposition);
        Assert.AreEqual("SERVICE_PRESTATE_DRIFT_DETECTED", outcome.ProductCode);
        var persisted = await fixture.Journal.GetAsync(fixture.ActionId);
        Assert.IsNotNull(persisted);
        Assert.AreEqual(PersistedModeActionState.Failed, persisted.State);
        Assert.AreEqual("FAILED", persisted.ApplyResultCode);
        Assert.IsNotNull(persisted.PreStateJson);
        Assert.AreEqual(3, persisted.Revision);
    }

    [TestMethod]
    public async Task LostBrokerApplyResponsePersistsUnknownInsteadOfLeavingApplying()
    {
        var fixture = await CreateFixtureAsync();
        var broker = new FakeBrokerClient(
            _ => Task.FromResult(CapturedSnapshot("SEARCH_INDEXER", "RUNNING")),
            _ => Task.FromException<MachineServicePolicyApplyResult>(new IOException("pipe closed after request write")));
        var coordinator = new ManagedServiceActionApplyCoordinator(fixture.Journal, broker);

        var outcome = await coordinator.ApplyAsync(fixture.Command);

        Assert.AreEqual(ManagedServiceActionApplyDisposition.BrokerUnavailable, outcome.Disposition);
        Assert.AreEqual("MODE_SERVICE_APPLY_RESPONSE_UNKNOWN", outcome.ProductCode);
        var persisted = await fixture.Journal.GetAsync(fixture.ActionId);
        Assert.IsNotNull(persisted);
        Assert.AreEqual(PersistedModeActionState.Failed, persisted.State);
        Assert.AreEqual("UNKNOWN", persisted.ApplyResultCode);
        Assert.IsNotNull(persisted.PreStateJson);
        Assert.AreEqual(3, persisted.Revision);
    }

    [TestMethod]
    public async Task InconsistentSucceededBrokerEvidencePersistsUnknown()
    {
        var fixture = await CreateFixtureAsync();
        var broker = new FakeBrokerClient(
            _ => Task.FromResult(CapturedSnapshot("SEARCH_INDEXER", "RUNNING")),
            _ => Task.FromResult(new MachineServicePolicyApplyResult(
                "SUCCEEDED",
                "SERVICE_POLICY_APPLIED_VERIFIED",
                [new ManagedServicePolicyEntryResult(
                    "SEARCH_INDEXER",
                    "STOPPED",
                    true,
                    "SCM_OPERATION_ACCEPTED",
                    "RUNNING",
                    "VERIFIED",
                    null)])));
        var coordinator = new ManagedServiceActionApplyCoordinator(fixture.Journal, broker);

        var outcome = await coordinator.ApplyAsync(fixture.Command);

        Assert.AreEqual(ManagedServiceActionApplyDisposition.BrokerRejected, outcome.Disposition);
        var persisted = await fixture.Journal.GetAsync(fixture.ActionId);
        Assert.IsNotNull(persisted);
        Assert.AreEqual(PersistedModeActionState.Failed, persisted.State);
        Assert.AreEqual("UNKNOWN", persisted.ApplyResultCode);
    }

    [TestMethod]
    public async Task StaleExpectedActionRevisionIsRejectedBeforeBrokerCall()
    {
        var fixture = await CreateFixtureAsync();
        var broker = new FakeBrokerClient(
            _ => throw new AssertFailedException("Snapshot must not be called for stale action revision."),
            _ => throw new AssertFailedException("Apply must not be called for stale action revision."));
        var coordinator = new ManagedServiceActionApplyCoordinator(fixture.Journal, broker);
        var stale = fixture.Command with { ExpectedActionRevision = 2 };

        var outcome = await coordinator.ApplyAsync(stale);

        Assert.AreEqual(ManagedServiceActionApplyDisposition.ActionRejected, outcome.Disposition);
        Assert.AreEqual("MODE_SERVICE_ACTION_REVISION_MISMATCH", outcome.ProductCode);
        Assert.AreEqual(0, broker.SnapshotCalls);
        Assert.AreEqual(0, broker.ApplyCalls);
    }

    private async Task<Fixture> CreateFixtureAsync()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "SplitOS.RuntimeHost.ManagedServiceApply.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var db = Path.Combine(_root, "machine.db");
        var marker = Path.Combine(_root, "machine-store.initialized");
        var quarantineMarker = Path.Combine(_root, "machine-store.quarantined.json");
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero));
        var machine = new MachineStateStore(
            db,
            marker,
            Path.Combine(_root, "maintenance", "backups"),
            Path.Combine(_root, "maintenance", "quarantine"),
            quarantineMarker);
        await machine.InitializeAsync();

        var operationId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        const string controlSessionKey = "runtime-managed-service-apply-session";

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
                "mode-policy.runtime-managed-service-apply",
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
        _ = await AdvanceAsync(
            transitions,
            transitionId,
            transitionRevision,
            lease,
            operationId,
            PersistedModeTransitionState.Applying,
            PersistedModeTransitionStage.ApplyStarted);

        var journal = new ModeTransitionActionJournalStore(db, marker, quarantineMarker, time);
        await journal.InitializeAsync();
        var command = new ManagedServiceActionApplyCommand(
            transitionId,
            actionId,
            1,
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

    private static MachineServicePolicySnapshotResult CapturedSnapshot(
        string managedServiceId,
        string actualState)
    {
        var entries = new[] { new ManagedServicePreStateEntry(managedServiceId, actualState) };
        return new MachineServicePolicySnapshotResult(
            "CAPTURED",
            "SERVICE_PRESTATE_CAPTURED",
            entries,
            ManagedServicePolicyActionContract.SerializePreState(entries),
            ManagedServicePolicyActionContract.ComputePreStateDigest(entries));
    }

    private static MachineServicePolicyApplyResult VerifiedApply(
        string managedServiceId,
        string desiredState)
        => new(
            "SUCCEEDED",
            "SERVICE_POLICY_APPLIED_VERIFIED",
            [new ManagedServicePolicyEntryResult(
                managedServiceId,
                desiredState,
                true,
                "SCM_OPERATION_ACCEPTED",
                desiredState,
                "VERIFIED",
                null)]);

    private sealed record Fixture(
        ModeTransitionActionJournalStore Journal,
        ManagedServiceActionApplyCommand Command,
        Guid ActionId);

    private sealed class FakeBrokerClient(
        Func<MachineServicePolicySnapshotRequest, Task<MachineServicePolicySnapshotResult>> snapshot,
        Func<MachineServicePolicyApplyRequest, Task<MachineServicePolicyApplyResult>> apply)
        : IManagedServiceActionBrokerClient
    {
        public int SnapshotCalls { get; private set; }
        public int ApplyCalls { get; private set; }
        public MachineServicePolicySnapshotRequest? LastSnapshotRequest { get; private set; }
        public MachineServicePolicyApplyRequest? LastApplyRequest { get; private set; }

        public Task<MachineServicePolicySnapshotResult> SnapshotAsync(
            Guid operationId,
            Guid correlationId,
            MachineServicePolicySnapshotRequest request,
            CancellationToken cancellationToken = default)
        {
            SnapshotCalls++;
            LastSnapshotRequest = request;
            return snapshot(request);
        }

        public Task<MachineServicePolicyApplyResult> ApplyAsync(
            Guid operationId,
            Guid correlationId,
            MachineServicePolicyApplyRequest request,
            CancellationToken cancellationToken = default)
        {
            ApplyCalls++;
            LastApplyRequest = request;
            return apply(request);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
