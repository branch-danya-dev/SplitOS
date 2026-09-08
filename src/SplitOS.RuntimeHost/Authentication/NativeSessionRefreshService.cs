using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using SplitOS.Persistence.ProtectedSecrets;
using SplitOS.Persistence.User;

namespace SplitOS.RuntimeHost.Authentication;

public enum NativeSessionRefreshDisposition
{
    Refreshed,
    AlreadyInProgress,
    ReauthRequired,
    BackendUnavailable,
    Rejected,
    PersistenceFailed,
    RecoveryRequired
}

public sealed record RefreshedNativeAccessSession(
    string AccountId,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresUtc,
    string? GrantedScope)
{
    public override string ToString()
        => $"RefreshedNativeAccessSession(AccountId={AccountId}, AccessToken=<redacted>, AccessTokenExpiresUtc={AccessTokenExpiresUtc:O}, GrantedScope={GrantedScope ?? "<none>"})";
}

public sealed record NativeSessionRefreshResult(
    NativeSessionRefreshDisposition Disposition,
    string ProductCode,
    bool Retryable,
    RefreshedNativeAccessSession? Session);

public sealed class NativeSessionRefreshService
{
    private const int MaximumTokenResponseBytes = 64 * 1024;
    private const int MaximumBearerTokenCharacters = 32 * 1024;
    private const int MaximumScopeCharacters = 4 * 1024;

    private readonly HttpClient _httpClient;
    private readonly NativeAuthAuthorityConfiguration _authority;
    private readonly IUserAccountAssociationStore _associationStore;
    private readonly IAccountSecretStore _secretStore;
    private readonly IWindowsUserContext _windowsUserContext;
    private readonly RuntimeStateRefreshSignal _refreshSignal;
    private readonly TimeProvider _timeProvider;
    private int _refreshInProgress;

    public NativeSessionRefreshService(
        HttpClient httpClient,
        NativeAuthAuthorityConfiguration authority,
        IUserAccountAssociationStore associationStore,
        IAccountSecretStore secretStore,
        IWindowsUserContext windowsUserContext,
        RuntimeStateRefreshSignal refreshSignal,
        TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _associationStore = associationStore ?? throw new ArgumentNullException(nameof(associationStore));
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        _windowsUserContext = windowsUserContext ?? throw new ArgumentNullException(nameof(windowsUserContext));
        _refreshSignal = refreshSignal ?? throw new ArgumentNullException(nameof(refreshSignal));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _authority.Validate();
    }

    public async Task<NativeSessionRefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _refreshInProgress, 1, 0) != 0)
        {
            return Result(
                NativeSessionRefreshDisposition.AlreadyInProgress,
                "SESSION_REFRESH_ALREADY_IN_PROGRESS",
                retryable: true);
        }

        try
        {
            return await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _refreshInProgress, 0);
        }
    }

    private async Task<NativeSessionRefreshResult> RefreshCoreAsync(CancellationToken cancellationToken)
    {
        UserAccountAssociationRecord? association;
        try
        {
            association = await _associationStore.GetAccountAssociationAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsLocalPersistenceFailure(ex))
        {
            return Result(
                NativeSessionRefreshDisposition.PersistenceFailed,
                "LOCAL_ASSOCIATION_STORE_FAILED",
                retryable: false);
        }

        if (association is null)
        {
            return Result(
                NativeSessionRefreshDisposition.ReauthRequired,
                "ACCOUNT_NOT_ASSOCIATED",
                retryable: false);
        }

        var currentSid = _windowsUserContext.GetCurrentUserSid();
        if (!string.Equals(currentSid, association.WindowsUserSid, StringComparison.OrdinalIgnoreCase))
        {
            return Result(
                NativeSessionRefreshDisposition.ReauthRequired,
                "LOCAL_ASSOCIATION_CONTEXT_MISMATCH",
                retryable: false);
        }

        if (!string.Equals(association.AssociationState, "ACTIVE", StringComparison.Ordinal))
        {
            return Result(
                NativeSessionRefreshDisposition.ReauthRequired,
                "REAUTH_REQUIRED",
                retryable: false);
        }

        AccountSecretReadResult secretRead;
        try
        {
            secretRead = await _secretStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsSecretPersistenceFailure(ex))
        {
            return await EnterReauthAsync(
                association,
                "LOCAL_SECRET_UNREADABLE",
                clearSecret: false).ConfigureAwait(false);
        }

        if (secretRead.Status != AccountSecretReadStatus.Available || secretRead.Secret is null)
        {
            return await EnterReauthAsync(
                association,
                secretRead.Status == AccountSecretReadStatus.Missing
                    ? "LOCAL_SECRET_MISSING"
                    : "LOCAL_SECRET_UNREADABLE",
                clearSecret: false).ConfigureAwait(false);
        }

        var secret = secretRead.Secret;
        if (!string.Equals(secret.AccountId, association.AccountId, StringComparison.Ordinal))
        {
            return await EnterReauthAsync(
                association,
                "LOCAL_SECRET_ACCOUNT_MISMATCH",
                clearSecret: true).ConfigureAwait(false);
        }

        var now = _timeProvider.GetUtcNow();
        if (secret.RefreshAbsoluteExpiryUtc <= now)
        {
            return await EnterReauthAsync(
                association,
                "REFRESH_TOKEN_EXPIRED",
                clearSecret: true).ConfigureAwait(false);
        }

        RefreshEndpointResult endpointResult;
        try
        {
            endpointResult = await SendRefreshAsync(secret.RefreshToken, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await EnterReauthAsync(
                association,
                "REFRESH_RESULT_UNKNOWN",
                clearSecret: true).ConfigureAwait(false);
            throw;
        }
        catch (OperationCanceledException)
        {
            return await EnterReauthAsync(
                association,
                "REFRESH_RESULT_UNKNOWN",
                clearSecret: true).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return await EnterReauthAsync(
                association,
                "REFRESH_RESULT_UNKNOWN",
                clearSecret: true).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return await EnterReauthAsync(
                association,
                "REFRESH_RESULT_UNKNOWN",
                clearSecret: true).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            return await EnterReauthAsync(
                association,
                "REFRESH_RESULT_INVALID",
                clearSecret: true).ConfigureAwait(false);
        }

        if (endpointResult.Disposition == RefreshEndpointDisposition.BackendUnavailable)
        {
            return Result(
                NativeSessionRefreshDisposition.BackendUnavailable,
                endpointResult.ProductCode,
                retryable: true);
        }

        if (endpointResult.Disposition == RefreshEndpointDisposition.InvalidGrant)
        {
            return await EnterReauthAsync(
                association,
                "REFRESH_TOKEN_REVOKED",
                clearSecret: true).ConfigureAwait(false);
        }

        if (endpointResult.Disposition != RefreshEndpointDisposition.Accepted || endpointResult.TokenSet is null)
        {
            return Result(
                NativeSessionRefreshDisposition.Rejected,
                endpointResult.ProductCode,
                retryable: false);
        }

        var tokenSet = endpointResult.TokenSet;
        if (string.Equals(tokenSet.RefreshToken, secret.RefreshToken, StringComparison.Ordinal))
        {
            return await EnterReauthAsync(
                association,
                "REFRESH_ROTATION_NOT_OBSERVED",
                clearSecret: true).ConfigureAwait(false);
        }

        DateTimeOffset accessTokenExpiresUtc;
        try
        {
            accessTokenExpiresUtc = now.AddSeconds(tokenSet.ExpiresInSeconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return await EnterReauthAsync(
                association,
                "REFRESH_RESULT_INVALID",
                clearSecret: true).ConfigureAwait(false);
        }

        var rotatedSecret = new AccountSecretEnvelope
        {
            AccountId = secret.AccountId,
            RefreshToken = tokenSet.RefreshToken,
            RefreshTokenFamilyId = secret.RefreshTokenFamilyId,
            RefreshIssuedUtc = now,
            RefreshAbsoluteExpiryUtc = secret.RefreshAbsoluteExpiryUtc,
            LastTrustedServerUtc = secret.LastTrustedServerUtc,
            LastTrustedServerObservationLocalUtc = secret.LastTrustedServerObservationLocalUtc,
            LastValidAssertionJti = secret.LastValidAssertionJti,
            OfflineEntitlementAssertion = secret.OfflineEntitlementAssertion,
            OfflineAssertionStoredUtc = secret.OfflineAssertionStoredUtc
        };

        try
        {
            await _secretStore.WriteAsync(rotatedSecret, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsSecretPersistenceFailure(ex))
        {
            var cleanup = await EnterReauthAsync(
                association,
                "TOKEN_ROTATED_BUT_LOCAL_PERSIST_FAILED",
                clearSecret: true).ConfigureAwait(false);
            return cleanup.Disposition == NativeSessionRefreshDisposition.RecoveryRequired
                ? cleanup
                : Result(
                    NativeSessionRefreshDisposition.ReauthRequired,
                    "TOKEN_ROTATED_BUT_LOCAL_PERSIST_FAILED",
                    retryable: false);
        }

        _refreshSignal.RequestRefresh();
        return new NativeSessionRefreshResult(
            NativeSessionRefreshDisposition.Refreshed,
            "SESSION_REFRESHED",
            false,
            new RefreshedNativeAccessSession(
                association.AccountId,
                tokenSet.AccessToken,
                accessTokenExpiresUtc,
                tokenSet.Scope));
    }

    private async Task<RefreshEndpointResult> SendRefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _authority.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = _authority.ClientId
            })
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
            {
                return new RefreshEndpointResult(
                    RefreshEndpointDisposition.BackendUnavailable,
                    "AUTH_BACKEND_UNAVAILABLE",
                    null);
            }

            var oauthError = await TryReadOAuthErrorAsync(response, cancellationToken).ConfigureAwait(false);
            if (string.Equals(oauthError, "invalid_grant", StringComparison.Ordinal))
            {
                return new RefreshEndpointResult(
                    RefreshEndpointDisposition.InvalidGrant,
                    "REFRESH_TOKEN_REVOKED",
                    null);
            }

            return new RefreshEndpointResult(
                RefreshEndpointDisposition.Rejected,
                "REFRESH_REQUEST_REJECTED",
                null);
        }

        var root = await ReadBoundedJsonAsync(response, cancellationToken).ConfigureAwait(false);
        RequireObject(root);
        EnsureNoDuplicateProperties(root);

        var accessToken = GetRequiredString(root, "access_token");
        var tokenType = GetRequiredString(root, "token_type");
        var rotatedRefreshToken = GetRequiredString(root, "refresh_token");
        if (!string.Equals(tokenType, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Refresh response used an unsupported token type.");
        }

        EnsureBoundedToken(accessToken, "access_token");
        EnsureBoundedToken(rotatedRefreshToken, "refresh_token");

        if (!root.TryGetProperty("expires_in", out var expiresElement) ||
            expiresElement.ValueKind != JsonValueKind.Number ||
            !expiresElement.TryGetInt64(out var expiresInSeconds) ||
            expiresInSeconds <= 0)
        {
            throw new InvalidDataException("Refresh response omitted a valid positive expires_in value.");
        }

        var scope = GetOptionalString(root, "scope");
        if (scope is not null &&
            (scope.Length > MaximumScopeCharacters || scope.Any(static character => char.IsControl(character))))
        {
            throw new InvalidDataException("Refresh response scope exceeded supported bounds.");
        }

        return new RefreshEndpointResult(
            RefreshEndpointDisposition.Accepted,
            "OK",
            new RefreshTokenSet(accessToken, rotatedRefreshToken, scope, expiresInSeconds));
    }

    private async Task<string?> TryReadOAuthErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var root = await ReadBoundedJsonAsync(response, cancellationToken).ConfigureAwait(false);
            RequireObject(root);
            EnsureNoDuplicateProperties(root);
            return root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String
                ? error.GetString()
                : null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private async Task<JsonElement> ReadBoundedJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is not null &&
            !string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase) &&
            !mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Refresh response content type is not JSON.");
        }

        if (response.Content.Headers.ContentLength > MaximumTokenResponseBytes)
        {
            throw new InvalidDataException("Refresh response exceeded the allowed size.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var rented = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(rented.AsMemory(0, rented.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                if (memory.Length + read > MaximumTokenResponseBytes)
                {
                    throw new InvalidDataException("Refresh response exceeded the allowed size.");
                }
                memory.Write(rented, 0, read);
            }

            if (!memory.TryGetBuffer(out var buffer) || buffer.Array is null)
            {
                throw new InvalidDataException("Refresh response buffer could not be inspected.");
            }

            using var document = JsonDocument.Parse(
                new ReadOnlyMemory<byte>(buffer.Array, buffer.Offset, checked((int)memory.Length)),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 16
                });
            return document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Refresh response JSON was malformed.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rented);
            ArrayPool<byte>.Shared.Return(rented);
            if (memory.TryGetBuffer(out var buffer) && buffer.Array is not null)
            {
                CryptographicOperations.ZeroMemory(buffer.Array.AsSpan(buffer.Offset, checked((int)memory.Length)));
            }
        }
    }

    private async Task<NativeSessionRefreshResult> EnterReauthAsync(
        UserAccountAssociationRecord association,
        string productCode,
        bool clearSecret)
    {
        var secretCleared = true;
        if (clearSecret)
        {
            try
            {
                await _secretStore.DeleteAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsSecretPersistenceFailure(ex))
            {
                secretCleared = false;
            }
        }

        var associationConverged = await MarkReauthRequiredAsync(association).ConfigureAwait(false);
        _refreshSignal.RequestRefresh();

        if (!secretCleared || !associationConverged)
        {
            return Result(
                NativeSessionRefreshDisposition.RecoveryRequired,
                "LOCAL_SECRET_RECONCILIATION_REQUIRED",
                retryable: false);
        }

        return Result(NativeSessionRefreshDisposition.ReauthRequired, productCode, retryable: false);
    }

    private async Task<bool> MarkReauthRequiredAsync(UserAccountAssociationRecord association)
    {
        if (string.Equals(association.AssociationState, "REAUTH_REQUIRED", StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            var outcome = await _associationStore.MarkReauthRequiredAsync(
                association.Revision,
                Guid.NewGuid(),
                CancellationToken.None).ConfigureAwait(false);

            if (outcome.Disposition is UserAssociationWriteDisposition.Applied or UserAssociationWriteDisposition.Unchanged or UserAssociationWriteDisposition.Missing)
            {
                return true;
            }

            if (outcome.Disposition == UserAssociationWriteDisposition.RevisionConflict && outcome.Record is not null)
            {
                if (string.Equals(outcome.Record.AssociationState, "REAUTH_REQUIRED", StringComparison.Ordinal))
                {
                    return true;
                }

                if (!string.Equals(outcome.Record.AccountId, association.AccountId, StringComparison.Ordinal) ||
                    !string.Equals(outcome.Record.WindowsUserSid, association.WindowsUserSid, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var retry = await _associationStore.MarkReauthRequiredAsync(
                    outcome.Record.Revision,
                    Guid.NewGuid(),
                    CancellationToken.None).ConfigureAwait(false);
                return retry.Disposition is UserAssociationWriteDisposition.Applied or UserAssociationWriteDisposition.Unchanged or UserAssociationWriteDisposition.Missing;
            }

            return false;
        }
        catch (Exception ex) when (IsLocalPersistenceFailure(ex))
        {
            return false;
        }
    }

    private static void RequireObject(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Refresh response must be a JSON object.");
        }
    }

    private static void EnsureNoDuplicateProperties(JsonElement root)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                throw new InvalidDataException($"Refresh response contains duplicate property '{property.Name}'.");
            }
        }
    }

    private static string GetRequiredString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"Refresh response omitted required string '{propertyName}'.");
        }

        var result = value.GetString();
        if (string.IsNullOrWhiteSpace(result) || result.Any(static character => char.IsControl(character)))
        {
            throw new InvalidDataException($"Refresh response field '{propertyName}' is malformed.");
        }
        return result;
    }

    private static string? GetOptionalString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"Refresh response field '{propertyName}' is malformed.");
        }
        return value.GetString();
    }

    private static void EnsureBoundedToken(string value, string fieldName)
    {
        if (value.Length > MaximumBearerTokenCharacters)
        {
            throw new InvalidDataException($"Refresh response field '{fieldName}' exceeded supported bounds.");
        }
    }

    private static NativeSessionRefreshResult Result(
        NativeSessionRefreshDisposition disposition,
        string productCode,
        bool retryable)
        => new(disposition, productCode, retryable, null);

    private static bool IsLocalPersistenceFailure(Exception ex)
        => ex is IOException or UnauthorizedAccessException or InvalidDataException;

    private static bool IsSecretPersistenceFailure(Exception ex)
        => ex is IOException or UnauthorizedAccessException or CryptographicException or InvalidDataException;

    private enum RefreshEndpointDisposition
    {
        Accepted,
        InvalidGrant,
        BackendUnavailable,
        Rejected
    }

    private sealed record RefreshTokenSet(
        string AccessToken,
        string RefreshToken,
        string? Scope,
        long ExpiresInSeconds);

    private sealed record RefreshEndpointResult(
        RefreshEndpointDisposition Disposition,
        string ProductCode,
        RefreshTokenSet? TokenSet);
}
