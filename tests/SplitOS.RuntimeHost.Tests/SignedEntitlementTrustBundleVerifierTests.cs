using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.ProductIdentity;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class SignedEntitlementTrustBundleVerifierTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 19, 30, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task ValidRootSignedBundleProducesEntitlementVerificationTrust()
    {
        using var rootRsa = RSA.Create(2048);
        using var entitlementRsa = RSA.Create(2048);
        var verifier = CreateVerifier(rootRsa);
        var envelope = CreateEnvelope(rootRsa, entitlementRsa);

        var bundle = await verifier.VerifyAsync(envelope);
        var trust = bundle.CreateValidationTrust(EntitlementAssertionTrustBundle.ProductionTrustDomain, Now);

        Assert.AreEqual(7L, bundle.Version);
        Assert.AreEqual(3L, bundle.SecurityEpoch);
        Assert.AreEqual(1, trust.SigningKeys.Count);
        Assert.AreEqual("ent-v7", trust.SigningKeys[0].KeyId);
        CollectionAssert.AreEquivalent(new[] { SecurityAlgorithms.RsaSha256 }, trust.AllowedAlgorithms.ToArray());
    }

    [TestMethod]
    public async Task PayloadTamperingAfterSigningIsRejected()
    {
        using var rootRsa = RSA.Create(2048);
        using var entitlementRsa = RSA.Create(2048);
        var verifier = CreateVerifier(rootRsa);
        var envelope = CreateEnvelope(rootRsa, entitlementRsa);
        var segments = envelope.Split('.');
        var tamperedPayload = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(
            CreatePayloadJson(entitlementRsa, version: 99, securityEpoch: 3)));
        var tampered = $"{segments[0]}.{tamperedPayload}.{segments[2]}";

        await AssertThrowsAsync<InvalidDataException>(() => verifier.VerifyAsync(tampered).AsTask());
    }

    [TestMethod]
    public async Task UnknownReleaseSigningKeyIsRejectedBeforePayloadAuthority()
    {
        using var trustedRoot = RSA.Create(2048);
        using var untrustedRoot = RSA.Create(2048);
        using var entitlementRsa = RSA.Create(2048);
        var verifier = CreateVerifier(trustedRoot);
        var envelope = CreateEnvelope(untrustedRoot, entitlementRsa, rootKeyId: "unknown-root");

        await AssertThrowsAsync<InvalidDataException>(() => verifier.VerifyAsync(envelope).AsTask());
    }

    [TestMethod]
    public async Task BundleVersionBelowTrustedFloorIsRejected()
    {
        using var rootRsa = RSA.Create(2048);
        using var entitlementRsa = RSA.Create(2048);
        var verifier = CreateVerifier(rootRsa, minimumBundleVersion: 8);
        var envelope = CreateEnvelope(rootRsa, entitlementRsa, version: 7);

        await AssertThrowsAsync<InvalidDataException>(() => verifier.VerifyAsync(envelope).AsTask());
    }

    [TestMethod]
    public async Task SecurityEpochBelowTrustedFloorIsRejected()
    {
        using var rootRsa = RSA.Create(2048);
        using var entitlementRsa = RSA.Create(2048);
        var verifier = CreateVerifier(rootRsa, minimumSecurityEpoch: 4);
        var envelope = CreateEnvelope(rootRsa, entitlementRsa, securityEpoch: 3);

        await AssertThrowsAsync<InvalidDataException>(() => verifier.VerifyAsync(envelope).AsTask());
    }

    [TestMethod]
    public async Task DevelopmentTrustDomainCannotCrossProductionRootBoundary()
    {
        using var rootRsa = RSA.Create(2048);
        using var entitlementRsa = RSA.Create(2048);
        var verifier = CreateVerifier(rootRsa);
        var envelope = CreateEnvelope(rootRsa, entitlementRsa, trustDomain: "splitos-development");

        await AssertThrowsAsync<InvalidDataException>(() => verifier.VerifyAsync(envelope).AsTask());
    }

    [TestMethod]
    public async Task PrivateEntitlementKeyMaterialInReleaseMetadataIsRejected()
    {
        using var rootRsa = RSA.Create(2048);
        using var entitlementRsa = RSA.Create(2048);
        var verifier = CreateVerifier(rootRsa);
        var envelope = CreateEnvelope(rootRsa, entitlementRsa, includePrivateMarker: true);

        await AssertThrowsAsync<InvalidDataException>(() => verifier.VerifyAsync(envelope).AsTask());
    }

    private static SignedEntitlementTrustBundleVerifier CreateVerifier(
        RSA rootRsa,
        long minimumBundleVersion = 1,
        long minimumSecurityEpoch = 1)
        => new(new ReleaseSecurityRootTrustConfiguration(
            EntitlementAssertionTrustBundle.ProductionTrustDomain,
            new SecurityKey[] { new RsaSecurityKey(rootRsa) { KeyId = "root-1" } },
            new[] { SecurityAlgorithms.RsaSha256 },
            minimumBundleVersion,
            minimumSecurityEpoch));

    private static string CreateEnvelope(
        RSA signingRoot,
        RSA entitlementRsa,
        string rootKeyId = "root-1",
        long version = 7,
        long securityEpoch = 3,
        string trustDomain = EntitlementAssertionTrustBundle.ProductionTrustDomain,
        bool includePrivateMarker = false)
    {
        var header = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["alg"] = SecurityAlgorithms.RsaSha256,
            ["kid"] = rootKeyId,
            ["typ"] = "splitos-entitlement-trust+jws"
        });
        var payload = CreatePayloadJson(
            entitlementRsa,
            version,
            securityEpoch,
            trustDomain,
            includePrivateMarker);

        var encodedHeader = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(header));
        var encodedPayload = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(payload));
        var signingInput = Encoding.ASCII.GetBytes($"{encodedHeader}.{encodedPayload}");
        var signature = signingRoot.SignData(signingInput, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{encodedHeader}.{encodedPayload}.{Base64UrlEncoder.Encode(signature)}";
    }

    private static string CreatePayloadJson(
        RSA entitlementRsa,
        long version = 7,
        long securityEpoch = 3,
        string trustDomain = EntitlementAssertionTrustBundle.ProductionTrustDomain,
        bool includePrivateMarker = false)
    {
        var publicParameters = entitlementRsa.ExportParameters(false);
        var key = new Dictionary<string, object?>
        {
            ["kid"] = "ent-v7",
            ["alg"] = SecurityAlgorithms.RsaSha256,
            ["lifecycle"] = nameof(EntitlementAssertionKeyLifecycle.Active),
            ["notBefore"] = Now.AddDays(-1).ToUnixTimeSeconds(),
            ["notAfter"] = Now.AddDays(30).ToUnixTimeSeconds(),
            ["kty"] = "RSA",
            ["n"] = Base64UrlEncoder.Encode(publicParameters.Modulus!),
            ["e"] = Base64UrlEncoder.Encode(publicParameters.Exponent!)
        };
        if (includePrivateMarker)
        {
            key["d"] = "must-never-be-present";
        }

        var payload = new Dictionary<string, object?>
        {
            ["role"] = EntitlementAssertionTrustBundle.EntitlementAssertionRole,
            ["trustDomain"] = trustDomain,
            ["version"] = version,
            ["securityEpoch"] = securityEpoch,
            ["issuer"] = "https://identity.splitos.test",
            ["keys"] = new object[] { key }
        };
        return JsonSerializer.Serialize(payload);
    }

    private static async Task AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
            Assert.Fail($"Expected exception {typeof(TException).Name}.");
        }
        catch (TException)
        {
        }
    }
}
