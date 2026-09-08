using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace SplitOS.RuntimeHost.ProductIdentity;

public sealed record OfflineEntitlementTrustConfiguration(
    string Issuer,
    IReadOnlyList<SecurityKey> SigningKeys,
    IReadOnlyList<string> AllowedAlgorithms)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer) || Issuer.Length > 512 || Issuer.Any(char.IsControl))
        {
            throw new ArgumentException("Offline entitlement issuer is missing or outside supported bounds.", nameof(Issuer));
        }

        if (SigningKeys is null || SigningKeys.Count == 0)
        {
            throw new ArgumentException("At least one trusted entitlement assertion signing key is required.", nameof(SigningKeys));
        }

        var keyIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in SigningKeys)
        {
            if (key is null || string.IsNullOrWhiteSpace(key.KeyId) || !keyIds.Add(key.KeyId))
            {
                throw new ArgumentException("Trusted entitlement signing keys require unique non-empty key IDs.", nameof(SigningKeys));
            }
        }

        if (AllowedAlgorithms is null || AllowedAlgorithms.Count == 0 ||
            AllowedAlgorithms.Any(static algorithm => string.IsNullOrWhiteSpace(algorithm)))
        {
            throw new ArgumentException("At least one allowed entitlement assertion algorithm is required.", nameof(AllowedAlgorithms));
        }
    }
}

public sealed record OfflineEntitlementValidationPolicy(
    TimeSpan MaximumAssertionLifetime,
    TimeSpan AllowedClockSkew,
    int MaximumAssertionCharacters,
    int MaximumCapabilities)
{
    public static OfflineEntitlementValidationPolicy Default { get; } = new(
        TimeSpan.FromDays(7),
        TimeSpan.FromMinutes(5),
        32 * 1024,
        64);

    public void Validate()
    {
        if (MaximumAssertionLifetime <= TimeSpan.Zero || MaximumAssertionLifetime > TimeSpan.FromDays(7))
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumAssertionLifetime));
        }

        if (AllowedClockSkew < TimeSpan.Zero || AllowedClockSkew > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(AllowedClockSkew));
        }

        if (MaximumAssertionCharacters < 1024 || MaximumAssertionCharacters > 128 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumAssertionCharacters));
        }

        if (MaximumCapabilities < 1 || MaximumCapabilities > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumCapabilities));
        }
    }
}

public sealed record OfflineEntitlementValidationContext(
    string AccountId,
    string AssociationId,
    string InstallationId,
    string RequiredCapability,
    long? MinimumEntitlementVersion,
    DateTimeOffset? LastTrustedServerUtc,
    DateTimeOffset? LastTrustedServerObservationLocalUtc)
{
    public void Validate()
    {
        ValidateBounded(AccountId, 512, nameof(AccountId));
        ValidateBounded(AssociationId, 128, nameof(AssociationId));
        ValidateBounded(InstallationId, 256, nameof(InstallationId));
        ValidateBounded(RequiredCapability, 128, nameof(RequiredCapability));
        if (MinimumEntitlementVersion is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumEntitlementVersion));
        }
    }

    private static void ValidateBounded(string value, int maximumLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength || value.Any(char.IsControl))
        {
            throw new ArgumentException($"{name} is missing or outside supported bounds.", name);
        }
    }
}

public enum OfflineEntitlementValidationDisposition
{
    Accepted,
    Missing,
    Rejected,
    ClockRollbackSuspected
}

public sealed record OfflineEntitlementAssertionClaims(
    int Version,
    string Issuer,
    string AccountId,
    string AssertionId,
    string InstallationId,
    string AssociationId,
    long EntitlementVersion,
    string Plan,
    IReadOnlyList<string> Capabilities,
    DateTimeOffset IssuedUtc,
    DateTimeOffset NotBeforeUtc,
    DateTimeOffset ExpiresUtc)
{
    public bool HasCapability(string capability)
        => Capabilities.Contains(capability, StringComparer.Ordinal);
}

public sealed record OfflineEntitlementValidationResult(
    OfflineEntitlementValidationDisposition Disposition,
    string ProductCode,
    OfflineEntitlementAssertionClaims? Claims)
{
    public bool IsAccepted => Disposition == OfflineEntitlementValidationDisposition.Accepted;
}

/// <summary>
/// Verifies server-signed OfflineEntitlementAssertion v1. The validator is intentionally pure: it never
/// persists, renews or rewrites assertion lifetime. Trusted-time observations are supplied from protected
/// RuntimeHost-owned state and local clock rollback fails closed.
/// </summary>
public sealed class OfflineEntitlementAssertionValidator
{
    private readonly OfflineEntitlementTrustConfiguration _trust;
    private readonly OfflineEntitlementValidationPolicy _policy;
    private readonly TimeProvider _timeProvider;

    public OfflineEntitlementAssertionValidator(
        OfflineEntitlementTrustConfiguration trust,
        OfflineEntitlementValidationPolicy? policy = null,
        TimeProvider? timeProvider = null)
    {
        _trust = trust ?? throw new ArgumentNullException(nameof(trust));
        _policy = policy ?? OfflineEntitlementValidationPolicy.Default;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _trust.Validate();
        _policy.Validate();
    }

    public async ValueTask<OfflineEntitlementValidationResult> ValidateAsync(
        string? compactAssertion,
        OfflineEntitlementValidationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(compactAssertion))
        {
            return Reject(OfflineEntitlementValidationDisposition.Missing, "OFFLINE_ASSERTION_MISSING");
        }

        if (compactAssertion.Length > _policy.MaximumAssertionCharacters || compactAssertion.Any(char.IsWhiteSpace))
        {
            return Reject(OfflineEntitlementValidationDisposition.Rejected, "OFFLINE_ASSERTION_INVALID");
        }

        JsonElement header;
        JsonElement payload;
        try
        {
            var segments = compactAssertion.Split('.');
            if (segments.Length != 3 || segments.Any(static segment => string.IsNullOrEmpty(segment)))
            {
                throw new InvalidDataException("JWS compact serialization is malformed.");
            }

            header = DecodeJsonObject(segments[0], "JWS header");
            payload = DecodeJsonObject(segments[1], "JWS payload");
            EnsureNoDuplicateProperties(header, "JWS header");
            EnsureNoDuplicateProperties(payload, "JWS payload");
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or FormatException)
        {
            return Reject(OfflineEntitlementValidationDisposition.Rejected, "OFFLINE_ASSERTION_INVALID");
        }

        string algorithm;
        string keyId;
        try
        {
            algorithm = RequiredString(header, "alg", 64);
            keyId = RequiredString(header, "kid", 256);
            if (string.Equals(algorithm, "none", StringComparison.OrdinalIgnoreCase) ||
                !_trust.AllowedAlgorithms.Contains(algorithm, StringComparer.Ordinal))
            {
                return Reject(OfflineEntitlementValidationDisposition.Rejected, "OFFLINE_ASSERTION_ALGORITHM_REJECTED");
            }

            if (header.TryGetProperty("crit", out _))
            {
                // v1 defines no understood critical extension. Unknown critical semantics must fail closed.
                return Reject(OfflineEntitlementValidationDisposition.Rejected, "OFFLINE_ASSERTION_CRITICAL_HEADER_REJECTED");
            }
        }
        catch (InvalidDataException)
        {
            return Reject(OfflineEntitlementValidationDisposition.Rejected, "OFFLINE_ASSERTION_INVALID");
        }

        var signingKey = _trust.SigningKeys.SingleOrDefault(key => string.Equals(key.KeyId, keyId, StringComparison.Ordinal));
        if (signingKey is null)
        {
            return Reject(OfflineEntitlementValidationDisposition.Rejected, "OFFLINE_ASSERTION_KEY_UNTRUSTED");
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
            ValidAlgorithms = _trust.AllowedAlgorithms.ToArray()
        };

        var handler = new JsonWebTokenHandler { MaximumTokenSizeInBytes = _policy.MaximumAssertionCharacters };
        TokenValidationResult signatureResult;
        try
        {
            signatureResult = await handler.ValidateTokenAsync(compactAssertion, validationParameters).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is ArgumentException or SecurityTokenException or FormatException or CryptographicException)
        {
            return Reject(OfflineEntitlementValidationDisposition.Rejected, "OFFLINE_ASSERTION_SIGNATURE_INVALID");
        }

        if (!signatureResult.IsValid)
        {
            return Reject(OfflineEntitlementValidationDisposition.Rejected, "OFFLINE_ASSERTION_SIGNATURE_INVALID");
        }

        OfflineEntitlementAssertionClaims claims;
        try
        {
            claims = ParseClaims(payload);
        }
        catch (InvalidDataException)
        {
            return Reject(OfflineEntitlementValidationDisposition.Rejected, "OFFLINE_ASSERTION_CLAIMS_INVALID");
        }

        if (!string.Equals(claims.Issuer, _trust.Issuer, StringComparison.Ordinal))
        {
            return Reject(OfflineEntitlementValidationDisposition.Rejected, "OFFLINE_ASSERTION_ISSUER_MISMATCH");
        }

        if (claims.Version != 1)
        {
            return Reject(OfflineEntitlementValidationDisposition.Rejected, "OFFLINE_ASSERTION_VERSION_UNSUPPORTED");
        }

        if (!string.Equals(claims.AccountId, context.AccountId, StringComparison.Ordinal) ||
            !string.Equals(claims.AssociationId, context.AssociationId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(claims.InstallationId, context.InstallationId, StringComparison.Ordinal))
        {
            return Reject(OfflineEntitlementValidationDisposition.Rejected, "OFFLINE_ASSERTION_CONTEXT_MISMATCH");
        }

        if (context.MinimumEntitlementVersion is long minimumVersion && claims.EntitlementVersion < minimumVersion)
        {
            return Reject(OfflineEntitlementValidationDisposition.Rejected, "OFFLINE_ASSERTION_VERSION_ROLLBACK");
        }

        if (claims.Capabilities.Count == 0 || claims.Capabilities.Count > _policy.MaximumCapabilities ||
            claims.Capabilities.Distinct(StringComparer.Ordinal).Count() != claims.Capabilities.Count ||
            claims.Capabilities.Any(static capability =>
                string.IsNullOrWhiteSpace(capability) || capability.Length > 128 || capability.Any(char.IsControl)))
        {
            return Reject(OfflineEntitlementValidationDisposition.Rejected, "OFFLINE_ASSERTION_CLAIMS_INVALID");
        }

        if (!claims.HasCapability(context.RequiredCapability))
        {
            return Reject(OfflineEntitlementValidationDisposition.Rejected, "OFFLINE_ASSERTION_CAPABILITY_MISSING");
        }

        if (!string.Equals(claims.Plan, "PRO", StringComparison.Ordinal))
        {
            return Reject(OfflineEntitlementValidationDisposition.Rejected, "OFFLINE_ASSERTION_NOT_PRO");
        }

        var lifetime = claims.ExpiresUtc - claims.IssuedUtc;
        if (lifetime <= TimeSpan.Zero || lifetime > _policy.MaximumAssertionLifetime || claims.NotBeforeUtc < claims.IssuedUtc.Subtract(_policy.AllowedClockSkew))
        {
            return Reject(OfflineEntitlementValidationDisposition.Rejected, "OFFLINE_ASSERTION_LIFETIME_INVALID");
        }

        var now = _timeProvider.GetUtcNow();
        if (context.LastTrustedServerUtc is DateTimeOffset trustedServerUtc)
        {
            if (now < trustedServerUtc.Subtract(_policy.AllowedClockSkew))
            {
                return Reject(OfflineEntitlementValidationDisposition.ClockRollbackSuspected, "CLOCK_ROLLBACK_SUSPECTED");
            }

            if (context.LastTrustedServerObservationLocalUtc is DateTimeOffset observedLocalUtc &&
                observedLocalUtc > now.Add(_policy.AllowedClockSkew))
            {
                return Reject(OfflineEntitlementValidationDisposition.ClockRollbackSuspected, "CLOCK_ROLLBACK_SUSPECTED");
            }
        }

        if (claims.IssuedUtc > now.Add(_policy.AllowedClockSkew) ||
            claims.NotBeforeUtc > now.Add(_policy.AllowedClockSkew))
        {
            return Reject(OfflineEntitlementValidationDisposition.Rejected, "OFFLINE_ASSERTION_NOT_YET_VALID");
        }

        if (now > claims.ExpiresUtc.Add(_policy.AllowedClockSkew))
        {
            return Reject(OfflineEntitlementValidationDisposition.Rejected, "OFFLINE_ASSERTION_EXPIRED");
        }

        return new OfflineEntitlementValidationResult(
            OfflineEntitlementValidationDisposition.Accepted,
            "PRO_OFFLINE_ASSERTION_VALID",
            claims);
    }

    private OfflineEntitlementAssertionClaims ParseClaims(JsonElement payload)
    {
        var capabilitiesElement = RequiredProperty(payload, "capabilities");
        if (capabilitiesElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("capabilities must be an array.");
        }

        var capabilities = new List<string>();
        foreach (var capability in capabilitiesElement.EnumerateArray())
        {
            if (capability.ValueKind != JsonValueKind.String || capability.GetString() is not string value)
            {
                throw new InvalidDataException("capability is malformed.");
            }
            capabilities.Add(value);
            if (capabilities.Count > _policy.MaximumCapabilities)
            {
                throw new InvalidDataException("capability count exceeded the supported bound.");
            }
        }

        return new OfflineEntitlementAssertionClaims(
            RequiredInt32(payload, "ver"),
            RequiredString(payload, "iss", 512),
            RequiredString(payload, "sub", 512),
            RequiredString(payload, "jti", 256),
            RequiredString(payload, "installationId", 256),
            RequiredString(payload, "associationId", 128),
            RequiredInt64(payload, "entitlementVersion"),
            RequiredString(payload, "plan", 32),
            capabilities,
            UnixTime(payload, "iat"),
            UnixTime(payload, "nbf"),
            UnixTime(payload, "exp"));
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
                MaxDepth = 16
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"{description} must be a JSON object.");
            }
            return document.RootElement.Clone();
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
                throw new InvalidDataException($"{description} contains duplicate properties.");
            }
        }
    }

    private static JsonElement RequiredProperty(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
            ? value
            : throw new InvalidDataException($"Missing required claim {name}.");

    private static string RequiredString(JsonElement element, string name, int maximumLength)
    {
        var property = RequiredProperty(element, name);
        if (property.ValueKind != JsonValueKind.String || property.GetString() is not string value ||
            string.IsNullOrWhiteSpace(value) || value.Length > maximumLength || value.Any(char.IsControl))
        {
            throw new InvalidDataException($"Claim/header {name} is malformed.");
        }
        return value;
    }

    private static int RequiredInt32(JsonElement element, string name)
    {
        var property = RequiredProperty(element, name);
        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value)
            ? value
            : throw new InvalidDataException($"Claim {name} is malformed.");
    }

    private static long RequiredInt64(JsonElement element, string name)
    {
        var property = RequiredProperty(element, name);
        return property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var value) && value >= 0
            ? value
            : throw new InvalidDataException($"Claim {name} is malformed.");
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
            throw new InvalidDataException($"Claim {name} is outside supported time bounds.", exception);
        }
    }

    private static OfflineEntitlementValidationResult Reject(
        OfflineEntitlementValidationDisposition disposition,
        string code)
        => new(disposition, code, null);
}
