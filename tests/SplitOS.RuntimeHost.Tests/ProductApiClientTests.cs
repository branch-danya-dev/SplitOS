using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.ProductIdentity;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class ProductApiClientTests
{
    private static readonly Uri AccountEndpoint = new("https://api.example.test/v1/account");
    private static readonly Uri EntitlementEndpoint = new("https://api.example.test/v1/entitlements/current");
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task AccountRequestUsesBearerAndRequiredProductMetadata()
    {
        using var handler = new FixtureHandler();
        using var http = new HttpClient(handler);
        var client = new ProductApiClient(http, CreateConfiguration());
        var context = CreateContext();

        var result = await client.GetAccountAsync(context);

        Assert.AreEqual(ProductApiDisposition.Accepted, result.Disposition);
        Assert.IsNotNull(result.Value);
        Assert.AreEqual("acc_test_01", result.Value.AccountId);
        Assert.AreEqual("ACTIVE", result.Value.Status);
        Assert.AreEqual("Daniel", result.Value.DisplayName);
        Assert.AreEqual("user@example.test", result.Value.Email);
        Assert.IsTrue(result.Value.EmailVerified);

        Assert.AreEqual("Bearer", handler.LastAuthorizationScheme);
        Assert.AreEqual("ACCESS_SECRET", handler.LastAuthorizationParameter);
        Assert.AreEqual("1.2.3-test", handler.LastClientVersion);
        Assert.AreEqual("installation-test-01", handler.LastInstallationId);
        Assert.AreEqual(context.CorrelationId.ToString("D"), handler.LastCorrelationId);
        Assert.IsFalse(context.ToString().Contains("ACCESS_SECRET", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task EntitlementPreservesCapabilityFirstEvidenceWithoutGrantingRuntimeAccess()
    {
        using var handler = new FixtureHandler();
        using var http = new HttpClient(handler);
        var client = new ProductApiClient(http, CreateConfiguration());

        var result = await client.GetCurrentEntitlementAsync(CreateContext(), "acc_test_01");

        Assert.AreEqual(ProductApiDisposition.Accepted, result.Disposition);
        Assert.IsNotNull(result.Value);
        Assert.AreEqual(42L, result.Value.EntitlementVersion);
        Assert.AreEqual("PRO", result.Value.Plan);
        Assert.AreEqual("ACTIVE", result.Value.Status);
        Assert.IsTrue(result.Value.HasCapability("runtime.managed_modes"));
        Assert.IsTrue(result.Value.HasCapability("game.launcher"));
        Assert.IsTrue(result.Value.OfflineEligible);
        Assert.AreEqual(Now, result.Value.ServerUtc);
    }

    [TestMethod]
    public async Task EntitlementForDifferentAccountFailsClosed()
    {
        using var handler = new FixtureHandler();
        using var http = new HttpClient(handler);
        var client = new ProductApiClient(http, CreateConfiguration());

        var result = await client.GetCurrentEntitlementAsync(CreateContext(), "acc_expected_other");

        Assert.AreEqual(ProductApiDisposition.MalformedResponse, result.Disposition);
        Assert.AreEqual("ENTITLEMENT_ACCOUNT_MISMATCH", result.ProductCode);
        Assert.IsNull(result.Value);
    }

    [TestMethod]
    public async Task UnknownEntitlementPlanIsRejectedInsteadOfInterpretedAsProOrFree()
    {
        using var handler = new FixtureHandler(entitlementPlan: "ULTIMATE");
        using var http = new HttpClient(handler);
        var client = new ProductApiClient(http, CreateConfiguration());

        var result = await client.GetCurrentEntitlementAsync(CreateContext(), "acc_test_01");

        Assert.AreEqual(ProductApiDisposition.MalformedResponse, result.Disposition);
        Assert.AreEqual("ENTITLEMENT_RESPONSE_INVALID", result.ProductCode);
        Assert.IsNull(result.Value);
    }

    [TestMethod]
    public async Task DuplicateSecurityRelevantEntitlementFieldIsRejected()
    {
        using var handler = new FixtureHandler(duplicateEntitlementPlan: true);
        using var http = new HttpClient(handler);
        var client = new ProductApiClient(http, CreateConfiguration());

        var result = await client.GetCurrentEntitlementAsync(CreateContext(), "acc_test_01");

        Assert.AreEqual(ProductApiDisposition.MalformedResponse, result.Disposition);
        Assert.IsNull(result.Value);
    }

    [TestMethod]
    public async Task UnauthorizedMapsToAuthRequiredWithoutParsingHumanMessage()
    {
        using var handler = new FixtureHandler(
            accountStatusCode: HttpStatusCode.Unauthorized,
            errorCode: "TOKEN_REVOKED");
        using var http = new HttpClient(handler);
        var client = new ProductApiClient(http, CreateConfiguration());

        var result = await client.GetAccountAsync(CreateContext());

        Assert.AreEqual(ProductApiDisposition.AuthRequired, result.Disposition);
        Assert.AreEqual("TOKEN_REVOKED", result.ProductCode);
        Assert.IsFalse(result.Retryable);
        Assert.IsNull(result.Value);
    }

    [TestMethod]
    public async Task ForbiddenDoesNotBecomeFreeEntitlement()
    {
        using var handler = new FixtureHandler(
            entitlementStatusCode: HttpStatusCode.Forbidden,
            errorCode: "ENTITLEMENT_NOT_ACTIVE");
        using var http = new HttpClient(handler);
        var client = new ProductApiClient(http, CreateConfiguration());

        var result = await client.GetCurrentEntitlementAsync(CreateContext(), "acc_test_01");

        Assert.AreEqual(ProductApiDisposition.Rejected, result.Disposition);
        Assert.AreEqual("ENTITLEMENT_NOT_ACTIVE", result.ProductCode);
        Assert.IsNull(result.Value);
    }

    [TestMethod]
    public async Task RateLimitAndServerFailureRemainRetryableTransportPolicyResults()
    {
        using (var rateHandler = new FixtureHandler(accountStatusCode: HttpStatusCode.TooManyRequests, errorCode: "RATE_LIMITED"))
        using (var rateHttp = new HttpClient(rateHandler))
        {
            var client = new ProductApiClient(rateHttp, CreateConfiguration());
            var rate = await client.GetAccountAsync(CreateContext());
            Assert.AreEqual(ProductApiDisposition.RateLimited, rate.Disposition);
            Assert.IsTrue(rate.Retryable);
        }

        using (var serverHandler = new FixtureHandler(accountStatusCode: HttpStatusCode.ServiceUnavailable, errorCode: "TEMPORARILY_UNAVAILABLE"))
        using (var serverHttp = new HttpClient(serverHandler))
        {
            var client = new ProductApiClient(serverHttp, CreateConfiguration());
            var unavailable = await client.GetAccountAsync(CreateContext());
            Assert.AreEqual(ProductApiDisposition.BackendUnavailable, unavailable.Disposition);
            Assert.IsTrue(unavailable.Retryable);
        }
    }

    [TestMethod]
    public void ProductEndpointsMustBeExactReleaseOwnedHttpsAuthority()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new ProductApiConfiguration(
            new Uri("http://api.example.test/v1/account"),
            EntitlementEndpoint).Validate());

        Assert.ThrowsExactly<ArgumentException>(() => new ProductApiConfiguration(
            AccountEndpoint,
            new Uri("https://attacker.invalid/v1/entitlements/current")).Validate());

        Assert.ThrowsExactly<ArgumentException>(() => new ProductApiConfiguration(
            new Uri("https://api.example.test/v1/account?target=other"),
            EntitlementEndpoint).Validate());
    }

    private static ProductApiConfiguration CreateConfiguration()
        => new(AccountEndpoint, EntitlementEndpoint);

    private static ProductApiRequestContext CreateContext()
        => new(
            "ACCESS_SECRET",
            "1.2.3-test",
            "installation-test-01",
            Guid.Parse("84f20ced-adbc-46bf-9432-598f4cfde213"));

    private sealed class FixtureHandler(
        HttpStatusCode accountStatusCode = HttpStatusCode.OK,
        HttpStatusCode entitlementStatusCode = HttpStatusCode.OK,
        string? errorCode = null,
        string entitlementPlan = "PRO",
        bool duplicateEntitlementPlan = false) : HttpMessageHandler
    {
        public string? LastAuthorizationScheme { get; private set; }
        public string? LastAuthorizationParameter { get; private set; }
        public string? LastClientVersion { get; private set; }
        public string? LastInstallationId { get; private set; }
        public string? LastCorrelationId { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastAuthorizationScheme = request.Headers.Authorization?.Scheme;
            LastAuthorizationParameter = request.Headers.Authorization?.Parameter;
            LastClientVersion = FirstHeader(request, "X-SplitOS-Client-Version");
            LastInstallationId = FirstHeader(request, "X-SplitOS-Installation-Id");
            LastCorrelationId = FirstHeader(request, "X-Correlation-Id");

            if (request.RequestUri == AccountEndpoint)
            {
                return Task.FromResult(accountStatusCode == HttpStatusCode.OK
                    ? Json(HttpStatusCode.OK, new
                    {
                        accountId = "acc_test_01",
                        status = "ACTIVE",
                        displayName = "Daniel",
                        email = "user@example.test",
                        emailVerified = true,
                        createdUtc = Now.AddYears(-1).ToString("O")
                    })
                    : Error(accountStatusCode, errorCode ?? "INVALID_REQUEST"));
            }

            if (request.RequestUri == EntitlementEndpoint)
            {
                if (entitlementStatusCode != HttpStatusCode.OK)
                {
                    return Task.FromResult(Error(entitlementStatusCode, errorCode ?? "INVALID_REQUEST"));
                }

                if (duplicateEntitlementPlan)
                {
                    var raw = $$"""
                        {
                          "accountId":"acc_test_01",
                          "entitlementVersion":42,
                          "plan":"PRO",
                          "plan":"FREE",
                          "status":"ACTIVE",
                          "validFrom":"{{Now.AddDays(-1):O}}",
                          "validUntil":"{{Now.AddDays(30):O}}",
                          "capabilities":["runtime.managed_modes"],
                          "offlineEligible":true,
                          "serverUtc":"{{Now:O}}"
                        }
                        """;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(raw, Encoding.UTF8, "application/json")
                    });
                }

                return Task.FromResult(Json(HttpStatusCode.OK, new
                {
                    accountId = "acc_test_01",
                    entitlementVersion = 42,
                    plan = entitlementPlan,
                    status = "ACTIVE",
                    validFrom = Now.AddDays(-1).ToString("O"),
                    validUntil = Now.AddDays(30).ToString("O"),
                    capabilities = new[] { "runtime.managed_modes", "game.launcher" },
                    offlineEligible = true,
                    serverUtc = Now.ToString("O")
                }));
            }

            return Task.FromResult(Error(HttpStatusCode.NotFound, "NOT_FOUND"));
        }

        private static string? FirstHeader(HttpRequestMessage request, string name)
            => request.Headers.TryGetValues(name, out var values) ? values.SingleOrDefault() : null;

        private static HttpResponseMessage Error(HttpStatusCode status, string code)
            => Json(status, new
            {
                error = new
                {
                    code,
                    message = "Human text must not drive behavior.",
                    retryable = status == HttpStatusCode.TooManyRequests || (int)status >= 500,
                    correlationId = "server-correlation"
                }
            });

        private static HttpResponseMessage Json(HttpStatusCode status, object value)
            => new(status)
            {
                Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
            };
    }
}
