using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.IdentityModel.Tokens;
using SplitOS.RuntimeHost.ProductIdentity;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class OfflineEntitlementAssertionValidatorTests
{
    private const string Issuer = "https://entitlement.splitos.example/";
    private const string AccountId = "acc_test_01";
    private const string AssociationId = "ecdd5d14-ec89-40c5-9eb0-25656d243462";
    private const string InstallationId = "installation-test-01";
    private const string Capability = "runtime.managed_modes";
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 15, 30, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task ValidBoundProAssertionIsAccepted()
    {
        using var rsa = RSA.Create(2048);
        var validator = CreateValidator(rsa);
        var assertion = Sign(rsa, Payload());

        var result = await validator.ValidateAsync(assertion, Context());

        Assert.IsTrue(result.IsAccepted);
        Assert.AreEqual("PRO_OFFLINE_ASSERTION_VALID", result.ProductCode);
        Assert.IsNotNull(result.Claims);
        Assert.AreEqual(42L, result.Claims.EntitlementVersion);
        Assert.IsTrue(result.Claims.HasCapability(Capability));
    }

    [TestMethod]
    public async Task BadSignatureIsRejected()
    {
        using var trusted = RSA.Create(2048);
        using var attacker = RSA.Create(2048);
        var validator = CreateValidator(trusted);
        var assertion = Sign(attacker, Payload());

        var result = await validator.ValidateAsync(assertion, Context());

        Assert.AreEqual(OfflineEntitlementValidationDisposition.Rejected, result.Disposition);
        Assert.AreEqual("OFFLINE_ASSERTION_SIGNATURE_INVALID", result.ProductCode);
    }

    [TestMethod]
    public async Task UnknownKeyIdIsRejectedWithoutTryingOtherKeys()
    {
        using var trusted = RSA.Create(2048);
        var validator = CreateValidator(trusted);
        var assertion = Sign(trusted, Payload(), keyId: "unknown-key");

        var result = await validator.ValidateAsync(assertion, Context());

        Assert.AreEqual("OFFLINE_ASSERTION_KEY_UNTRUSTED", result.ProductCode);
    }

    [TestMethod]
    public async Task WrongAssociationCannotReuseAssertion()
    {
        using var rsa = RSA.Create(2048);
        var validator = CreateValidator(rsa);
        var assertion = Sign(rsa, Payload());
        var context = Context() with { AssociationId = "7c4dd3f2-d80f-44b1-a0e8-a0590fe74acb" };

        var result = await validator.ValidateAsync(assertion, context);

        Assert.AreEqual("OFFLINE_ASSERTION_CONTEXT_MISMATCH", result.ProductCode);
    }

    [TestMethod]
    public async Task WrongInstallationCannotReuseAssertion()
    {
        using var rsa = RSA.Create(2048);
        var validator = CreateValidator(rsa);
        var assertion = Sign(rsa, Payload());

        var result = await validator.ValidateAsync(assertion, Context() with { InstallationId = "other-installation" });

        Assert.AreEqual("OFFLINE_ASSERTION_CONTEXT_MISMATCH", result.ProductCode);
    }

    [TestMethod]
    public async Task MissingManagedRuntimeCapabilityIsRejected()
    {
        using var rsa = RSA.Create(2048);
        var validator = CreateValidator(rsa);
        var assertion = Sign(rsa, Payload(capabilities: ["game.launcher"]));

        var result = await validator.ValidateAsync(assertion, Context());

        Assert.AreEqual("OFFLINE_ASSERTION_CAPABILITY_MISSING", result.ProductCode);
    }

    [TestMethod]
    public async Task FreeAssertionCannotAuthorizePremiumCapability()
    {
        using var rsa = RSA.Create(2048);
        var validator = CreateValidator(rsa);
        var assertion = Sign(rsa, Payload(plan: "FREE"));

        var result = await validator.ValidateAsync(assertion, Context());

        Assert.AreEqual("OFFLINE_ASSERTION_NOT_PRO", result.ProductCode);
    }

    [TestMethod]
    public async Task AssertionLongerThanSevenDaysIsRejected()
    {
        using var rsa = RSA.Create(2048);
        var validator = CreateValidator(rsa);
        var assertion = Sign(rsa, Payload(expiresUtc: Now.AddDays(8)));

        var result = await validator.ValidateAsync(assertion, Context());

        Assert.AreEqual("OFFLINE_ASSERTION_LIFETIME_INVALID", result.ProductCode);
    }

    [TestMethod]
    public async Task ExpiredAssertionCannotBeExtendedLocally()
    {
        using var rsa = RSA.Create(2048);
        var validator = CreateValidator(rsa);
        var assertion = Sign(rsa, Payload(
            issuedUtc: Now.AddDays(-2),
            notBeforeUtc: Now.AddDays(-2),
            expiresUtc: Now.AddMinutes(-6)));

        var result = await validator.ValidateAsync(assertion, Context());

        Assert.AreEqual("OFFLINE_ASSERTION_EXPIRED", result.ProductCode);
    }

    [TestMethod]
    public async Task EntitlementVersionRollbackIsRejected()
    {
        using var rsa = RSA.Create(2048);
        var validator = CreateValidator(rsa);
        var assertion = Sign(rsa, Payload(version: 41));

        var result = await validator.ValidateAsync(assertion, Context() with { MinimumEntitlementVersion = 42 });

        Assert.AreEqual("OFFLINE_ASSERTION_VERSION_ROLLBACK", result.ProductCode);
    }

    [TestMethod]
    public async Task ClockRollbackBeforeTrustedServerTimeBlocksOfflinePremium()
    {
        using var rsa = RSA.Create(2048);
        var validator = CreateValidator(rsa);
        var assertion = Sign(rsa, Payload());
        var context = Context() with
        {
            LastTrustedServerUtc = Now.AddMinutes(6),
            LastTrustedServerObservationLocalUtc = Now.AddMinutes(-1)
        };

        var result = await validator.ValidateAsync(assertion, context);

        Assert.AreEqual(OfflineEntitlementValidationDisposition.ClockRollbackSuspected, result.Disposition);
        Assert.AreEqual("CLOCK_ROLLBACK_SUSPECTED", result.ProductCode);
    }

    [TestMethod]
    public async Task FutureTrustedObservationBlocksOfflinePremium()
    {
        using var rsa = RSA.Create(2048);
        var validator = CreateValidator(rsa);
        var assertion = Sign(rsa, Payload());
        var context = Context() with
        {
            LastTrustedServerUtc = Now.AddMinutes(-1),
            LastTrustedServerObservationLocalUtc = Now.AddMinutes(6)
        };

        var result = await validator.ValidateAsync(assertion, context);

        Assert.AreEqual("CLOCK_ROLLBACK_SUSPECTED", result.ProductCode);
    }

    [TestMethod]
    public async Task UnknownCriticalHeaderIsRejected()
    {
        using var rsa = RSA.Create(2048);
        var validator = CreateValidator(rsa);
        var assertion = Sign(rsa, Payload(), criticalHeader: true);

        var result = await validator.ValidateAsync(assertion, Context());

        Assert.AreEqual("OFFLINE_ASSERTION_CRITICAL_HEADER_REJECTED", result.ProductCode);
    }

    private static OfflineEntitlementAssertionValidator CreateValidator(RSA rsa)
    {
        var publicKey = new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = "entitlement-test-key-01" };
        return new OfflineEntitlementAssertionValidator(
            new OfflineEntitlementTrustConfiguration(
                Issuer,
                [publicKey],
                [SecurityAlgorithms.RsaSha256]),
            OfflineEntitlementValidationPolicy.Default,
            new FixedTimeProvider(Now));
    }

    private static OfflineEntitlementValidationContext Context()
        => new(
            AccountId,
            AssociationId,
            InstallationId,
            Capability,
            42,
            Now.AddMinutes(-1),
            Now.AddMinutes(-1));

    private static Dictionary<string, object> Payload(
        string plan = "PRO",
        IReadOnlyList<string>? capabilities = null,
        long version = 42,
        DateTimeOffset? issuedUtc = null,
        DateTimeOffset? notBeforeUtc = null,
        DateTimeOffset? expiresUtc = null)
    {
        var issued = issuedUtc ?? Now.AddHours(-1);
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["ver"] = 1,
            ["iss"] = Issuer,
            ["sub"] = AccountId,
            ["jti"] = "assertion-test-01",
            ["installationId"] = InstallationId,
            ["associationId"] = AssociationId,
            ["entitlementVersion"] = version,
            ["plan"] = plan,
            ["capabilities"] = capabilities ?? [Capability],
            ["iat"] = issued.ToUnixTimeSeconds(),
            ["nbf"] = (notBeforeUtc ?? issued).ToUnixTimeSeconds(),
            ["exp"] = (expiresUtc ?? Now.AddDays(2)).ToUnixTimeSeconds()
        };
    }

    private static string Sign(
        RSA rsa,
        Dictionary<string, object> payload,
        string keyId = "entitlement-test-key-01",
        bool criticalHeader = false)
    {
        var header = criticalHeader
            ? new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["alg"] = "RS256",
                ["kid"] = keyId,
                ["crit"] = new[] { "future" },
                ["future"] = true
            }
            : new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["alg"] = "RS256",
                ["kid"] = keyId
            };

        var encodedHeader = Base64Url(JsonSerializer.SerializeToUtf8Bytes(header));
        var encodedPayload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signingInput = Encoding.ASCII.GetBytes(encodedHeader + "." + encodedPayload);
        var signature = rsa.SignData(signingInput, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return encodedHeader + "." + encodedPayload + "." + Base64Url(signature);
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
