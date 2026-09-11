using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Broker.Service;
using SplitOS.Contracts.ModePersistence;
using SplitOS.RuntimeHost.ModeRuntime;
using SplitOS.Persistence.Machine;

namespace SplitOS.RuntimeHost.Tests;

public sealed partial class RuntimeModeOrchestratorTests
{
    [TestMethod]
    public async Task PreviouslyCommittedBasePlanCanVerifyNoneWithoutManagedEntitlement()
    {
        var fixture = await CreateFixtureAsync(false);
        await fixture.Orchestrator.ExecuteAsync(fixture.Command);
        var deactivate = fixture.Command with { OperationId = Guid.NewGuid(), CorrelationId = Guid.NewGuid(),
            TransitionId = Guid.NewGuid(), TargetMode = OperationalMode.None, ActivationEpochId = null };
        Assert.IsTrue((await fixture.Orchestrator.ExecuteAsync(deactivate)).IsCompleted);
        fixture.VerifyBroker.Fail = true;
        var activate = fixture.Command with { OperationId = Guid.NewGuid(), CorrelationId = Guid.NewGuid(), TransitionId = Guid.NewGuid() };
        await fixture.Orchestrator.ExecuteAsync(activate);
        fixture.Adapter.State = ManagedServiceObservedState.Stopped;
        await new ManagedServiceActionRollbackCoordinator(fixture.Proxy, fixture.Proxy, fixture.Proxy, fixture.Proxy)
            .ExecuteNextAsync(activate.TransitionId);
        fixture.Adapter.State = ManagedServiceObservedState.Stopped;
        var access = new RecoveryAccess { Enabled = false };
        var authority = new RuntimeModeSourceAuthority(fixture.Proxy, new RecoverySession(activate.ControlSessionKey), access);
        var source = await fixture.Machine.GetOperationalModeAsync();
        var completed = await new RuntimeModeRollbackCompletionCoordinator(fixture.Proxy, fixture.Proxy, authority, fixture.Proxy)
            .CompleteAsync(activate.TransitionId);
        Assert.AreEqual("FAILED_WITH_SAFE_FALLBACK", completed.Disposition, completed.ProductCode);
        Assert.AreEqual(source, await fixture.Machine.GetOperationalModeAsync());
        Assert.AreEqual(0, access.Calls);
        Assert.IsFalse((await fixture.Leases.GetAsync()).IsHeld);
    }

    [TestMethod]
    public async Task RestoredPreStateDoesNotReplaceWholeSourcePlanVerification()
    {
        var fixture = await CreateManagedRolledBackFixtureAsync();
        var coordinator = CreateCompletion(fixture);
        // Captured RUNNING was restored, but committed WORK requires STOPPED.
        Assert.AreEqual(ManagedServiceObservedState.Running, fixture.Adapter.State);
        var mismatch = await coordinator.CompleteAsync(fixture.Command.TransitionId);
        Assert.AreEqual("MISMATCH", mismatch.Disposition);
        Assert.AreEqual(1, (await fixture.Transitions.GetIncompleteAsync()).Count);
        fixture.Adapter.State = ManagedServiceObservedState.Stopped;
        var source = await fixture.Machine.GetOperationalModeAsync();
        var completed = await coordinator.CompleteAsync(fixture.Command.TransitionId);
        Assert.AreEqual("FAILED_WITH_SAFE_FALLBACK", completed.Disposition, completed.ProductCode);
        Assert.AreEqual(source, await fixture.Machine.GetOperationalModeAsync());
        Assert.AreEqual(0, (await fixture.Transitions.GetIncompleteAsync()).Count);
        Assert.IsFalse((await fixture.Leases.GetAsync()).IsHeld);
    }

    [TestMethod]
    public async Task InitialBaseVerifiesRestorationOfOriginalWindowsState()
    {
        var fixture = await CreateRollbackFixtureAsync();
        await new ManagedServiceActionRollbackCoordinator(fixture.Proxy, fixture.Proxy, fixture.Proxy, fixture.Proxy)
            .ExecuteNextAsync(fixture.Command.TransitionId);
        var result = await CreateCompletion(fixture).CompleteAsync(fixture.Command.TransitionId);
        Assert.AreEqual("FAILED_WITH_SAFE_FALLBACK", result.Disposition, result.ProductCode);
        Assert.AreEqual(0, (await fixture.Transitions.GetIncompleteAsync()).Count);
        Assert.IsFalse((await fixture.Leases.GetAsync()).IsHeld);
        Assert.AreEqual(ManagedServiceObservedState.Running, fixture.Adapter.State);
    }

    [TestMethod]
    public async Task SourceVerificationRejectsIncompleteCompensation()
    {
        var fixture = await CreateRollbackFixtureAsync();
        var result = await CreateCompletion(fixture).CompleteAsync(fixture.Command.TransitionId);
        Assert.AreEqual("REJECTED", result.Disposition);
        Assert.AreEqual("MODE_SOURCE_VERIFY_OWNER_INVALID", result.ProductCode);
        Assert.AreEqual(0, fixture.Adapter.Mutations);
    }

    [TestMethod]
    public async Task SourceVerificationCannotOutliveItsLease()
    {
        var fixture = await CreateManagedRolledBackFixtureAsync();
        fixture.Adapter.State = ManagedServiceObservedState.Stopped;
        fixture.Adapter.OnQuery = () => fixture.Time.Advance(TimeSpan.FromMinutes(3));
        var result = await CreateCompletion(fixture).CompleteAsync(fixture.Command.TransitionId);
        Assert.AreEqual("REJECTED", result.Disposition);
        Assert.AreEqual(PersistedModeTransitionState.RollingBack, (await fixture.Transitions.GetAsync(fixture.Command.TransitionId))!.TransitionState);
    }

    [TestMethod]
    public async Task SourceVerificationCannotTerminalizeAfterEntitlementRevocation()
    {
        var fixture = await CreateManagedRolledBackFixtureAsync();
        fixture.Adapter.State = ManagedServiceObservedState.Stopped;
        var access = new RecoveryAccess();
        fixture.Adapter.OnQuery = () => access.Enabled = false;
        var authority = new RuntimeModeSourceAuthority(fixture.Proxy, new RecoverySession(fixture.Command.ControlSessionKey), access);
        var result = await new RuntimeModeRollbackCompletionCoordinator(fixture.Proxy, fixture.Proxy, authority, fixture.Proxy)
            .CompleteAsync(fixture.Command.TransitionId);
        Assert.AreEqual("MODE_SOURCE_AUTHORITY_BASE_CONVERGENCE_REQUIRED", result.ProductCode);
        Assert.AreEqual(1, (await fixture.Transitions.GetIncompleteAsync()).Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SourceCompletionRecoversLostStageOrTerminalResponse(bool loseTerminal)
    {
        var fixture = await CreateManagedRolledBackFixtureAsync();
        fixture.Adapter.State = ManagedServiceObservedState.Stopped;
        var losing = new NamedPipeModePersistenceClient(async (request, token) =>
        {
            var response = await fixture.Pipe.SendAsync(request, token);
            if (request.MessageType == nameof(ModeTransitionAdvanceRequest))
            {
                var advance = request.ReadPayload<ModeTransitionAdvanceRequest>();
                if ((advance.NextStage == PersistedModeTransitionStage.Terminal) == loseTerminal)
                    throw new IOException("Lost durable completion response.");
            }
            return response;
        });
        var authority = new RuntimeModeSourceAuthority(fixture.Proxy, new RecoverySession(fixture.Command.ControlSessionKey), new RecoveryAccess());
        await Assert.ThrowsAsync<IOException>(() => new RuntimeModeRollbackCompletionCoordinator(losing, losing, authority, losing)
            .CompleteAsync(fixture.Command.TransitionId));
        if (loseTerminal)
        {
            Assert.AreEqual(PersistedModeTransitionState.FailedWithSafeFallback,
                (await fixture.Transitions.GetAsync(fixture.Command.TransitionId))!.TransitionState);
            await new RuntimeModeRecoveryCoordinator(fixture.Proxy, fixture.Proxy)
                .PrepareAsync(fixture.Command.ControlSessionKey, fixture.Command.LeaseLifetime);
        }
        else
        {
            Assert.AreEqual(PersistedModeTransitionStage.RollbackVerify,
                (await fixture.Transitions.GetAsync(fixture.Command.TransitionId))!.Stage);
            Assert.AreEqual("FAILED_WITH_SAFE_FALLBACK", (await CreateCompletion(fixture).CompleteAsync(fixture.Command.TransitionId)).Disposition);
        }
        Assert.IsFalse((await fixture.Leases.GetAsync()).IsHeld);
        Assert.AreEqual("WORK", (await fixture.Machine.GetOperationalModeAsync()).CommittedMode);
    }

    private async Task<Fixture> CreateManagedRolledBackFixtureAsync()
    {
        var fixture = await CreateFixtureAsync(false);
        await fixture.Orchestrator.ExecuteAsync(fixture.Command);
        fixture.VerifyBroker.Fail = true;
        var command = fixture.Command with { OperationId = Guid.NewGuid(), CorrelationId = Guid.NewGuid(),
            TransitionId = Guid.NewGuid(), TargetMode = OperationalMode.Game };
        await fixture.Orchestrator.ExecuteAsync(command);
        fixture.Adapter.State = ManagedServiceObservedState.Stopped;
        var authority = new RuntimeModeSourceAuthority(fixture.Proxy, new RecoverySession(command.ControlSessionKey), new RecoveryAccess());
        Assert.AreEqual("ROLLED_BACK", (await new ManagedServiceActionRollbackCoordinator(fixture.Proxy, fixture.Proxy, fixture.Proxy, fixture.Proxy, authority)
            .ExecuteNextAsync(command.TransitionId)).Disposition);
        return fixture with { Command = command };
    }

    private static RuntimeModeRollbackCompletionCoordinator CreateCompletion(Fixture fixture)
        => new(fixture.Proxy, fixture.Proxy, new RuntimeModeSourceAuthority(fixture.Proxy,
            new RecoverySession(fixture.Command.ControlSessionKey), new RecoveryAccess()), fixture.Proxy);
}
