namespace SplitOS.RuntimeHost.Authentication;

public sealed record NativeAuthClientOptions(
    Uri AuthorizationEndpoint,
    string ClientId,
    IReadOnlyList<string> RequestedScopes,
    TimeSpan TransactionLifetime)
{
    public static readonly TimeSpan MaximumTransactionLifetime = TimeSpan.FromMinutes(10);

    public void Validate()
    {
        if (AuthorizationEndpoint is null || !AuthorizationEndpoint.IsAbsoluteUri)
        {
            throw new ArgumentException("Authorization endpoint must be an absolute URI.", nameof(AuthorizationEndpoint));
        }

        if (!string.Equals(AuthorizationEndpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Authorization endpoint must use HTTPS.", nameof(AuthorizationEndpoint));
        }

        if (!string.IsNullOrEmpty(AuthorizationEndpoint.Query) || !string.IsNullOrEmpty(AuthorizationEndpoint.Fragment))
        {
            throw new ArgumentException("Authorization endpoint must not contain query or fragment components.", nameof(AuthorizationEndpoint));
        }

        if (string.IsNullOrWhiteSpace(ClientId))
        {
            throw new ArgumentException("Native OAuth client ID is required.", nameof(ClientId));
        }

        if (RequestedScopes is null || RequestedScopes.Count == 0 ||
            !RequestedScopes.Contains("openid", StringComparer.Ordinal))
        {
            throw new ArgumentException("OIDC scope 'openid' is required.", nameof(RequestedScopes));
        }

        if (TransactionLifetime <= TimeSpan.Zero || TransactionLifetime > MaximumTransactionLifetime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(TransactionLifetime),
                $"Native auth transaction lifetime must be > 0 and <= {MaximumTransactionLifetime}.");
        }
    }
}

public enum NativeAuthStartDisposition
{
    Started,
    AlreadyInProgress
}

public enum NativeAuthCallbackDisposition
{
    Accepted,
    NoActiveTransaction,
    Expired,
    ResultRejected,
    Cancelled,
    LoginRequired,
    ServerError
}

public sealed class NativeAuthTransaction
{
    internal NativeAuthTransaction(
        Guid authTransactionId,
        int windowsSessionId,
        string windowsUserSidReference,
        DateTimeOffset createdUtc,
        DateTimeOffset expiresUtc,
        Uri redirectUri,
        string state,
        string nonce,
        string codeVerifier,
        string codeChallenge,
        IReadOnlyList<string> requestedScopes,
        Uri authorizationUri)
    {
        AuthTransactionId = authTransactionId;
        WindowsSessionId = windowsSessionId;
        WindowsUserSidReference = windowsUserSidReference;
        CreatedUtc = createdUtc;
        ExpiresUtc = expiresUtc;
        RedirectUri = redirectUri;
        State = state;
        Nonce = nonce;
        CodeVerifier = codeVerifier;
        CodeChallenge = codeChallenge;
        RequestedScopes = requestedScopes;
        AuthorizationUri = authorizationUri;
    }

    public Guid AuthTransactionId { get; }
    public int WindowsSessionId { get; }
    public string WindowsUserSidReference { get; }
    public DateTimeOffset CreatedUtc { get; }
    public DateTimeOffset ExpiresUtc { get; }
    public Uri RedirectUri { get; }
    public string State { get; }
    public string Nonce { get; }
    public string CodeVerifier { get; }
    public string CodeChallenge { get; }
    public string CodeChallengeMethod => "S256";
    public IReadOnlyList<string> RequestedScopes { get; }
    public Uri AuthorizationUri { get; }

    public override string ToString()
        => $"NativeAuthTransaction {{ AuthTransactionId = {AuthTransactionId}, WindowsSessionId = {WindowsSessionId}, ExpiresUtc = {ExpiresUtc:O}, State = [REDACTED], Nonce = [REDACTED], CodeVerifier = [REDACTED] }}";
}

public sealed record NativeAuthStartResult(
    NativeAuthStartDisposition Disposition,
    NativeAuthTransaction Transaction)
{
    public override string ToString()
        => $"NativeAuthStartResult {{ Disposition = {Disposition}, AuthTransactionId = {Transaction.AuthTransactionId} }}";
}

public sealed record NativeAuthActiveSnapshot(
    Guid AuthTransactionId,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ExpiresUtc,
    int WindowsSessionId);

public sealed class NativeAuthCodeExchangeContext
{
    internal NativeAuthCodeExchangeContext(
        Guid authTransactionId,
        string authorizationCode,
        string codeVerifier,
        string nonce,
        Uri redirectUri,
        string clientId)
    {
        AuthTransactionId = authTransactionId;
        AuthorizationCode = authorizationCode;
        CodeVerifier = codeVerifier;
        Nonce = nonce;
        RedirectUri = redirectUri;
        ClientId = clientId;
    }

    public Guid AuthTransactionId { get; }
    public string AuthorizationCode { get; }
    public string CodeVerifier { get; }
    public string Nonce { get; }
    public Uri RedirectUri { get; }
    public string ClientId { get; }

    public override string ToString()
        => $"NativeAuthCodeExchangeContext {{ AuthTransactionId = {AuthTransactionId}, AuthorizationCode = [REDACTED], CodeVerifier = [REDACTED], Nonce = [REDACTED] }}";
}

public sealed record NativeAuthCallbackResult(
    NativeAuthCallbackDisposition Disposition,
    Guid? AuthTransactionId,
    string ProductCode,
    NativeAuthCodeExchangeContext? ExchangeContext)
{
    public override string ToString()
        => $"NativeAuthCallbackResult {{ Disposition = {Disposition}, AuthTransactionId = {AuthTransactionId}, ProductCode = {ProductCode}, ExchangeContext = {(ExchangeContext is null ? "null" : "[REDACTED]")} }}";
}
