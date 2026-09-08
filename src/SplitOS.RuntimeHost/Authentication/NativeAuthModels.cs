namespace SplitOS.RuntimeHost.Authentication;

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
