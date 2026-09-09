using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.Authentication;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class ProvisionedNativeAuthAuthorityProviderTests
{
    [TestMethod]
    public async Task MissingPackageFailsClosedWithoutAuthority()
    {
        using var root = RSA.Create(2048);
        var path = Path.Combine(Path.GetTempPath(), $"splitos-auth-{Guid.NewGuid():N}.jws");
        var provider = new ProvisionedNativeAuthAuthorityProvider(path, CreateVerifier(root));

        var result = await provider.ReadAsync();

        Assert.AreEqual(NativeAuthAuthorityPackageStatus.Missing, result.Status);
        Assert.AreEqual("AUTH_AUTHORITY_PACKAGE_MISSING", result.ProductCode);
        Assert.IsNull(result.Metadata);
    }

    [TestMethod]
    public async Task ValidProvisionedPackageReturnsOnlyVerifiedAuthority()
    {
        using var root = RSA.Create(2048);
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "native-auth-authority.jws");
            await File.WriteAllTextAsync(path, CreateEnvelope(root), new UTF8Encoding(false));
            var provider = new ProvisionedNativeAuthAuthorityProvider(path, CreateVerifier(root));

            var result = await provider.ReadAsync();

            Assert.IsTrue(result.IsAvailable);
            Assert.AreEqual("AUTH_AUTHORITY_PACKAGE_AVAILABLE", result.ProductCode);
            Assert.AreEqual(7L, result.Metadata!.Version);
            Assert.AreEqual("splitos-native", result.Metadata.Authority.ClientId);
            Assert.AreEqual("https://identity.splitos.test/token", result.Metadata.Authority.TokenEndpoint.AbsoluteUri);
            Assert.AreEqual("https://api.splitos.test/v1/account", result.Metadata.ProductApi.AccountEndpoint.AbsoluteUri);
            Assert.AreEqual(
                "https://api.splitos.test/v1/entitlements/current",
                result.Metadata.ProductApi.CurrentEntitlementEndpoint.AbsoluteUri);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task TamperedProvisionedPackageIsRejected()
    {
        using var root = RSA.Create(2048);
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "native-auth-authority.jws");
            var envelope = CreateEnvelope(root);
            var segments = envelope.Split('.');
            var tamperedPayload = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(Payload(tokenEndpoint: "https://evil.example/token")));
            await File.WriteAllTextAsync(path, $"{segments[0]}.{tamperedPayload}.{segments[2]}", new UTF8Encoding(false));
            var provider = new ProvisionedNativeAuthAuthorityProvider(path, CreateVerifier(root));

            var result = await provider.ReadAsync();

            Assert.AreEqual(NativeAuthAuthorityPackageStatus.Rejected, result.Status);
            Assert.AreEqual("AUTH_AUTHORITY_PACKAGE_REJECTED", result.ProductCode);
            Assert.IsNull(result.Metadata);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task TerminalNewlineIsNotSilentlyAcceptedAsReleaseAuthority()
    {
        using var root = RSA.Create(2048);
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "native-auth-authority.jws");
            await File.WriteAllTextAsync(path, CreateEnvelope(root) + Environment.NewLine, new UTF8Encoding(false));
            var provider = new ProvisionedNativeAuthAuthorityProvider(path, CreateVerifier(root));

            var result = await provider.ReadAsync();

            Assert.AreEqual(NativeAuthAuthorityPackageStatus.Rejected, result.Status);
            Assert.IsNull(result.Metadata);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task OversizedPackageIsRejectedBeforeSignatureVerification()
    {
        using var root = RSA.Create(2048);
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "native-auth-authority.jws");
            await File.WriteAllBytesAsync(path, new byte[(128 * 1024) + 1]);
            var provider = new ProvisionedNativeAuthAuthorityProvider(path, CreateVerifier(root));

            var result = await provider.ReadAsync();

            Assert.AreEqual(NativeAuthAuthorityPackageStatus.Rejected, result.Status);
            Assert.IsNull(result.Metadata);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static SignedNativeAuthAuthorityVerifier CreateVerifier(RSA root)
        => new(new NativeAuthReleaseTrustConfiguration(
            NativeAuthReleaseTrustConfiguration.ProductionTrustDomain,
            new SecurityKey[] { new RsaSecurityKey(root) { KeyId = "root-1" } },
            new[] { SecurityAlgorithms.RsaSha256 },
            1,
            1));

    private static string CreateEnvelope(RSA signingRoot)
    {
        var header = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["alg"] = SecurityAlgorithms.RsaSha256,
            ["kid"] = "root-1"
        });
        var payload = Payload();
        var encodedHeader = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(header));
        var encodedPayload = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(payload));
        var input = Encoding.ASCII.GetBytes($"{encodedHeader}.{encodedPayload}");
        var signature = signingRoot.SignData(input, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{encodedHeader}.{encodedPayload}.{Base64UrlEncoder.Encode(signature)}";
    }

    private static string Payload(string tokenEndpoint = "https://identity.splitos.test/token")
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["role"] = NativeAuthReleaseTrustConfiguration.NativeAuthAuthorityRole,
            ["trustDomain"] = NativeAuthReleaseTrustConfiguration.ProductionTrustDomain,
            ["version"] = 7,
            ["securityEpoch"] = 3,
            ["issuer"] = "https://identity.splitos.test/",
            ["discoveryEndpoint"] = "https://identity.splitos.test/.well-known/openid-configuration",
            ["authorizationEndpoint"] = "https://identity.splitos.test/authorize",
            ["tokenEndpoint"] = tokenEndpoint,
            ["jwksEndpoint"] = "https://identity.splitos.test/.well-known/jwks.json",
            ["clientId"] = "splitos-native",
            ["productApiAuthority"] = "https://api.splitos.test/",
            ["scopes"] = new[] { "openid", "offline_access" },
            ["idTokenAlgorithms"] = new[] { "RS256" },
            ["clockSkewSeconds"] = 120,
            ["transactionLifetimeSeconds"] = 600
        });

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"SplitOS-AuthAuthority-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
