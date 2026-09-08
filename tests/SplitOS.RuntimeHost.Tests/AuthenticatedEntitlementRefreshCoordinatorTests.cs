using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.User;
using SplitOS.RuntimeHost.ProductIdentity;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class AuthenticatedEntitlementRefreshCoordinatorTests
{
    private static readonly Uri AccountEndpoint = new("https://api.example.test/v1/account");
    private static readonly Uri EntitlementEndpoint = new("https://api.example.test/v1/entitlements/current");
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 50, 0, TimeSpan.Zero);
    private const string AccountId = "acc_test_01";
    private const string AssociationId = "ecdd5d14-ec89-40c5-9eb0-25656d243462";

    [TestMethod]
    public async Task AcceptedEntitlementPublishesAssociationBoundOnlineEvidence()
    {
        var evidence = new OnlineEntitlementEvidenceState();
        using var handler = new FixtureHandler();
        using var http = new HttpClient(handler);
        var coordinator = CreateCoordinator(http, ActiveAssociation(), evidence);

        var result = await coordinator.RefreshAsync(Context());

        Assert.AreEqual(AuthenticatedEntitlementRefreshDisposition.Refreshed, result.Disposition);
        Assert.AreEqual("ENTITLEMENT_REFRESHED", result.ProductCode);
        Assert.AreEqual(42L, result.EntitlementVersion);
        var current = evidence.Read();
        Assert.IsNotNull(current);
        Assert.AreEqual(AssociationId, current.AssociationId);
        Assert.AreEqual(AccountId, current.Entitlement.AccountId);
        Assert.AreEqual(42L, current.Entitlement.EntitlementVersion);
        Assert.AreEqual(Now, current.ObservedLocalUtc);
    }

    [TestMethod]
    public async Task BackendUnavailableKeepsExistingEvidenceForBoundedEvaluatorFreshness()
    {
        var evidence = SeedEvidence(version: 41);
        using var handler = new FixtureHandler(HttpStatusCode.ServiceUnavailable, "TEMPORARILY_UNAVAILABLE");
        using var http = new HttpClient(handler);
        var coordinator = CreateCoordinator(http, ActiveAssociation(), evidence);

        var result = await coordinator.RefreshAsync(Context());

        Assert.AreEqual(AuthenticatedEntitlementRefreshDisposition.BackendUnavailable, result.Disposition);
        Assert.IsTrue(result.Retryable);
        Assert.AreEqual(41L, evidence.Read()!.Entitlement.EntitlementVersion);
    }

    [TestMethod]
    public async Task AuthRequiredClearsPriorOnlineAuthorityImmediately()
    {
        var evidence = SeedEvidence(version: 41);
        using var handler = new FixtureHandler(HttpStatusCode.Unauthorized, "AUTH_REQUIRED");
        using var http = new HttpClient(handler);
        var coordinator = CreateCoordinator(http, ActiveAssociation(), evidence);

        var result = await coordinator.RefreshAsync(Context());

        Assert.AreEqual(AuthenticatedEntitlementRefreshDisposition.AuthRequired, result.Disposition);
        Assert.IsNull(evidence.Read());
    }

    [TestMethod]
    public async Task SessionAccountMismatchClearsBoundEvidenceWithoutCallingBackend()
    {
        var evidence = SeedEvidence(version: 41);
        using var handler = new FixtureHandler();
        using var http = new HttpClient(handler);
        var coordinator = CreateCoordinator(http, ActiveAssociation(), evidence);
        var context = Context() with { AccountId = "acc_other" };

        var result = await coordinator.RefreshAsync(context);

        Assert.AreEqual(AuthenticatedEntitlementRefreshDisposition.AccountMismatch, result.Disposition);
        Assert.AreEqual("SESSION_ACCOUNT_MISMATCH", result.ProductCode);
        Assert.AreEqual(0, handler.RequestCount);
        Assert.IsNull(evidence.Read());
    }

    [TestMethod]
    public async Task ReauthRequiredAssociationCannotRefreshOrRetainOnlineEvidence()
    {
        var evidence = SeedEvidence(version: 41);
        using var handler = new FixtureHandler();
        using var http = new HttpClient(handler);
        var association = ActiveAssociation() with { AssociationState = "REAUTH_REQUIRED" };
        var coordinator = CreateCoordinator(http, association, evidence);

        var result = await coordinator.RefreshAsync(Context());

        Assert.AreEqual(AuthenticatedEntitlementRefreshDisposition.ReauthRequired, result.Disposition);
        Assert.AreEqual(0, handler.RequestCount);
        Assert.IsNull(evidence.Read());
    }

    [TestMethod]
    public async Task LowerServerEntitlementVersionCannotRollBackNewerEvidence()
    {
        var evidence = SeedEvidence(version: 50);
        using var handler = new FixtureHandler(entitlementVersion: 49);
        using var http = new HttpClient(handler);
        var coordinator = CreateCoordinator(http, ActiveAssociation(), evidence);

        var result = await coordinator.RefreshAsync(Context());

        Assert.AreEqual(AuthenticatedEntitlementRefreshDisposition.Rejected, result.Disposition);
        Assert.AreEqual("ENTITLEMENT_VERSION_ROLLBACK", result.ProductCode);
        Assert.AreEqual(50L, evidence.Read()!.Entitlement.EntitlementVersion);
    }

    private static AuthenticatedEntitlementRefreshCoordinator CreateCoordinator(
        HttpClient http,
        UserAccountAssociationRecord association,
        OnlineEntitlementEvidenceState evidence)
    {
        var api = new ProductApiClient(http, new ProductApiConfiguration(AccountEndpoint, EntitlementEndpoint));
        return new AuthenticatedEntitlementRefreshCoordinator(
            api,
            new FixedAssociationStore(association),
            new FixedWindowsUserContext("S-1-5-21-test"),
            evidence,
            new RuntimeStateRefreshSignal(),
            new FixedTimeProvider(Now));
    }

    private static AuthenticatedEntitlementRefreshContext Context()
        => new(
            AccountId,
            "ACCESS_TOKEN_SECRET",
            Now.AddMinutes(15),
            "1.2.3-test",
            "installation-test-01",
            Guid.Parse("5846b359-0279-4cd5-a5e6-c08592ef210f"));

    private static UserAccountAssociationRecord ActiveAssociation()
        => new(
            AssociationId,
            "S-1-5-21-test",
            AccountId,
            "ACTIVE",
            Now.AddDays(-30),
            Now.AddDays(-1),
            "41",
            Now.AddMinutes(-2),
            Now.AddMinutes(-2),
            "account.v1",
            5,
            Now.AddMinutes(-2),
            Guid.Parse("6fd30d0b-edaa-448d-99ac-f0436190b236").ToString("D"));

    private static OnlineEntitlementEvidenceState SeedEvidence(long version)
    {
        var state = new OnlineEntitlementEvidenceState();
        state.Publish(
            AssociationId,
            new SplitOSEntitlementSnapshot(
                AccountId,
                version,
                "PRO",
                "ACTIVE",
                Now.AddDays(-1),
                Now.AddDays(30),
                ["runtime.managed_modes"],
                true,
                Now.AddMinutes(-1)),
            Now.AddMinutes(-1));
        return state;
    }

    private sealed class FixedAssociationStore(UserAccountAssociationRecord association) : IUserAccountAssociationStore
    {
        public Task<UserAccountAssociationRecord?> GetAccountAssociationAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<UserAccountAssociationRecord?>(association);

        public Task<UserAssociationWriteOutcome> CreateActiveAssociationAsync(
            Guid associationId,
            string windowsUserSid,
            string accountId,
            DateTimeOffset authenticatedUtc,
            string secretReference,
            string? entitlementVersion,
            DateTimeOffset? entitlementObservedUtc,
            DateTimeOffset? lastServerUtc,
            Guid operationId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<UserAssociationWriteOutcome> MarkReauthRequiredAsync(
            int expectedRevision,
            Guid operationId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FixedWindowsUserContext(string sid) : IWindowsUserContext
    {
        public string GetCurrentUserSid() => sid;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FixtureHandler(
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string errorCode = "TEMPORARILY_UNAVAILABLE",
        long entitlementVersion = 42) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (request.RequestUri != EntitlementEndpoint)
            {
                return Task.FromResult(Json(HttpStatusCode.NotFound, new { error = new { code = "NOT_FOUND", retryable = false } }));
            }

            if (statusCode != HttpStatusCode.OK)
            {
                return Task.FromResult(Json(statusCode, new
                {
                    error = new
                    {
                        code = errorCode,
                        message = "Human text must not drive behavior.",
                        retryable = statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500,
                        correlationId = "server-correlation"
                    }
                }));
            }

            return Task.FromResult(Json(HttpStatusCode.OK, new
            {
                accountId = AccountId,
                entitlementVersion,
                plan = "PRO",
                status = "ACTIVE",
                validFrom = Now.AddDays(-1).ToString("O"),
                validUntil = Now.AddDays(30).ToString("O"),
                capabilities = new[] { "runtime.managed_modes" },
                offlineEligible = true,
                serverUtc = Now.ToString("O")
            }));
        }

        private static HttpResponseMessage Json(HttpStatusCode status, object value)
            => new(status)
            {
                Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
            };
    }
}
