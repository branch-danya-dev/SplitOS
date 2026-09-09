using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.Authentication;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class SignedNativeAuthAuthorityVerifierTests
{
    [TestMethod]
    public async Task ValidSignedAuthorityProducesReleaseOwnedConfiguration()
    {
        using var root = RSA.Create(2048);
        var verifier = CreateVerifier(root);
        var envelope = CreateEnvelope(root);

        var metadata = await verifier.VerifyAsync(envelope);

        Assert.AreEqual(7L, metadata.Version);
        Assert.AreEqual(3L, metadata.SecurityEpoch);
        Assert.AreEqual("https://identity.splitos.test/", metadata.Authority.Issuer.AbsoluteUri);
        Assert.AreEqual("splitos-native", metadata.Authority.ClientId);
        CollectionAssert.AreEquivalent(new[] { "openid", "offline_access" }, metadata.Authority.RequestedScopes.ToArray());
        CollectionAssert.AreEquivalent(new[] { "RS256" }, metadata.Authority.AllowedIdTokenAlgorithms.ToArray());
        Assert.AreEqual("https://api.splitos.test/v1/account", metadata.ProductApi.AccountEndpoint.AbsoluteUri);
        Assert.AreEqual("https://api.splitos.test/v1/entitlements/current", metadata.ProductApi.CurrentEntitlementEndpoint.AbsoluteUri);
    }

    [TestMethod]
    public async Task TamperedAuthorityEndpointIsRejectedBySignature()
    {
        using var root = RSA.Create(2048);
        var verifier = CreateVerifier(root);
        var envelope = CreateEnvelope(root);
        var segments = envelope.Split('.');
        var payload = Payload(tokenEndpoint: "https://evil.example/token");
        var tampered = $"{segments[0]}.{Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(payload))}.{segments[2]}";

        await AssertThrowsAsync<InvalidDataException>(() => verifier.VerifyAsync(tampered).AsTask());
    }

    [TestMethod]
    public async Task UnknownRootKidIsRejectedWithoutTryingOtherKeys()
    {
        using var trustedRoot = RSA.Create(2048);
        using var otherRoot = RSA.Create(2048);
        var verifier = CreateVerifier(trustedRoot);
        var envelope = CreateEnvelope(otherRoot, rootKid: "other-root");

        await AssertThrowsAsync<InvalidDataException>(() => verifier.VerifyAsync(envelope).AsTask());
    }

    [TestMethod]
    public async Task DevelopmentTrustDomainCannotConfigureProductionAuth()
    {
        using var root = RSA.Create(2048);
        var verifier = CreateVerifier(root);
        var envelope = CreateEnvelope(root, trustDomain: "splitos-development");

        await AssertThrowsAsync<InvalidDataException>(() => verifier.VerifyAsync(envelope).AsTask());
    }

    [TestMethod]
    public async Task MetadataRollbackFloorsFailClosed()
    {
        using var root = RSA.Create(2048);
        var versionVerifier = CreateVerifier(root, minimumVersion: 8);
        var epochVerifier = CreateVerifier(root, minimumEpoch: 4);

        await AssertThrowsAsync<InvalidDataException>(() => versionVerifier.VerifyAsync(CreateEnvelope(root, version: 7)).AsTask());
        await AssertThrowsAsync<InvalidDataException>(() => epochVerifier.VerifyAsync(CreateEnvelope(root, securityEpoch: 3)).AsTask());
    }

    [TestMethod]
    public async Task HttpOrUserInfoEndpointCannotBecomeReleaseAuthority()
    {
        using var root = RSA.Create(2048);
        var verifier = CreateVerifier(root);

        await AssertThrowsAsync<InvalidDataException>(() =>
            verifier.VerifyAsync(CreateEnvelope(root, authorizationEndpoint: "http://identity.splitos.test/authorize")).AsTask());
        await AssertThrowsAsync<InvalidDataException>(() =>
            verifier.VerifyAsync(CreateEnvelope(root, tokenEndpoint: "https://user@identity.splitos.test/token")).AsTask());
    }

    [TestMethod]
    public async Task ProductApiAuthorityMustBeReleaseOwnedHttpsOrigin()
    {
        using var root = RSA.Create(2048);
        var verifier = CreateVerifier(root);

        await AssertThrowsAsync<InvalidDataException>(() =>
            verifier.VerifyAsync(CreateEnvelope(root, productApiAuthority: "http://api.splitos.test/")).AsTask());
        await AssertThrowsAsync<InvalidDataException>(() =>
            verifier.VerifyAsync(CreateEnvelope(root, productApiAuthority: "https://user@api.splitos.test/")).AsTask());
        await AssertThrowsAsync<InvalidDataException>(() =>
            verifier.VerifyAsync(CreateEnvelope(root, productApiAuthority: "https://api.splitos.test/custom/")).AsTask());
    }

    [TestMethod]
    public async Task UnsupportedIdTokenAlgorithmCannotBecomeAuthority()
    {
        using var root = RSA.Create(2048);
        var verifier = CreateVerifier(root);
        var envelope = CreateEnvelope(root, idTokenAlgorithms: new[] { "HS256" });

        await AssertThrowsAsync<InvalidDataException>(() => verifier.VerifyAsync(envelope).AsTask());
    }

    [TestMethod]
    public async Task MissingOpenIdScopeCannotBecomeAuthority()
    {
        using var root = RSA.Create(2048);
        var verifier = CreateVerifier(root);
        var envelope = CreateEnvelope(root, scopes: new[] { "offline_access" });

        await AssertThrowsAsync<InvalidDataException>(() => verifier.VerifyAsync(envelope).AsTask());
    }

    private static SignedNativeAuthAuthorityVerifier CreateVerifier(
        RSA root,
        long minimumVersion = 1,
        long minimumEpoch = 1)
        => new(new NativeAuthReleaseTrustConfiguration(
            NativeAuthReleaseTrustConfiguration.ProductionTrustDomain,
            new SecurityKey[] { new RsaSecurityKey(root) { KeyId = "root-1" } },
            new[] { SecurityAlgorithms.RsaSha256 },
            minimumVersion,
            minimumEpoch));

    private static string CreateEnvelope(
        RSA signingRoot,
        string rootKid = "root-1",
        long version = 7,
        long securityEpoch = 3,
        string trustDomain = NativeAuthReleaseTrustConfiguration.ProductionTrustDomain,
        string authorizationEndpoint = "https://identity.splitos.test/authorize",
        string tokenEndpoint = "https://identity.splitos.test/token",
        string productApiAuthority = "https://api.splitos.test/",
        IReadOnlyList<string>? scopes = null,
        IReadOnlyList<string>? idTokenAlgorithms = null)
    {
        var header = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["alg"] = SecurityAlgorithms.RsaSha256,
            ["kid"] = rootKid
        });
        var payload = Payload(
            version,
            securityEpoch,
            trustDomain,
            authorizationEndpoint,
            tokenEndpoint,
            productApiAuthority,
            scopes,
            idTokenAlgorithms);

        var encodedHeader = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(header));
        var encodedPayload = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(payload));
        var input = Encoding.ASCII.GetBytes($"{encodedHeader}.{encodedPayload}");
        var signature = signingRoot.SignData(input, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{encodedHeader}.{encodedPayload}.{Base64UrlEncoder.Encode(signature)}";
    }

    private static string Payload(
        long version = 7,
        long securityEpoch = 3,
        string trustDomain = NativeAuthReleaseTrustConfiguration.ProductionTrustDomain,
        string authorizationEndpoint = "https://identity.splitos.test/authorize",
        string tokenEndpoint = "https://identity.splitos.test/token",
        string productApiAuthority = "https://api.splitos.test/",
        IReadOnlyList<string>? scopes = null,
        IReadOnlyList<string>? idTokenAlgorithms = null)
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["role"] = NativeAuthReleaseTrustConfiguration.NativeAuthAuthorityRole,
            ["trustDomain"] = trustDomain,
            ["version"] = version,
            ["securityEpoch"] = securityEpoch,
            ["issuer"] = "https://identity.splitos.test/",
            ["discoveryEndpoint"] = "https://identity.splitos.test/.well-known/openid-configuration",
            ["authorizationEndpoint"] = authorizationEndpoint,
            ["tokenEndpoint"] = tokenEndpoint,
            ["jwksEndpoint"] = "https://identity.splitos.test/.well-known/jwks.json",
            ["clientId"] = "splitos-native",
            ["productApiAuthority"] = productApiAuthority,
            ["scopes"] = scopes ?? new[] { "openid", "offline_access" },
            ["idTokenAlgorithms"] = idTokenAlgorithms ?? new[] { "RS256" },
            ["clockSkewSeconds"] = 120,
            ["transactionLifetimeSeconds"] = 600
        });

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
