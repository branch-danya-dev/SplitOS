using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Broker.Service;
using SplitOS.Persistence.Machine;
using SplitOS.RuntimeHost.ModeRuntime;

namespace SplitOS.RuntimeHost.Tests;

public sealed partial class RuntimeModeOrchestratorTests
{
    private static RuntimeModeAutomaticRecoveryCoordinator CreateAutomaticRecovery(Fixture fixture,
        RecoveryAccess? access = null, string? sessionKey = null)
    {
        var session = new RecoverySession(sessionKey ?? fixture.Command.ControlSessionKey);
        var authority = new RuntimeModeSourceAuthority(fixture.Proxy, session, access ?? new RecoveryAccess());
        return new(new(fixture.Proxy, fixture.Proxy), fixture.Proxy, fixture.Proxy, fixture.Proxy, session, authority,
            new(fixture.Proxy, fixture.Proxy, fixture.Proxy, fixture.Proxy, authority),
            new(fixture.Proxy, fixture.Proxy, authority, fixture.Proxy));
    }

    [TestMethod]
    public async Task AutomaticRecoveryWaitsThenCancelsUnmutatedRequest()
    {
        var fixture = await CreateFixtureAsync(false);
        await CreateInterruptedRequestAsync(fixture);
        var recovery = CreateAutomaticRecovery(fixture);
        Assert.IsFalse((await recovery.ReconcileAsync()).MayCheckAccess);
        fixture.Time.Advance(TimeSpan.FromMinutes(3));
        Assert.IsTrue((await recovery.ReconcileAsync()).MayCheckAccess);
        Assert.AreEqual(0, (await fixture.Transitions.GetIncompleteAsync()).Count);
        Assert.AreEqual(0, fixture.Adapter.Mutations);
        Assert.IsFalse((await fixture.Leases.GetAsync()).IsHeld);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AutomaticRecoveryCompletesExpiredCompensation(bool interruptedRollback)
    {
        var fixture = await CreateRollbackFixtureAsync();
        if (interruptedRollback)
        {
            fixture.Adapter.ThrowAfterMutation = true;
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                new ManagedServiceActionRollbackCoordinator(fixture.Proxy, fixture.Proxy, fixture.Proxy, fixture.Proxy)
                    .ExecuteNextAsync(fixture.Command.TransitionId));
            fixture.Adapter.ThrowAfterMutation = false;
        }
        fixture.Time.Advance(TimeSpan.FromMinutes(3));
        var outcome = await CreateAutomaticRecovery(fixture).ReconcileAsync();
        Assert.IsTrue(outcome.MayCheckAccess, outcome.ProductCode);
        Assert.AreEqual(PersistedModeTransitionState.FailedWithSafeFallback,
            (await fixture.Transitions.GetAsync(fixture.Command.TransitionId))!.TransitionState);
        Assert.AreEqual(ManagedServiceObservedState.Running, fixture.Adapter.State);
        Assert.AreEqual(1, fixture.Adapter.Mutations);
        Assert.IsFalse((await fixture.Leases.GetAsync()).IsHeld);
    }

    [TestMethod]
    public async Task AutomaticRecoveryDoesNotAdoptAnotherLogon()
    {
        var fixture = await CreateRollbackFixtureAsync();
        fixture.Time.Advance(TimeSpan.FromMinutes(3));
        var before = await fixture.Transitions.GetAsync(fixture.Command.TransitionId);
        Assert.IsFalse((await CreateAutomaticRecovery(fixture, sessionKey: "different-logon").ReconcileAsync()).MayCheckAccess);
        Assert.AreEqual(before, await fixture.Transitions.GetAsync(fixture.Command.TransitionId));
        Assert.AreEqual(0, fixture.Adapter.Mutations);
    }

    [TestMethod]
    public async Task AutomaticRecoveryPreservesJournalWhenManagedAccessWasRevoked()
    {
        var fixture = await CreateFixtureAsync(false);
        await fixture.Orchestrator.ExecuteAsync(fixture.Command);
        fixture.VerifyBroker.Fail = true;
        var command = fixture.Command with { OperationId = Guid.NewGuid(), CorrelationId = Guid.NewGuid(),
            TransitionId = Guid.NewGuid(), TargetMode = OperationalMode.Game };
        await fixture.Orchestrator.ExecuteAsync(command);
        fixture.Time.Advance(TimeSpan.FromMinutes(3));
        var result = await CreateAutomaticRecovery(fixture, new RecoveryAccess { Enabled = false }).ReconcileAsync();
        Assert.IsFalse(result.MayCheckAccess);
        Assert.AreEqual("MODE_SOURCE_AUTHORITY_BASE_CONVERGENCE_REQUIRED", result.ProductCode);
        Assert.AreEqual("WORK", (await fixture.Machine.GetOperationalModeAsync()).CommittedMode);
        Assert.AreEqual(1, (await fixture.Transitions.GetIncompleteAsync()).Count);
        Assert.AreEqual(0, fixture.Adapter.Mutations);
    }

    [TestMethod]
    public async Task AutomaticRecoveryStopsOnFailedReadBackAndRetriesAfterExpiry()
    {
        var fixture = await CreateRollbackFixtureAsync();
        fixture.Time.Advance(TimeSpan.FromMinutes(3));
        fixture.Adapter.RefuseMutation = true;
        Assert.IsFalse((await CreateAutomaticRecovery(fixture).ReconcileAsync()).MayCheckAccess);
        Assert.AreEqual(1, (await fixture.Transitions.GetIncompleteAsync()).Count);
        fixture.Adapter.RefuseMutation = false;
        fixture.Time.Advance(TimeSpan.FromMinutes(3));
        Assert.IsTrue((await CreateAutomaticRecovery(fixture).ReconcileAsync()).MayCheckAccess);
        Assert.AreEqual(0, (await fixture.Transitions.GetIncompleteAsync()).Count);
    }

    [TestMethod]
    public async Task AutomaticRecoveryStopsWhenLeaseExpiresDuringObservation()
    {
        var fixture = await CreateRollbackFixtureAsync();
        fixture.Time.Advance(TimeSpan.FromMinutes(3));
        fixture.Adapter.OnQuery = () => fixture.Time.Advance(TimeSpan.FromMinutes(3));
        Assert.IsFalse((await CreateAutomaticRecovery(fixture).ReconcileAsync()).MayCheckAccess);
        Assert.AreEqual(0, fixture.Adapter.Mutations);
        Assert.AreEqual(1, (await fixture.Transitions.GetIncompleteAsync()).Count);
        fixture.Adapter.OnQuery = null;
        Assert.IsTrue((await CreateAutomaticRecovery(fixture).ReconcileAsync()).MayCheckAccess);
    }

    [TestMethod]
    public async Task AutomaticRecoveryVerifiesAlreadySettledCompensation()
    {
        var fixture = await CreateRollbackFixtureAsync();
        await new ManagedServiceActionRollbackCoordinator(fixture.Proxy, fixture.Proxy, fixture.Proxy, fixture.Proxy)
            .ExecuteNextAsync(fixture.Command.TransitionId);
        fixture.Time.Advance(TimeSpan.FromMinutes(3));
        Assert.IsTrue((await CreateAutomaticRecovery(fixture).ReconcileAsync()).MayCheckAccess);
        Assert.AreEqual(1, fixture.Adapter.Mutations);
        Assert.AreEqual(0, (await fixture.Transitions.GetIncompleteAsync()).Count);
    }

    [TestMethod]
    public async Task AutomaticRecoveryAllowsIdleAccessCheckWithoutRollingBackCommittedMode()
    {
        var fixture = await CreateFixtureAsync(false);
        await fixture.Orchestrator.ExecuteAsync(fixture.Command);
        var canonical = await fixture.Machine.GetOperationalModeAsync();
        Assert.IsTrue((await CreateAutomaticRecovery(fixture).ReconcileAsync()).MayCheckAccess);
        Assert.AreEqual(canonical, await fixture.Machine.GetOperationalModeAsync());
        Assert.AreEqual(0, fixture.Adapter.Mutations);
    }
}
