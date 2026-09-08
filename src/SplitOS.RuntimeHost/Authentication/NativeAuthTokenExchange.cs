using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace SplitOS.RuntimeHost.Authentication;

public enum NativeAuthTokenExchangeDisposition
{
    Accepted,
    BackendUnavailable,
    CodeRejected,
    TokenResponseRejected,
    IdentityRejected
}

public sealed record ValidatedNativeAuthSession(
    string Subject,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresUtc,
    string? RefreshToken,
    string? GrantedScope)
{
    public override string ToString()
        => $"ValidatedNativeAuthSession {{ Subject = {Subject}, AccessToken = <redacted>, AccessTokenExpiresUtc = {AccessTokenExpiresUtc:O}, RefreshToken = {(RefreshToken is null ? "<none>" : "<redacted>")}, GrantedScope = {GrantedScope ?? "<none>"} }}";
}

public sealed record NativeAuthTokenExchangeResult(
    NativeAuthTokenExchangeDisposition Disposition,
    string ProductCode,
    ValidatedNativeAuthSession? Session);

public sealed class NativeAuthTokenExchangeService
{
    private const int MaximumDiscoveryBytes = 64 * 1024;
    private const int MaximumTokenResponseBytes = 64 * 1024;
    private const int MaximumJwksBytes = 256 * 1024;
    private const int MaximumIdTokenCharacters = 32 * 1024;
    private const int MaximumBearerTokenCharacters = 32 * 1024;

    private readonly HttpClient _httpClient;
    private readonly NativeAuthTrustConfiguration _trust;
    private readonly TimeProvider _timeProvider;

    public NativeAuthTokenExchangeService(
        HttpClient httpClient,
        NativeAuthTrustConfiguration trust,
        TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _trust = trust ?? throw new ArgumentNullException(nameof(trust));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _trust.Validate();
    }

    public async Task<NativeAuthTokenExchangeResult> ExchangeAsync(
        NativeAuthCodeExchangeContext exchangeContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exchangeContext);
        if (!string.Equals(exchangeContext.ClientId, _trust.ClientId, StringComparison.Ordinal))
        {
            return Reject(NativeAuthTokenExchangeDisposition.IdentityRejected, "AUTH_RESULT_REJECTED");
        }

        OidcDiscoveryMetadata metadata;
        try
        {
            metadata = await FetchDiscoveryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsBackendFailure(exception))
        {
            return Reject(NativeAuthTokenExchangeDisposition.BackendUnavailable, "AUTH_BACKEND_UNAVAILABLE");
        }
        catch (InvalidDataException)
        {
            return Reject(NativeAuthTokenExchangeDisposition.IdentityRejected, "AUTH_RESULT_REJECTED");
        }

        TokenEndpointResult tokenResult;
        try
        {
            tokenResult = await ExchangeCodeAsync(metadata.TokenEndpoint, exchangeContext, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsBackendFailure(exception))
        {
            return Reject(NativeAuthTokenExchangeDisposition.BackendUnavailable, "AUTH_BACKEND_UNAVAILABLE");
        }
        catch (InvalidDataException)
        {
            return Reject(NativeAuthTokenExchangeDisposition.TokenResponseRejected, "AUTH_RESULT_REJECTED");
        }

        if (tokenResult.Disposition == TokenEndpointDisposition.InvalidGrant)
        {
            return Reject(NativeAuthTokenExchangeDisposition.CodeRejected, "AUTH_CODE_INVALID");
        }

        if (tokenResult.Disposition == TokenEndpointDisposition.BackendUnavailable)
        {
            return Reject(NativeAuthTokenExchangeDisposition.BackendUnavailable, "AUTH_BACKEND_UNAVAILABLE");
        }

        if (tokenResult.Disposition != TokenEndpointDisposition.Accepted || tokenResult.TokenSet is null)
        {
            return Reject(NativeAuthTokenExchangeDisposition.TokenResponseRejected, "AUTH_RESULT_REJECTED");
        }

        IReadOnlyList<OidcJsonWebKey> jwks;
        try
        {
            jwks = await FetchJwksAsync(metadata.JwksUri, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsBackendFailure(exception))
        {
            return Reject(NativeAuthTokenExchangeDisposition.BackendUnavailable, "AUTH_BACKEND_UNAVAILABLE");
        }
        catch (InvalidDataException)
        {
            return Reject(NativeAuthTokenExchangeDisposition.IdentityRejected, "AUTH_RESULT_REJECTED");
        }

        string subject;
        try
        {
            subject = await ValidateIdTokenAsync(
                tokenResult.TokenSet.IdToken,
                exchangeContext.Nonce,
                jwks).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            return Reject(NativeAuthTokenExchangeDisposition.IdentityRejected, "AUTH_RESULT_REJECTED");
        }

        DateTimeOffset accessTokenExpiresUtc;
        try
        {
            accessTokenExpiresUtc = _timeProvider.GetUtcNow().AddSeconds(tokenResult.TokenSet.ExpiresInSeconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return Reject(NativeAuthTokenExchangeDisposition.TokenResponseRejected, "AUTH_RESULT_REJECTED");
        }

        var session = new ValidatedNativeAuthSession(
            subject,
            tokenResult.TokenSet.AccessToken,
            accessTokenExpiresUtc,
            tokenResult.TokenSet.RefreshToken,
            tokenResult.TokenSet.Scope);

        return new NativeAuthTokenExchangeResult(
            NativeAuthTokenExchangeDisposition.Accepted,
            "AUTH_IDENTITY_VALIDATED",
            session);
    }

    private async Task<OidcDiscoveryMetadata> FetchDiscoveryAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _trust.DiscoveryEndpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                "OIDC discovery endpoint did not return success.",
                null,
                response.StatusCode);
        }

        var root = await ReadBoundedJsonAsync(response, MaximumDiscoveryBytes, cancellationToken).ConfigureAwait(false);
        RequireObject(root, "OIDC discovery document");
        EnsureNoDuplicateProperties(root, "OIDC discovery document");

        var issuer = GetRequiredString(root, "issuer");
        var authorizationEndpoint = GetRequiredHttpsUri(root, "authorization_endpoint");
        var tokenEndpoint = GetRequiredHttpsUri(root, "token_endpoint");
        var jwksUri = GetRequiredHttpsUri(root, "jwks_uri");

        if (!string.Equals(issuer, _trust.Issuer.AbsoluteUri, StringComparison.Ordinal) ||
            !UriEquals(authorizationEndpoint, _trust.AuthorizationEndpoint))
        {
            throw new InvalidDataException("OIDC discovery metadata does not match release-owned trust configuration.");
        }

        var responseTypes = GetOptionalStringArray(root, "response_types_supported");
        if (responseTypes is null || !responseTypes.Contains("code", StringComparer.Ordinal))
        {
            throw new InvalidDataException("OIDC provider does not advertise authorization-code response support.");
        }

        var pkceMethods = GetOptionalStringArray(root, "code_challenge_methods_supported");
        if (pkceMethods is null || !pkceMethods.Contains("S256", StringComparer.Ordinal))
        {
            throw new InvalidDataException("OIDC provider does not advertise PKCE S256 support.");
        }

        var tokenAuthMethods = GetOptionalStringArray(root, "token_endpoint_auth_methods_supported");
        if (tokenAuthMethods is not null && !tokenAuthMethods.Contains("none", StringComparer.Ordinal))
        {
            throw new InvalidDataException("OIDC provider does not advertise public-client token endpoint authentication.");
        }

        return new OidcDiscoveryMetadata(tokenEndpoint, jwksUri);
    }

    private async Task<TokenEndpointResult> ExchangeCodeAsync(
        Uri tokenEndpoint,
        NativeAuthCodeExchangeContext exchangeContext,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "authorization_code",
                ["code"] = exchangeContext.AuthorizationCode,
                ["redirect_uri"] = exchangeContext.RedirectUri.AbsoluteUri,
                ["client_id"] = exchangeContext.ClientId,
                ["code_verifier"] = exchangeContext.CodeVerifier
            })
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return new TokenEndpointResult(TokenEndpointDisposition.BackendUnavailable, null);
            }

            var errorRoot = await TryReadBoundedJsonAsync(response, MaximumTokenResponseBytes, cancellationToken).ConfigureAwait(false);
            if (errorRoot is { ValueKind: JsonValueKind.Object } &&
                errorRoot.Value.TryGetProperty("error", out var error) &&
                error.ValueKind == JsonValueKind.String &&
                string.Equals(error.GetString(), "invalid_grant", StringComparison.Ordinal))
            {
                return new TokenEndpointResult(TokenEndpointDisposition.InvalidGrant, null);
            }

            return new TokenEndpointResult(TokenEndpointDisposition.Rejected, null);
        }

        var root = await ReadBoundedJsonAsync(response, MaximumTokenResponseBytes, cancellationToken).ConfigureAwait(false);
        RequireObject(root, "OAuth token response");
        EnsureNoDuplicateProperties(root, "OAuth token response");

        var accessToken = GetRequiredString(root, "access_token");
        var tokenType = GetRequiredString(root, "token_type");
        var idToken = GetRequiredString(root, "id_token");
        if (!string.Equals(tokenType, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("OAuth token response used an unsupported token type.");
        }

        EnsureBoundedToken(accessToken, "access_token");
        EnsureBoundedToken(idToken, "id_token");
        if (idToken.Length > MaximumIdTokenCharacters)
        {
            throw new InvalidDataException("OIDC ID token exceeded the allowed size.");
        }

        if (!root.TryGetProperty("expires_in", out var expiresInElement) ||
            expiresInElement.ValueKind != JsonValueKind.Number ||
            !expiresInElement.TryGetInt64(out var expiresInSeconds) ||
            expiresInSeconds <= 0)
        {
            throw new InvalidDataException("OAuth token response omitted a valid positive expires_in value.");
        }

        var refreshToken = GetOptionalString(root, "refresh_token");
        if (refreshToken is not null)
        {
            EnsureBoundedToken(refreshToken, "refresh_token");
        }

        var scope = GetOptionalString(root, "scope");
        return new TokenEndpointResult(
            TokenEndpointDisposition.Accepted,
            new OAuthTokenSet(accessToken, idToken, refreshToken, scope, expiresInSeconds));
    }

    private async Task<IReadOnlyList<OidcJsonWebKey>> FetchJwksAsync(Uri jwksUri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, jwksUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException("OIDC JWKS endpoint did not return success.", null, response.StatusCode);
        }

        var root = await ReadBoundedJsonAsync(response, MaximumJwksBytes, cancellationToken).ConfigureAwait(false);
        RequireObject(root, "OIDC JWKS document");
        EnsureNoDuplicateProperties(root, "OIDC JWKS document");
        if (!root.TryGetProperty("keys", out var keysElement) || keysElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("OIDC JWKS document does not contain a keys array.");
        }

        var keys = new List<OidcJsonWebKey>();
        foreach (var key in keysElement.EnumerateArray())
        {
            if (key.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("OIDC JWKS key entry is malformed.");
            }

            EnsureNoDuplicateProperties(key, "OIDC JWKS key");
            keys.Add(new OidcJsonWebKey(
                GetRequiredString(key, "kty"),
                GetRequiredString(key, "kid"),
                GetOptionalString(key, "use"),
                GetOptionalString(key, "alg"),
                GetOptionalString(key, "n"),
                GetOptionalString(key, "e")));
        }

        if (keys.Count == 0)
        {
            throw new InvalidDataException("OIDC JWKS document contains no keys.");
        }

        return keys;
    }

    private async Task<string> ValidateIdTokenAsync(
        string idToken,
        string expectedNonce,
        IReadOnlyList<OidcJsonWebKey> jwks)
    {
        var signingKeys = CreateSigningKeys(jwks);
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _trust.Issuer.AbsoluteUri,
            ValidateAudience = true,
            ValidAudience = _trust.ClientId,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = signingKeys,
            TryAllIssuerSigningKeys = false,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ClockSkew = _trust.ClockSkew,
            ValidAlgorithms = _trust.AllowedIdTokenAlgorithms.ToArray(),
            LifetimeValidator = ValidateLifetime
        };

        var handler = new JsonWebTokenHandler
        {
            MaximumTokenSizeInBytes = MaximumIdTokenCharacters
        };

        TokenValidationResult validation;
        try
        {
            validation = await handler.ValidateTokenAsync(idToken, validationParameters).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is ArgumentException or SecurityTokenException or FormatException or CryptographicException)
        {
            throw new InvalidDataException("OIDC ID token could not be parsed or validated.", exception);
        }

        if (!validation.IsValid || validation.SecurityToken is not JsonWebToken token)
        {
            throw new InvalidDataException("OIDC ID token validation failed.", validation.Exception);
        }

        var audiences = token.Audiences.ToArray();
        if ((audiences.Length > 1 || !string.IsNullOrWhiteSpace(token.Azp)) &&
            !string.Equals(token.Azp, _trust.ClientId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("OIDC ID token authorized party does not match the native client ID.");
        }

        if (!token.TryGetClaim("nonce", out Claim? nonceClaim) ||
            string.IsNullOrWhiteSpace(nonceClaim.Value) ||
            !FixedTimeEquals(expectedNonce, nonceClaim.Value))
        {
            throw new InvalidDataException("OIDC ID token nonce validation failed.");
        }

        var subject = token.Subject;
        if (string.IsNullOrWhiteSpace(subject) ||
            subject.Length > 512 ||
            subject.Any(static character => char.IsControl(character)))
        {
            throw new InvalidDataException("OIDC subject is not a valid stable account reference.");
        }

        return subject;
    }

    private List<SecurityKey> CreateSigningKeys(IReadOnlyList<OidcJsonWebKey> jwks)
    {
        var keyIds = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<SecurityKey>();

        foreach (var jwk in jwks)
        {
            if (!string.Equals(jwk.KeyType, "RSA", StringComparison.Ordinal) ||
                (jwk.Use is not null && !string.Equals(jwk.Use, "sig", StringComparison.Ordinal)) ||
                (jwk.Algorithm is not null && !_trust.AllowedIdTokenAlgorithms.Contains(jwk.Algorithm, StringComparer.Ordinal)))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(jwk.Modulus) ||
                string.IsNullOrWhiteSpace(jwk.Exponent) ||
                !keyIds.Add(jwk.KeyId))
            {
                throw new InvalidDataException("OIDC JWKS contains an incomplete or ambiguous signing key.");
            }

            var key = new JsonWebKey
            {
                Kty = jwk.KeyType,
                Kid = jwk.KeyId,
                N = jwk.Modulus,
                E = jwk.Exponent
            };
            if (jwk.Use is not null)
            {
                key.Use = jwk.Use;
            }

            if (jwk.Algorithm is not null)
            {
                key.Alg = jwk.Algorithm;
            }

            result.Add(key);
        }

        if (result.Count == 0)
        {
            throw new InvalidDataException("OIDC JWKS contains no release-supported signing keys.");
        }

        return result;
    }

    private bool ValidateLifetime(
        DateTime? notBefore,
        DateTime? expires,
        SecurityToken _,
        TokenValidationParameters __)
    {
        if (expires is null)
        {
            return false;
        }

        var now = _timeProvider.GetUtcNow();
        var skew = _trust.ClockSkew;
        var expiration = new DateTimeOffset(DateTime.SpecifyKind(expires.Value, DateTimeKind.Utc));
        if (now > expiration.Add(skew))
        {
            return false;
        }

        if (notBefore is not null)
        {
            var earliest = new DateTimeOffset(DateTime.SpecifyKind(notBefore.Value, DateTimeKind.Utc));
            if (now.Add(skew) < earliest)
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<JsonElement> ReadBoundedJsonAsync(
        HttpResponseMessage response,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        var declaredLength = response.Content.Headers.ContentLength;
        if (declaredLength > maximumBytes)
        {
            throw new InvalidDataException("HTTP JSON response exceeded the allowed size.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var memory = new MemoryStream(declaredLength is > 0 and <= int.MaxValue ? (int)declaredLength.Value : 4096);
        var rented = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(rented.AsMemory(0, rented.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                if (memory.Length + read > maximumBytes)
                {
                    throw new InvalidDataException("HTTP JSON response exceeded the allowed size.");
                }

                memory.Write(rented, 0, read);
            }

            if (!memory.TryGetBuffer(out var segment) || segment.Array is null)
            {
                throw new InvalidDataException("HTTP JSON response buffer could not be inspected safely.");
            }

            using var document = JsonDocument.Parse(
                new ReadOnlyMemory<byte>(segment.Array, segment.Offset, checked((int)memory.Length)),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 32
                });
            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("HTTP JSON response was malformed.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rented);
            ArrayPool<byte>.Shared.Return(rented);
            if (memory.TryGetBuffer(out var buffer) && buffer.Array is not null)
            {
                CryptographicOperations.ZeroMemory(buffer.Array.AsSpan(buffer.Offset, checked((int)memory.Length)));
            }
        }
    }

    private static async Task<JsonElement?> TryReadBoundedJsonAsync(
        HttpResponseMessage response,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ReadBoundedJsonAsync(response, maximumBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static void RequireObject(JsonElement value, string description)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{description} must be a JSON object.");
        }
    }

    private static void EnsureNoDuplicateProperties(JsonElement value, string description)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                throw new InvalidDataException($"{description} contains duplicate property names.");
            }
        }
    }

    private static string GetRequiredString(JsonElement root, string propertyName)
    {
        var value = GetOptionalString(root, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"Required string '{propertyName}' is missing or empty.");
        }

        return value;
    }

    private static string? GetOptionalString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"String '{propertyName}' is malformed.");
        }

        return value.GetString();
    }

    private static List<string>? GetOptionalStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"Array '{propertyName}' is malformed.");
        }

        var result = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                throw new InvalidDataException($"Array '{propertyName}' contains a malformed value.");
            }

            result.Add(item.GetString()!);
        }

        return result;
    }

    private static Uri GetRequiredHttpsUri(JsonElement root, string propertyName)
    {
        var raw = GetRequiredString(root, propertyName);
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidDataException($"OIDC endpoint '{propertyName}' is not a trusted HTTPS URI shape.");
        }

        return uri;
    }

    private static bool UriEquals(Uri left, Uri right)
        => string.Equals(left.AbsoluteUri, right.AbsoluteUri, StringComparison.Ordinal);

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);
        var actualBytes = System.Text.Encoding.UTF8.GetBytes(actual);
        try
        {
            return expectedBytes.Length == actualBytes.Length &&
                   CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expectedBytes);
            CryptographicOperations.ZeroMemory(actualBytes);
        }
    }

    private static void EnsureBoundedToken(string token, string fieldName)
    {
        if (token.Length > MaximumBearerTokenCharacters ||
            token.Any(static character => char.IsControl(character)))
        {
            throw new InvalidDataException($"OAuth token field '{fieldName}' is outside the accepted bounds.");
        }
    }

    private static bool IsBackendFailure(Exception exception)
        => exception is HttpRequestException or IOException or TimeoutException or OperationCanceledException;

    private static NativeAuthTokenExchangeResult Reject(
        NativeAuthTokenExchangeDisposition disposition,
        string productCode)
        => new(disposition, productCode, null);

    private sealed record OidcDiscoveryMetadata(Uri TokenEndpoint, Uri JwksUri);

    private sealed record OidcJsonWebKey(
        string KeyType,
        string KeyId,
        string? Use,
        string? Algorithm,
        string? Modulus,
        string? Exponent);

    private enum TokenEndpointDisposition
    {
        Accepted,
        InvalidGrant,
        Rejected,
        BackendUnavailable
    }

    private sealed record OAuthTokenSet(
        string AccessToken,
        string IdToken,
        string? RefreshToken,
        string? Scope,
        long ExpiresInSeconds);

    private sealed record TokenEndpointResult(
        TokenEndpointDisposition Disposition,
        OAuthTokenSet? TokenSet);
}