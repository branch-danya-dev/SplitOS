using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost;
using SplitOS.RuntimeHost.Authentication;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class NativeAuthTokenExchangeTests
{
    private static readonly Uri Issuer = new("https://auth.example.test/");
    private static readonly Uri Discovery = new("https://auth.example.test/.well-known/openid-configuration");
    private static readonly Uri Authorization = new("https://auth.example.test/authorize");
    private static readonly Uri Token = new("https://auth.example.test/token");
    private static readonly Uri Jwks = new("https://auth.example.test/jwks");
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 8, 0, 0, TimeSpan.Zero);
    private static readonly string[] AllowedAlgorithms = ["RS256"];
    private static readonly string[] ResponseTypes = ["code"];
    private static readonly string[] PkceMethods = ["S256"];
    private static readonly string[] TokenAuthMethods = ["none"];

    [TestMethod]
    public async Task ValidExchangeReturnsValidatedSubjectAndRedactsReusableValues()
    {
        using var rsa = RSA.Create(2048);
        var exchange = CreateExchange(out var transaction);
        var jwt = CreateJwt(
            rsa,
            transaction.Nonce,
            Issuer.AbsoluteUri,
            "splitos-windows-native-v1",
            Now.AddMinutes(5));
        using var handler = new FixtureHandler(rsa, jwt);
        using var client = new HttpClient(handler);
        var service = new NativeAuthTokenExchangeService(client, CreateTrust(), new FixedTimeProvider(Now));

        var result = await service.ExchangeAsync(exchange);

        Assert.AreEqual(NativeAuthTokenExchangeDisposition.Accepted, result.Disposition);
        Assert.AreEqual("AUTH_IDENTITY_VALIDATED", result.ProductCode);
        Assert.IsNotNull(result.Session);
        Assert.AreEqual("acc_test_01", result.Session.Subject);
        Assert.AreEqual("ACCESS_FIXTURE", result.Session.AccessToken);
        Assert.AreEqual("REFRESH_FIXTURE", result.Session.RefreshToken);
        Assert.AreEqual(Now.AddMinutes(15), result.Session.AccessTokenExpiresUtc);
        Assert.IsFalse(result.Session.ToString().Contains("ACCESS_FIXTURE", StringComparison.Ordinal));
        Assert.IsFalse(result.Session.ToString().Contains("REFRESH_FIXTURE", StringComparison.Ordinal));
        Assert.IsNotNull(handler.TokenRequestBody);
        Assert.IsTrue(handler.TokenRequestBody.Contains("grant_type=authorization_code", StringComparison.Ordinal));
        Assert.IsTrue(handler.TokenRequestBody.Contains("code=CODE_FIXTURE", StringComparison.Ordinal));
        Assert.IsTrue(handler.TokenRequestBody.Contains("client_id=splitos-windows-native-v1", StringComparison.Ordinal));
        Assert.IsTrue(handler.TokenRequestBody.Contains("code_verifier=", StringComparison.Ordinal));
        Assert.IsFalse(handler.TokenRequestBody.Contains("client_secret", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(handler.TokenRequestHadAuthorizationHeader);
    }

    [TestMethod]
    public async Task NonceMismatchRejectsIdentity()
    {
        using var rsa = RSA.Create(2048);
        var exchange = CreateExchange(out _);
        var jwt = CreateJwt(
            rsa,
            "nonce-from-another-transaction",
            Issuer.AbsoluteUri,
            "splitos-windows-native-v1",
            Now.AddMinutes(5));

        var result = await ExchangeWithFixtureAsync(exchange, rsa, jwt);

        AssertIdentityRejected(result);
    }

    [TestMethod]
    public async Task WrongIssuerRejectsSignedIdentity()
    {
        using var rsa = RSA.Create(2048);
        var exchange = CreateExchange(out var transaction);
        var jwt = CreateJwt(
            rsa,
            transaction.Nonce,
            "https://attacker.invalid/",
            "splitos-windows-native-v1",
            Now.AddMinutes(5));

        var result = await ExchangeWithFixtureAsync(exchange, rsa, jwt);

        AssertIdentityRejected(result);
    }

    [TestMethod]
    public async Task WrongAudienceRejectsSignedIdentity()
    {
        using var rsa = RSA.Create(2048);
        var exchange = CreateExchange(out var transaction);
        var jwt = CreateJwt(
            rsa,
            transaction.Nonce,
            Issuer.AbsoluteUri,
            "another-native-client",
            Now.AddMinutes(5));

        var result = await ExchangeWithFixtureAsync(exchange, rsa, jwt);

        AssertIdentityRejected(result);
    }

    [TestMethod]
    public async Task ExpiredIdTokenRejectsIdentity()
    {
        using var rsa = RSA.Create(2048);
        var exchange = CreateExchange(out var transaction);
        var jwt = CreateJwt(
            rsa,
            transaction.Nonce,
            Issuer.AbsoluteUri,
            "splitos-windows-native-v1",
            Now.AddMinutes(-2));

        var result = await ExchangeWithFixtureAsync(exchange, rsa, jwt);

        AssertIdentityRejected(result);
    }

    [TestMethod]
    public async Task InvalidSignatureRejectsIdentity()
    {
        using var signingKey = RSA.Create(2048);
        using var publishedKey = RSA.Create(2048);
        var exchange = CreateExchange(out var transaction);
        var jwt = CreateJwt(
            signingKey,
            transaction.Nonce,
            Issuer.AbsoluteUri,
            "splitos-windows-native-v1",
            Now.AddMinutes(5));

        var result = await ExchangeWithFixtureAsync(exchange, publishedKey, jwt);

        AssertIdentityRejected(result);
    }

    [TestMethod]
    public async Task UnknownKidRejectsIdentity()
    {
        using var rsa = RSA.Create(2048);
        var exchange = CreateExchange(out var transaction);
        var jwt = CreateJwt(
            rsa,
            transaction.Nonce,
            Issuer.AbsoluteUri,
            "splitos-windows-native-v1",
            Now.AddMinutes(5),
            keyId: "rotated-but-unpublished-key");

        var result = await ExchangeWithFixtureAsync(exchange, rsa, jwt);

        AssertIdentityRejected(result);
    }

    [TestMethod]
    public async Task InvalidGrantMapsToCodeInvalid()
    {
        using var rsa = RSA.Create(2048);
        var exchange = CreateExchange(out _);
        using var handler = new FixtureHandler(rsa, null, invalidGrant: true);
        using var client = new HttpClient(handler);
        var service = new NativeAuthTokenExchangeService(client, CreateTrust(), new FixedTimeProvider(Now));

        var result = await service.ExchangeAsync(exchange);

        Assert.AreEqual(NativeAuthTokenExchangeDisposition.CodeRejected, result.Disposition);
        Assert.AreEqual("AUTH_CODE_INVALID", result.ProductCode);
        Assert.IsNull(result.Session);
    }

    [TestMethod]
    public void HttpIssuerIsRejectedByReleaseTrustConfiguration()
    {
        var trust = CreateTrust() with { Issuer = new Uri("http://auth.example.test/") };
        Assert.ThrowsExactly<ArgumentException>(() => trust.Validate());
    }

    [TestMethod]
    public void UnsupportedIdTokenAlgorithmIsRejectedBeforeNetworkUse()
    {
        var trust = CreateTrust() with { AllowedIdTokenAlgorithms = ["HS256"] };
        Assert.ThrowsExactly<ArgumentException>(() => trust.Validate());
    }

    private static async Task<NativeAuthTokenExchangeResult> ExchangeWithFixtureAsync(
        NativeAuthCodeExchangeContext exchange,
        RSA publishedKey,
        string jwt)
    {
        using var handler = new FixtureHandler(publishedKey, jwt);
        using var client = new HttpClient(handler);
        var service = new NativeAuthTokenExchangeService(client, CreateTrust(), new FixedTimeProvider(Now));
        return await service.ExchangeAsync(exchange);
    }

    private static void AssertIdentityRejected(NativeAuthTokenExchangeResult result)
    {
        Assert.AreEqual(NativeAuthTokenExchangeDisposition.IdentityRejected, result.Disposition);
        Assert.AreEqual("AUTH_RESULT_REJECTED", result.ProductCode);
        Assert.IsNull(result.Session);
    }

    private static NativeAuthCodeExchangeContext CreateExchange(out NativeAuthTransaction transaction)
    {
        var manager = new NativeAuthTransactionManager(
            new NativeAuthClientOptions(
                Authorization,
                "splitos-windows-native-v1",
                ["openid"],
                TimeSpan.FromMinutes(10)),
            new FakeWindowsUserContext(),
            new FixedTimeProvider(Now));
        transaction = manager.Start(new Uri("http://127.0.0.1:49170/oauth/callback")).Transaction;
        var callback = manager.ConsumeCallback(new Uri(
            $"http://127.0.0.1:49170/oauth/callback?code=CODE_FIXTURE&state={Uri.EscapeDataString(transaction.State)}"));
        Assert.IsNotNull(callback.ExchangeContext);
        return callback.ExchangeContext;
    }

    private static NativeAuthTrustConfiguration CreateTrust()
        => new(
            Issuer,
            Discovery,
            Authorization,
            "splitos-windows-native-v1",
            AllowedAlgorithms,
            TimeSpan.FromMinutes(1));

    private static string CreateJwt(
        RSA rsa,
        string nonce,
        string issuer,
        string audience,
        DateTimeOffset expiresUtc,
        string keyId = "test-key")
    {
        var header = Encode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            alg = "RS256",
            kid = keyId,
            typ = "JWT"
        }));
        var payload = Encode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = issuer,
            sub = "acc_test_01",
            aud = audience,
            exp = expiresUtc.ToUnixTimeSeconds(),
            nbf = Now.AddMinutes(-5).ToUnixTimeSeconds(),
            iat = Now.AddMinutes(-1).ToUnixTimeSeconds(),
            nonce
        }));
        var input = Encoding.ASCII.GetBytes($"{header}.{payload}");
        try
        {
            var signature = rsa.SignData(input, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            try
            {
                return $"{header}.{payload}.{Encode(signature)}";
            }
            finally
            {
                CryptographicOperations.ZeroMemory(signature);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }

    private static string Encode(ReadOnlySpan<byte> bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class FixtureHandler(
        RSA publishedKey,
        string? idToken,
        bool invalidGrant = false) : HttpMessageHandler
    {
        public string? TokenRequestBody { get; private set; }
        public bool TokenRequestHadAuthorizationHeader { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get && request.RequestUri == Discovery)
            {
                return Json(HttpStatusCode.OK, new
                {
                    issuer = Issuer.AbsoluteUri,
                    authorization_endpoint = Authorization.AbsoluteUri,
                    token_endpoint = Token.AbsoluteUri,
                    jwks_uri = Jwks.AbsoluteUri,
                    response_types_supported = ResponseTypes,
                    code_challenge_methods_supported = PkceMethods,
                    token_endpoint_auth_methods_supported = TokenAuthMethods
                });
            }

            if (request.Method == HttpMethod.Post && request.RequestUri == Token)
            {
                TokenRequestBody = request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync(cancellationToken);
                TokenRequestHadAuthorizationHeader = request.Headers.Authorization is not null;
                if (invalidGrant)
                {
                    return Json(HttpStatusCode.BadRequest, new { error = "invalid_grant" });
                }

                return Json(HttpStatusCode.OK, new
                {
                    access_token = "ACCESS_FIXTURE",
                    token_type = "Bearer",
                    expires_in = 900,
                    refresh_token = "REFRESH_FIXTURE",
                    scope = "openid",
                    id_token = idToken
                });
            }

            if (request.Method == HttpMethod.Get && request.RequestUri == Jwks)
            {
                var parameters = publishedKey.ExportParameters(false);
                return Json(HttpStatusCode.OK, new
                {
                    keys = new[]
                    {
                        new
                        {
                            kty = "RSA",
                            kid = "test-key",
                            use = "sig",
                            alg = "RS256",
                            n = Encode(parameters.Modulus!),
                            e = Encode(parameters.Exponent!)
                        }
                    }
                });
            }

            return Json(HttpStatusCode.NotFound, new { error = "not_found" });
        }

        private static HttpResponseMessage Json(HttpStatusCode status, object value)
            => new(status)
            {
                Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
            };
    }

    private sealed class FakeWindowsUserContext : IWindowsUserContext
    {
        public string GetCurrentUserSid() => "S-1-5-21-token-test";
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
