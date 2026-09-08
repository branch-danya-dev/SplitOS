using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.IdentityModel.Tokens;
using SplitOS.Persistence.ProtectedSecrets;
using SplitOS.RuntimeHost.ProductIdentity;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class OfflineEntitlementIssuanceServiceTests
{
    private const string AccountId = "acc_test_01";
    private const string AssociationId = "ecdd5d14-ec89-40c5-9eb0-25656d243462";
    private const string InstallationId = "installation-test-01";
    private const string Capability = "runtime.managed_modes";
    private const string Issuer = "https://entitlement.splitos.example/";
    private static readonly Uri Endpoint = new("https://api.splitos.example/v1/entitlements/offline-assertion");
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 16, 20, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task ValidServerAssertionIsValidatedThenPersistedWithTrustedTime()
    {
        using var rsa = RSA.Create(2048);
        var secretStore = new MemorySecretStore(Secret());
        var handler = new FixtureHandler(rsa);
        using var http = new HttpClient(handler);
        var service = CreateService(http, rsa, secretStore);

        var result = await service.IssueAndStoreAsync(Context());

        Assert.IsTrue(result.IsStored);
        Assert.AreEqual("OFFLINE_ASSERTION_STORED", result.ProductCode);
        Assert.AreEqual(42L, result.EntitlementVersion);
        Assert.AreEqual(1, secretStore.WriteCalls);
        Assert.IsNotNull(secretStore.Stored!.OfflineEntitlementAssertion);
        Assert.AreEqual(Now, secretStore.Stored.LastTrustedServerUtc);
        Assert.AreEqual(Now, secretStore.Stored.LastTrustedServerObservationLocalUtc);
        Assert.AreEqual("assertion-42", secretStore.Stored.LastValidAssertionJti);
        Assert.AreEqual("REFRESH_SECRET", secretStore.Stored.RefreshToken);
        Assert.AreEqual(HttpMethod.Post, handler.LastMethod);
        Assert.AreEqual("Bearer", handler.LastAuthorizationScheme);
        Assert.IsTrue(handler.LastBody!.Contains(AssociationId, StringComparison.Ordinal));
        Assert.IsTrue(handler.LastBody.Contains(Capability, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ResponseSignedByUntrustedKeyIsNeverPersisted()
    {
        using var trusted = RSA.Create(2048);
        using var attacker = RSA.Create(2048);
        var store = new MemorySecretStore(Secret());
        using var http = new HttpClient(new FixtureHandler(attacker));
        var service = CreateService(http, trusted, store);

        var result = await service.IssueAndStoreAsync(Context());

        Assert.AreEqual(OfflineEntitlementIssuanceDisposition.ValidationRejected, result.Disposition);
        Assert.AreEqual(0, store.WriteCalls);
        Assert.IsNull(store.Stored!.OfflineEntitlementAssertion);
    }

    [TestMethod]
    public async Task ResponseMetadataMustMatchSignedClaims()
    {
        using var rsa = RSA.Create(2048);
        var store = new MemorySecretStore(Secret());
        using var http = new HttpClient(new FixtureHandler(rsa, responseVersion: 43));
        var service = CreateService(http, rsa, store);

        var result = await service.IssueAndStoreAsync(Context());

        Assert.AreEqual(OfflineEntitlementIssuanceDisposition.ValidationRejected, result.Disposition);
        Assert.AreEqual("OFFLINE_ASSERTION_RESPONSE_MISMATCH", result.ProductCode);
        Assert.AreEqual(0, store.WriteCalls);
    }

    [TestMethod]
    public async Task TrustedServerTimeRegressionIsRejected()
    {
        using var rsa = RSA.Create(2048);
        var store = new MemorySecretStore(Secret(Now.AddMinutes(6)));
        using var http = new HttpClient(new FixtureHandler(rsa));
        var service = CreateService(http, rsa, store);

        var result = await service.IssueAndStoreAsync(Context());

        Assert.AreEqual("TRUSTED_SERVER_TIME_ROLLBACK", result.ProductCode);
        Assert.AreEqual(0, store.WriteCalls);
    }

    [TestMethod]
    public async Task AuthRequiredIsClassifiedWithoutSecretMutation()
    {
        using var rsa = RSA.Create(2048);
        var store = new MemorySecretStore(Secret());
        using var http = new HttpClient(new FixtureHandler(rsa, status: HttpStatusCode.Unauthorized, errorCode: "TOKEN_REVOKED"));
        var service = CreateService(http, rsa, store);

        var result = await service.IssueAndStoreAsync(Context());

        Assert.AreEqual(OfflineEntitlementIssuanceDisposition.AuthRequired, result.Disposition);
        Assert.AreEqual("TOKEN_REVOKED", result.ProductCode);
        Assert.AreEqual(0, store.WriteCalls);
    }

    [TestMethod]
    public async Task OfflineNotEligibleIsClassifiedWithoutSecretMutation()
    {
        using var rsa = RSA.Create(2048);
        var store = new MemorySecretStore(Secret());
        using var http = new HttpClient(new FixtureHandler(rsa, status: HttpStatusCode.Forbidden, errorCode: "OFFLINE_NOT_ELIGIBLE"));
        var service = CreateService(http, rsa, store);

        var result = await service.IssueAndStoreAsync(Context());

        Assert.AreEqual(OfflineEntitlementIssuanceDisposition.NotEligible, result.Disposition);
        Assert.AreEqual(0, store.WriteCalls);
    }

    [TestMethod]
    public async Task MissingProtectedSessionFailsBeforeNetwork()
    {
        using var rsa = RSA.Create(2048);
        var store = new MemorySecretStore(null);
        var handler = new FixtureHandler(rsa);
        using var http = new HttpClient(handler);
        var service = CreateService(http, rsa, store);

        var result = await service.IssueAndStoreAsync(Context());

        Assert.AreEqual(OfflineEntitlementIssuanceDisposition.LocalSecretUnavailable, result.Disposition);
        Assert.AreEqual(0, handler.RequestCount);
    }

    [TestMethod]
    public async Task PersistenceFailureDoesNotReportStoredAssertion()
    {
        using var rsa = RSA.Create(2048);
        var store = new MemorySecretStore(Secret()) { FailWrite = true };
        using var http = new HttpClient(new FixtureHandler(rsa));
        var service = CreateService(http, rsa, store);

        var result = await service.IssueAndStoreAsync(Context());

        Assert.AreEqual(OfflineEntitlementIssuanceDisposition.PersistenceFailed, result.Disposition);
        Assert.AreEqual("OFFLINE_ASSERTION_LOCAL_STORE_FAILED", result.ProductCode);
    }

    private static OfflineEntitlementIssuanceService CreateService(HttpClient http, RSA rsa, IAccountSecretStore store)
    {
        var publicKey = new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = "entitlement-test-key-01" };
        var validator = new OfflineEntitlementAssertionValidator(
            new OfflineEntitlementTrustConfiguration(Issuer, [publicKey], [SecurityAlgorithms.RsaSha256]),
            OfflineEntitlementValidationPolicy.Default,
            new FixedTimeProvider(Now));
        return new OfflineEntitlementIssuanceService(
            http,
            new OfflineEntitlementIssuanceConfiguration(Endpoint),
            validator,
            store,
            new FixedTimeProvider(Now));
    }

    private static OfflineEntitlementIssuanceContext Context()
        => new(
            "ACCESS_SECRET",
            "1.2.3-test",
            InstallationId,
            Guid.Parse("a62defbc-0104-4fb4-af66-0f46ae57a8ad"),
            AccountId,
            AssociationId,
            42,
            [Capability]);

    private static AccountSecretEnvelope Secret(DateTimeOffset? lastTrustedServerUtc = null)
        => new()
        {
            AccountId = AccountId,
            RefreshToken = "REFRESH_SECRET",
            RefreshIssuedUtc = Now.AddDays(-1),
            RefreshAbsoluteExpiryUtc = Now.AddDays(30),
            LastTrustedServerUtc = lastTrustedServerUtc ?? Now.AddMinutes(-10)
        };

    private sealed class MemorySecretStore(AccountSecretEnvelope? stored) : IAccountSecretStore
    {
        public AccountSecretEnvelope? Stored { get; private set; } = stored;
        public bool FailWrite { get; init; }
        public int WriteCalls { get; private set; }

        public Task<AccountSecretReadResult> ReadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Stored is null ? AccountSecretReadResult.Missing() : AccountSecretReadResult.Available(Stored));

        public Task WriteAsync(AccountSecretEnvelope secret, CancellationToken cancellationToken = default)
        {
            WriteCalls++;
            if (FailWrite) throw new IOException("synthetic write failure");
            Stored = secret;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(CancellationToken cancellationToken = default)
        {
            Stored = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FixtureHandler(
        RSA signingKey,
        HttpStatusCode status = HttpStatusCode.OK,
        string errorCode = "TEMPORARILY_UNAVAILABLE",
        long responseVersion = 42) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public HttpMethod? LastMethod { get; private set; }
        public string? LastAuthorizationScheme { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastMethod = request.Method;
            LastAuthorizationScheme = request.Headers.Authorization?.Scheme;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            if (status != HttpStatusCode.OK)
            {
                return Json(status, new { error = new { code = errorCode, message = "ignored", retryable = (int)status >= 500 } });
            }

            var assertion = Sign(signingKey);
            return Json(HttpStatusCode.OK, new
            {
                assertion,
                issuedUtc = Now.AddHours(-1).ToString("O"),
                expiresUtc = Now.AddDays(2).ToString("O"),
                entitlementVersion = responseVersion,
                serverUtc = Now.ToString("O")
            });
        }

        private static string Sign(RSA rsa)
        {
            var header = new { alg = "RS256", kid = "entitlement-test-key-01" };
            var payload = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["ver"] = 1,
                ["iss"] = Issuer,
                ["sub"] = AccountId,
                ["jti"] = "assertion-42",
                ["installationId"] = InstallationId,
                ["associationId"] = AssociationId,
                ["entitlementVersion"] = 42,
                ["plan"] = "PRO",
                ["capabilities"] = new[] { Capability },
                ["iat"] = Now.AddHours(-1).ToUnixTimeSeconds(),
                ["nbf"] = Now.AddHours(-1).ToUnixTimeSeconds(),
                ["exp"] = Now.AddDays(2).ToUnixTimeSeconds()
            };
            var h = B64(JsonSerializer.SerializeToUtf8Bytes(header));
            var p = B64(JsonSerializer.SerializeToUtf8Bytes(payload));
            var data = Encoding.ASCII.GetBytes(h + "." + p);
            var signature = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return h + "." + p + "." + B64(signature);
        }

        private static string B64(byte[] bytes)
            => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static HttpResponseMessage Json(HttpStatusCode code, object value)
            => new(code) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
