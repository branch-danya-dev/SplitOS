using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Broker.Service;
using SplitOS.Contracts.Protocol;
using SplitOS.RuntimeHost.ModeRuntime;

namespace SplitOS.RuntimeHost.Tests;

public sealed partial class RuntimeModeOrchestratorTests
{
    [TestMethod]
    [DataRow(ManagedServiceObservedState.Running)]
    [DataRow(ManagedServiceObservedState.Stopped)]
    public async Task AccessLossRestoresPreActivationBaselineThroughActualBroker(ManagedServiceObservedState initial)
    {
        var fixture = await CreateFixtureAsync(false, fullBroker: true);
        fixture.Adapter.State = initial;
        Assert.IsTrue((await fixture.Orchestrator.ExecuteAsync(fixture.Command)).IsCompleted);
        var switchCommand = fixture.Command with { OperationId = Guid.NewGuid(), CorrelationId = Guid.NewGuid(),
            TransitionId = Guid.NewGuid(), TargetMode = OperationalMode.Game };
        Assert.IsTrue((await fixture.Orchestrator.ExecuteAsync(switchCommand)).IsCompleted);
        Assert.AreEqual(ManagedServiceObservedState.Stopped, fixture.Adapter.State);
        var result = await CreateBaselineAccessLoss(fixture).ReconcileAsync();
        Assert.AreEqual("DEACTIVATED", result.Disposition, result.Execution?.Detail);
        Assert.AreEqual(initial, fixture.Adapter.State);
        Assert.AreEqual("NONE", (await fixture.Machine.GetOperationalModeAsync()).CommittedMode);
        Assert.IsFalse((await fixture.Leases.GetAsync()).IsHeld);
    }

    [TestMethod]
    public async Task NewActivationCapturesUsersNewWindowsBaseline()
    {
        var fixture = await CreateFixtureAsync(false, fullBroker: true);
        Assert.IsTrue((await fixture.Orchestrator.ExecuteAsync(fixture.Command)).IsCompleted);
        Assert.AreEqual("DEACTIVATED", (await CreateBaselineAccessLoss(fixture).ReconcileAsync()).Disposition);
        Assert.AreEqual(ManagedServiceObservedState.Running, fixture.Adapter.State);
        // The user changes Windows while modes are off. Preserve that choice for the next activation.
        fixture.Adapter.State = ManagedServiceObservedState.Stopped;
        var next = fixture.Command with { OperationId = Guid.NewGuid(), CorrelationId = Guid.NewGuid(),
            TransitionId = Guid.NewGuid(), ActivationEpochId = Guid.NewGuid() };
        Assert.IsTrue((await fixture.Orchestrator.ExecuteAsync(next)).IsCompleted);
        Assert.AreEqual("DEACTIVATED", (await CreateBaselineAccessLoss(fixture).ReconcileAsync()).Disposition);
        Assert.AreEqual(ManagedServiceObservedState.Stopped, fixture.Adapter.State);
    }

    [TestMethod]
    [DataRow(ManagedServiceObservedState.Running)]
    [DataRow(ManagedServiceObservedState.Stopped)]
    public async Task FailedInitialActivationCanVerifyUsersOriginalBase(ManagedServiceObservedState initial)
    {
        var fixture = await CreateFixtureAsync(false, fullBroker: true);
        fixture.Adapter.State = initial;
        fixture.Adapter.FailObservationAfterMutation = true;
        Assert.IsFalse((await fixture.Orchestrator.ExecuteAsync(fixture.Command)).IsCompleted);
        fixture.Adapter.FailObservationAfterMutation = false;
        Assert.AreEqual("ROLLED_BACK", (await new ManagedServiceActionRollbackCoordinator(fixture.Proxy, fixture.Proxy, fixture.Proxy, fixture.Proxy)
            .ExecuteNextAsync(fixture.Command.TransitionId)).Disposition);
        Assert.AreEqual("FAILED_WITH_SAFE_FALLBACK", (await CreateCompletion(fixture).CompleteAsync(fixture.Command.TransitionId)).Disposition);
        Assert.AreEqual(initial, fixture.Adapter.State);
        Assert.AreEqual("NONE", (await fixture.Machine.GetOperationalModeAsync()).CommittedMode);
    }

    [TestMethod]
    public async Task BaselineResolverRequiresOwningDeactivation()
    {
        var fixture = await CreateFixtureAsync(false);
        await fixture.Orchestrator.ExecuteAsync(fixture.Command);
        var result = await fixture.Proxy.ResolveBaseAsync(Guid.NewGuid(), Guid.NewGuid(),
            new MachineModeBasePolicyRequest(2, fixture.Command.ControlSessionKey));
        Assert.AreEqual("UNAVAILABLE", result.Disposition);
        Assert.AreEqual(0, result.Entries.Count);
    }

    private static RuntimeModeAccessLossCoordinator CreateBaselineAccessLoss(Fixture fixture)
    {
        var services = new ServicePipeClient(fixture.Pipe);
        var executor = new RuntimeModeOrchestrator(fixture.Proxy, fixture.Proxy, fixture.Proxy, fixture.Proxy, fixture.Proxy,
            new ManagedServiceActionApplyCoordinator(fixture.Proxy, services),
            new ManagedServiceActionVerifyCoordinator(fixture.Proxy, services), fixture.Proxy,
            new ModeBlockerEngine(Array.Empty<IModeBlockerProvider>(), fixture.Time), new BaselineModeTargetPreparationProvider(fixture.Proxy), fixture.Time);
        return new(fixture.Proxy, fixture.Proxy, new RecoverySession(fixture.Command.ControlSessionKey),
            new RecoveryAccess { Enabled = false }, executor, fixture.Time);
    }
}
