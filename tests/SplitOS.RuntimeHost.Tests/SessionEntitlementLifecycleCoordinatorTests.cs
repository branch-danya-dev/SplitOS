using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.Authentication;
using SplitOS.RuntimeHost.ProductIdentity;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class SessionEntitlementLifecycleCoordinatorTests
{
    private const string AccountId = "acc_test_01";
    private const string AssociationId = "ecdd5d14-ec89-40c5-9eb0-25656d243462";
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 13, 5, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task InitialAssociationClearsOrphanEvidenceBeforeBootstrapAndRefreshesOnlyPostCommit()
    {
        var evidence = SeedEvidence("acc_orphan", "56e7b554-35d0-44bc-8bea-becbb0e1c90c");
        var trace = new List<string>();
        var association = new FakeAssociationFlow(
            trace,
            new AccountAssociationEvaluation("UNASSOCIATED", null, null, null, null));
        var bootstrap = new FakeBootstrapFlow(
            trace,
            evidence,
            new DurableLoginBootstrapResult(
                DurableLoginBootstrapDisposition.Associated,
                "ACCOUNT_ASSOCIATED",
                AccountId,
                AssociationId,
                true));
        var reauth = new FakeReauthenticationFlow(trace, evidence);
        var entitlement = new FakeEntitlementRefreshFlow(
            trace,
            evidence,
            AuthenticatedEntitlementRefreshDisposition.Refreshed,
            "ENTITLEMENT_REFRESHED",
            publishAssociationId: AssociationId);
        var coordinator = CreateInteractive(association, bootstrap, reauth, entitlement, evidence);

        var result = await coordinator.CompleteAsync(Session(), InteractiveContext());

        Assert.AreEqual(InteractiveSessionLifecycleDisposition.Associated, result.Disposition);
        Assert.IsTrue(result.HasFreshOnlineEntitlement);
        Assert.IsTrue(bootstrap.EvidenceWasClearWhenCalled);
        Assert.IsTrue(entitlement.EvidenceWasClearWhenCalled);
        Assert.AreEqual(AssociationId, evidence.Read()!.AssociationId);
        CollectionAssert.AreEqual(
            new[] { "association.evaluate", "bootstrap", "entitlement.refresh" },
            trace);
    }

    [TestMethod]
    public async Task ReauthenticationClearsBoundPremiumEvidenceBeforeCredentialRecovery()
    {
        var evidence = SeedEvidence(AccountId, AssociationId);
        var trace = new List<string>();
        var association = new FakeAssociationFlow(
            trace,
            new AccountAssociationEvaluation(
                "REAUTH_REQUIRED",
                AccountId,
                AssociationId,
                "LOCAL_SECRET_UNREADABLE",
                5));
        var bootstrap = new FakeBootstrapFlow(trace, evidence);
        var reauth = new FakeReauthenticationFlow(
            trace,
            evidence,
            new SameAccountReauthenticationResult(
                SameAccountReauthenticationDisposition.Reactivated,
                "ACCOUNT_REAUTHENTICATED",
                AccountId,
                AssociationId,
                true));
        var entitlement = new FakeEntitlementRefreshFlow(
            trace,
            evidence,
            AuthenticatedEntitlementRefreshDisposition.BackendUnavailable,
            "TEMPORARILY_UNAVAILABLE");
        var coordinator = CreateInteractive(association, bootstrap, reauth, entitlement, evidence);

        var result = await coordinator.CompleteAsync(Session(), InteractiveContext());

        Assert.AreEqual(InteractiveSessionLifecycleDisposition.ReactivatedDegraded, result.Disposition);
        Assert.IsFalse(result.HasFreshOnlineEntitlement);
        Assert.IsTrue(reauth.EvidenceWasClearWhenCalled);
        Assert.IsNull(evidence.Read());
        CollectionAssert.AreEqual(
            new[] { "association.evaluate", "reauth", "entitlement.refresh" },
            trace);
    }

    [TestMethod]
    public async Task FailedReauthenticationCannotReachPostCommitEntitlementRefresh()
    {
        var evidence = SeedEvidence(AccountId, AssociationId);
        var trace = new List<string>();
        var association = new FakeAssociationFlow(
            trace,
            new AccountAssociationEvaluation("REAUTH_REQUIRED", AccountId, AssociationId, "REAUTH_REQUIRED", 5));
        var bootstrap = new FakeBootstrapFlow(trace, evidence);
        var reauth = new FakeReauthenticationFlow(
            trace,
            evidence,
            new SameAccountReauthenticationResult(
                SameAccountReauthenticationDisposition.AccountSwitchRequired,
                "ACCOUNT_SWITCH_REQUIRED",
                AccountId,
                AssociationId,
                false));
        var entitlement = new FakeEntitlementRefreshFlow(trace, evidence);
        var coordinator = CreateInteractive(association, bootstrap, reauth, entitlement, evidence);

        var result = await coordinator.CompleteAsync(Session(), InteractiveContext());

        Assert.AreEqual(InteractiveSessionLifecycleDisposition.AccountSwitchRequired, result.Disposition);
        Assert.AreEqual(0, entitlement.Calls);
        Assert.IsNull(evidence.Read());
    }

    [TestMethod]
    public async Task ActiveAssociationDoesNotConsumeASecondInteractiveLoginAsImplicitAccountSwitch()
    {
        var evidence = SeedEvidence(AccountId, AssociationId);
        var trace = new List<string>();
        var association = new FakeAssociationFlow(
            trace,
            new AccountAssociationEvaluation("ACTIVE", AccountId, AssociationId, null, 5));
        var bootstrap = new FakeBootstrapFlow(trace, evidence);
        var reauth = new FakeReauthenticationFlow(trace, evidence);
        var entitlement = new FakeEntitlementRefreshFlow(trace, evidence);
        var coordinator = CreateInteractive(association, bootstrap, reauth, entitlement, evidence);

        var result = await coordinator.CompleteAsync(Session(), InteractiveContext());

        Assert.AreEqual(InteractiveSessionLifecycleDisposition.AlreadyActive, result.Disposition);
        Assert.AreEqual(0, bootstrap.Calls);
        Assert.AreEqual(0, reauth.Calls);
        Assert.AreEqual(0, entitlement.Calls);
        Assert.IsNotNull(evidence.Read());
    }

    [TestMethod]
    public async Task InitialAssociationBackendFailureLeavesNoOrphanPremiumEvidence()
    {
        var evidence = SeedEvidence("acc_orphan", "56e7b554-35d0-44bc-8bea-becbb0e1c90c");
        var trace = new List<string>();
        var association = new FakeAssociationFlow(
            trace,
            new AccountAssociationEvaluation("UNASSOCIATED", null, null, null, null));
        var bootstrap = new FakeBootstrapFlow(
            trace,
            evidence,
            new DurableLoginBootstrapResult(
                DurableLoginBootstrapDisposition.BackendUnavailable,
                "TEMPORARILY_UNAVAILABLE",
                null,
                null,
                false));
        var coordinator = CreateInteractive(
            association,
            bootstrap,
            new FakeReauthenticationFlow(trace, evidence),
            new FakeEntitlementRefreshFlow(trace, evidence),
            evidence);

        var result = await coordinator.CompleteAsync(Session(), InteractiveContext());

        Assert.AreEqual(InteractiveSessionLifecycleDisposition.BackendUnavailable, result.Disposition);
        Assert.IsNull(evidence.Read());
    }

    [TestMethod]
    public async Task RotatedSessionImmediatelyRefreshesEntitlementEvidence()
    {
        var evidence = new OnlineEntitlementEvidenceState();
        var trace = new List<string>();
        var sessionRefresh = new FakeNativeSessionRefreshFlow(
            trace,
            new NativeSessionRefreshResult(
                NativeSessionRefreshDisposition.Refreshed,
                "SESSION_REFRESHED",
                false,
                new RefreshedNativeAccessSession(
                    AccountId,
                    "NEW_ACCESS_TOKEN",
                    Now.AddMinutes(15),
                    "openid offline_access")));
        var entitlement = new FakeEntitlementRefreshFlow(
            trace,
            evidence,
            AuthenticatedEntitlementRefreshDisposition.Refreshed,
            "ENTITLEMENT_REFRESHED",
            AssociationId);
        var coordinator = CreateRefresh(sessionRefresh, entitlement, evidence);

        var result = await coordinator.RefreshAsync(RefreshContext());

        Assert.AreEqual(SessionRefreshLifecycleDisposition.RefreshedOnline, result.Disposition);
        Assert.IsTrue(result.HasFreshOnlineEntitlement);
        Assert.AreEqual(AccountId, entitlement.LastContext!.AccountId);
        Assert.AreEqual("NEW_ACCESS_TOKEN", entitlement.LastContext.AccessToken);
        CollectionAssert.AreEqual(new[] { "session.refresh", "entitlement.refresh" }, trace);
    }

    [TestMethod]
    public async Task RefreshThatRequiresReauthClearsCurrentOnlinePremiumEvidence()
    {
        var evidence = SeedEvidence(AccountId, AssociationId);
        var trace = new List<string>();
        var sessionRefresh = new FakeNativeSessionRefreshFlow(
            trace,
            new NativeSessionRefreshResult(
                NativeSessionRefreshDisposition.ReauthRequired,
                "REFRESH_TOKEN_REVOKED",
                false,
                null));
        var entitlement = new FakeEntitlementRefreshFlow(trace, evidence);
        var coordinator = CreateRefresh(sessionRefresh, entitlement, evidence);

        var result = await coordinator.RefreshAsync(RefreshContext());

        Assert.AreEqual(SessionRefreshLifecycleDisposition.ReauthRequired, result.Disposition);
        Assert.IsNull(evidence.Read());
        Assert.AreEqual(0, entitlement.Calls);
    }

    [TestMethod]
    public async Task RetryableRefreshBackendOutageLeavesOnlyPreviouslyBoundedEvidence()
    {
        var evidence = SeedEvidence(AccountId, AssociationId);
        var trace = new List<string>();
        var sessionRefresh = new FakeNativeSessionRefreshFlow(
            trace,
            new NativeSessionRefreshResult(
                NativeSessionRefreshDisposition.BackendUnavailable,
                "AUTH_BACKEND_UNAVAILABLE",
                true,
                null));
        var coordinator = CreateRefresh(
            sessionRefresh,
            new FakeEntitlementRefreshFlow(trace, evidence),
            evidence);

        var result = await coordinator.RefreshAsync(RefreshContext());

        Assert.AreEqual(SessionRefreshLifecycleDisposition.BackendUnavailable, result.Disposition);
        Assert.IsNotNull(evidence.Read());
    }

    [TestMethod]
    public async Task SuccessfulTokenRotationWithEntitlementOutageIsDegradedNotPremiumFabrication()
    {
        var evidence = new OnlineEntitlementEvidenceState();
        var trace = new List<string>();
        var sessionRefresh = new FakeNativeSessionRefreshFlow(
            trace,
            new NativeSessionRefreshResult(
                NativeSessionRefreshDisposition.Refreshed,
                "SESSION_REFRESHED",
                false,
                new RefreshedNativeAccessSession(
                    AccountId,
                    "NEW_ACCESS_TOKEN",
                    Now.AddMinutes(15),
                    null)));
        var entitlement = new FakeEntitlementRefreshFlow(
            trace,
            evidence,
            AuthenticatedEntitlementRefreshDisposition.BackendUnavailable,
            "TEMPORARILY_UNAVAILABLE");
        var coordinator = CreateRefresh(sessionRefresh, entitlement, evidence);

        var result = await coordinator.RefreshAsync(RefreshContext());

        Assert.AreEqual(SessionRefreshLifecycleDisposition.RefreshedDegraded, result.Disposition);
        Assert.IsFalse(result.HasFreshOnlineEntitlement);
        Assert.IsNull(evidence.Read());
    }

    private static InteractiveSessionEntitlementLifecycleCoordinator CreateInteractive(
        IAccountAssociationEvaluationFlow association,
        IDurableLoginBootstrapFlow bootstrap,
        ISameAccountReauthenticationFlow reauth,
        IAuthenticatedEntitlementRefreshFlow entitlement,
        OnlineEntitlementEvidenceState evidence)
        => new(
            association,
            bootstrap,
            reauth,
            entitlement,
            evidence,
            new RuntimeStateRefreshSignal());

    private static SessionRefreshEntitlementLifecycleCoordinator CreateRefresh(
        INativeSessionRefreshFlow sessionRefresh,
        IAuthenticatedEntitlementRefreshFlow entitlement,
        OnlineEntitlementEvidenceState evidence)
        => new(
            sessionRefresh,
            entitlement,
            evidence,
            new RuntimeStateRefreshSignal());

    private static ValidatedNativeAuthSession Session()
        => new(
            "oidc-subject-test",
            "ACCESS_TOKEN_SECRET",
            Now.AddMinutes(15),
            "REFRESH_TOKEN_SECRET",
            "openid offline_access");

    private static InteractiveSessionLifecycleContext InteractiveContext()
        => new(
            "1.2.3-test",
            "installation-test-01",
            Guid.Parse("5846b359-0279-4cd5-a5e6-c08592ef210f"),
            Guid.Parse("74beea75-c789-40f2-9fbe-18f70a8ab055"));

    private static SessionRefreshLifecycleContext RefreshContext()
        => new(
            "1.2.3-test",
            "installation-test-01",
            Guid.Parse("5846b359-0279-4cd5-a5e6-c08592ef210f"));

    private static OnlineEntitlementEvidenceState SeedEvidence(string accountId, string associationId)
    {
        var state = new OnlineEntitlementEvidenceState();
        state.Publish(
            associationId,
            new SplitOSEntitlementSnapshot(
                accountId,
                42,
                "PRO",
                "ACTIVE",
                Now.AddDays(-1),
                Now.AddDays(30),
                ["runtime.managed_modes"],
                true,
                Now),
            Now);
        return state;
    }

    private sealed class FakeAssociationFlow(
        List<string> trace,
        AccountAssociationEvaluation evaluation) : IAccountAssociationEvaluationFlow
    {
        public Task<AccountAssociationEvaluation> EvaluateAsync(CancellationToken cancellationToken = default)
        {
            trace.Add("association.evaluate");
            return Task.FromResult(evaluation);
        }
    }

    private sealed class FakeBootstrapFlow : IDurableLoginBootstrapFlow
    {
        private readonly List<string> _trace;
        private readonly OnlineEntitlementEvidenceState _evidence;
        private readonly DurableLoginBootstrapResult _result;

        public FakeBootstrapFlow(
            List<string> trace,
            OnlineEntitlementEvidenceState evidence,
            DurableLoginBootstrapResult? result = null)
        {
            _trace = trace;
            _evidence = evidence;
            _result = result ?? new DurableLoginBootstrapResult(
                DurableLoginBootstrapDisposition.Rejected,
                "NOT_CONFIGURED",
                null,
                null,
                false);
        }

        public int Calls { get; private set; }
        public bool EvidenceWasClearWhenCalled { get; private set; }

        public Task<DurableLoginBootstrapResult> BootstrapInitialAssociationAsync(
            ValidatedNativeAuthSession session,
            DurableLoginBootstrapContext context,
            CancellationToken cancellationToken = default)
        {
            _trace.Add("bootstrap");
            Calls++;
            EvidenceWasClearWhenCalled = _evidence.Read() is null;
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeReauthenticationFlow : ISameAccountReauthenticationFlow
    {
        private readonly List<string> _trace;
        private readonly OnlineEntitlementEvidenceState _evidence;
        private readonly SameAccountReauthenticationResult _result;

        public FakeReauthenticationFlow(
            List<string> trace,
            OnlineEntitlementEvidenceState evidence,
            SameAccountReauthenticationResult? result = null)
        {
            _trace = trace;
            _evidence = evidence;
            _result = result ?? new SameAccountReauthenticationResult(
                SameAccountReauthenticationDisposition.Rejected,
                "NOT_CONFIGURED",
                null,
                null,
                false);
        }

        public int Calls { get; private set; }
        public bool EvidenceWasClearWhenCalled { get; private set; }

        public Task<SameAccountReauthenticationResult> ReactivateAsync(
            ValidatedNativeAuthSession session,
            SameAccountReauthenticationContext context,
            CancellationToken cancellationToken = default)
        {
            _trace.Add("reauth");
            Calls++;
            EvidenceWasClearWhenCalled = _evidence.Read() is null;
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeEntitlementRefreshFlow : IAuthenticatedEntitlementRefreshFlow
    {
        private readonly List<string> _trace;
        private readonly OnlineEntitlementEvidenceState _evidence;
        private readonly AuthenticatedEntitlementRefreshDisposition _disposition;
        private readonly string _productCode;
        private readonly string? _publishAssociationId;

        public FakeEntitlementRefreshFlow(
            List<string> trace,
            OnlineEntitlementEvidenceState evidence,
            AuthenticatedEntitlementRefreshDisposition disposition = AuthenticatedEntitlementRefreshDisposition.Rejected,
            string productCode = "NOT_CONFIGURED",
            string? publishAssociationId = null)
        {
            _trace = trace;
            _evidence = evidence;
            _disposition = disposition;
            _productCode = productCode;
            _publishAssociationId = publishAssociationId;
        }

        public int Calls { get; private set; }
        public bool EvidenceWasClearWhenCalled { get; private set; }
        public AuthenticatedEntitlementRefreshContext? LastContext { get; private set; }

        public Task<AuthenticatedEntitlementRefreshResult> RefreshAsync(
            AuthenticatedEntitlementRefreshContext context,
            CancellationToken cancellationToken = default)
        {
            _trace.Add("entitlement.refresh");
            Calls++;
            LastContext = context;
            EvidenceWasClearWhenCalled = _evidence.Read() is null;

            if (_disposition == AuthenticatedEntitlementRefreshDisposition.Refreshed &&
                _publishAssociationId is not null)
            {
                _evidence.Publish(
                    _publishAssociationId,
                    new SplitOSEntitlementSnapshot(
                        context.AccountId,
                        43,
                        "PRO",
                        "ACTIVE",
                        Now.AddDays(-1),
                        Now.AddDays(30),
                        ["runtime.managed_modes"],
                        true,
                        Now),
                    Now);
            }

            return Task.FromResult(new AuthenticatedEntitlementRefreshResult(
                _disposition,
                _productCode,
                _disposition == AuthenticatedEntitlementRefreshDisposition.Refreshed ? 43 : null,
                _disposition == AuthenticatedEntitlementRefreshDisposition.BackendUnavailable));
        }
    }

    private sealed class FakeNativeSessionRefreshFlow(
        List<string> trace,
        NativeSessionRefreshResult result) : INativeSessionRefreshFlow
    {
        public Task<NativeSessionRefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
        {
            trace.Add("session.refresh");
            return Task.FromResult(result);
        }
    }
}
