namespace SplitOS.RuntimeHost.Authentication;

public sealed record NativeAuthAuthorityConfiguration(
    Uri Issuer,
    Uri DiscoveryEndpoint,
    Uri AuthorizationEndpoint,
    Uri TokenEndpoint,
    Uri JwksEndpoint,
    string ClientId,
    IReadOnlyList<string> RequestedScopes,
    IReadOnlyList<string> AllowedIdTokenAlgorithms,
    TimeSpan ClockSkew,
    TimeSpan TransactionLifetime)
{
    public static readonly TimeSpan MaximumTransactionLifetime = TimeSpan.FromMinutes(10);

    private static readonly HashSet<string> SupportedAlgorithms = new(StringComparer.Ordinal)
    {
        "RS256"
    };

    public void Validate()
    {
        ValidateHttpsUri(Issuer, nameof(Issuer));
        ValidateHttpsUri(DiscoveryEndpoint, nameof(DiscoveryEndpoint));
        ValidateHttpsUri(AuthorizationEndpoint, nameof(AuthorizationEndpoint));
        ValidateHttpsUri(TokenEndpoint, nameof(TokenEndpoint));
        ValidateHttpsUri(JwksEndpoint, nameof(JwksEndpoint));

        if (string.IsNullOrWhiteSpace(ClientId) ||
            ClientId.Any(static character => char.IsControl(character)))
        {
            throw new ArgumentException("Native auth client ID must be a non-empty release-owned value.", nameof(ClientId));
        }

        ArgumentNullException.ThrowIfNull(RequestedScopes);
        if (RequestedScopes.Count == 0 ||
            !RequestedScopes.Contains("openid", StringComparer.Ordinal) ||
            RequestedScopes.Any(static scope =>
                string.IsNullOrWhiteSpace(scope) ||
                scope.Any(static character => char.IsControl(character) || char.IsWhiteSpace(character))))
        {
            throw new ArgumentException(
                "Native auth scopes must be non-empty protocol tokens and include OIDC scope 'openid'.",
                nameof(RequestedScopes));
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

        if (TransactionLifetime <= TimeSpan.Zero || TransactionLifetime > MaximumTransactionLifetime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(TransactionLifetime),
                $"Native auth transaction lifetime must be > 0 and <= {MaximumTransactionLifetime}.");
        }
    }

    private static void ValidateHttpsUri(Uri value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (!value.IsAbsoluteUri ||
            !string.Equals(value.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(value.UserInfo) ||
            !string.IsNullOrEmpty(value.Query) ||
            !string.IsNullOrEmpty(value.Fragment))
        {
            throw new ArgumentException(
                "Release-owned native auth endpoints must be absolute HTTPS URIs without user-info, query or fragment components.",
                parameterName);
        }
    }
}
