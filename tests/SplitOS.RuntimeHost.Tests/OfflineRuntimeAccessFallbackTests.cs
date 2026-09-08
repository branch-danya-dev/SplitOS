using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.ProtectedSecrets;
using SplitOS.RuntimeHost.ProductIdentity;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class OfflineRuntimeAccessFallbackTests
{
    private const string AccountId = "acc_test_01";
    private const string AssociationId = "ecdd5d14-ec89-40c5-9eb0-25656d243462";
    private const string InstallationId = "installation-test-01";
    private const string Capability = "runtime.managed_modes";
    private const string Issuer = "https://entitlement.splitos.example/";
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 17, 30, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task ValidOfflineProofEnablesAccessWhenOnlineEvidenceIsMissing()
    {
        using var rsa = RSA.Create(2048);
        var store = new MemorySecretStore(Secret(Sign(rsa, entitlementVersion: 42)));
        var evaluator = CreateEvaluator(new OnlineEntitlementEvidenceState(), store, rsa);

        var result = await evaluator.EvaluateAsync(ActiveAssociation());

        Assert.IsTrue(result.IsEnabled);
        Assert.AreEqual("PRO_OFFLINE_ASSERTION_VALID", result.Reason);
        Assert.AreEqual(42L, result.EntitlementVersion);
        Assert.AreEqual(1, store.ReadCalls);
    }

    [TestMethod]
    public async Task ValidOfflineProofCanReplaceOnlyStaleOnlineEvidence()
    {
        using var rsa = RSA.Create(2048);
        var online = new OnlineEntitlementEvidenceState();
        online.Publish(
            AssociationId,
            Entitlement("PRO", "ACTIVE", [Capability], 42),
            Now.AddMinutes(-6));
        var store = new MemorySecretStore(Secret(Sign(rsa, entitlementVersion: 42)));

        var result = await CreateEvaluator(online, store, rsa).EvaluateAsync(ActiveAssociation());

        Assert.IsTrue(result.IsEnabled);
        Assert.AreEqual("PRO_OFFLINE_ASSERTION_VALID", result.Reason);
    }

    [TestMethod]
    public async Task FreshFreeOnlineAuthorityCannotBeOverriddenByOfflineProProof()
    {
        using var rsa = RSA.Create(2048);
        var online = new OnlineEntitlementEvidenceState();
        online.Publish(
            AssociationId,
            Entitlement("FREE", "ACTIVE", [Capability], 43),
            Now);
        var store = new MemorySecretStore(Secret(Sign(rsa, entitlementVersion: 42)));

        var result = await CreateEvaluator(online, store, rsa).EvaluateAsync(ActiveAssociation());

        Assert.IsFalse(result.IsEnabled);
        Assert.AreEqual("FREE_ENTITLEMENT", result.Reason);
        Assert.AreEqual(0, store.ReadCalls);
    }

    [TestMethod]
    public async Task OlderOfflineProofCannotRollBackNewerStaleOnlineVersion()
    {
        using var rsa = RSA.Create(2048);
        var online = new OnlineEntitlementEvidenceState();
        online.Publish(
            AssociationId,
            Entitlement("PRO", "ACTIVE", [Capability], 43),
            Now.AddMinutes(-6));
        var store = new MemorySecretStore(Secret(Sign(rsa, entitlementVersion: 42)));

        var result = await CreateEvaluator(online, store, rsa).EvaluateAsync(ActiveAssociation());

        Assert.IsFalse(result.IsEnabled);
        Assert.AreEqual("OFFLINE_ASSERTION_VERSION_ROLLBACK", result.Reason);
        Assert.AreEqual(43L, result.EntitlementVersion);
    }

    [TestMethod]
    public async Task ReauthRequiredAssociationCannotUseOfflineProof()
    {
        using var rsa = RSA.Create(2048);
        var store = new MemorySecretStore(Secret(Sign(rsa)));
        var association = ActiveAssociation() with { AssociationState = "REAUTH_REQUIRED" };

        var result = await CreateEvaluator(new OnlineEntitlementEvidenceState(), store, rsa)
            .EvaluateAsync(association);

        Assert.IsFalse(result.IsEnabled);
        Assert.AreEqual("ACCOUNT_NOT_ACTIVE", result.Reason);
        Assert.AreEqual(0, store.ReadCalls);
    }

    [TestMethod]
    public async Task ClockRollbackSuspicionFailsClosedOffline()
    {
        using var rsa = RSA.Create(2048);
        var secret = Secret(Sign(rsa));
        secret = new AccountSecretEnvelope
        {
            AccountId = secret.AccountId,
            RefreshToken = secret.RefreshToken,
            RefreshIssuedUtc = secret.RefreshIssuedUtc,
            RefreshAbsoluteExpiryUtc = secret.RefreshAbsoluteExpiryUtc,
            LastTrustedServerUtc = Now.AddMinutes(10),
            LastTrustedServerObservationLocalUtc = Now.AddMinutes(-1),
            OfflineEntitlementAssertion = secret.OfflineEntitlementAssertion,
            OfflineAssertionStoredUtc = secret.OfflineAssertionStoredUtc,
            LastValidAssertionJti = secret.LastValidAssertionJti
        };
        var store = new MemorySecretStore(secret);

        var result = await CreateEvaluator(new OnlineEntitlementEvidenceState(), store, rsa)
            .EvaluateAsync(ActiveAssociation());

        Assert.IsFalse(result.IsEnabled);
        Assert.AreEqual("CLOCK_ROLLBACK_SUSPECTED", result.Reason);
    }

    [TestMethod]
    public async Task AssertionForDifferentAssociationCannotAuthorizeCurrentAssociation()
    {
        using var rsa = RSA.Create(2048);
        var store = new MemorySecretStore(Secret(Sign(
            rsa,
            associationId: "7be1b230-c4a3-4f24-a878-6810a2612cf5")));

        var result = await CreateEvaluator(new OnlineEntitlementEvidenceState(), store, rsa)
            .EvaluateAsync(ActiveAssociation());

        Assert.IsFalse(result.IsEnabled);
        Assert.AreEqual("OFFLINE_ASSERTION_CONTEXT_MISMATCH", result.Reason);
    }

    [TestMethod]
    public async Task UnreadableProtectedSecretFailsClosed()
    {
        using var rsa = RSA.Create(2048);
        var store = new MemorySecretStore(null, AccountSecretReadStatus.Unreadable);

        var result = await CreateEvaluator(new OnlineEntitlementEvidenceState(), store, rsa)
            .EvaluateAsync(ActiveAssociation());

        Assert.IsFalse(result.IsEnabled);
        Assert.AreEqual("LOCAL_SECRET_UNREADABLE", result.Reason);
    }

    [TestMethod]
    public async Task FreshOnlineProWinsWithoutReadingOfflineSecret()
    {
        using var rsa = RSA.Create(2048);
        var online = new OnlineEntitlementEvidenceState();
        online.Publish(
            AssociationId,
            Entitlement("PRO", "ACTIVE", [Capability], 44),
            Now);
        var store = new MemorySecretStore(Secret(Sign(rsa, entitlementVersion: 42)));

        var result = await CreateEvaluator(online, store, rsa).EvaluateAsync(ActiveAssociation());

        Assert.IsTrue(result.IsEnabled);
        Assert.AreEqual("PRO_ONLINE_CONFIRMED", result.Reason);
        Assert.AreEqual(44L, result.EntitlementVersion);
        Assert.AreEqual(0, store.ReadCalls);
    }

    private static OfflineCapableRuntimeAccessEvaluator CreateEvaluator(
        OnlineEntitlementEvidenceState online,
        IAccountSecretStore store,
        RSA rsa)
    {
        var onlineEvaluator = new OnlineEntitlementRuntimeAccessEvaluator(
            online,
            RuntimeAccessPolicy.Default,
            new FixedTimeProvider(Now));
        var publicKey = new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = "entitlement-test-key-01" };
        var offlineValidator = new OfflineEntitlementAssertionValidator(
            new OfflineEntitlementTrustConfiguration(
                Issuer,
                [publicKey],
                [SecurityAlgorithms.RsaSha256]),
            OfflineEntitlementValidationPolicy.Default,
            new FixedTimeProvider(Now));

        return new OfflineCapableRuntimeAccessEvaluator(
            onlineEvaluator,
            store,
            offlineValidator,
            InstallationId);
    }

    private static AccountAssociationEvaluation ActiveAssociation()
        => new("ACTIVE", AccountId, AssociationId, null, 5);

    private static AccountSecretEnvelope Secret(string assertion)
        => new()
        {
            AccountId = AccountId,
            RefreshToken = "REFRESH_SECRET",
            RefreshIssuedUtc = Now.AddDays(-1),
            RefreshAbsoluteExpiryUtc = Now.AddDays(30),
            LastTrustedServerUtc = Now.AddMinutes(-10),
            LastTrustedServerObservationLocalUtc = Now.AddMinutes(-10),
            OfflineEntitlementAssertion = assertion,
            OfflineAssertionStoredUtc = Now.AddMinutes(-5),
            LastValidAssertionJti = "assertion-42"
        };

    private static SplitOSEntitlementSnapshot Entitlement(
        string plan,
        string status,
        IReadOnlyList<string> capabilities,
        long version)
        => new(
            AccountId,
            version,
            plan,
            status,
            Now.AddDays(-1),
            Now.AddDays(30),
            capabilities,
            true,
            Now);

    private static string Sign(
        RSA rsa,
        long entitlementVersion = 42,
        string associationId = AssociationId)
    {
        var header = new { alg = "RS256", kid = "entitlement-test-key-01" };
        var payload = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["ver"] = 1,
            ["iss"] = Issuer,
            ["sub"] = AccountId,
            ["jti"] = $"assertion-{entitlementVersion}",
            ["installationId"] = InstallationId,
            ["associationId"] = associationId,
            ["entitlementVersion"] = entitlementVersion,
            ["plan"] = "PRO",
            ["capabilities"] = new[] { Capability },
            ["iat"] = Now.AddHours(-1).ToUnixTimeSeconds(),
            ["nbf"] = Now.AddHours(-1).ToUnixTimeSeconds(),
            ["exp"] = Now.AddDays(2).ToUnixTimeSeconds()
        };

        var encodedHeader = B64(JsonSerializer.SerializeToUtf8Bytes(header));
        var encodedPayload = B64(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signingInput = Encoding.ASCII.GetBytes(encodedHeader + "." + encodedPayload);
        var signature = rsa.SignData(signingInput, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return encodedHeader + "." + encodedPayload + "." + B64(signature);
    }

    private static string B64(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class MemorySecretStore(
        AccountSecretEnvelope? secret,
        AccountSecretReadStatus status = AccountSecretReadStatus.Available) : IAccountSecretStore
    {
        public int ReadCalls { get; private set; }

        public Task<AccountSecretReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            return Task.FromResult(status switch
            {
                AccountSecretReadStatus.Missing => AccountSecretReadResult.Missing(),
                AccountSecretReadStatus.Unreadable => AccountSecretReadResult.Unreadable(),
                _ when secret is not null => AccountSecretReadResult.Available(secret),
                _ => AccountSecretReadResult.Missing()
            });
        }

        public Task WriteAsync(AccountSecretEnvelope value, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
