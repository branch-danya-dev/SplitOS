using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.ModeRuntime;

namespace SplitOS.RuntimeHost.Tests;

public sealed partial class RuntimeModeOrchestratorTests
{
    [TestMethod]
    public async Task CurrentPhysicalConsoleOwnerMayEnterModeOrchestrator()
    {
        var fixture = await CreateFixtureAsync(false);
        var executor = new RuntimeModeCommandAuthorityExecutor(
            fixture.Proxy,
            new FixedControlSessionIdentity(fixture.Command.ControlSessionKey),
            fixture.Orchestrator);

        var outcome = await executor.ExecuteAsync(fixture.Command);

        Assert.AreEqual(RuntimeModeExecutionDisposition.Completed, outcome.Disposition, outcome.Detail);
        Assert.AreEqual("WORK", outcome.OperationalMode.CommittedMode);
        Assert.AreEqual(fixture.Command.ControlSessionKey, outcome.OperationalMode.ControlSessionKey);
        Assert.IsFalse((await fixture.Leases.GetAsync()).IsHeld);
    }

    [TestMethod]
    public async Task StaleControlContextIsRejectedBeforeLeaseTransitionOrMutation()
    {
        var fixture = await CreateFixtureAsync(false);
        var executor = new RuntimeModeCommandAuthorityExecutor(
            fixture.Proxy,
            new FixedControlSessionIdentity("current-physical-console"),
            fixture.Orchestrator);

        var outcome = await executor.ExecuteAsync(fixture.Command);

        Assert.AreEqual(RuntimeModeExecutionDisposition.AuthorityDenied, outcome.Disposition);
        Assert.AreEqual("MODE_CONTROL_CONTEXT_STALE", outcome.ProductCode);
        Assert.AreEqual("NONE", outcome.OperationalMode.CommittedMode);
        Assert.IsFalse((await fixture.Leases.GetAsync()).IsHeld);
        Assert.AreEqual(0, (await fixture.Transitions.GetIncompleteAsync()).Count);
        Assert.AreEqual(0, fixture.ApplyBroker.SnapshotCalls);
        Assert.AreEqual(0, fixture.ApplyBroker.ApplyCalls);
        Assert.AreEqual(0, fixture.VerifyBroker.VerifyCalls);
    }

    [TestMethod]
    public async Task LostPhysicalConsoleOwnershipFailsClosedBeforeModeExecution()
    {
        var fixture = await CreateFixtureAsync(false);
        var executor = new RuntimeModeCommandAuthorityExecutor(
            fixture.Proxy,
            new ThrowingControlSessionIdentity(),
            fixture.Orchestrator);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => executor.ExecuteAsync(fixture.Command));

        Assert.IsFalse((await fixture.Leases.GetAsync()).IsHeld);
        Assert.AreEqual(0, (await fixture.Transitions.GetIncompleteAsync()).Count);
        Assert.AreEqual(0, fixture.ApplyBroker.SnapshotCalls);
        Assert.AreEqual(0, fixture.ApplyBroker.ApplyCalls);
        Assert.AreEqual(0, fixture.VerifyBroker.VerifyCalls);
    }

    private sealed class FixedControlSessionIdentity(string key) : IControlSessionIdentity
    {
        public string GetCurrentKey() => key;
    }

    private sealed class ThrowingControlSessionIdentity : IControlSessionIdentity
    {
        public string GetCurrentKey() => throw new InvalidDataException("RuntimeHost no longer owns the active physical console.");
    }
}
