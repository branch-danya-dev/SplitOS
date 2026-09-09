using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.ModeRuntime;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class ModeTransitionDomainTests
{
    [TestMethod]
    public void NoneToManagedModeIsActivate()
    {
        var plan = Plan(OperationalMode.None, OperationalMode.Work);

        Assert.AreEqual(ModeOperationPlanDisposition.Accepted, plan.Disposition);
        Assert.AreEqual(ModeOperationKind.Activate, plan.OperationKind);
        Assert.IsTrue(plan.CreatesTransition);
    }

    [TestMethod]
    public void ManagedModeSwitchIsClassifiedExplicitly()
    {
        Assert.AreEqual(ModeOperationKind.Switch, Plan(OperationalMode.Work, OperationalMode.Game).OperationKind);
        Assert.AreEqual(ModeOperationKind.Switch, Plan(OperationalMode.Game, OperationalMode.Work).OperationKind);
    }

    [TestMethod]
    public void ManagedModeToNoneIsDeactivate()
    {
        var plan = Plan(OperationalMode.Game, OperationalMode.None);

        Assert.AreEqual(ModeOperationKind.Deactivate, plan.OperationKind);
        Assert.AreEqual(OperationalMode.None, plan.TargetMode);
    }

    [TestMethod]
    public void SameModeIntentIsNoOpAndCreatesNoTransition()
    {
        var plan = Plan(OperationalMode.Work, OperationalMode.Work);

        Assert.AreEqual(ModeOperationPlanDisposition.NoOp, plan.Disposition);
        Assert.IsNull(plan.OperationKind);
        Assert.IsFalse(plan.CreatesTransition);
    }

    [TestMethod]
    public void OperationKindCannotBeUsedToDisguiseInvalidTuple()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ModeTransitionDomain.ValidateTuple(
                ModeOperationKind.Activate,
                OperationalMode.Work,
                OperationalMode.Game));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ModeTransitionDomain.ValidateTuple(
                ModeOperationKind.Deactivate,
                OperationalMode.None,
                OperationalMode.Game));
    }

    [TestMethod]
    public void StateGraphRejectsSkippingRequiredLifecycleBoundaries()
    {
        Assert.IsTrue(ModeTransitionDomain.CanAdvanceState(
            ModeTransitionState.Requested,
            ModeTransitionState.Inspecting));

        Assert.IsFalse(ModeTransitionDomain.CanAdvanceState(
            ModeTransitionState.Requested,
            ModeTransitionState.Applying));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ModeTransitionDomain.ValidateStateAdvance(
                ModeTransitionState.Verifying,
                ModeTransitionState.Completed));
    }

    [TestMethod]
    public void RollbackIsARecoveryMechanismNotTerminalState()
    {
        Assert.IsTrue(ModeTransitionDomain.CanAdvanceState(
            ModeTransitionState.Applying,
            ModeTransitionState.RollingBack));
        Assert.IsTrue(ModeTransitionDomain.CanAdvanceState(
            ModeTransitionState.RollingBack,
            ModeTransitionState.Cancelled));
        Assert.IsTrue(ModeTransitionDomain.CanAdvanceState(
            ModeTransitionState.RollingBack,
            ModeTransitionState.FailedWithSafeFallback));
        Assert.IsFalse(ModeTransitionDomain.CanAdvanceState(
            ModeTransitionState.RollingBack,
            ModeTransitionState.Completed));
    }

    [TestMethod]
    public void CommitDurableStageAndCompletedStateRequireDurableMarker()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ModeTransitionDomain.ValidateStateStage(
                ModeTransitionState.Committing,
                ModeTransitionStage.CommitDurable,
                commitDurable: false));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ModeTransitionDomain.ValidateStateStage(
                ModeTransitionState.Completed,
                ModeTransitionStage.Terminal,
                commitDurable: false));

        ModeTransitionDomain.ValidateStateStage(
            ModeTransitionState.Completed,
            ModeTransitionStage.Terminal,
            commitDurable: true);
    }

    [TestMethod]
    public void CancelledOrSafeFallbackCannotClaimTargetCommit()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ModeTransitionDomain.ValidateStateStage(
                ModeTransitionState.Cancelled,
                ModeTransitionStage.Terminal,
                commitDurable: true));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ModeTransitionDomain.ValidateStateStage(
                ModeTransitionState.FailedWithSafeFallback,
                ModeTransitionStage.Terminal,
                commitDurable: true));
    }

    [TestMethod]
    public void PremiumTargetCommitRequiresAllAuthorityAndVerificationPredicates()
    {
        Assert.IsTrue(ModeTransitionDomain.CanCommitTarget(
            ModeOperationKind.Activate,
            OperationalMode.Game,
            leaseFenceCurrent: true,
            sourceRevisionCurrent: true,
            mandatoryTargetVerified: true,
            runtimeAccessPermitsTarget: true,
            policyIdentityCompatible: true));

        Assert.IsFalse(ModeTransitionDomain.CanCommitTarget(
            ModeOperationKind.Activate,
            OperationalMode.Game,
            leaseFenceCurrent: true,
            sourceRevisionCurrent: true,
            mandatoryTargetVerified: true,
            runtimeAccessPermitsTarget: false,
            policyIdentityCompatible: true));

        Assert.IsFalse(ModeTransitionDomain.CanCommitTarget(
            ModeOperationKind.Switch,
            OperationalMode.Work,
            leaseFenceCurrent: false,
            sourceRevisionCurrent: true,
            mandatoryTargetVerified: true,
            runtimeAccessPermitsTarget: true,
            policyIdentityCompatible: true));
    }

    [TestMethod]
    public void DeactivateCommitDoesNotRequirePremiumRuntimeAccess()
    {
        Assert.IsTrue(ModeTransitionDomain.CanCommitTarget(
            ModeOperationKind.Deactivate,
            OperationalMode.None,
            leaseFenceCurrent: true,
            sourceRevisionCurrent: true,
            mandatoryTargetVerified: true,
            runtimeAccessPermitsTarget: false,
            policyIdentityCompatible: true));
    }

    [TestMethod]
    public void InvalidControlSessionIdentityIsRejectedBeforeOperationAcceptance()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            ModeTransitionDomain.Plan(
                OperationalMode.None,
                OperationalMode.Work,
                1,
                Guid.NewGuid(),
                Guid.NewGuid(),
                " "));
    }

    private static ModeOperationPlan Plan(OperationalMode source, OperationalMode target)
        => ModeTransitionDomain.Plan(
            source,
            target,
            sourceModeRevision: 7,
            operationId: Guid.Parse("8541f24e-3c39-42d8-9868-03c09797f0f9"),
            correlationId: Guid.Parse("6467cf31-e75f-42be-a1c8-a01ccdd1ecbc"),
            controlSessionKey: "console-session-1");
}
