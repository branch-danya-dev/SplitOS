using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Contracts.Protocol;
using SplitOS.Persistence.Machine;
using SplitOS.RuntimeHost.ModeRuntime;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class RuntimeModeOrchestratorTests
{
    private string? _root;

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (_root is not null && Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public async Task NoneToWorkRunsDurablePipelineAndCommitsCanonicalIdentity()
    {
        var fixture = await CreateFixtureAsync(blocked: false);
        var activationEpoch = Guid.NewGuid();
        var command = fixture.Command with { TargetMode = OperationalMode.Work, ActivationEpochId = activationEpoch };

        var outcome = await fixture.Orchestrator.ExecuteAsync(command);

        Assert.AreEqual(RuntimeModeExecutionDisposition.Completed, outcome.Disposition, outcome.Detail);
        Assert.AreEqual("WORK", outcome.OperationalMode.CommittedMode);
        Assert.AreEqual(2, outcome.OperationalMode.Revision);
        Assert.AreEqual(command.ControlSessionKey, outcome.OperationalMode.ControlSessionKey);
        Assert.AreEqual(activationEpoch, outcome.OperationalMode.ActivationEpochId);
        Assert.IsNotNull(outcome.OperationalMode.PolicyIdentity);
        Assert.AreEqual("mode-policy.runtime-orchestrator", outcome.OperationalMode.PolicyIdentity.PolicyCatalogId);
        Assert.AreEqual(PersistedModePolicyTarget.Work, outcome.OperationalMode.PolicyTarget);
        Assert.IsNotNull(outcome.Transition);
        Assert.AreEqual(PersistedModeTransitionState.Completed, outcome.Transition.TransitionState);
        Assert.IsTrue(outcome.Transition.CommitDurable);
        Assert.AreEqual("COMPLETED", outcome.Transition.TerminalOutcome);
        Assert.AreEqual(1, fixture.ApplyBroker.SnapshotCalls);
        Assert.AreEqual(1, fixture.ApplyBroker.ApplyCalls);
        Assert.AreEqual(1, fixture.VerifyBroker.VerifyCalls);
        Assert.IsFalse((await fixture.Leases.GetAsync()).IsHeld);
        Assert.AreEqual(0, (await fixture.Transitions.GetIncompleteAsync()).Count);
    }

    [TestMethod]
    public async Task SameTargetReturnsNoOpWithoutLeaseTransitionOrBrokerMutation()
    {
        var fixture = await CreateFixtureAsync(blocked: false);
        var command = fixture.Command with { TargetMode = OperationalMode.None, ActivationEpochId = null };

        var outcome = await fixture.Orchestrator.ExecuteAsync(command);

        Assert.AreEqual(RuntimeModeExecutionDisposition.NoOp, outcome.Disposition);
        Assert.AreEqual("NONE", outcome.OperationalMode.CommittedMode);
        Assert.IsFalse((await fixture.Leases.GetAsync()).IsHeld);
        Assert.AreEqual(0, (await fixture.Transitions.GetIncompleteAsync()).Count);
        Assert.AreEqual(0, fixture.ApplyBroker.SnapshotCalls);
        Assert.AreEqual(0, fixture.ApplyBroker.ApplyCalls);
        Assert.AreEqual(0, fixture.VerifyBroker.VerifyCalls);
    }

    [TestMethod]
    public async Task HardBlockCancelsBeforePlanOrBrokerMutationAndReleasesLease()
    {
        var fixture = await CreateFixtureAsync(blocked: true);
        var command = fixture.Command with { TargetMode = OperationalMode.Work, ActivationEpochId = Guid.NewGuid() };

        var outcome = await fixture.Orchestrator.ExecuteAsync(command);

        Assert.AreEqual(RuntimeModeExecutionDisposition.Blocked, outcome.Disposition, outcome.Detail);
        Assert.AreEqual("NONE", outcome.OperationalMode.CommittedMode);
        Assert.IsNotNull(outcome.Transition);
        Assert.AreEqual(PersistedModeTransitionState.Cancelled, outcome.Transition.TransitionState);
        Assert.AreEqual(1, outcome.Blockers?.Count);
        Assert.IsFalse((await fixture.Leases.GetAsync()).IsHeld);
        Assert.AreEqual(0, fixture.Preparation.Calls);
        Assert.AreEqual(0, fixture.ApplyBroker.SnapshotCalls);
        Assert.AreEqual(0, fixture.ApplyBroker.ApplyCalls);
        Assert.AreEqual(0, fixture.VerifyBroker.VerifyCalls);
    }

    private async Task<Fixture> CreateFixtureAsync(bool blocked)
    {
        _root = Path.Combine(Path.GetTempPath(), "SplitOS.RuntimeModeOrchestrator.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var db = Path.Combine(_root, "machine.db");
        var marker = Path.Combine(_root, "machine-store.initialized");
        var quarantine = Path.Combine(_root, "machine-store.quarantined.json");
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 9, 10, 8, 0, 0, TimeSpan.Zero));
        var machine = new MachineStateStore(
            db,
            marker,
            Path.Combine(_root, "maintenance", "backups"),
            Path.Combine(_root, "maintenance", "quarantine"),
            quarantine);
        await machine.InitializeAsync();

        var leases = new MachineMutationLeaseStore(db, marker, quarantine, time);
        var transitions = new ModeTransitionStore(db, marker, quarantine, time);
        var policies = new ModeTransitionPolicyStore(db, marker, quarantine, time);
        var plans = new ModeTransitionActionPlanStore(db, marker, quarantine, time);
        var journal = new ModeTransitionActionJournalStore(db, marker, quarantine, time);
        var commits = new ModeTransitionCommitStore(db, marker, quarantine, time);
        var applyBroker = new FakeApplyBroker();
        var verifyBroker = new FakeVerifyBroker();
        var preparation = new FakePreparationProvider();
        var blockers = blocked
            ? new ModeBlockerEngine([new HardBlockProvider(time)], time)
            : new ModeBlockerEngine(Array.Empty<IModeBlockerProvider>(), time);
        var orchestrator = new RuntimeModeOrchestrator(
            machine,
            leases,
            transitions,
            policies,
            plans,
            new ManagedServiceActionApplyCoordinator(journal, applyBroker),
            new ManagedServiceActionVerifyCoordinator(journal, verifyBroker),
            commits,
            blockers,
            preparation,
            time);
        var command = new RuntimeModeExecutionCommand(
            OperationalMode.Work,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "console-runtime-orchestrator-test",
            new RuntimeAccessEvaluation("ENABLED", "PRO_ONLINE_CONFIRMED", 7),
            time.GetUtcNow(),
            Guid.NewGuid(),
            TimeSpan.FromMinutes(2));
        return new Fixture(orchestrator, machine, leases, transitions, applyBroker, verifyBroker, preparation, command);
    }

    private sealed record Fixture(
        RuntimeModeOrchestrator Orchestrator,
        MachineStateStore Machine,
        MachineMutationLeaseStore Leases,
        ModeTransitionStore Transitions,
        FakeApplyBroker ApplyBroker,
        FakeVerifyBroker VerifyBroker,
        FakePreparationProvider Preparation,
        RuntimeModeExecutionCommand Command);

    private sealed class FakePreparationProvider : IRuntimeModeTargetPreparationProvider
    {
        public int Calls { get; private set; }

        public ValueTask<RuntimeModePreparedTarget> PrepareAsync(
            ModeOperationPlan operation,
            RuntimeAccessEvaluation runtimeAccess,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            var target = operation.TargetMode switch
            {
                OperationalMode.None => ModePolicyTarget.Base,
                OperationalMode.Work => ModePolicyTarget.Work,
                OperationalMode.Game => ModePolicyTarget.Game,
                _ => throw new ArgumentOutOfRangeException()
            };
            var identity = new ModePolicyIdentity(
                "mode-policy.runtime-orchestrator",
                1,
                "development",
                new string('a', 64));
            var policy = new ResolvedModePolicySnapshot(
                identity,
                target,
                Array.Empty<ModePolicyRule>(),
                Array.Empty<ModePolicyFallbackSelection>(),
                new string('b', 64));
            var desired = new[] { new ManagedServicePolicyEntry("SEARCH_INDEXER", "STOPPED") };
            var action = new PersistedModeActionDefinition(
                Guid.NewGuid(),
                100,
                ManagedServicePolicyActionContract.OwningModule,
                ManagedServicePolicyActionContract.ActionType,
                ManagedServicePolicyActionContract.TargetRef,
                ManagedServicePolicyActionContract.DesiredSchemaVersion,
                ManagedServicePolicyActionContract.SerializeDesiredState(desired),
                ManagedServicePolicyActionContract.ComputeDesiredStateDigest(desired),
                true,
                "restore_pre_state",
                "service.actual-state");
            return ValueTask.FromResult(new RuntimeModePreparedTarget(policy, [action]));
        }
    }

    private sealed class FakeApplyBroker : IManagedServiceActionBrokerClient
    {
        public int SnapshotCalls { get; private set; }
        public int ApplyCalls { get; private set; }

        public Task<MachineServicePolicySnapshotResult> SnapshotAsync(
            Guid operationId,
            Guid correlationId,
            MachineServicePolicySnapshotRequest request,
            CancellationToken cancellationToken = default)
        {
            SnapshotCalls++;
            var preState = request.Entries.Select(entry => new ManagedServicePreStateEntry(entry.ManagedServiceId, "RUNNING")).ToArray();
            return Task.FromResult(new MachineServicePolicySnapshotResult(
                "CAPTURED",
                "SERVICE_PRESTATE_CAPTURED",
                preState,
                ManagedServicePolicyActionContract.SerializePreState(preState),
                ManagedServicePolicyActionContract.ComputePreStateDigest(preState)));
        }

        public Task<MachineServicePolicyApplyResult> ApplyAsync(
            Guid operationId,
            Guid correlationId,
            MachineServicePolicyApplyRequest request,
            CancellationToken cancellationToken = default)
        {
            ApplyCalls++;
            return Task.FromResult(new MachineServicePolicyApplyResult(
                "SUCCEEDED",
                "SERVICE_POLICY_APPLIED_VERIFIED",
                request.Entries.Select(entry => new ManagedServicePolicyEntryResult(
                    entry.ManagedServiceId,
                    entry.DesiredState,
                    true,
                    "SCM_OPERATION_ACCEPTED",
                    entry.DesiredState,
                    "VERIFIED",
                    null)).ToArray()));
        }
    }

    private sealed class FakeVerifyBroker : IManagedServiceActionVerificationBrokerClient
    {
        public int VerifyCalls { get; private set; }

        public Task<MachineServicePolicyVerifyResult> VerifyAsync(
            Guid operationId,
            Guid correlationId,
            MachineServicePolicyVerifyRequest request,
            CancellationToken cancellationToken = default)
        {
            VerifyCalls++;
            return Task.FromResult(new MachineServicePolicyVerifyResult(
                "VERIFIED",
                "SERVICE_POLICY_VERIFIED",
                request.Entries.Select(entry => new ManagedServicePolicyVerificationEntryResult(
                    entry.ManagedServiceId,
                    entry.DesiredState,
                    entry.DesiredState,
                    "VERIFIED",
                    null)).ToArray()));
        }
    }

    private sealed class HardBlockProvider(TimeProvider time) : IModeBlockerProvider
    {
        public string ProviderId => "test-hard-block";
        public string ProviderVersion => "1";

        public ValueTask<BlockerProviderInspectionResult> InspectAsync(
            ModeBlockerInspectionContext context,
            CancellationToken cancellationToken = default)
        {
            var observation = new BlockerObservation(
                Guid.NewGuid(),
                ProviderId,
                "TEST_HARD_BLOCK",
                BlockerClass.HardBlock,
                "test.subject",
                "test.blocker",
                time.GetUtcNow(),
                new string('c', 64),
                null,
                null,
                Array.Empty<BlockerDecisionOption>());
            return ValueTask.FromResult(BlockerProviderInspectionResult.Available(observation));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
