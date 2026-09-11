using SplitOS.Broker.Service;
using SplitOS.Contracts.ModePersistence;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Contracts.Protocol;
using SplitOS.Persistence.Machine;
using SplitOS.RuntimeHost.ModeRuntime;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed partial class RuntimeModeOrchestratorTests
{
    private string? _root;

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (_root is not null && Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NoneToWorkRunsDurablePipelineAndCommitsCanonicalIdentity(bool remote)
    {
        var fixture = await CreateFixtureAsync(blocked: false, remote);
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
    [DataRow(false)]
    [DataRow(true)]
    public async Task SameTargetReturnsNoOpWithoutLeaseTransitionOrBrokerMutation(bool remote)
    {
        var fixture = await CreateFixtureAsync(blocked: false, remote);
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
    [DataRow(false)]
    [DataRow(true)]
    public async Task HardBlockCancelsBeforePlanOrBrokerMutationAndReleasesLease(bool remote)
    {
        var fixture = await CreateFixtureAsync(blocked: true, remote);
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

    [TestMethod]
    public async Task RemotePipelineCompletesActivateSwitchAndDeactivate()
    {
        var fixture = await CreateFixtureAsync(false);
        var epoch = Guid.NewGuid();
        foreach (var target in new[] { OperationalMode.Work, OperationalMode.Game, OperationalMode.Work, OperationalMode.None })
        {
            var command = fixture.Command with
            {
                TargetMode = target,
                OperationId = Guid.NewGuid(),
                CorrelationId = Guid.NewGuid(),
                TransitionId = Guid.NewGuid(),
                ActivationEpochId = target == OperationalMode.None ? null : epoch
            };
            var result = await fixture.Orchestrator.ExecuteAsync(command);
            Assert.AreEqual(RuntimeModeExecutionDisposition.Completed, result.Disposition, result.Detail);
            Assert.AreEqual(target.ToString().ToUpperInvariant(), (await fixture.Machine.GetOperationalModeAsync()).CommittedMode);
            Assert.IsFalse((await fixture.Leases.GetAsync()).IsHeld);
        }
        Assert.AreEqual(5, (await fixture.Machine.GetOperationalModeAsync()).Revision);
    }

    [TestMethod]
    public async Task FreeAccessCannotAcquireLeaseOrApplyMode()
    {
        var fixture = await CreateFixtureAsync(false);
        var result = await fixture.Orchestrator.ExecuteAsync(fixture.Command with
        {
            RuntimeAccess = new RuntimeAccessEvaluation("DISABLED", "FREE", 1)
        });
        Assert.AreEqual(RuntimeModeExecutionDisposition.AuthorityDenied, result.Disposition);
        Assert.AreEqual(0, fixture.ApplyBroker.ApplyCalls);
        Assert.IsFalse((await fixture.Leases.GetAsync()).IsHeld);
    }

    [TestMethod]
    public async Task VerificationFailurePreservesSourceAndDurableRollbackRequirementOverPipe()
    {
        var fixture = await CreateFixtureAsync(false);
        fixture.VerifyBroker.Fail = true;
        var result = await fixture.Orchestrator.ExecuteAsync(fixture.Command);
        Assert.IsFalse(result.IsCompleted);
        Assert.AreEqual("NONE", (await fixture.Machine.GetOperationalModeAsync()).CommittedMode);
        var incomplete = await fixture.Transitions.GetIncompleteAsync();
        Assert.AreEqual(1, incomplete.Count);
        Assert.AreEqual(PersistedModeTransitionState.RollingBack, incomplete[0].TransitionState);
        Assert.IsFalse(incomplete[0].CommitDurable);
    }

    [TestMethod]
    public async Task LeaseReplayStaleFenceAndNullableReadSurviveWireRoundTrip()
    {
        var fixture = await CreateFixtureAsync(false);
        IMachineMutationLeaseStore leases = fixture.Proxy;
        IModeTransitionStore transitions = fixture.Proxy;
        Assert.IsNull(await transitions.GetAsync(Guid.NewGuid()));
        var command = fixture.Command;
        var acquired = await leases.TryAcquireAsync(MachineMutationType.Mode, command.OperationId,
            command.CorrelationId, command.ControlSessionKey, command.LeaseLifetime);
        var replay = await leases.TryAcquireAsync(MachineMutationType.Mode, command.OperationId,
            command.CorrelationId, command.ControlSessionKey, command.LeaseLifetime);
        Assert.AreEqual(acquired.Lease.LeaseId, replay.Lease.LeaseId);
        Assert.AreEqual(MachineMutationLeaseAcquireDisposition.AlreadyOwned, replay.Disposition);
        var stale = await leases.ReleaseAsync(acquired.Lease.LeaseId!.Value, acquired.Lease.FenceToken + 1, command.OperationId);
        Assert.AreEqual(MachineMutationLeaseReleaseDisposition.StaleOwner, stale.Disposition);
        Assert.IsTrue((await fixture.Leases.GetAsync()).IsHeld);
        var released = await leases.ReleaseAsync(acquired.Lease.LeaseId.Value, acquired.Lease.FenceToken, command.OperationId);
        Assert.AreEqual(MachineMutationLeaseReleaseDisposition.Released, released.Disposition);
    }

    [TestMethod]
    public async Task BrokerRejectsUnknownFieldsMissingFieldsAndNonModeLease()
    {
        var fixture = await CreateFixtureAsync(false);
        var operation = Guid.NewGuid();
        var correlation = Guid.NewGuid();
        var request = ModePersistenceProtocol.CreateRequest(new MachineMutationLeaseTryAcquireRequest(
            MachineMutationType.Update, operation, correlation, "test", TimeSpan.FromMinutes(1)), operation, correlation);
        var denied = await fixture.Pipe.SendAsync(request, default);
        Assert.AreEqual(ErrorCodes.InvalidMessage, denied.ReadPayload<ErrorResponse>().Code);
        foreach (var json in new[] { "{}", "{\"sql\":\"DELETE FROM operational_mode\"}", "null" })
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            denied = await fixture.Pipe.SendAsync(request with { Payload = document.RootElement.Clone() }, default);
            Assert.AreEqual(ErrorCodes.InvalidMessage, denied.ReadPayload<ErrorResponse>().Code);
        }
        Assert.IsFalse((await fixture.Leases.GetAsync()).IsHeld);
    }

    [TestMethod]
    public async Task BrokerRejectsEnvelopeOperationMismatchBeforeMutation()
    {
        var fixture = await CreateFixtureAsync(false);
        var request = ModePersistenceProtocol.CreateRequest(new MachineMutationLeaseTryAcquireRequest(
            MachineMutationType.Mode, Guid.NewGuid(), Guid.NewGuid(), "test", TimeSpan.FromMinutes(1)));
        var response = await fixture.Pipe.SendAsync(request, default);
        Assert.AreEqual(ErrorCodes.InvalidMessage, response.ReadPayload<ErrorResponse>().Code);
        Assert.IsFalse((await fixture.Leases.GetAsync()).IsHeld);
    }

    [TestMethod]
    public async Task BrokerRejectsUntypedPreStateBeforeJournalMutation()
    {
        var fixture = await CreateFixtureAsync(false);
        var operation = Guid.NewGuid();
        var request = ModePersistenceProtocol.CreateRequest(new ModeTransitionActionJournalBeginApplyRequest(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), 1, operation,
            "{\"path\":\"C:/arbitrary\"}", new string('a', 64)), operation);
        var response = await fixture.Pipe.SendAsync(request, default);
        Assert.AreEqual(ErrorCodes.InvalidMessage, response.ReadPayload<ErrorResponse>().Code);
        Assert.AreEqual(0, (await fixture.Transitions.GetIncompleteAsync()).Count);
    }

    [TestMethod]
    public async Task ClientRejectsResponseFromDifferentRequest()
    {
        IMachineStateStore client = new NamedPipeModePersistenceClient((request, _) => Task.FromResult(
            ModePersistenceProtocol.Respond(request, true) with { RequestId = Guid.NewGuid() }));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => client.InitializeAsync());
    }

    [TestMethod]
    public async Task CompleteBrokerPipelineChecksDurableEvidenceBeforeAdapterMutation()
    {
        var fixture = await CreateFixtureAsync(false, fullBroker: true);
        var result = await fixture.Orchestrator.ExecuteAsync(fixture.Command);
        Assert.AreEqual(RuntimeModeExecutionDisposition.Completed, result.Disposition, result.Detail);
        Assert.AreEqual(1, fixture.Adapter.Mutations);
        Assert.AreEqual(ManagedServiceObservedState.Stopped, fixture.Adapter.State);
        Assert.AreEqual("WORK", (await fixture.Machine.GetOperationalModeAsync()).CommittedMode);
        Assert.IsFalse((await fixture.Leases.GetAsync()).IsHeld);
    }

    [TestMethod]
    public void RuntimeAssemblyDoesNotReferenceMachineSqliteImplementation()
    {
        Assert.IsFalse(typeof(RuntimeModeOrchestrator).Assembly.GetReferencedAssemblies()
            .Any(name => name.Name == "SplitOS.Persistence.Machine"));
        Assert.AreEqual("SplitOS.Contracts", typeof(IModeTransitionStore).Assembly.GetName().Name);
    }

    [TestMethod]
    public async Task ClientRejectsNullRequiredReadResult()
    {
        IMachineStateStore client = new NamedPipeModePersistenceClient((request, _) => Task.FromResult(
            ModePersistenceProtocol.Respond<OperationalModeRecord?>(request, null)));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => client.GetOperationalModeAsync());
    }

    [TestMethod]
    public async Task RecoveryInitializesOverPipeAndLeavesCleanBaseReady()
    {
        var fixture = await CreateFixtureAsync(false);
        IModeTransitionReconciliationStore recovery = fixture.Proxy;
        IModeTransitionRollbackStore rollback = fixture.Proxy;
        await recovery.InitializeAsync();
        await rollback.InitializeAsync();
        Assert.IsNull(await rollback.GetNextRollbackCandidateAsync(Guid.NewGuid()));
        var outcome = await new RuntimeModeRecoveryCoordinator(recovery, fixture.Proxy)
            .PrepareAsync(fixture.Command.ControlSessionKey, fixture.Command.LeaseLifetime);
        Assert.AreEqual(RuntimeModeRecoveryDisposition.Ready, outcome.Disposition);
        Assert.AreEqual("NONE", outcome.Snapshot.CanonicalMode.CommittedMode);
        Assert.AreEqual(ModeTerminalLeaseCleanupDisposition.AlreadyReleased, (await recovery.ReleaseTerminalLeaseAsync()).Disposition);
    }

    [TestMethod]
    public async Task RecoveryWaitsForLiveOwnerThenCancelsExpiredUnmutatedTransition()
    {
        var fixture = await CreateFixtureAsync(false);
        var transition = await CreateInterruptedRequestAsync(fixture);
        var coordinator = new RuntimeModeRecoveryCoordinator(fixture.Proxy, fixture.Proxy);
        var waiting = await coordinator.PrepareAsync(fixture.Command.ControlSessionKey, fixture.Command.LeaseLifetime);
        Assert.AreEqual(RuntimeModeRecoveryDisposition.WaitingForOwner, waiting.Disposition);
        Assert.AreEqual(transition.Revision, (await fixture.Transitions.GetAsync(transition.TransitionId))!.Revision);

        fixture.Time.Advance(TimeSpan.FromMinutes(3));
        var recovered = await coordinator.PrepareAsync(fixture.Command.ControlSessionKey, fixture.Command.LeaseLifetime);
        Assert.AreEqual(RuntimeModeRecoveryDisposition.Ready, recovered.Disposition, recovered.ProductCode);
        var terminal = (await fixture.Transitions.GetAsync(transition.TransitionId))!;
        Assert.AreEqual(PersistedModeTransitionState.Cancelled, terminal.TransitionState);
        Assert.IsTrue(terminal.FenceToken > transition.FenceToken);
        Assert.AreEqual(1, recovered.Snapshot.CanonicalMode.Revision);
        Assert.IsFalse((await fixture.Leases.GetAsync()).IsHeld);
        Assert.AreEqual(0, fixture.Adapter.Mutations);
    }

    [TestMethod]
    public async Task RecoveryDoesNotAdoptAnEarlierLogon()
    {
        var fixture = await CreateFixtureAsync(false);
        var transition = await CreateInterruptedRequestAsync(fixture);
        fixture.Time.Advance(TimeSpan.FromMinutes(3));
        var result = await new RuntimeModeRecoveryCoordinator(fixture.Proxy, fixture.Proxy)
            .PrepareAsync("another-logon", fixture.Command.LeaseLifetime);
        Assert.AreEqual(RuntimeModeRecoveryDisposition.RecoveryRequired, result.Disposition);
        Assert.AreEqual(ModeCrashReconciliationAction.ConvergeBaseForFreshSession, result.Snapshot.Action);
        IModeTransitionReconciliationStore recovery = fixture.Proxy;
        var denied = await recovery.TakeOverAsync(transition.TransitionId, transition.Revision, "another-logon", fixture.Command.LeaseLifetime);
        Assert.AreEqual(ModeReconciliationTakeoverDisposition.SessionConflict, denied.Disposition);
        Assert.AreEqual(transition, await fixture.Transitions.GetAsync(transition.TransitionId));
    }

    [TestMethod]
    public async Task RecoveryTakeoverRejectsForeignEnvelopeAndStaleRevision()
    {
        var fixture = await CreateFixtureAsync(false);
        var transition = await CreateInterruptedRequestAsync(fixture);
        fixture.Time.Advance(TimeSpan.FromMinutes(3));
        var request = new ModeTransitionReconciliationTakeOverRequest(transition.TransitionId, transition.Revision,
            transition.ControlSessionKey, fixture.Command.LeaseLifetime);
        var denied = await fixture.Pipe.SendAsync(ModePersistenceProtocol.CreateRequest(request, Guid.NewGuid(), transition.CorrelationId), CancellationToken.None);
        Assert.AreEqual(MessageTypes.ErrorResponse, denied.MessageType);
        Assert.AreEqual(transition, await fixture.Transitions.GetAsync(transition.TransitionId));
        IModeTransitionReconciliationStore recovery = fixture.Proxy;
        Assert.AreEqual(ModeReconciliationTakeoverDisposition.RevisionConflict,
            (await recovery.TakeOverAsync(transition.TransitionId, transition.Revision + 1, transition.ControlSessionKey, fixture.Command.LeaseLifetime)).Disposition);
        var acquired = await recovery.TakeOverAsync(transition.TransitionId, transition.Revision, transition.ControlSessionKey, fixture.Command.LeaseLifetime);
        Assert.AreEqual(ModeReconciliationTakeoverDisposition.Acquired, acquired.Disposition);
        var stale = await fixture.Transitions.AdvanceAsync(transition.TransitionId, acquired.Transition!.Revision,
            transition.LeaseId, transition.FenceToken, transition.OperationId, PersistedModeTransitionState.Cancelled,
            PersistedModeTransitionStage.Terminal, false, "CANCELLED");
        Assert.AreEqual(ModeTransitionAdvanceDisposition.LeaseConflict, stale.Disposition);
    }

    [TestMethod]
    public async Task RecoveryAfterLostCancellationResponseCleansTerminalLease()
    {
        var fixture = await CreateFixtureAsync(false);
        var transition = await CreateInterruptedRequestAsync(fixture);
        fixture.Time.Advance(TimeSpan.FromMinutes(3));
        var losingClient = new NamedPipeModePersistenceClient(async (request, token) =>
        {
            var response = await fixture.Pipe.SendAsync(request, token);
            if (request.MessageType == nameof(ModeTransitionAdvanceRequest))
                throw new IOException("Simulated lost response after durable cancellation.");
            return response;
        });
        await Assert.ThrowsAsync<IOException>(() => new RuntimeModeRecoveryCoordinator(losingClient, losingClient)
            .PrepareAsync(transition.ControlSessionKey, fixture.Command.LeaseLifetime));
        Assert.AreEqual(PersistedModeTransitionState.Cancelled, (await fixture.Transitions.GetAsync(transition.TransitionId))!.TransitionState);
        Assert.IsTrue((await fixture.Leases.GetAsync()).IsHeld);
        var freshClient = new NamedPipeModePersistenceClient(fixture.Pipe.SendAsync);
        var resumed = await new RuntimeModeRecoveryCoordinator(freshClient, freshClient)
            .PrepareAsync(transition.ControlSessionKey, fixture.Command.LeaseLifetime);
        Assert.AreEqual(RuntimeModeRecoveryDisposition.Ready, resumed.Disposition);
        Assert.IsFalse((await fixture.Leases.GetAsync()).IsHeld);
    }

    [TestMethod]
    public async Task RecoveryLeavesMutatedTransitionPendingActualStateVerification()
    {
        var fixture = await CreateFixtureAsync(false);
        fixture.VerifyBroker.Fail = true;
        await fixture.Orchestrator.ExecuteAsync(fixture.Command);
        fixture.Time.Advance(TimeSpan.FromMinutes(3));
        var recovered = await new RuntimeModeRecoveryCoordinator(fixture.Proxy, fixture.Proxy)
            .PrepareAsync(fixture.Command.ControlSessionKey, fixture.Command.LeaseLifetime);
        Assert.AreEqual(RuntimeModeRecoveryDisposition.ActualStateReconciliationRequired, recovered.Disposition);
        Assert.AreEqual(ModeCrashReconciliationAction.RollbackSource, recovered.Snapshot.Action);
        Assert.AreEqual("NONE", recovered.Snapshot.CanonicalMode.CommittedMode);
        Assert.AreEqual(1, (await fixture.Transitions.GetIncompleteAsync()).Count);
        Assert.IsTrue((await fixture.Leases.GetAsync()).IsHeld);
    }

    [TestMethod]
    public async Task RollbackJournalRoundTripPreservesUnknownOutcomeAndRejectsStaleFence()
    {
        var fixture = await CreateFixtureAsync(false);
        fixture.VerifyBroker.Fail = true;
        await fixture.Orchestrator.ExecuteAsync(fixture.Command);
        var transition = (await fixture.Transitions.GetIncompleteAsync()).Single();
        IModeTransitionRollbackStore rollback = fixture.Proxy;
        var candidate = (await rollback.GetNextRollbackCandidateAsync(transition.TransitionId))!;
        var stale = await rollback.BeginRollbackAsync(transition.TransitionId, candidate.ActionId, candidate.Revision,
            transition.LeaseId, transition.FenceToken + 1, transition.OperationId);
        Assert.AreEqual(ModeRollbackAdvanceDisposition.OwnershipConflict, stale.Disposition);
        var started = await rollback.BeginRollbackAsync(transition.TransitionId, candidate.ActionId, candidate.Revision,
            transition.LeaseId, transition.FenceToken, transition.OperationId);
        Assert.AreEqual(ModeRollbackAdvanceDisposition.Advanced, started.Disposition);
        var restarted = new NamedPipeModePersistenceClient(fixture.Pipe.SendAsync);
        var snapshot = await ((IModeTransitionReconciliationStore)restarted).InspectAsync(transition.ControlSessionKey);
        Assert.AreEqual(ModeCrashReconciliationAction.ReconcileRollbackOutcome, snapshot.Action);
        var recorded = await ((IModeTransitionRollbackStore)restarted).RecordRollbackResultAsync(transition.TransitionId,
            candidate.ActionId, started.Action!.Revision, transition.LeaseId, transition.FenceToken, transition.OperationId,
            PersistedModeRollbackResult.Unknown);
        Assert.AreEqual(PersistedModeActionState.RollbackFailed, recorded.Action!.State);
        Assert.AreEqual(ModeCrashReconciliationAction.EscalateRecovery,
            (await ((IModeTransitionReconciliationStore)restarted).InspectAsync(transition.ControlSessionKey)).Action);
        Assert.AreEqual("NONE", (await fixture.Machine.GetOperationalModeAsync()).CommittedMode);
    }

    [TestMethod]
    public async Task RecoveryNeverRevertsCommittedModeOrCleansUpdateLease()
    {
        var fixture = await CreateFixtureAsync(false);
        await fixture.Orchestrator.ExecuteAsync(fixture.Command);
        var committed = await fixture.Machine.GetOperationalModeAsync();
        var result = await new RuntimeModeRecoveryCoordinator(fixture.Proxy, fixture.Proxy)
            .PrepareAsync(fixture.Command.ControlSessionKey, fixture.Command.LeaseLifetime);
        Assert.AreEqual(RuntimeModeRecoveryDisposition.ActualStateReconciliationRequired, result.Disposition);
        Assert.AreEqual(ModeCrashReconciliationAction.VerifyCommittedTarget, result.Snapshot.Action);
        await fixture.Leases.TryAcquireAsync(MachineMutationType.Update, Guid.NewGuid(), Guid.NewGuid(),
            fixture.Command.ControlSessionKey, fixture.Command.LeaseLifetime);
        IModeTransitionReconciliationStore recovery = fixture.Proxy;
        Assert.AreEqual(ModeTerminalLeaseCleanupDisposition.Busy, (await recovery.ReleaseTerminalLeaseAsync()).Disposition);
        Assert.AreEqual(MachineMutationType.Update, (await fixture.Leases.GetAsync()).MutationType);
        Assert.AreEqual(committed, await fixture.Machine.GetOperationalModeAsync());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ManagedRollbackChecksFreshAuthorityBeforeAndAfterBroker(bool revokeDuringRollback)
    {
        var fixture = await CreateFixtureAsync(false);
        await fixture.Orchestrator.ExecuteAsync(fixture.Command);
        fixture.VerifyBroker.Fail = true;
        var command = fixture.Command with { OperationId = Guid.NewGuid(), CorrelationId = Guid.NewGuid(),
            TransitionId = Guid.NewGuid(), TargetMode = OperationalMode.Game };
        await fixture.Orchestrator.ExecuteAsync(command);
        fixture.Adapter.State = ManagedServiceObservedState.Stopped;
        var access = new RecoveryAccess();
        var authority = new RuntimeModeSourceAuthority(fixture.Proxy, new RecoverySession(command.ControlSessionKey), access);
        if (revokeDuringRollback) fixture.Adapter.OnQuery = () => access.Enabled = false;
        var result = await new ManagedServiceActionRollbackCoordinator(fixture.Proxy, fixture.Proxy, fixture.Proxy, fixture.Proxy, authority)
            .ExecuteNextAsync(command.TransitionId);
        Assert.AreEqual(revokeDuringRollback ? "RECONCILIATION_REQUIRED" : "ROLLED_BACK", result.Disposition, result.ProductCode);
        Assert.AreEqual(2, access.Calls);
        Assert.AreEqual("WORK", (await fixture.Machine.GetOperationalModeAsync()).CommittedMode);
        var snapshot = await ((IModeTransitionReconciliationStore)fixture.Proxy).InspectAsync(command.ControlSessionKey);
        Assert.AreEqual(revokeDuringRollback ? ModeCrashReconciliationAction.ReconcileRollbackOutcome : ModeCrashReconciliationAction.VerifySourceAfterRollback, snapshot.Action);
    }

    private sealed class RecoverySession(string key) : IControlSessionIdentity
    {
        public string GetCurrentKey() => key;
    }
    private sealed class RecoveryAccess : ICurrentModeAccess
    {
        public bool Enabled { get; set; } = true;
        public int Calls { get; private set; }
        public ValueTask<RuntimeAccessEvaluation> EvaluateAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(new RuntimeAccessEvaluation(Enabled ? "ENABLED" : "DISABLED", "TEST", 1));
        }
    }

    [TestMethod]
    public async Task ManagedSourceRollbackRequiresFreshAuthorityDecision()
    {
        var fixture = await CreateFixtureAsync(false);
        await fixture.Orchestrator.ExecuteAsync(fixture.Command);
        fixture.VerifyBroker.Fail = true;
        var switchCommand = fixture.Command with { OperationId = Guid.NewGuid(), CorrelationId = Guid.NewGuid(),
            TransitionId = Guid.NewGuid(), TargetMode = OperationalMode.Game };
        await fixture.Orchestrator.ExecuteAsync(switchCommand);
        var result = await new ManagedServiceActionRollbackCoordinator(fixture.Proxy, fixture.Proxy, fixture.Proxy, fixture.Proxy)
            .ExecuteNextAsync(switchCommand.TransitionId);
        Assert.AreEqual("MODE_ROLLBACK_SOURCE_AUTHORITY_REQUIRED", result.ProductCode);
        Assert.AreEqual(0, fixture.Adapter.Mutations);
        Assert.AreEqual("WORK", (await fixture.Machine.GetOperationalModeAsync()).CommittedMode);
    }

    [TestMethod]
    public async Task FullBrokerPipelineCompensatesAfterActualVerificationBecomesUnavailable()
    {
        var fixture = await CreateFixtureAsync(false, fullBroker: true);
        fixture.Adapter.FailObservationAfterMutation = true;
        var forward = await fixture.Orchestrator.ExecuteAsync(fixture.Command);
        Assert.IsFalse(forward.IsCompleted);
        Assert.AreEqual(ManagedServiceObservedState.Stopped, fixture.Adapter.State);
        Assert.AreEqual(1, fixture.Adapter.Mutations);
        fixture.Adapter.FailObservationAfterMutation = false;
        var result = await new ManagedServiceActionRollbackCoordinator(fixture.Proxy, fixture.Proxy, fixture.Proxy, fixture.Proxy)
            .ExecuteNextAsync(fixture.Command.TransitionId);
        Assert.AreEqual("ROLLED_BACK", result.Disposition, result.ProductCode);
        Assert.AreEqual(ManagedServiceObservedState.Running, fixture.Adapter.State);
        Assert.AreEqual(2, fixture.Adapter.Mutations);
        Assert.AreEqual("NONE", (await fixture.Machine.GetOperationalModeAsync()).CommittedMode);
    }

    [TestMethod]
    public async Task BrokerRollbackRestoresCapturedStateAndRequiresSourceVerification()
    {
        var fixture = await CreateRollbackFixtureAsync();
        var coordinator = new ManagedServiceActionRollbackCoordinator(fixture.Proxy, fixture.Proxy, fixture.Proxy, fixture.Proxy);
        var result = await coordinator.ExecuteNextAsync(fixture.Command.TransitionId);
        Assert.AreEqual("ROLLED_BACK", result.Disposition, result.ProductCode);
        Assert.AreEqual(ManagedServiceObservedState.Running, fixture.Adapter.State);
        Assert.AreEqual(1, fixture.Adapter.Mutations);
        var repeat = await coordinator.ExecuteNextAsync(fixture.Command.TransitionId);
        Assert.AreEqual("SOURCE_VERIFICATION_REQUIRED", repeat.Disposition);
        Assert.AreEqual(1, fixture.Adapter.Mutations);
        Assert.AreEqual("NONE", (await fixture.Machine.GetOperationalModeAsync()).CommittedMode);
        Assert.AreEqual(1, (await fixture.Transitions.GetIncompleteAsync()).Count);
    }

    [TestMethod]
    public async Task LostRollbackResponseIsReconciledWithoutRepeatingSatisfiedMutation()
    {
        var fixture = await CreateRollbackFixtureAsync();
        var losing = new NamedPipeModePersistenceClient(async (request, token) =>
        {
            var response = await fixture.Pipe.SendAsync(request, token);
            if (request.Capability == ManagedServiceRollbackProtocol.Capability) throw new IOException("Lost rollback reply.");
            return response;
        });
        await Assert.ThrowsAsync<IOException>(() => new ManagedServiceActionRollbackCoordinator(losing, losing, losing, losing)
            .ExecuteNextAsync(fixture.Command.TransitionId));
        var snapshot = await ((IModeTransitionReconciliationStore)fixture.Proxy).InspectAsync(fixture.Command.ControlSessionKey);
        Assert.AreEqual(ModeCrashReconciliationAction.ReconcileRollbackOutcome, snapshot.Action);
        Assert.AreEqual(ManagedServiceObservedState.Running, fixture.Adapter.State);
        var fresh = new NamedPipeModePersistenceClient(fixture.Pipe.SendAsync);
        var resumed = await new ManagedServiceActionRollbackCoordinator(fresh, fresh, fresh, fresh).ExecuteNextAsync(fixture.Command.TransitionId);
        Assert.AreEqual("ROLLED_BACK", resumed.Disposition);
        Assert.AreEqual(1, fixture.Adapter.Mutations);
    }

    [TestMethod]
    public async Task AdapterExceptionAfterMutationLeavesRecoverableRollbackJournal()
    {
        var fixture = await CreateRollbackFixtureAsync();
        fixture.Adapter.ThrowAfterMutation = true;
        var coordinator = new ManagedServiceActionRollbackCoordinator(fixture.Proxy, fixture.Proxy, fixture.Proxy, fixture.Proxy);
        await Assert.ThrowsAsync<InvalidDataException>(() => coordinator.ExecuteNextAsync(fixture.Command.TransitionId));
        fixture.Adapter.ThrowAfterMutation = false;
        Assert.AreEqual("ROLLED_BACK", (await coordinator.ExecuteNextAsync(fixture.Command.TransitionId)).Disposition);
        Assert.AreEqual(1, fixture.Adapter.Mutations);
    }

    [TestMethod]
    public async Task RollbackUsesReadBackInsteadOfAdapterSuccessClaim()
    {
        var fixture = await CreateRollbackFixtureAsync();
        fixture.Adapter.RefuseMutation = true;
        var coordinator = new ManagedServiceActionRollbackCoordinator(fixture.Proxy, fixture.Proxy, fixture.Proxy, fixture.Proxy);
        var result = await coordinator.ExecuteNextAsync(fixture.Command.TransitionId);
        Assert.AreEqual("UNKNOWN", result.Disposition);
        Assert.AreEqual(ModeCrashReconciliationAction.ReconcileRollbackOutcome,
            (await ((IModeTransitionReconciliationStore)fixture.Proxy).InspectAsync(fixture.Command.ControlSessionKey)).Action);
        fixture.Adapter.RefuseMutation = false;
        Assert.AreEqual("ROLLED_BACK", (await coordinator.ExecuteNextAsync(fixture.Command.TransitionId)).Disposition);
    }

    [TestMethod]
    public async Task RollbackRechecksLeaseAfterQueryBeforeMutation()
    {
        var fixture = await CreateRollbackFixtureAsync();
        fixture.Adapter.OnQuery = () => fixture.Time.Advance(TimeSpan.FromMinutes(3));
        var result = await new ManagedServiceActionRollbackCoordinator(fixture.Proxy, fixture.Proxy, fixture.Proxy, fixture.Proxy)
            .ExecuteNextAsync(fixture.Command.TransitionId);
        Assert.AreEqual("REJECTED", result.Disposition);
        Assert.AreEqual(0, fixture.Adapter.Mutations);
        fixture.Adapter.OnQuery = null;
        var snapshot = await ((IModeTransitionReconciliationStore)fixture.Proxy).InspectAsync(fixture.Command.ControlSessionKey);
        var transition = snapshot.Transition!;
        var takeover = await ((IModeTransitionReconciliationStore)fixture.Proxy).TakeOverAsync(transition.TransitionId,
            transition.Revision, transition.ControlSessionKey, fixture.Command.LeaseLifetime);
        Assert.AreEqual(ModeReconciliationTakeoverDisposition.Acquired, takeover.Disposition);
        var stale = await fixture.Proxy.RollbackAsync(transition.OperationId, transition.CorrelationId,
            new(transition.TransitionId, snapshot.FocusAction!.ActionId, transition.LeaseId, transition.FenceToken,
                transition.ControlSessionKey, snapshot.FocusAction.Revision));
        Assert.AreEqual("REJECTED", stale.Disposition);
        Assert.AreEqual(0, fixture.Adapter.Mutations);
        Assert.AreEqual("ROLLED_BACK", (await new ManagedServiceActionRollbackCoordinator(fixture.Proxy, fixture.Proxy, fixture.Proxy, fixture.Proxy)
            .ExecuteNextAsync(transition.TransitionId)).Disposition);
    }

    [TestMethod]
    public async Task RollbackRejectsCommittedTransitionAndClientSuppliedServiceTargets()
    {
        var fixture = await CreateFixtureAsync(false);
        await fixture.Orchestrator.ExecuteAsync(fixture.Command);
        Assert.AreEqual("REJECTED", (await new ManagedServiceActionRollbackCoordinator(fixture.Proxy, fixture.Proxy, fixture.Proxy, fixture.Proxy)
            .ExecuteNextAsync(fixture.Command.TransitionId)).Disposition);
        var request = ModePersistenceProtocol.CreateRequest(new { transitionId = fixture.Command.TransitionId,
            actionId = Guid.NewGuid(), leaseId = Guid.NewGuid(), fenceToken = 1, controlSessionKey = fixture.Command.ControlSessionKey,
            expectedActionRevision = 1, entries = new[] { new { managedServiceId = "ARBITRARY", desiredState = "RUNNING" } } })
            with { Capability = ManagedServiceRollbackProtocol.Capability, MessageType = nameof(MachineServicePolicyRollbackRequest) };
        Assert.AreEqual(MessageTypes.ErrorResponse, (await fixture.Pipe.SendAsync(request, CancellationToken.None)).MessageType);
        Assert.AreEqual(0, fixture.Adapter.Mutations);
        Assert.AreEqual("WORK", (await fixture.Machine.GetOperationalModeAsync()).CommittedMode);
    }

    private async Task<Fixture> CreateRollbackFixtureAsync()
    {
        var fixture = await CreateFixtureAsync(false);
        fixture.VerifyBroker.Fail = true;
        await fixture.Orchestrator.ExecuteAsync(fixture.Command);
        // The forward fake reports STOPPED; model the corresponding actual Windows state.
        fixture.Adapter.State = ManagedServiceObservedState.Stopped;
        return fixture;
    }

    private static async Task<ModeTransitionRecord> CreateInterruptedRequestAsync(Fixture fixture)
    {
        var command = fixture.Command;
        var lease = (await fixture.Leases.TryAcquireAsync(MachineMutationType.Mode, command.OperationId,
            command.CorrelationId, command.ControlSessionKey, command.LeaseLifetime)).Lease;
        var result = await fixture.Transitions.CreateAsync(command.TransitionId, command.OperationId, command.CorrelationId,
            PersistedModeOperationKind.Activate, "NONE", "WORK", 1, command.ControlSessionKey, lease.LeaseId!.Value, lease.FenceToken);
        Assert.AreEqual(ModeTransitionCreateDisposition.Created, result.Disposition);
        return result.Transition!;
    }

    private async Task<Fixture> CreateFixtureAsync(bool blocked, bool remote = true, bool fullBroker = false)
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
        var persistence = new BrokerModePersistenceHandler(machine, leases, transitions, policies, plans, journal, commits,
            new ModeTransitionRollbackStore(db, marker, quarantine, time),
            new ModeTransitionReconciliationStore(db, marker, quarantine, time));
        var adapter = new MemoryServiceAdapter();
        var catalog = new ReleaseManagedServiceCatalog();
        var pipe = new ModePersistencePipeFixture(new BrokerMessageHandler(machine,
            new BrokerManagedServicePolicyExecutor(new BrokerModeMutationFenceBoundary(new ModeMutationFenceStore(db, marker, quarantine, time)), journal, catalog, adapter),
            new BrokerManagedServiceSnapshotExecutor(new ModePreMutationEvidenceStore(db, marker, quarantine, time), catalog, adapter),
            new BrokerManagedServiceVerificationExecutor(new ModeVerificationEvidenceStore(db, marker, quarantine, time), catalog, adapter),
            persistence, new BrokerManagedServiceRollbackExecutor(new ModeMutationFenceStore(db, marker, quarantine, time), journal, catalog, adapter),
            new BrokerManagedServiceSourceVerificationExecutor(new ModeTransitionReconciliationStore(db, marker, quarantine, time),
                transitions, plans, policies, catalog, adapter),
            new BrokerModeBasePolicyResolver(new ModeTransitionReconciliationStore(db, marker, quarantine, time), transitions, plans),
            new BrokerModeBaseRecoveryExecutor(new ModeTransitionReconciliationStore(db, marker, quarantine, time), transitions, plans, catalog, adapter)));
        var proxy = new NamedPipeModePersistenceClient(pipe.SendAsync);
        var services = new ServicePipeClient(pipe);
        var orchestrator = new RuntimeModeOrchestrator(
            remote ? proxy : machine,
            remote ? proxy : leases,
            remote ? proxy : transitions,
            remote ? proxy : policies,
            remote ? proxy : plans,
            new ManagedServiceActionApplyCoordinator(remote ? proxy : journal, fullBroker ? services : applyBroker),
            new ManagedServiceActionVerifyCoordinator(remote ? proxy : journal, fullBroker ? services : verifyBroker),
            remote ? proxy : commits,
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
        return new Fixture(orchestrator, machine, leases, transitions, applyBroker, verifyBroker, preparation, command, proxy, pipe, adapter, time);
    }

    private sealed record Fixture(
        RuntimeModeOrchestrator Orchestrator,
        MachineStateStore Machine,
        MachineMutationLeaseStore Leases,
        ModeTransitionStore Transitions,
        FakeApplyBroker ApplyBroker,
        FakeVerifyBroker VerifyBroker,
        FakePreparationProvider Preparation,
        RuntimeModeExecutionCommand Command,
        NamedPipeModePersistenceClient Proxy,
        ModePersistencePipeFixture Pipe,
        MemoryServiceAdapter Adapter,
        FixedTimeProvider Time);

    private sealed class MemoryServiceAdapter : IManagedServiceAdapter
    {
        public ManagedServiceObservedState State { get; set; } = ManagedServiceObservedState.Running;
        public bool ThrowAfterMutation { get; set; }
        public bool RefuseMutation { get; set; }
        public bool FailObservationAfterMutation { get; set; }
        public Action? OnQuery { get; set; }
        public int Mutations { get; private set; }
        public ValueTask<ManagedServiceObservation> QueryAsync(ManagedServiceCatalogEntry entry, CancellationToken cancellationToken = default)
        {
            OnQuery?.Invoke();
            return ValueTask.FromResult(new ManagedServiceObservation(FailObservationAfterMutation && Mutations > 0 ? ManagedServiceObservedState.Unknown : State));
        }
        public ValueTask<ManagedServiceTechnicalResult> ApplyAsync(ManagedServiceCatalogEntry entry, ManagedServiceDesiredState desiredState, CancellationToken cancellationToken = default)
        {
            Mutations++;
            if (!RefuseMutation)
                State = desiredState == ManagedServiceDesiredState.Running ? ManagedServiceObservedState.Running : ManagedServiceObservedState.Stopped;
            if (ThrowAfterMutation) throw new IOException("Simulated adapter response loss after mutation.");
            return ValueTask.FromResult(new ManagedServiceTechnicalResult(ManagedServiceApplyDisposition.AppliedVerified,
                true, "SCM_OPERATION_ACCEPTED", State, "VERIFIED"));
        }
    }

    private sealed class ServicePipeClient(ModePersistencePipeFixture pipe) : IManagedServiceActionBrokerClient, IManagedServiceActionVerificationBrokerClient
    {
        public Task<MachineServicePolicySnapshotResult> SnapshotAsync(Guid operationId, Guid correlationId, MachineServicePolicySnapshotRequest request, CancellationToken cancellationToken = default)
            => SendAsync<MachineServicePolicySnapshotRequest, MachineServicePolicySnapshotResult>(request, MessageTypes.MachineServicePolicySnapshotRequest, Capabilities.MachineServicePolicySnapshot, operationId, correlationId, cancellationToken);
        public Task<MachineServicePolicyApplyResult> ApplyAsync(Guid operationId, Guid correlationId, MachineServicePolicyApplyRequest request, CancellationToken cancellationToken = default)
            => SendAsync<MachineServicePolicyApplyRequest, MachineServicePolicyApplyResult>(request, MessageTypes.MachineServicePolicyApplyRequest, Capabilities.MachineServicePolicyApply, operationId, correlationId, cancellationToken);
        public Task<MachineServicePolicyVerifyResult> VerifyAsync(Guid operationId, Guid correlationId, MachineServicePolicyVerifyRequest request, CancellationToken cancellationToken = default)
            => SendAsync<MachineServicePolicyVerifyRequest, MachineServicePolicyVerifyResult>(request, MessageTypes.MachineServicePolicyVerifyRequest, Capabilities.MachineServicePolicyVerify, operationId, correlationId, cancellationToken);
        private async Task<TResponse> SendAsync<TRequest, TResponse>(TRequest payload, string type, string capability, Guid operation, Guid correlation, CancellationToken token)
        {
            var result = await pipe.SendAsync(WireMessage.Create(type, payload, capability, operation, correlation), token);
            Assert.AreNotEqual(MessageTypes.ErrorResponse, result.MessageType, result.Payload.ToString());
            return result.ReadPayload<TResponse>();
        }
    }

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
        public bool Fail { get; set; }

        public Task<MachineServicePolicyVerifyResult> VerifyAsync(
            Guid operationId,
            Guid correlationId,
            MachineServicePolicyVerifyRequest request,
            CancellationToken cancellationToken = default)
        {
            VerifyCalls++;
            return Task.FromResult(new MachineServicePolicyVerifyResult(
                Fail ? "MISMATCH" : "VERIFIED",
                "SERVICE_POLICY_VERIFIED",
                request.Entries.Select(entry => new ManagedServicePolicyVerificationEntryResult(
                    entry.ManagedServiceId,
                    entry.DesiredState,
                    Fail ? "RUNNING" : entry.DesiredState,
                    Fail ? "MISMATCH" : "VERIFIED",
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
        private DateTimeOffset _utcNow = utcNow;
        public void Advance(TimeSpan elapsed) => _utcNow += elapsed;
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
