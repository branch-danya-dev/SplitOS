using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace SplitOS.RuntimeHost.ProductIdentity;

public sealed record ReleaseSecurityRootTrustConfiguration(
    string TrustDomain,
    IReadOnlyList<SecurityKey> RootKeys,
    IReadOnlyList<string> AllowedAlgorithms,
    long MinimumBundleVersion,
    long MinimumSecurityEpoch)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(TrustDomain) || TrustDomain.Length > 128 || TrustDomain.Any(char.IsControl))
        {
            throw new ArgumentException("Release security trust domain is missing or outside supported bounds.", nameof(TrustDomain));
        }

        if (RootKeys is null || RootKeys.Count == 0 || RootKeys.Count > 16)
        {
            throw new ArgumentException("Release security root key set is missing or outside supported bounds.", nameof(RootKeys));
        }

        var keyIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in RootKeys)
        {
            if (key is null || string.IsNullOrWhiteSpace(key.KeyId) || key.KeyId.Length > 256 ||
                key.KeyId.Any(char.IsControl) || !keyIds.Add(key.KeyId))
            {
                throw new ArgumentException("Release security root keys require unique bounded key IDs.", nameof(RootKeys));
            }
        }

        if (AllowedAlgorithms is null || AllowedAlgorithms.Count == 0 || AllowedAlgorithms.Count > 8 ||
            AllowedAlgorithms.Any(static algorithm =>
                string.IsNullOrWhiteSpace(algorithm) || algorithm.Length > 64 || algorithm.Any(char.IsControl) ||
                string.Equals(algorithm, "none", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Release security signature algorithms are missing or invalid.", nameof(AllowedAlgorithms));
        }

        if (MinimumBundleVersion < 1) throw new ArgumentOutOfRangeException(nameof(MinimumBundleVersion));
        if (MinimumSecurityEpoch < 1) throw new ArgumentOutOfRangeException(nameof(MinimumSecurityEpoch));
    }
}

/// <summary>
/// Verifies the signed release-security envelope that authorizes the public keys used for
/// OfflineEntitlementAssertion validation. The verifier consumes only caller-supplied trusted root keys;
/// it never downloads trust anchors or accepts user-configured files/URLs.
/// </summary>
public sealed class SignedEntitlementTrustBundleVerifier
{
    private const int MaximumEnvelopeCharacters = 128 * 1024;
    private const int MaximumKeyCount = 32;
    private readonly ReleaseSecurityRootTrustConfiguration _rootTrust;

    public SignedEntitlementTrustBundleVerifier(ReleaseSecurityRootTrustConfiguration rootTrust)
    {
        _rootTrust = rootTrust ?? throw new ArgumentNullException(nameof(rootTrust));
        _rootTrust.Validate();
    }

    public async ValueTask<EntitlementAssertionTrustBundle> VerifyAsync(
        string compactEnvelope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(compactEnvelope) || compactEnvelope.Length > MaximumEnvelopeCharacters ||
            compactEnvelope.Any(char.IsWhiteSpace))
        {
            throw new InvalidDataException("Signed entitlement trust metadata is missing or outside supported bounds.");
        }

        var segments = compactEnvelope.Split('.');
        if (segments.Length != 3 || segments.Any(static segment => string.IsNullOrEmpty(segment)))
        {
            throw new InvalidDataException("Signed entitlement trust metadata is not compact JWS.");
        }

        var header = DecodeJsonObject(segments[0], "release trust header");
        EnsureNoDuplicateProperties(header, "release trust header");
        var algorithm = RequiredString(header, "alg", 64);
        var keyId = RequiredString(header, "kid", 256);
        if (header.TryGetProperty("crit", out _))
        {
            throw new InvalidDataException("Signed entitlement trust metadata contains unsupported critical headers.");
        }
        if (!_rootTrust.AllowedAlgorithms.Contains(algorithm, StringComparer.Ordinal) ||
            string.Equals(algorithm, "none", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Signed entitlement trust metadata uses an untrusted algorithm.");
        }

        var signingKey = _rootTrust.RootKeys.SingleOrDefault(key =>
            string.Equals(key.KeyId, keyId, StringComparison.Ordinal));
        if (signingKey is null)
        {
            throw new InvalidDataException("Signed entitlement trust metadata references an untrusted release key.");
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
            throw new InvalidDataException("Signed entitlement trust metadata signature validation failed.", exception);
        }

        if (!signatureResult.IsValid)
        {
            throw new InvalidDataException("Signed entitlement trust metadata signature validation failed.");
        }

        var payload = DecodeJsonObject(segments[1], "release trust payload");
        EnsureNoDuplicateProperties(payload, "release trust payload");
        return ParseBundle(payload);
    }

    private EntitlementAssertionTrustBundle ParseBundle(JsonElement payload)
    {
        var role = RequiredString(payload, "role", 64);
        if (!string.Equals(role, EntitlementAssertionTrustBundle.EntitlementAssertionRole, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Signed entitlement trust metadata has the wrong delegated role.");
        }

        var trustDomain = RequiredString(payload, "trustDomain", 128);
        if (!string.Equals(trustDomain, _rootTrust.TrustDomain, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Signed entitlement trust metadata belongs to another trust domain.");
        }

        var version = RequiredInt64(payload, "version");
        var securityEpoch = RequiredInt64(payload, "securityEpoch");
        if (version < _rootTrust.MinimumBundleVersion)
        {
            throw new InvalidDataException("Signed entitlement trust metadata version is below the trusted rollback floor.");
        }
        if (securityEpoch < _rootTrust.MinimumSecurityEpoch)
        {
            throw new InvalidDataException("Signed entitlement trust metadata security epoch is below the trusted rollback floor.");
        }

        var issuer = RequiredString(payload, "issuer", 512);
        var keysElement = RequiredProperty(payload, "keys");
        if (keysElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Signed entitlement trust key set must be an array.");
        }

        var keys = new List<EntitlementAssertionTrustKey>();
        foreach (var element in keysElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Signed entitlement trust key entry must be an object.");
            }
            EnsureNoDuplicateProperties(element, "release trust key");
            keys.Add(ParseKey(element));
            if (keys.Count > MaximumKeyCount)
            {
                throw new InvalidDataException("Signed entitlement trust key set exceeded the supported bound.");
            }
        }

        var bundle = new EntitlementAssertionTrustBundle(
            trustDomain,
            issuer,
            version,
            securityEpoch,
            keys);
        bundle.Validate(_rootTrust.TrustDomain);
        return bundle;
    }

    private static EntitlementAssertionTrustKey ParseKey(JsonElement element)
    {
        var keyId = RequiredString(element, "kid", 256);
        var algorithm = RequiredString(element, "alg", 64);
        if (!string.Equals(algorithm, SecurityAlgorithms.RsaSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Only RS256 entitlement assertion verification keys are supported by the current release profile.");
        }

        var lifecycleText = RequiredString(element, "lifecycle", 32);
        if (!Enum.TryParse<EntitlementAssertionKeyLifecycle>(lifecycleText, ignoreCase: false, out var lifecycle))
        {
            throw new InvalidDataException("Signed entitlement trust key lifecycle is unsupported.");
        }

        var notBeforeUtc = UnixTime(element, "notBefore");
        DateTimeOffset? notAfterUtc = null;
        if (element.TryGetProperty("notAfter", out var notAfterElement) && notAfterElement.ValueKind != JsonValueKind.Null)
        {
            if (notAfterElement.ValueKind != JsonValueKind.Number || !notAfterElement.TryGetInt64(out var seconds))
            {
                throw new InvalidDataException("Signed entitlement trust key retirement time is malformed.");
            }
            notAfterUtc = DateTimeOffset.FromUnixTimeSeconds(seconds);
        }

        var keyType = RequiredString(element, "kty", 16);
        if (!string.Equals(keyType, "RSA", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Signed entitlement trust metadata contains an unsupported public-key type.");
        }

        foreach (var privateParameter in new[] { "d", "p", "q", "dp", "dq", "qi", "oth" })
        {
            if (element.TryGetProperty(privateParameter, out _))
            {
                throw new InvalidDataException("Signed entitlement trust metadata must never carry private key material.");
            }
        }

        var modulus = RequiredString(element, "n", 4096);
        var exponent = RequiredString(element, "e", 32);
        byte[] modulusBytes;
        byte[] exponentBytes;
        try
        {
            modulusBytes = Base64UrlEncoder.DecodeBytes(modulus);
            exponentBytes = Base64UrlEncoder.DecodeBytes(exponent);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new InvalidDataException("Signed entitlement trust RSA key material is malformed.", exception);
        }

        if (modulusBytes.Length < 256 || modulusBytes.Length > 1024 || exponentBytes.Length < 1 || exponentBytes.Length > 8)
        {
            throw new InvalidDataException("Signed entitlement trust RSA key material is outside supported bounds.");
        }

        var securityKey = new RsaSecurityKey(new RSAParameters
        {
            Modulus = modulusBytes,
            Exponent = exponentBytes
        })
        {
            KeyId = keyId
        };

        return new EntitlementAssertionTrustKey(
            securityKey,
            algorithm,
            lifecycle,
            notBeforeUtc,
            notAfterUtc);
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
            throw new InvalidDataException($"Signed entitlement trust metadata omitted '{name}'.");
        }
        return value;
    }

    private static string RequiredString(JsonElement element, string name, int maximumLength)
    {
        var value = RequiredProperty(element, name);
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not string result ||
            string.IsNullOrWhiteSpace(result) || result.Length > maximumLength || result.Any(char.IsControl))
        {
            throw new InvalidDataException($"Signed entitlement trust metadata field '{name}' is malformed.");
        }
        return result;
    }

    private static long RequiredInt64(JsonElement element, string name)
    {
        var value = RequiredProperty(element, name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var result) || result < 0)
        {
            throw new InvalidDataException($"Signed entitlement trust metadata field '{name}' is malformed.");
        }
        return result;
    }

    private static DateTimeOffset UnixTime(JsonElement element, string name)
    {
        var seconds = RequiredInt64(element, name);
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException($"Signed entitlement trust metadata field '{name}' is outside supported time bounds.", exception);
        }
    }
}
