using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace SplitOS.RuntimeHost.Authentication;

public sealed record NativeAuthReleaseTrustConfiguration(
    string TrustDomain,
    IReadOnlyList<SecurityKey> RootKeys,
    IReadOnlyList<string> AllowedAlgorithms,
    long MinimumMetadataVersion,
    long MinimumSecurityEpoch)
{
    public const string ProductionTrustDomain = "splitos-production";
    public const string NativeAuthAuthorityRole = "NATIVE_AUTH_AUTHORITY";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(TrustDomain) || TrustDomain.Length > 128 || TrustDomain.Any(char.IsControl))
        {
            throw new ArgumentException("Native auth release trust domain is missing or outside supported bounds.", nameof(TrustDomain));
        }

        if (RootKeys is null || RootKeys.Count == 0 || RootKeys.Count > 16)
        {
            throw new ArgumentException("Native auth release root key set is missing or outside supported bounds.", nameof(RootKeys));
        }

        var keyIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in RootKeys)
        {
            if (key is null || string.IsNullOrWhiteSpace(key.KeyId) || key.KeyId.Length > 256 ||
                key.KeyId.Any(char.IsControl) || !keyIds.Add(key.KeyId))
            {
                throw new ArgumentException("Native auth release root keys require unique bounded key IDs.", nameof(RootKeys));
            }
        }

        if (AllowedAlgorithms is null || AllowedAlgorithms.Count == 0 || AllowedAlgorithms.Count > 8 ||
            AllowedAlgorithms.Any(static algorithm =>
                string.IsNullOrWhiteSpace(algorithm) || algorithm.Length > 64 || algorithm.Any(char.IsControl) ||
                string.Equals(algorithm, "none", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Native auth release signature algorithms are missing or invalid.", nameof(AllowedAlgorithms));
        }

        if (MinimumMetadataVersion < 1) throw new ArgumentOutOfRangeException(nameof(MinimumMetadataVersion));
        if (MinimumSecurityEpoch < 1) throw new ArgumentOutOfRangeException(nameof(MinimumSecurityEpoch));
    }
}

public sealed record VerifiedNativeAuthAuthorityMetadata(
    string TrustDomain,
    long Version,
    long SecurityEpoch,
    NativeAuthAuthorityConfiguration Authority);

/// <summary>
/// Verifies compact release-signed metadata that defines the public native OAuth/OIDC authority.
/// No endpoint, client ID, scope or trust key is accepted from Manager or ordinary user configuration.
/// </summary>
public sealed class SignedNativeAuthAuthorityVerifier
{
    private const int MaximumEnvelopeCharacters = 128 * 1024;
    private const int MaximumScopes = 32;
    private const int MaximumAlgorithms = 8;
    private readonly NativeAuthReleaseTrustConfiguration _rootTrust;

    public SignedNativeAuthAuthorityVerifier(NativeAuthReleaseTrustConfiguration rootTrust)
    {
        _rootTrust = rootTrust ?? throw new ArgumentNullException(nameof(rootTrust));
        _rootTrust.Validate();
    }

    public async ValueTask<VerifiedNativeAuthAuthorityMetadata> VerifyAsync(
        string compactEnvelope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(compactEnvelope) || compactEnvelope.Length > MaximumEnvelopeCharacters ||
            compactEnvelope.Any(char.IsWhiteSpace))
        {
            throw new InvalidDataException("Signed native auth authority metadata is missing or outside supported bounds.");
        }

        var segments = compactEnvelope.Split('.');
        if (segments.Length != 3 || segments.Any(static segment => string.IsNullOrEmpty(segment)))
        {
            throw new InvalidDataException("Signed native auth authority metadata is not compact JWS.");
        }

        var header = DecodeJsonObject(segments[0], "native auth release header");
        EnsureNoDuplicateProperties(header, "native auth release header");
        var algorithm = RequiredString(header, "alg", 64);
        var keyId = RequiredString(header, "kid", 256);
        if (header.TryGetProperty("crit", out _))
        {
            throw new InvalidDataException("Signed native auth authority metadata contains unsupported critical headers.");
        }
        if (!_rootTrust.AllowedAlgorithms.Contains(algorithm, StringComparer.Ordinal) ||
            string.Equals(algorithm, "none", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Signed native auth authority metadata uses an untrusted algorithm.");
        }

        var signingKey = _rootTrust.RootKeys.SingleOrDefault(key =>
            string.Equals(key.KeyId, keyId, StringComparison.Ordinal));
        if (signingKey is null)
        {
            throw new InvalidDataException("Signed native auth authority metadata references an untrusted release key.");
        }

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            TryAllIssuerSigningKeys = false,
            RequireSignedTokens = true,
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false,
            ValidAlgorithms = _rootTrust.AllowedAlgorithms.ToArray()
        };

        var handler = new JsonWebTokenHandler { MaximumTokenSizeInBytes = MaximumEnvelopeCharacters };
        TokenValidationResult signatureResult;
        try
        {
            signatureResult = await handler.ValidateTokenAsync(compactEnvelope, validationParameters).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is ArgumentException or SecurityTokenException or FormatException or CryptographicException)
        {
            throw new InvalidDataException("Signed native auth authority metadata signature validation failed.", exception);
        }

        if (!signatureResult.IsValid)
        {
            throw new InvalidDataException("Signed native auth authority metadata signature validation failed.");
        }

        var payload = DecodeJsonObject(segments[1], "native auth release payload");
        EnsureNoDuplicateProperties(payload, "native auth release payload");
        return ParseMetadata(payload);
    }

    private VerifiedNativeAuthAuthorityMetadata ParseMetadata(JsonElement payload)
    {
        var role = RequiredString(payload, "role", 64);
        if (!string.Equals(role, NativeAuthReleaseTrustConfiguration.NativeAuthAuthorityRole, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Signed native auth authority metadata has the wrong delegated role.");
        }

        var trustDomain = RequiredString(payload, "trustDomain", 128);
        if (!string.Equals(trustDomain, _rootTrust.TrustDomain, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Signed native auth authority metadata belongs to another trust domain.");
        }

        var version = RequiredInt64(payload, "version");
        var securityEpoch = RequiredInt64(payload, "securityEpoch");
        if (version < _rootTrust.MinimumMetadataVersion)
        {
            throw new InvalidDataException("Signed native auth authority metadata version is below the trusted rollback floor.");
        }
        if (securityEpoch < _rootTrust.MinimumSecurityEpoch)
        {
            throw new InvalidDataException("Signed native auth authority metadata security epoch is below the trusted rollback floor.");
        }

        var scopes = RequiredStringArray(payload, "scopes", MaximumScopes, 128);
        var idTokenAlgorithms = RequiredStringArray(payload, "idTokenAlgorithms", MaximumAlgorithms, 64);
        var clockSkewSeconds = RequiredInt64(payload, "clockSkewSeconds");
        var transactionLifetimeSeconds = RequiredInt64(payload, "transactionLifetimeSeconds");

        TimeSpan clockSkew;
        TimeSpan transactionLifetime;
        try
        {
            clockSkew = TimeSpan.FromSeconds(clockSkewSeconds);
            transactionLifetime = TimeSpan.FromSeconds(transactionLifetimeSeconds);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("Signed native auth timing policy is outside supported bounds.", exception);
        }

        var authority = new NativeAuthAuthorityConfiguration(
            RequiredHttpsUri(payload, "issuer"),
            RequiredHttpsUri(payload, "discoveryEndpoint"),
            RequiredHttpsUri(payload, "authorizationEndpoint"),
            RequiredHttpsUri(payload, "tokenEndpoint"),
            RequiredHttpsUri(payload, "jwksEndpoint"),
            RequiredString(payload, "clientId", 512),
            scopes,
            idTokenAlgorithms,
            clockSkew,
            transactionLifetime);

        try
        {
            authority.Validate();
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException)
        {
            throw new InvalidDataException("Signed native auth authority metadata violates the supported native-auth profile.", exception);
        }

        return new VerifiedNativeAuthAuthorityMetadata(
            trustDomain,
            version,
            securityEpoch,
            authority);
    }

    private static Uri RequiredHttpsUri(JsonElement element, string name)
    {
        var text = RequiredString(element, name, 2048);
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidDataException($"Signed native auth field '{name}' is not an allowed HTTPS URI.");
        }
        return uri;
    }

    private static IReadOnlyList<string> RequiredStringArray(
        JsonElement element,
        string name,
        int maximumCount,
        int maximumLength)
    {
        var value = RequiredProperty(element, name);
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"Signed native auth field '{name}' must be an array.");
        }

        var result = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not string text ||
                string.IsNullOrWhiteSpace(text) || text.Length > maximumLength || text.Any(char.IsControl))
            {
                throw new InvalidDataException($"Signed native auth field '{name}' contains an invalid item.");
            }
            result.Add(text);
            if (result.Count > maximumCount)
            {
                throw new InvalidDataException($"Signed native auth field '{name}' exceeded the supported count.");
            }
        }

        if (result.Count == 0 || result.Distinct(StringComparer.Ordinal).Count() != result.Count)
        {
            throw new InvalidDataException($"Signed native auth field '{name}' must contain unique values.");
        }
        return result;
    }

    private static JsonElement DecodeJsonObject(string base64Url, string description)
    {
        byte[] bytes;
        try
        {
            bytes = Base64UrlEncoder.DecodeBytes(base64Url);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new InvalidDataException($"{description} is not valid base64url.", exception);
        }

        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 24
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"{description} must be a JSON object.");
            }
            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{description} is malformed JSON.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static void EnsureNoDuplicateProperties(JsonElement element, string description)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                throw new InvalidDataException($"{description} contains duplicate property '{property.Name}'.");
            }
        }
    }

    private static JsonElement RequiredProperty(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            throw new InvalidDataException($"Signed native auth authority metadata omitted '{name}'.");
        }
        return value;
    }

    private static string RequiredString(JsonElement element, string name, int maximumLength)
    {
        var value = RequiredProperty(element, name);
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not string result ||
            string.IsNullOrWhiteSpace(result) || result.Length > maximumLength || result.Any(char.IsControl))
        {
            throw new InvalidDataException($"Signed native auth field '{name}' is malformed.");
        }
        return result;
    }

    private static long RequiredInt64(JsonElement element, string name)
    {
        var value = RequiredProperty(element, name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var result) || result < 0)
        {
            throw new InvalidDataException($"Signed native auth field '{name}' is malformed.");
        }
        return result;
    }
}
