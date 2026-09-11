using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.Machine;
using SplitOS.RuntimeHost.ModeRuntime;

namespace SplitOS.RuntimeHost.Tests;

public sealed partial class RuntimeModeOrchestratorTests
{
    [TestMethod]
    [DataRow(OperationalMode.Work)]
    [DataRow(OperationalMode.Game)]
    public async Task AccessLossRunsVerifiedDeactivationAndOnlyThenCommitsNone(OperationalMode initialMode)
    {
        var fixture = await CreateFixtureAsync(false);
        Assert.IsTrue((await fixture.Orchestrator.ExecuteAsync(fixture.Command with { TargetMode = initialMode })).IsCompleted);
        var source = await fixture.Machine.GetOperationalModeAsync();
        var result = await CreateAccessLossCoordinator(fixture, new RecoveryAccess { Enabled = false }).ReconcileAsync();
        Assert.AreEqual("DEACTIVATED", result.Disposition, result.ProductCode);
        Assert.IsNotNull(result.Execution);
        Assert.AreEqual(PersistedModeOperationKind.Deactivate, result.Execution.Transition!.OperationKind);
        Assert.IsTrue(result.Execution.Transition.CommitDurable);
        var canonical = await fixture.Machine.GetOperationalModeAsync();
        Assert.AreEqual("NONE", canonical.CommittedMode);
        Assert.AreEqual(source.Revision + 1, canonical.Revision);
        Assert.IsNull(canonical.ActivationEpochId);
        Assert.AreEqual(2, fixture.ApplyBroker.ApplyCalls);
        Assert.AreEqual(2, fixture.VerifyBroker.VerifyCalls);
        Assert.IsFalse((await fixture.Leases.GetAsync()).IsHeld);
    }

    [TestMethod]
    public async Task RetainedAccessDoesNotStartDeactivation()
    {
        var fixture = await CreateFixtureAsync(false);
        await fixture.Orchestrator.ExecuteAsync(fixture.Command);
        var original = await fixture.Machine.GetOperationalModeAsync();
        var result = await CreateAccessLossCoordinator(fixture, new RecoveryAccess()).ReconcileAsync();
        Assert.AreEqual("NO_CHANGE", result.Disposition);
        Assert.AreEqual(original, await fixture.Machine.GetOperationalModeAsync());
        Assert.AreEqual(1, fixture.ApplyBroker.ApplyCalls);
    }

    [TestMethod]
    public async Task AccessLossDoesNotTreatIncompleteActivationAsVerifiedNone()
    {
        var fixture = await CreateRollbackFixtureAsync();
        var result = await CreateAccessLossCoordinator(fixture, new RecoveryAccess { Enabled = false }).ReconcileAsync();
        Assert.AreEqual("MODE_ACCESS_LOSS_INCOMPLETE_TRANSITION", result.ProductCode);
        Assert.AreEqual(1, (await fixture.Transitions.GetIncompleteAsync()).Count);
        Assert.AreEqual(1, fixture.ApplyBroker.ApplyCalls);
    }

    [TestMethod]
    public async Task FailedBaseVerificationPreservesManagedCanonicalMode()
    {
        var fixture = await CreateFixtureAsync(false);
        await fixture.Orchestrator.ExecuteAsync(fixture.Command);
        fixture.VerifyBroker.Fail = true;
        var original = await fixture.Machine.GetOperationalModeAsync();
        var result = await CreateAccessLossCoordinator(fixture, new RecoveryAccess { Enabled = false }).ReconcileAsync();
        Assert.AreEqual("RECONCILIATION_REQUIRED", result.Disposition);
        Assert.AreEqual(original, await fixture.Machine.GetOperationalModeAsync());
        Assert.AreEqual(1, (await fixture.Transitions.GetIncompleteAsync()).Count);
    }

    [TestMethod]
    public async Task AccessLossDoesNotAdoptAnotherWindowsLogon()
    {
        var fixture = await CreateFixtureAsync(false);
        await fixture.Orchestrator.ExecuteAsync(fixture.Command);
        var access = new RecoveryAccess { Enabled = false };
        var result = await new RuntimeModeAccessLossCoordinator(fixture.Proxy, fixture.Proxy,
            new RecoverySession("another-logon"), access, fixture.Orchestrator, fixture.Time).ReconcileAsync();
        Assert.AreEqual("MODE_ACCESS_LOSS_SOURCE_SESSION_MISMATCH", result.ProductCode);
        Assert.AreEqual(0, access.Calls);
        Assert.AreEqual(1, fixture.ApplyBroker.ApplyCalls);
    }

    private static RuntimeModeAccessLossCoordinator CreateAccessLossCoordinator(Fixture fixture, RecoveryAccess access)
        => new(fixture.Proxy, fixture.Proxy, new RecoverySession(fixture.Command.ControlSessionKey), access, fixture.Orchestrator, fixture.Time);
}
