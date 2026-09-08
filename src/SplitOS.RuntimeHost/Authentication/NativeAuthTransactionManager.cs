using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace SplitOS.RuntimeHost.Authentication;

public sealed class NativeAuthTransactionManager
{
    private const string CallbackPath = "/oauth/callback";
    private readonly object _gate = new();
    private readonly NativeAuthAuthorityConfiguration _authority;
    private readonly IWindowsUserContext _windowsUserContext;
    private readonly TimeProvider _timeProvider;
    private NativeAuthTransaction? _active;

    public NativeAuthTransactionManager(
        NativeAuthAuthorityConfiguration authority,
        IWindowsUserContext windowsUserContext,
        TimeProvider? timeProvider = null)
    {
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _windowsUserContext = windowsUserContext ?? throw new ArgumentNullException(nameof(windowsUserContext));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _authority.Validate();
    }

    public NativeAuthStartResult Start(Uri redirectUri)
    {
        ArgumentNullException.ThrowIfNull(redirectUri);
        ValidateRedirectUri(redirectUri);

        lock (_gate)
        {
            var now = _timeProvider.GetUtcNow();
            ExpireWithoutResult(now);
            if (_active is not null)
            {
                return new NativeAuthStartResult(NativeAuthStartDisposition.AlreadyInProgress, _active);
            }

            var state = GenerateRandomBase64Url(32);
            var nonce = GenerateRandomBase64Url(32);
            var codeVerifier = GenerateRandomBase64Url(32);
            var codeChallenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
            var scopes = _authority.RequestedScopes.ToArray();
            var transaction = new NativeAuthTransaction(
                Guid.NewGuid(),
                Process.GetCurrentProcess().SessionId,
                HashSidReference(_windowsUserContext.GetCurrentUserSid()),
                now,
                now.Add(_authority.TransactionLifetime),
                redirectUri,
                state,
                nonce,
                codeVerifier,
                codeChallenge,
                Array.AsReadOnly(scopes),
                BuildAuthorizationUri(redirectUri, state, nonce, codeChallenge, scopes));

            _active = transaction;
            return new NativeAuthStartResult(NativeAuthStartDisposition.Started, transaction);
        }
    }

    public NativeAuthActiveSnapshot? GetActiveSnapshot()
    {
        lock (_gate)
        {
            ExpireWithoutResult(_timeProvider.GetUtcNow());
            return _active is null
                ? null
                : new NativeAuthActiveSnapshot(
                    _active.AuthTransactionId,
                    _active.CreatedUtc,
                    _active.ExpiresUtc,
                    _active.WindowsSessionId);
        }
    }

    public bool Cancel(Guid authTransactionId)
    {
        lock (_gate)
        {
            ExpireWithoutResult(_timeProvider.GetUtcNow());
            if (_active is null || _active.AuthTransactionId != authTransactionId)
            {
                return false;
            }

            _active = null;
            return true;
        }
    }

    public NativeAuthCallbackResult ConsumeCallback(Uri callbackUri)
    {
        ArgumentNullException.ThrowIfNull(callbackUri);

        lock (_gate)
        {
            if (_active is null)
            {
                return Result(NativeAuthCallbackDisposition.NoActiveTransaction, null, "AUTH_NO_ACTIVE_TRANSACTION");
            }

            var transaction = _active;
            var now = _timeProvider.GetUtcNow();
            if (now >= transaction.ExpiresUtc)
            {
                _active = null;
                return Result(NativeAuthCallbackDisposition.Expired, transaction.AuthTransactionId, "AUTH_TIMEOUT");
            }

            if (!CallbackTargetsActiveListener(callbackUri, transaction.RedirectUri) ||
                !TryParseQuery(callbackUri, out var values) ||
                !values.TryGetValue("state", out var returnedState) ||
                !FixedTimeEquals(transaction.State, returnedState))
            {
                _active = null;
                return Result(NativeAuthCallbackDisposition.ResultRejected, transaction.AuthTransactionId, "AUTH_RESULT_REJECTED");
            }

            if (values.TryGetValue("error", out var oauthError) && !string.IsNullOrWhiteSpace(oauthError))
            {
                _active = null;
                return oauthError switch
                {
                    "access_denied" => Result(NativeAuthCallbackDisposition.Cancelled, transaction.AuthTransactionId, "AUTH_CANCELLED"),
                    "login_required" => Result(NativeAuthCallbackDisposition.LoginRequired, transaction.AuthTransactionId, "AUTH_LOGIN_REQUIRED"),
                    "server_error" => Result(NativeAuthCallbackDisposition.ServerError, transaction.AuthTransactionId, "AUTH_SERVER_ERROR"),
                    _ => Result(NativeAuthCallbackDisposition.ResultRejected, transaction.AuthTransactionId, "AUTH_RESULT_REJECTED")
                };
            }

            if (!values.TryGetValue("code", out var authorizationCode) || string.IsNullOrWhiteSpace(authorizationCode))
            {
                _active = null;
                return Result(NativeAuthCallbackDisposition.ResultRejected, transaction.AuthTransactionId, "AUTH_RESULT_REJECTED");
            }

            var exchange = new NativeAuthCodeExchangeContext(
                transaction.AuthTransactionId,
                authorizationCode,
                transaction.CodeVerifier,
                transaction.Nonce,
                transaction.RedirectUri,
                _authority.ClientId);

            _active = null;
            return new NativeAuthCallbackResult(
                NativeAuthCallbackDisposition.Accepted,
                transaction.AuthTransactionId,
                "AUTH_CODE_ACCEPTED",
                exchange);
        }
    }

    private Uri BuildAuthorizationUri(
        Uri redirectUri,
        string state,
        string nonce,
        string codeChallenge,
        IReadOnlyList<string> scopes)
    {
        var query = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["response_type"] = "code",
            ["client_id"] = _authority.ClientId,
            ["redirect_uri"] = redirectUri.AbsoluteUri,
            ["scope"] = string.Join(' ', scopes),
            ["state"] = state,
            ["nonce"] = nonce,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256"
        };

        var builder = new UriBuilder(_authority.AuthorizationEndpoint)
        {
            Query = string.Join("&", query.Select(static pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"))
        };
        return builder.Uri;
    }

    private void ExpireWithoutResult(DateTimeOffset now)
    {
        if (_active is not null && now >= _active.ExpiresUtc)
        {
            _active = null;
        }
    }

    private static NativeAuthCallbackResult Result(
        NativeAuthCallbackDisposition disposition,
        Guid? transactionId,
        string productCode)
        => new(disposition, transactionId, productCode, null);

    private static void ValidateRedirectUri(Uri redirectUri)
    {
        if (!redirectUri.IsAbsoluteUri ||
            !string.Equals(redirectUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(redirectUri.Host, "127.0.0.1", StringComparison.Ordinal) ||
            redirectUri.IsDefaultPort ||
            redirectUri.Port <= 1024 ||
            !string.Equals(redirectUri.AbsolutePath, CallbackPath, StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(redirectUri.Query) ||
            !string.IsNullOrEmpty(redirectUri.Fragment) ||
            !string.IsNullOrEmpty(redirectUri.UserInfo))
        {
            throw new ArgumentException(
                "Native auth redirect must be an explicit http://127.0.0.1:<ephemeral-port>/oauth/callback URI.",
                nameof(redirectUri));
        }
    }

    private static bool CallbackTargetsActiveListener(Uri callbackUri, Uri redirectUri)
        => callbackUri.IsAbsoluteUri &&
           string.Equals(callbackUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(callbackUri.Host, "127.0.0.1", StringComparison.Ordinal) &&
           callbackUri.Port == redirectUri.Port &&
           string.Equals(callbackUri.AbsolutePath, redirectUri.AbsolutePath, StringComparison.Ordinal) &&
           string.IsNullOrEmpty(callbackUri.Fragment) &&
           string.IsNullOrEmpty(callbackUri.UserInfo);

    private static bool TryParseQuery(Uri uri, out Dictionary<string, string> values)
    {
        values = new Dictionary<string, string>(StringComparer.Ordinal);
        var query = uri.Query;
        if (string.IsNullOrEmpty(query))
        {
            return true;
        }

        foreach (var pair in query.AsSpan(1).ToString().Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var rawKey = separator >= 0 ? pair[..separator] : pair;
            var rawValue = separator >= 0 ? pair[(separator + 1)..] : string.Empty;
            var key = WebUtility.UrlDecode(rawKey);
            var value = WebUtility.UrlDecode(rawValue);
            if (string.IsNullOrEmpty(key) || !values.TryAdd(key, value))
            {
                return false;
            }
        }

        return true;
    }

    private static string GenerateRandomBase64Url(int byteCount)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteCount);
        try
        {
            return Base64UrlEncode(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static string HashSidReference(string sid)
    {
        if (string.IsNullOrWhiteSpace(sid))
        {
            throw new InvalidOperationException("Current Windows user SID is unavailable.");
        }

        var bytes = Encoding.UTF8.GetBytes(sid);
        try
        {
            return $"sha256:{Base64UrlEncode(SHA256.HashData(bytes))}";
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
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

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
