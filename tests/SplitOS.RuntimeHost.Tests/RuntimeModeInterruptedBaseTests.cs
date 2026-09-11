using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Broker.Service;
using SplitOS.Contracts.Protocol;
using SplitOS.Persistence.Machine;
using SplitOS.RuntimeHost.ModeRuntime;

namespace SplitOS.RuntimeHost.Tests;

public sealed partial class RuntimeModeOrchestratorTests
{
    private static RuntimeModeAutomaticRecoveryCoordinator CreateBaseRecovery(Fixture fixture, RecoveryAccess? access = null,
        IModeBaseRecoveryClient? client = null)
    {
        var session = new RecoverySession(fixture.Command.ControlSessionKey);
        var authority = new RuntimeModeSourceAuthority(fixture.Proxy, session, access ?? new RecoveryAccess { Enabled = false });
        return new(new(fixture.Proxy, fixture.Proxy), fixture.Proxy, fixture.Proxy, fixture.Proxy, session, authority,
            new(fixture.Proxy, fixture.Proxy, fixture.Proxy, fixture.Proxy, authority),
            new(fixture.Proxy, fixture.Proxy, authority, fixture.Proxy), client ?? fixture.Proxy);
    }

    private async Task<Fixture> CreateInterruptedManagedAsync(ManagedServiceObservedState initial = ManagedServiceObservedState.Running)
    {
        var fixture = await CreateFixtureAsync(false, fullBroker: true);
        fixture.Adapter.State = initial;
        Assert.IsTrue((await fixture.Orchestrator.ExecuteAsync(fixture.Command)).IsCompleted);
        fixture.VerifyBroker.Fail = true;
        var executor = new RuntimeModeOrchestrator(fixture.Proxy, fixture.Proxy, fixture.Proxy, fixture.Proxy, fixture.Proxy,
            new(fixture.Proxy, new ServicePipeClient(fixture.Pipe)), new(fixture.Proxy, fixture.VerifyBroker), fixture.Proxy,
            new ModeBlockerEngine(Array.Empty<IModeBlockerProvider>(), fixture.Time), fixture.Preparation, fixture.Time);
        var command = fixture.Command with { OperationId = Guid.NewGuid(), CorrelationId = Guid.NewGuid(),
            TransitionId = Guid.NewGuid(), TargetMode = OperationalMode.Game };
        Assert.IsFalse((await executor.ExecuteAsync(command)).IsCompleted);
        fixture.Time.Advance(TimeSpan.FromMinutes(3));
        return fixture;
    }

    [TestMethod]
    [DataRow(ManagedServiceObservedState.Running)]
    [DataRow(ManagedServiceObservedState.Stopped)]
    public async Task InterruptedManagedModeConvergesToOriginalWindowsBase(ManagedServiceObservedState initial)
    {
        var fixture = await CreateInterruptedManagedAsync(initial);
        var interrupted = (await fixture.Transitions.GetIncompleteAsync()).Single();
        var planBefore = await ((IModeTransitionActionPlanStore)fixture.Proxy).GetAsync(interrupted.TransitionId);
        var outcome = await CreateBaseRecovery(fixture).ReconcileAsync();
        Assert.IsTrue(outcome.MayCheckAccess, outcome.ProductCode);
        Assert.AreEqual("MODE_BASE_RECOVERY_COMPLETED", outcome.ProductCode);
        Assert.AreEqual(initial, fixture.Adapter.State);
        var canonical = await fixture.Machine.GetOperationalModeAsync();
        Assert.AreEqual("NONE", canonical.CommittedMode);
        Assert.IsNull(canonical.ActivationEpochId);
        Assert.IsNull(canonical.PolicyIdentity);
        Assert.AreEqual(3, canonical.Revision);
        Assert.IsFalse((await fixture.Leases.GetAsync()).IsHeld);
        Assert.AreEqual(0, (await fixture.Transitions.GetIncompleteAsync()).Count);
        var terminal = (await fixture.Transitions.GetAsync(interrupted.TransitionId))!;
        Assert.AreEqual("GAME", terminal.TargetMode);
        Assert.IsFalse(terminal.CommitDurable);
        Assert.IsNotNull(terminal.RecoveryContextId);
        var planAfter = await ((IModeTransitionActionPlanStore)fixture.Proxy).GetAsync(interrupted.TransitionId);
        CollectionAssert.AreEqual(planBefore!.Actions.ToArray(), planAfter!.Actions.ToArray());
    }

    [TestMethod]
    public async Task RevokedAccessDuringInterruptedSwitchConvergesBaseBeforeIdleAccessLossCheck()
    {
        var fixture = await CreateInterruptedManagedAsync();
        var access = new RecoveryAccess { Enabled = false };
        var recovered = await CreateBaseRecovery(fixture, access).ReconcileAsync();
        Assert.IsTrue(recovered.MayCheckAccess, recovered.ProductCode);
        Assert.AreEqual("MODE_BASE_RECOVERY_COMPLETED", recovered.ProductCode);
        Assert.AreEqual("NONE", (await fixture.Machine.GetOperationalModeAsync()).CommittedMode);
        Assert.AreEqual(0, (await fixture.Transitions.GetIncompleteAsync()).Count);
        var accessCallsAfterRecovery = access.Calls;
        var mutationsAfterRecovery = fixture.Adapter.Mutations;

        var idle = await CreateAccessLossCoordinator(fixture, access).ReconcileAsync();
        Assert.AreEqual("NO_CHANGE", idle.Disposition);
        Assert.AreEqual("MODE_ACCESS_LOSS_ALREADY_NONE", idle.ProductCode);
        Assert.AreEqual(accessCallsAfterRecovery, access.Calls);
        Assert.AreEqual(mutationsAfterRecovery, fixture.Adapter.Mutations);
        Assert.IsFalse((await fixture.Leases.GetAsync()).IsHeld);
    }

    [TestMethod]
    public async Task BaseRecoveryRetainsIntentAndResumesEvenWhenAccessReturns()
    {
        var fixture = await CreateInterruptedManagedAsync();
        fixture.Adapter.ThrowAfterMutation = true;
        var access = new RecoveryAccess { Enabled = false };
        await Assert.ThrowsAsync<InvalidDataException>(() => CreateBaseRecovery(fixture, access).ReconcileAsync());
        var pending = (await fixture.Transitions.GetIncompleteAsync()).Single();
        Assert.IsNotNull(pending.RecoveryContextId);
        Assert.AreEqual("WORK", (await fixture.Machine.GetOperationalModeAsync()).CommittedMode);
        fixture.Adapter.ThrowAfterMutation = false;
        access.Enabled = true;
        fixture.Time.Advance(TimeSpan.FromMinutes(3));
        Assert.IsTrue((await CreateBaseRecovery(fixture, access).ReconcileAsync()).MayCheckAccess);
        Assert.AreEqual("NONE", (await fixture.Machine.GetOperationalModeAsync()).CommittedMode);
        Assert.AreEqual(ManagedServiceObservedState.Running, fixture.Adapter.State);
    }

    [TestMethod]
    public async Task BaseRecoveryCannotCommitAnUnverifiedServiceState()
    {
        var fixture = await CreateInterruptedManagedAsync();
        var canonical = await fixture.Machine.GetOperationalModeAsync();
        fixture.Adapter.RefuseMutation = true;
        Assert.IsFalse((await CreateBaseRecovery(fixture).ReconcileAsync()).MayCheckAccess);
        Assert.AreEqual(canonical, await fixture.Machine.GetOperationalModeAsync());
        Assert.AreEqual(1, (await fixture.Transitions.GetIncompleteAsync()).Count);
        fixture.Adapter.RefuseMutation = false;
        fixture.Time.Advance(TimeSpan.FromMinutes(3));
        Assert.IsTrue((await CreateBaseRecovery(fixture).ReconcileAsync()).MayCheckAccess);
    }

    [TestMethod]
    public async Task BaseRecoveryRejectsLeaseExpiryDuringObservation()
    {
        var fixture = await CreateInterruptedManagedAsync();
        var mutations = fixture.Adapter.Mutations;
        fixture.Adapter.OnQuery = () => fixture.Time.Advance(TimeSpan.FromMinutes(3));
        Assert.IsFalse((await CreateBaseRecovery(fixture).ReconcileAsync()).MayCheckAccess);
        Assert.AreEqual(mutations, fixture.Adapter.Mutations);
        Assert.AreEqual("WORK", (await fixture.Machine.GetOperationalModeAsync()).CommittedMode);
        fixture.Adapter.OnQuery = null;
        Assert.IsTrue((await CreateBaseRecovery(fixture).ReconcileAsync()).MayCheckAccess);
    }

    [TestMethod]
    public async Task LostBaseCommitResponseIsRecoveredWithoutDuplicateMutation()
    {
        var fixture = await CreateInterruptedManagedAsync();
        var losing = new NamedPipeModePersistenceClient(async (request, token) =>
        {
            var response = await fixture.Pipe.SendAsync(request, token);
            if (request.Capability == ModeBaseRecoveryProtocol.Capability) throw new IOException("Lost BASE completion response.");
            return response;
        });
        await Assert.ThrowsAsync<IOException>(() => CreateBaseRecovery(fixture, client: losing).ReconcileAsync());
        var mutations = fixture.Adapter.Mutations;
        Assert.IsTrue((await CreateBaseRecovery(fixture).ReconcileAsync()).MayCheckAccess);
        Assert.AreEqual(mutations, fixture.Adapter.Mutations);
        Assert.AreEqual("NONE", (await fixture.Machine.GetOperationalModeAsync()).CommittedMode);
    }

    [TestMethod]
    public async Task FailedNextActivationCanVerifyPreviouslyRecoveredBase()
    {
        var fixture = await CreateInterruptedManagedAsync();
        Assert.IsTrue((await CreateBaseRecovery(fixture).ReconcileAsync()).MayCheckAccess);
        var command = fixture.Command with { OperationId = Guid.NewGuid(), CorrelationId = Guid.NewGuid(),
            TransitionId = Guid.NewGuid(), ActivationEpochId = Guid.NewGuid() };
        // Use actual apply and a failed verification to retain a complete compensation plan.
        var executor = new RuntimeModeOrchestrator(fixture.Proxy, fixture.Proxy, fixture.Proxy, fixture.Proxy, fixture.Proxy,
            new(fixture.Proxy, new ServicePipeClient(fixture.Pipe)), new(fixture.Proxy, fixture.VerifyBroker), fixture.Proxy,
            new ModeBlockerEngine(Array.Empty<IModeBlockerProvider>(), fixture.Time), fixture.Preparation, fixture.Time);
        Assert.IsFalse((await executor.ExecuteAsync(command)).IsCompleted);
        fixture.Time.Advance(TimeSpan.FromMinutes(3));
        Assert.IsTrue((await CreateBaseRecovery(fixture).ReconcileAsync()).MayCheckAccess);
        Assert.AreEqual(0, (await fixture.Transitions.GetIncompleteAsync()).Count);
        Assert.AreEqual(ManagedServiceObservedState.Running, fixture.Adapter.State);
    }

    [TestMethod]
    public async Task BaseRecoveryRejectsForeignSessionAndStaleFenceWithoutMutation()
    {
        var fixture = await CreateInterruptedManagedAsync();
        var owned = await new RuntimeModeRecoveryCoordinator(fixture.Proxy, fixture.Proxy)
            .PrepareAsync(fixture.Command.ControlSessionKey, fixture.Command.LeaseLifetime);
        var transition = owned.Snapshot.Transition!;
        var mutations = fixture.Adapter.Mutations;
        foreach (var request in new[]
        {
            new MachineModeBaseRecoveryRequest(transition.TransitionId, transition.LeaseId, transition.FenceToken + 1,
                transition.ControlSessionKey, transition.Revision),
            new MachineModeBaseRecoveryRequest(transition.TransitionId, transition.LeaseId, transition.FenceToken,
                "foreign-logon", transition.Revision)
        })
            Assert.AreEqual("RECOVERY_PENDING", (await fixture.Proxy.RecoverBaseAsync(transition.OperationId, transition.CorrelationId, request)).Disposition);
        Assert.AreEqual(mutations, fixture.Adapter.Mutations);
        Assert.IsNull((await fixture.Transitions.GetAsync(transition.TransitionId))!.RecoveryContextId);
    }

    [TestMethod]
    public async Task BaseIntentPreventsOrdinaryRollbackAndTerminalization()
    {
        var fixture = await CreateInterruptedManagedAsync();
        fixture.Adapter.RefuseMutation = true;
        Assert.IsFalse((await CreateBaseRecovery(fixture).ReconcileAsync()).MayCheckAccess);
        var transition = (await fixture.Transitions.GetIncompleteAsync()).Single();
        var action = (await ((IModeTransitionActionPlanStore)fixture.Proxy).GetAsync(transition.TransitionId))!.Actions.Single();
        Assert.AreEqual("REJECTED", (await fixture.Proxy.RollbackAsync(transition.OperationId, transition.CorrelationId,
            new(transition.TransitionId, action.ActionId, transition.LeaseId, transition.FenceToken, transition.ControlSessionKey, action.Revision))).Disposition);
        var advanced = await ((IModeTransitionStore)fixture.Proxy).AdvanceAsync(transition.TransitionId, transition.Revision, transition.LeaseId,
            transition.FenceToken, transition.OperationId, PersistedModeTransitionState.Cancelled,
            PersistedModeTransitionStage.Terminal, false, "CANCELLED");
        Assert.AreEqual("MODE_BASE_RECOVERY_OWNS_TRANSITION", advanced.ProductCode);
        Assert.AreEqual("WORK", (await fixture.Machine.GetOperationalModeAsync()).CommittedMode);
    }

    [TestMethod]
    public async Task LostForwardResponseCanConvergeToBaseWithoutGuessingApplyOutcome()
    {
        var fixture = await CreateFixtureAsync(false, fullBroker: true);
        Assert.IsTrue((await fixture.Orchestrator.ExecuteAsync(fixture.Command)).IsCompleted);
        var services = new ServicePipeClient(fixture.Pipe);
        var executor = new RuntimeModeOrchestrator(fixture.Proxy, fixture.Proxy, fixture.Proxy, fixture.Proxy, fixture.Proxy,
            new(fixture.Proxy, new LostServiceApplyResponse(services)), new(fixture.Proxy, services), fixture.Proxy,
            new ModeBlockerEngine(Array.Empty<IModeBlockerProvider>(), fixture.Time), fixture.Preparation, fixture.Time);
        var command = fixture.Command with { OperationId = Guid.NewGuid(), CorrelationId = Guid.NewGuid(),
            TransitionId = Guid.NewGuid(), TargetMode = OperationalMode.Game };
        Assert.IsFalse((await executor.ExecuteAsync(command)).IsCompleted);

        var transition = (await fixture.Transitions.GetIncompleteAsync()).Single();
        var plan = await ((IModeTransitionActionPlanStore)fixture.Proxy).GetAsync(transition.TransitionId);
        var action = await ((IModeTransitionActionJournalStore)fixture.Proxy).GetAsync(plan!.Actions.Single().ActionId);
        Assert.IsNotNull(action);
        Assert.AreEqual(PersistedModeActionState.Failed, action.State);
        Assert.AreEqual("UNKNOWN", action.ApplyResultCode);
        Assert.IsNotNull(action.PreStateJson);
        Assert.AreEqual(ModeCrashReconciliationAction.RollbackSource,
            (await ((IModeTransitionReconciliationStore)fixture.Proxy).InspectAsync(command.ControlSessionKey)).Action);

        fixture.Time.Advance(TimeSpan.FromMinutes(3));
        Assert.IsTrue((await CreateBaseRecovery(fixture).ReconcileAsync()).MayCheckAccess);
        Assert.AreEqual("NONE", (await fixture.Machine.GetOperationalModeAsync()).CommittedMode);
        Assert.AreEqual(ManagedServiceObservedState.Running, fixture.Adapter.State);
    }

    private sealed class LostServiceApplyResponse(IManagedServiceActionBrokerClient inner) : IManagedServiceActionBrokerClient
    {
        public Task<MachineServicePolicySnapshotResult> SnapshotAsync(Guid operationId, Guid correlationId,
            MachineServicePolicySnapshotRequest request, CancellationToken cancellationToken = default)
            => inner.SnapshotAsync(operationId, correlationId, request, cancellationToken);

        public async Task<MachineServicePolicyApplyResult> ApplyAsync(Guid operationId, Guid correlationId,
            MachineServicePolicyApplyRequest request, CancellationToken cancellationToken = default)
        {
            await inner.ApplyAsync(operationId, correlationId, request, cancellationToken);
            throw new IOException("Lost native apply response.");
        }
    }
}
