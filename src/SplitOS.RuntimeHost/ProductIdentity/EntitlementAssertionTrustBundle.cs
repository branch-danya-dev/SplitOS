using Microsoft.IdentityModel.Tokens;

namespace SplitOS.RuntimeHost.ProductIdentity;

public enum EntitlementAssertionKeyLifecycle
{
    Active,
    Rotating,
    Retired,
    Revoked
}

public sealed record EntitlementAssertionTrustKey(
    SecurityKey Key,
    string Algorithm,
    EntitlementAssertionKeyLifecycle Lifecycle,
    DateTimeOffset NotBeforeUtc,
    DateTimeOffset? NotAfterUtc)
{
    public void Validate()
    {
        if (Key is null || string.IsNullOrWhiteSpace(Key.KeyId) || Key.KeyId.Length > 256 || Key.KeyId.Any(char.IsControl))
        {
            throw new ArgumentException("Entitlement assertion trust keys require a bounded non-empty key ID.", nameof(Key));
        }

        if (string.IsNullOrWhiteSpace(Algorithm) || Algorithm.Length > 64 || Algorithm.Any(char.IsControl) ||
            string.Equals(Algorithm, "none", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Entitlement assertion trust key algorithm is missing or unsupported.", nameof(Algorithm));
        }

        if (NotBeforeUtc == default)
        {
            throw new ArgumentException("Entitlement assertion trust key activation time is required.", nameof(NotBeforeUtc));
        }

        if (NotAfterUtc is not null && NotAfterUtc <= NotBeforeUtc)
        {
            throw new ArgumentException("Entitlement assertion trust key retirement boundary must follow activation.", nameof(NotAfterUtc));
        }
    }

    public bool IsUsableAt(DateTimeOffset now)
        => Lifecycle is EntitlementAssertionKeyLifecycle.Active or EntitlementAssertionKeyLifecycle.Rotating &&
           now >= NotBeforeUtc &&
           (NotAfterUtc is null || now < NotAfterUtc.Value);
}

/// <summary>
/// Authenticated release-security projection for the dedicated ENTITLEMENT_ASSERTION role.
/// Production callers must construct this object only from release-owned metadata that has already
/// crossed the SplitOS release-security verification boundary. The bundle itself never trusts URLs,
/// user configuration, arbitrary files or developer fixture keys.
/// </summary>
public sealed record EntitlementAssertionTrustBundle(
    string TrustDomain,
    string Issuer,
    long Version,
    long SecurityEpoch,
    IReadOnlyList<EntitlementAssertionTrustKey> Keys)
{
    public const string ProductionTrustDomain = "splitos-production";
    public const string EntitlementAssertionRole = "ENTITLEMENT_ASSERTION";

    public void Validate(string expectedTrustDomain)
    {
        if (string.IsNullOrWhiteSpace(expectedTrustDomain))
        {
            throw new ArgumentException("Expected trust domain is required.", nameof(expectedTrustDomain));
        }

        if (!string.Equals(TrustDomain, expectedTrustDomain, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Entitlement assertion trust domain does not match the active release channel.");
        }

        if (string.IsNullOrWhiteSpace(Issuer) || Issuer.Length > 512 || Issuer.Any(char.IsControl))
        {
            throw new InvalidDataException("Entitlement assertion issuer is missing or outside supported bounds.");
        }

        if (Version < 1)
        {
            throw new InvalidDataException("Entitlement assertion trust bundle version must be positive.");
        }

        if (SecurityEpoch < 1)
        {
            throw new InvalidDataException("Entitlement assertion security epoch must be positive.");
        }

        if (Keys is null || Keys.Count == 0 || Keys.Count > 32)
        {
            throw new InvalidDataException("Entitlement assertion trust bundle must contain a bounded key set.");
        }

        var keyIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in Keys)
        {
            if (key is null)
            {
                throw new InvalidDataException("Entitlement assertion trust bundle contains a null key entry.");
            }

            key.Validate();
            if (!keyIds.Add(key.Key.KeyId))
            {
                throw new InvalidDataException("Entitlement assertion trust bundle contains duplicate key IDs.");
            }
        }
    }

    public OfflineEntitlementTrustConfiguration CreateValidationTrust(
        string expectedTrustDomain,
        DateTimeOffset now)
    {
        Validate(expectedTrustDomain);
        if (now == default)
        {
            throw new ArgumentException("Trust evaluation time is required.", nameof(now));
        }

        var usable = Keys.Where(key => key.IsUsableAt(now)).ToArray();
        if (usable.Length == 0)
        {
            throw new InvalidDataException("No active entitlement assertion verification key is available for the current release trust state.");
        }

        var algorithms = usable
            .Select(key => key.Algorithm)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new OfflineEntitlementTrustConfiguration(
            Issuer,
            usable.Select(key => key.Key).ToArray(),
            algorithms);
    }
}

public interface IEntitlementAssertionTrustBundleProvider
{
    ValueTask<EntitlementAssertionTrustBundle> ReadAuthenticatedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Converts authenticated release-security metadata into the narrow validator trust input consumed by
/// OfflineEntitlementAssertionValidator. It deliberately has no fallback to local/user supplied keys.
/// </summary>
public sealed class ReleaseOwnedOfflineEntitlementTrustProvider(
    IEntitlementAssertionTrustBundleProvider bundleProvider,
    string expectedTrustDomain,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async ValueTask<OfflineEntitlementTrustConfiguration> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        var bundle = await bundleProvider.ReadAuthenticatedAsync(cancellationToken).ConfigureAwait(false);
        if (bundle is null)
        {
            throw new InvalidDataException("Authenticated entitlement assertion trust metadata is unavailable.");
        }

        return bundle.CreateValidationTrust(expectedTrustDomain, _timeProvider.GetUtcNow());
    }
}
