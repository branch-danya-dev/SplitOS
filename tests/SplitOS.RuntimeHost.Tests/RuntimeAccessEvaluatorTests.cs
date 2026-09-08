using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.ProductIdentity;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class RuntimeAccessEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 35, 0, TimeSpan.Zero);
    private const string AccountId = "acc_test_01";
    private const string AssociationId = "ecdd5d14-ec89-40c5-9eb0-25656d243462";

    [TestMethod]
    public async Task ActiveProManagedRuntimeCapabilityEnablesAccess()
    {
        var state = StateWith(Entitlement(plan: "PRO", status: "ACTIVE", capabilities: ["runtime.managed_modes"]));
        var evaluator = Evaluator(state);

        var result = await evaluator.EvaluateAsync(ActiveAssociation());

        Assert.IsTrue(result.IsEnabled);
        Assert.AreEqual("ENABLED", result.ManagedRuntimeAccess);
        Assert.AreEqual("PRO_ONLINE_CONFIRMED", result.Reason);
        Assert.AreEqual(42L, result.EntitlementVersion);
    }

    [TestMethod]
    public async Task FreeEntitlementCannotEnableManagedRuntimeEvenIfCapabilityIsPresent()
    {
        var state = StateWith(Entitlement(plan: "FREE", status: "ACTIVE", capabilities: ["runtime.managed_modes"]));
        var result = await Evaluator(state).EvaluateAsync(ActiveAssociation());

        Assert.IsFalse(result.IsEnabled);
        Assert.AreEqual("FREE_ENTITLEMENT", result.Reason);
    }

    [TestMethod]
    public async Task ProWithoutManagedRuntimeCapabilityStaysDisabled()
    {
        var state = StateWith(Entitlement(plan: "PRO", status: "ACTIVE", capabilities: ["game.launcher"]));
        var result = await Evaluator(state).EvaluateAsync(ActiveAssociation());

        Assert.IsFalse(result.IsEnabled);
        Assert.AreEqual("MANAGED_RUNTIME_CAPABILITY_MISSING", result.Reason);
    }

    [TestMethod]
    public async Task SuspendedEntitlementStaysDisabled()
    {
        var state = StateWith(Entitlement(plan: "PRO", status: "SUSPENDED", capabilities: ["runtime.managed_modes"]));
        var result = await Evaluator(state).EvaluateAsync(ActiveAssociation());

        Assert.IsFalse(result.IsEnabled);
        Assert.AreEqual("ENTITLEMENT_NOT_ACTIVE", result.Reason);
    }

    [TestMethod]
    public async Task CancelledAtPeriodEndRemainsUsableOnlyWhileCommercialValidityRemains()
    {
        var state = StateWith(Entitlement(
            plan: "PRO",
            status: "CANCELLED_AT_PERIOD_END",
            capabilities: ["runtime.managed_modes"],
            validUntilUtc: Now.AddDays(3)));
        var result = await Evaluator(state).EvaluateAsync(ActiveAssociation());

        Assert.IsTrue(result.IsEnabled);
        Assert.AreEqual("PRO_ONLINE_CONFIRMED", result.Reason);
    }

    [TestMethod]
    public async Task EvidenceBoundToDifferentAssociationCannotAuthorizeCurrentAccount()
    {
        var state = new OnlineEntitlementEvidenceState();
        state.Publish(
            "3f53d593-4b6a-4df4-b8e1-56d6cc0af7be",
            Entitlement(plan: "PRO", status: "ACTIVE", capabilities: ["runtime.managed_modes"]),
            Now);

        var result = await Evaluator(state).EvaluateAsync(ActiveAssociation());

        Assert.IsFalse(result.IsEnabled);
        Assert.AreEqual("ONLINE_ENTITLEMENT_CONTEXT_MISMATCH", result.Reason);
    }

    [TestMethod]
    public async Task StaleOnlineEvidenceCannotAuthorizeManagedRuntime()
    {
        var state = new OnlineEntitlementEvidenceState();
        state.Publish(
            AssociationId,
            Entitlement(plan: "PRO", status: "ACTIVE", capabilities: ["runtime.managed_modes"]),
            Now.AddMinutes(-6));

        var result = await Evaluator(state).EvaluateAsync(ActiveAssociation());

        Assert.IsFalse(result.IsEnabled);
        Assert.AreEqual("ONLINE_ENTITLEMENT_STALE", result.Reason);
    }

    [TestMethod]
    public async Task ReauthRequiredAssociationCannotUseOtherwiseValidOnlineEvidence()
    {
        var state = StateWith(Entitlement(plan: "PRO", status: "ACTIVE", capabilities: ["runtime.managed_modes"]));
        var association = ActiveAssociation() with { AssociationState = "REAUTH_REQUIRED" };

        var result = await Evaluator(state).EvaluateAsync(association);

        Assert.IsFalse(result.IsEnabled);
        Assert.AreEqual("ACCOUNT_NOT_ACTIVE", result.Reason);
    }

    [TestMethod]
    public async Task ExpiredCommercialValidityCannotBeExtendedByLocalEvidenceFreshness()
    {
        var state = StateWith(Entitlement(
            plan: "PRO",
            status: "ACTIVE",
            capabilities: ["runtime.managed_modes"],
            validUntilUtc: Now.AddMinutes(-6)));

        var result = await Evaluator(state).EvaluateAsync(ActiveAssociation());

        Assert.IsFalse(result.IsEnabled);
        Assert.AreEqual("ENTITLEMENT_EXPIRED", result.Reason);
    }

    [TestMethod]
    public void EvidenceStateRejectsOlderVersionForSameAssociationAndAccount()
    {
        var state = new OnlineEntitlementEvidenceState();
        state.Publish(
            AssociationId,
            Entitlement(plan: "PRO", status: "ACTIVE", capabilities: ["runtime.managed_modes"], version: 42),
            Now.AddMinutes(-1));

        var result = state.Publish(
            AssociationId,
            Entitlement(plan: "FREE", status: "ACTIVE", capabilities: [], version: 41),
            Now);

        Assert.AreEqual(OnlineEntitlementPublishDisposition.StaleRejected, result.Disposition);
        Assert.IsNotNull(result.Current);
        Assert.AreEqual(42L, result.Current.Entitlement.EntitlementVersion);
        Assert.AreEqual("PRO", result.Current.Entitlement.Plan);
    }

    private static OnlineEntitlementRuntimeAccessEvaluator Evaluator(OnlineEntitlementEvidenceState state)
        => new(
            state,
            RuntimeAccessPolicy.Default,
            new FixedTimeProvider(Now));

    private static OnlineEntitlementEvidenceState StateWith(SplitOSEntitlementSnapshot entitlement)
    {
        var state = new OnlineEntitlementEvidenceState();
        state.Publish(AssociationId, entitlement, Now);
        return state;
    }

    private static AccountAssociationEvaluation ActiveAssociation()
        => new(
            "ACTIVE",
            AccountId,
            AssociationId,
            null,
            5);

    private static SplitOSEntitlementSnapshot Entitlement(
        string plan,
        string status,
        IReadOnlyList<string> capabilities,
        long version = 42,
        DateTimeOffset? validUntilUtc = null)
        => new(
            AccountId,
            version,
            plan,
            status,
            Now.AddDays(-1),
            validUntilUtc ?? Now.AddDays(30),
            capabilities,
            true,
            Now);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
