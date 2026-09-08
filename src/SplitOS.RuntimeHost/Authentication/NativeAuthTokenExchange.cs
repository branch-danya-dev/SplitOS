using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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

        IReadOnlyList<OidcJsonWebKey> keys;
        try
        {
            keys = await FetchJwksAsync(metadata.JwksUri, cancellationToken).ConfigureAwait(false);
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
            subject = ValidateIdToken(tokenResult.TokenSet.IdToken, exchangeContext.Nonce, keys);
        }
        catch (InvalidDataException)
        {
            return Reject(NativeAuthTokenExchangeDisposition.IdentityRejected, "AUTH_RESULT_REJECTED");
        }

        var session = new ValidatedNativeAuthSession(
            subject,
            tokenResult.TokenSet.AccessToken,
            _timeProvider.GetUtcNow().AddSeconds(tokenResult.TokenSet.ExpiresInSeconds),
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

        using var document = await ReadBoundedJsonAsync(response, MaximumDiscoveryBytes, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
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

            using var errorDocument = await TryReadBoundedJsonAsync(response, MaximumTokenResponseBytes, cancellationToken).ConfigureAwait(false);
            if (errorDocument is not null &&
                errorDocument.RootElement.ValueKind == JsonValueKind.Object &&
                errorDocument.RootElement.TryGetProperty("error", out var error) &&
                error.ValueKind == JsonValueKind.String &&
                string.Equals(error.GetString(), "invalid_grant", StringComparison.Ordinal))
            {
                return new TokenEndpointResult(TokenEndpointDisposition.InvalidGrant, null);
            }

            return new TokenEndpointResult(TokenEndpointDisposition.Rejected, null);
        }

        using var document = await ReadBoundedJsonAsync(response, MaximumTokenResponseBytes, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        RequireObject(root, "OAuth token response");
        EnsureNoDuplicateProperties(root, "OAuth token response");

        var accessToken = GetRequiredString(root, "access_token");
        var tokenType = GetRequiredString(root, "token_type");
        var idToken = GetRequiredString(root, "id_token");
        if (!string.Equals(tokenType, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("OAuth token response used an unsupported token type.");
        }

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

        using var document = await ReadBoundedJsonAsync(response, MaximumJwksBytes, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
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
            var keyType = GetRequiredString(key, "kty");
            var keyId = GetRequiredString(key, "kid");
            var use = GetOptionalString(key, "use");
            var algorithm = GetOptionalString(key, "alg");
            var modulus = GetOptionalString(key, "n");
            var exponent = GetOptionalString(key, "e");
            keys.Add(new OidcJsonWebKey(keyType, keyId, use, algorithm, modulus, exponent));
        }

        if (keys.Count == 0)
        {
            throw new InvalidDataException("OIDC JWKS document contains no keys.");
        }

        return keys;
    }

    private string ValidateIdToken(
        string idToken,
        string expectedNonce,
        IReadOnlyList<OidcJsonWebKey> keys)
    {
        var segments = idToken.Split('.');
        if (segments.Length != 3 || segments.Any(static segment => segment.Length == 0))
        {
            throw new InvalidDataException("OIDC ID token is not a compact JWS.");
        }

        byte[] headerBytes;
        byte[] payloadBytes;
        byte[] signatureBytes;
        try
        {
            headerBytes = Base64UrlDecode(segments[0]);
            payloadBytes = Base64UrlDecode(segments[1]);
            signatureBytes = Base64UrlDecode(segments[2]);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("OIDC ID token contains invalid base64url data.", exception);
        }

        try
        {
            using var headerDocument = JsonDocument.Parse(headerBytes);
            using var payloadDocument = JsonDocument.Parse(payloadBytes);
            var header = headerDocument.RootElement;
            var payload = payloadDocument.RootElement;
            RequireObject(header, "OIDC ID-token header");
            RequireObject(payload, "OIDC ID-token payload");
            EnsureNoDuplicateProperties(header, "OIDC ID-token header");
            EnsureNoDuplicateProperties(payload, "OIDC ID-token payload");

            var algorithm = GetRequiredString(header, "alg");
            var keyId = GetRequiredString(header, "kid");
            if (!_trust.AllowedIdTokenAlgorithms.Contains(algorithm, StringComparer.Ordinal))
            {
                throw new InvalidDataException("OIDC ID token uses a non-allowlisted signature algorithm.");
            }

            if (!string.Equals(algorithm, "RS256", StringComparison.Ordinal))
            {
                throw new InvalidDataException("OIDC ID token signature algorithm is not implemented by this release.");
            }

            var key = keys.SingleOrDefault(candidate =>
                string.Equals(candidate.KeyId, keyId, StringComparison.Ordinal) &&
                string.Equals(candidate.KeyType, "RSA", StringComparison.Ordinal) &&
                (candidate.Use is null || string.Equals(candidate.Use, "sig", StringComparison.Ordinal)) &&
                (candidate.Algorithm is null || string.Equals(candidate.Algorithm, algorithm, StringComparison.Ordinal)));
            if (key is null || string.IsNullOrWhiteSpace(key.Modulus) || string.IsNullOrWhiteSpace(key.Exponent))
            {
                throw new InvalidDataException("OIDC ID token signing key is unavailable or ambiguous.");
            }

            using var rsa = RSA.Create();
            rsa.ImportParameters(new RSAParameters
            {
                Modulus = Base64UrlDecode(key.Modulus),
                Exponent = Base64UrlDecode(key.Exponent)
            });

            var signingInput = Encoding.ASCII.GetBytes($"{segments[0]}.{segments[1]}");
            try
            {
                if (!rsa.VerifyData(signingInput, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                {
                    throw new InvalidDataException("OIDC ID token signature validation failed.");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(signingInput);
            }

            var issuer = GetRequiredString(payload, "iss");
            if (!string.Equals(issuer, _trust.Issuer.AbsoluteUri, StringComparison.Ordinal))
            {
                throw new InvalidDataException("OIDC ID token issuer does not match release trust.");
            }

            ValidateAudience(payload);
            ValidateLifetime(payload);

            var nonce = GetRequiredString(payload, "nonce");
            if (!FixedTimeEquals(expectedNonce, nonce))
            {
                throw new InvalidDataException("OIDC ID token nonce validation failed.");
            }

            var subject = GetRequiredString(payload, "sub");
            if (subject.Length > 512 || subject.Any(static character => char.IsControl(character)))
            {
                throw new InvalidDataException("OIDC subject is not a valid stable account reference.");
            }

            return subject;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("OIDC ID token JSON is malformed.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(headerBytes);
            CryptographicOperations.ZeroMemory(payloadBytes);
            CryptographicOperations.ZeroMemory(signatureBytes);
        }
    }

    private void ValidateAudience(JsonElement payload)
    {
        if (!payload.TryGetProperty("aud", out var audience))
        {
            throw new InvalidDataException("OIDC ID token audience is missing.");
        }

        List<string> audiences;
        if (audience.ValueKind == JsonValueKind.String)
        {
            audiences = [audience.GetString() ?? string.Empty];
        }
        else if (audience.ValueKind == JsonValueKind.Array)
        {
            audiences = audience.EnumerateArray()
                .Select(static value => value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty)
                .ToList();
            if (audiences.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidDataException("OIDC ID token audience claim is malformed.");
            }
        }
        else
        {
            throw new InvalidDataException("OIDC ID token audience claim is malformed.");
        }

        if (!audiences.Contains(_trust.ClientId, StringComparer.Ordinal))
        {
            throw new InvalidDataException("OIDC ID token audience does not contain the native client ID.");
        }

        var authorizedParty = GetOptionalString(payload, "azp");
        if ((audiences.Count > 1 || authorizedParty is not null) &&
            !string.Equals(authorizedParty, _trust.ClientId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("OIDC ID token authorized party does not match the native client ID.");
        }
    }

    private void ValidateLifetime(JsonElement payload)
    {
        var now = _timeProvider.GetUtcNow();
        var expiration = GetRequiredUnixTime(payload, "exp");
        if (now > expiration.Add(_trust.ClockSkew))
        {
            throw new InvalidDataException("OIDC ID token has expired.");
        }

        if (payload.TryGetProperty("nbf", out var notBeforeElement))
        {
            if (notBeforeElement.ValueKind != JsonValueKind.Number || !notBeforeElement.TryGetInt64(out var notBeforeSeconds))
            {
                throw new InvalidDataException("OIDC ID token nbf claim is malformed.");
            }

            var notBefore = DateTimeOffset.FromUnixTimeSeconds(notBeforeSeconds);
            if (now.Add(_trust.ClockSkew) < notBefore)
            {
                throw new InvalidDataException("OIDC ID token is not yet valid.");
            }
        }
    }

    private static DateTimeOffset GetRequiredUnixTime(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out var seconds))
        {
            throw new InvalidDataException($"Required numeric claim '{propertyName}' is missing or malformed.");
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException($"Required numeric claim '{propertyName}' is outside the supported range.", exception);
        }
    }

    private static async Task<JsonDocument> ReadBoundedJsonAsync(
        HttpResponseMessage response,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        var declaredLength = response.Content.Headers.ContentLength;
        if (declaredLength > maximumBytes)
        {
            throw new InvalidDataException("HTTP JSON response exceeded the allowed size.");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (bytes.Length > maximumBytes)
        {
            CryptographicOperations.ZeroMemory(bytes);
            throw new InvalidDataException("HTTP JSON response exceeded the allowed size.");
        }

        try
        {
            return JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32
            });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static async Task<JsonDocument?> TryReadBoundedJsonAsync(
        HttpResponseMessage response,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ReadBoundedJsonAsync(response, maximumBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
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

    private static IReadOnlyList<string>? GetOptionalStringArray(JsonElement root, string propertyName)
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

    private static byte[] Base64UrlDecode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized += normalized.Length % 4 switch
        {
            0 => string.Empty,
            2 => "==",
            3 => "=",
            _ => throw new FormatException("Invalid base64url length.")
        };
        return Convert.FromBase64String(normalized);
    }

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
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

    private static bool IsBackendFailure(Exception exception)
        => exception is HttpRequestException or IOException or TimeoutException ||
           exception is OperationCanceledException;

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
