namespace SplitOS.RuntimeHost.Authentication;

public sealed record NativeAuthTrustConfiguration(
    Uri Issuer,
    Uri DiscoveryEndpoint,
    Uri AuthorizationEndpoint,
    string ClientId,
    IReadOnlyList<string> AllowedIdTokenAlgorithms,
    TimeSpan ClockSkew)
{
    private static readonly HashSet<string> SupportedAlgorithms = new(StringComparer.Ordinal)
    {
        "RS256"
    };

    public void Validate()
    {
        ValidateHttpsUri(Issuer, nameof(Issuer), allowQuery: false);
        ValidateHttpsUri(DiscoveryEndpoint, nameof(DiscoveryEndpoint), allowQuery: false);
        ValidateHttpsUri(AuthorizationEndpoint, nameof(AuthorizationEndpoint), allowQuery: true);

        if (string.IsNullOrWhiteSpace(ClientId) ||
            ClientId.Any(static character => char.IsControl(character)))
        {
            throw new ArgumentException("Native auth client ID must be a non-empty release-owned value.", nameof(ClientId));
        }

        ArgumentNullException.ThrowIfNull(AllowedIdTokenAlgorithms);
        if (AllowedIdTokenAlgorithms.Count == 0)
        {
            throw new ArgumentException("At least one ID-token signature algorithm must be allowlisted.", nameof(AllowedIdTokenAlgorithms));
        }

        foreach (var algorithm in AllowedIdTokenAlgorithms)
        {
            if (string.IsNullOrWhiteSpace(algorithm) ||
                !SupportedAlgorithms.Contains(algorithm))
            {
                throw new ArgumentException(
                    $"ID-token algorithm '{algorithm}' is not supported by this release.",
                    nameof(AllowedIdTokenAlgorithms));
            }
        }

        if (ClockSkew < TimeSpan.Zero || ClockSkew > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(
                nameof(ClockSkew),
                "OIDC clock skew must be between zero and five minutes.");
        }
    }

    private static void ValidateHttpsUri(Uri value, string parameterName, bool allowQuery)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (!value.IsAbsoluteUri ||
            !string.Equals(value.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(value.UserInfo) ||
            !string.IsNullOrEmpty(value.Fragment) ||
            (!allowQuery && !string.IsNullOrEmpty(value.Query)))
        {
            throw new ArgumentException(
                "Release-owned native auth endpoints must be absolute HTTPS URIs without user-info or fragments.",
                parameterName);
        }
    }
}
