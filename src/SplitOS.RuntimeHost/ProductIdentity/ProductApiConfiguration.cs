namespace SplitOS.RuntimeHost.ProductIdentity;

public sealed record ProductApiConfiguration(
    Uri AccountEndpoint,
    Uri CurrentEntitlementEndpoint)
{
    public static ProductApiConfiguration FromAuthority(Uri authority)
    {
        ValidateAuthority(authority);
        return new ProductApiConfiguration(
            new Uri(authority, "/v1/account"),
            new Uri(authority, "/v1/entitlements/current"));
    }

    public void Validate()
    {
        ValidateEndpoint(AccountEndpoint, "/v1/account", nameof(AccountEndpoint));
        ValidateEndpoint(CurrentEntitlementEndpoint, "/v1/entitlements/current", nameof(CurrentEntitlementEndpoint));

        if (!SameAuthority(AccountEndpoint, CurrentEntitlementEndpoint))
        {
            throw new ArgumentException(
                "SplitOS account and entitlement endpoints must belong to the same release-owned HTTPS authority.");
        }
    }

    public static void ValidateAuthority(Uri authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (!authority.IsAbsoluteUri ||
            !string.Equals(authority.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(authority.UserInfo) ||
            !string.IsNullOrEmpty(authority.Query) ||
            !string.IsNullOrEmpty(authority.Fragment) ||
            !string.Equals(authority.AbsolutePath, "/", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Release-owned product API authority must be an absolute HTTPS origin URI with root path and no user-info, query or fragment.",
                nameof(authority));
        }
    }

    private static void ValidateEndpoint(Uri endpoint, string expectedPath, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(endpoint, parameterName);
        if (!endpoint.IsAbsoluteUri ||
            !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment) ||
            !string.Equals(endpoint.AbsolutePath, expectedPath, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Release-owned product endpoint must be exact HTTPS path '{expectedPath}' without user-info, query or fragment.",
                parameterName);
        }
    }

    private static bool SameAuthority(Uri left, Uri right)
        => string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.IdnHost, right.IdnHost, StringComparison.OrdinalIgnoreCase)
           && left.Port == right.Port;
}
