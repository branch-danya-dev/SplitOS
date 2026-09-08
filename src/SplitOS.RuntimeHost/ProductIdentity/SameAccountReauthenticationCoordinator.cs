using System.Globalization;
using System.Security.Cryptography;
using SplitOS.Persistence.ProtectedSecrets;
using SplitOS.Persistence.User;
using SplitOS.RuntimeHost.Authentication;

namespace SplitOS.RuntimeHost.ProductIdentity;

public enum SameAccountReauthenticationDisposition
{
    Reactivated,
    ReactivatedDegraded,
    NotRequired,
    InitialAssociationRequired,
    AccountSwitchRequired,
    AuthRequired,
    AccountDisabled,
    BackendUnavailable,
    ReusableSessionRequired,
    Rejected,
    PersistenceFailed,
    RecoveryRequired
}

public sealed record SameAccountReauthenticationContext(
    string ClientVersion,
    string InstallationId,
    Guid CorrelationId,
    Guid OperationId)
{
    public void Validate()
    {
        ValidateProtocolValue(ClientVersion, 128, nameof(ClientVersion));
        ValidateProtocolValue(InstallationId, 256, nameof(InstallationId));
        if (CorrelationId == Guid.Empty)
        {
            throw new ArgumentException("Correlation id must not be empty.", nameof(CorrelationId));
        }

        if (OperationId == Guid.Empty)
        {
            throw new ArgumentException("Operation id must not be empty.", nameof(OperationId));
        }
    }

    private static void ValidateProtocolValue(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > maximumLength ||
            value.Any(static character => char.IsControl(character)))
        {
            throw new ArgumentException($"{parameterName} is missing or outside the supported bounds.", parameterName);
        }
    }
}

public sealed record SameAccountReauthenticationPolicy(TimeSpan RefreshTokenLocalAbsoluteLifetime)
{
    public static SameAccountReauthenticationPolicy Default { get; } = new(TimeSpan.FromDays(90));

    public void Validate()
    {
        if (RefreshTokenLocalAbsoluteLifetime <= TimeSpan.Zero ||
            RefreshTokenLocalAbsoluteLifetime > TimeSpan.FromDays(90))
        {
            throw new ArgumentOutOfRangeException(
                nameof(RefreshTokenLocalAbsoluteLifetime),
                "The local refresh-token safety ceiling must be greater than zero and no more than 90 days.");
        }
    }
}

public sealed record SameAccountReauthenticationResult(
    SameAccountReauthenticationDisposition Disposition,
    string ProductCode,
    string? AccountId,
    string? AssociationId,
    bool EntitlementResolved)
{
    public bool IsActive
        => Disposition is SameAccountReauthenticationDisposition.Reactivated or
            SameAccountReauthenticationDisposition.ReactivatedDegraded;
}

public sealed class SameAccountReauthenticationCoordinator
{
    private const string ProtectedSecretReference = "account.v1";

    private readonly ProductApiClient _productApiClient;
    private readonly IUserAccountAssociationStore _associationStore;
    private readonly IUserAccountAssociationReactivationStore _reactivationStore;
    private readonly IAccountSecretStore _secretStore;
    private readonly IWindowsUserContext _windowsUserContext;
    private readonly RuntimeStateRefreshSignal _refreshSignal;
    private readonly SameAccountReauthenticationPolicy _policy;
    private readonly TimeProvider _timeProvider;
    private int _reauthenticationInProgress;

    public SameAccountReauthenticationCoordinator(
        ProductApiClient productApiClient,
        IUserAccountAssociationStore associationStore,
        IUserAccountAssociationReactivationStore reactivationStore,
        IAccountSecretStore secretStore,
        IWindowsUserContext windowsUserContext,
        RuntimeStateRefreshSignal refreshSignal,
        SameAccountReauthenticationPolicy? policy = null,
        TimeProvider? timeProvider = null)
    {
        _productApiClient = productApiClient ?? throw new ArgumentNullException(nameof(productApiClient));
        _associationStore = associationStore ?? throw new ArgumentNullException(nameof(associationStore));
        _reactivationStore = reactivationStore ?? throw new ArgumentNullException(nameof(reactivationStore));
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        _windowsUserContext = windowsUserContext ?? throw new ArgumentNullException(nameof(windowsUserContext));
        _refreshSignal = refreshSignal ?? throw new ArgumentNullException(nameof(refreshSignal));
        _policy = policy ?? SameAccountReauthenticationPolicy.Default;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _policy.Validate();
    }

    public async Task<SameAccountReauthenticationResult> ReactivateAsync(
        ValidatedNativeAuthSession session,
        SameAccountReauthenticationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(context);
        context.Validate();

        if (Interlocked.CompareExchange(ref _reauthenticationInProgress, 1, 0) != 0)
        {
            return Reject(
                SameAccountReauthenticationDisposition.Rejected,
                "REAUTH_ALREADY_IN_PROGRESS");
        }

        try
        {
            return await ReactivateCoreAsync(session, context, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _reauthenticationInProgress, 0);
        }
    }

    private async Task<SameAccountReauthenticationResult> ReactivateCoreAsync(
        ValidatedNativeAuthSession session,
        SameAccountReauthenticationContext context,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        if (session.AccessTokenExpiresUtc <= now)
        {
            return Reject(SameAccountReauthenticationDisposition.AuthRequired, "AUTH_SESSION_EXPIRED");
        }

        if (string.IsNullOrWhiteSpace(session.RefreshToken))
        {
            return Reject(
                SameAccountReauthenticationDisposition.ReusableSessionRequired,
                "AUTH_REUSABLE_SESSION_REQUIRED");
        }

        UserAccountAssociationRecord? existing;
        try
        {
            existing = await _associationStore.GetAccountAssociationAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsLocalPersistenceFailure(exception))
        {
            return Reject(
                SameAccountReauthenticationDisposition.PersistenceFailed,
                "LOCAL_ASSOCIATION_STORE_FAILED");
        }

        if (existing is null)
        {
            return Reject(
                SameAccountReauthenticationDisposition.InitialAssociationRequired,
                "ACCOUNT_ASSOCIATION_REQUIRED");
        }

        var currentSid = _windowsUserContext.GetCurrentUserSid();
        if (!string.Equals(existing.WindowsUserSid, currentSid, StringComparison.OrdinalIgnoreCase))
        {
            return new SameAccountReauthenticationResult(
                SameAccountReauthenticationDisposition.Rejected,
                "LOCAL_ASSOCIATION_CONTEXT_MISMATCH",
                existing.AccountId,
                existing.AssociationId,
                false);
        }

        if (string.Equals(existing.AssociationState, "ACTIVE", StringComparison.Ordinal))
        {
            return new SameAccountReauthenticationResult(
                SameAccountReauthenticationDisposition.NotRequired,
                "ACCOUNT_REAUTH_NOT_REQUIRED",
                existing.AccountId,
                existing.AssociationId,
                false);
        }

        if (!string.Equals(existing.AssociationState, "REAUTH_REQUIRED", StringComparison.Ordinal))
        {
            return new SameAccountReauthenticationResult(
                SameAccountReauthenticationDisposition.Rejected,
                "LOCAL_ASSOCIATION_STATE_REJECTED",
                existing.AccountId,
                existing.AssociationId,
                false);
        }

        var apiContext = new ProductApiRequestContext(
            session.AccessToken,
            context.ClientVersion,
            context.InstallationId,
            context.CorrelationId);

        var accountResult = await _productApiClient.GetAccountAsync(apiContext, cancellationToken).ConfigureAwait(false);
        if (accountResult.Disposition != ProductApiDisposition.Accepted || accountResult.Value is null)
        {
            return MapAccountFailure(accountResult, existing);
        }

        var account = accountResult.Value;
        if (string.Equals(account.Status, "DISABLED", StringComparison.Ordinal))
        {
            return new SameAccountReauthenticationResult(
                SameAccountReauthenticationDisposition.AccountDisabled,
                "ACCOUNT_DISABLED",
                existing.AccountId,
                existing.AssociationId,
                false);
        }

        if (!string.Equals(account.AccountId, existing.AccountId, StringComparison.Ordinal))
        {
            return new SameAccountReauthenticationResult(
                SameAccountReauthenticationDisposition.AccountSwitchRequired,
                "ACCOUNT_SWITCH_REQUIRED",
                existing.AccountId,
                existing.AssociationId,
                false);
        }

        var entitlementResult = await _productApiClient.GetCurrentEntitlementAsync(
            apiContext,
            existing.AccountId,
            cancellationToken).ConfigureAwait(false);

        if (entitlementResult.Disposition == ProductApiDisposition.AuthRequired)
        {
            return new SameAccountReauthenticationResult(
                SameAccountReauthenticationDisposition.AuthRequired,
                entitlementResult.ProductCode,
                existing.AccountId,
                existing.AssociationId,
                false);
        }

        if (entitlementResult.Disposition == ProductApiDisposition.AccountDisabled)
        {
            return new SameAccountReauthenticationResult(
                SameAccountReauthenticationDisposition.AccountDisabled,
                entitlementResult.ProductCode,
                existing.AccountId,
                existing.AssociationId,
                false);
        }

        var entitlementResolved = entitlementResult.Disposition == ProductApiDisposition.Accepted &&
                                  entitlementResult.Value is not null;
        var entitlement = entitlementResolved ? entitlementResult.Value : null;

        try
        {
            // REAUTH_REQUIRED means prior reusable credentials and any context-bound offline proof are not
            // allowed to survive into the newly authenticated session.
            await _secretStore.DeleteAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsSecretPersistenceFailure(exception))
        {
            return new SameAccountReauthenticationResult(
                SameAccountReauthenticationDisposition.PersistenceFailed,
                "LOCAL_SECRET_CLEANUP_FAILED",
                existing.AccountId,
                existing.AssociationId,
                entitlementResolved);
        }

        var freshSecret = new AccountSecretEnvelope
        {
            AccountId = existing.AccountId,
            RefreshToken = session.RefreshToken,
            RefreshTokenFamilyId = null,
            RefreshIssuedUtc = now,
            RefreshAbsoluteExpiryUtc = now.Add(_policy.RefreshTokenLocalAbsoluteLifetime),
            LastTrustedServerUtc = entitlement?.ServerUtc,
            OfflineEntitlementAssertion = null,
            OfflineAssertionStoredUtc = null
        };

        try
        {
            await _secretStore.WriteAsync(freshSecret, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsSecretPersistenceFailure(exception))
        {
            return new SameAccountReauthenticationResult(
                SameAccountReauthenticationDisposition.PersistenceFailed,
                "LOCAL_SECRET_STORE_FAILED",
                existing.AccountId,
                existing.AssociationId,
                entitlementResolved);
        }

        UserAssociationWriteOutcome reactivationOutcome;
        try
        {
            reactivationOutcome = await _reactivationStore.ReactivateSameAccountAsync(
                existing.AssociationId,
                currentSid,
                existing.AccountId,
                existing.Revision,
                now,
                ProtectedSecretReference,
                entitlement?.EntitlementVersion.ToString(CultureInfo.InvariantCulture),
                entitlementResolved ? now : null,
                entitlement?.ServerUtc,
                context.OperationId,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CleanupFreshSecretAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception) when (IsLocalPersistenceFailure(exception))
        {
            return await FailAfterSecretWriteAsync(
                "LOCAL_ASSOCIATION_STORE_FAILED",
                existing,
                entitlementResolved).ConfigureAwait(false);
        }

        if (reactivationOutcome.Disposition != UserAssociationWriteDisposition.Applied ||
            reactivationOutcome.Record is null)
        {
            return await FailAfterSecretWriteAsync(
                "LOCAL_ASSOCIATION_RACE",
                existing,
                entitlementResolved).ConfigureAwait(false);
        }

        _refreshSignal.RequestRefresh();
        return new SameAccountReauthenticationResult(
            entitlementResolved
                ? SameAccountReauthenticationDisposition.Reactivated
                : SameAccountReauthenticationDisposition.ReactivatedDegraded,
            entitlementResolved ? "ACCOUNT_REAUTHENTICATED" : entitlementResult.ProductCode,
            reactivationOutcome.Record.AccountId,
            reactivationOutcome.Record.AssociationId,
            entitlementResolved);
    }

    private async Task<SameAccountReauthenticationResult> FailAfterSecretWriteAsync(
        string productCode,
        UserAccountAssociationRecord existing,
        bool entitlementResolved)
    {
        if (!await CleanupFreshSecretAsync(CancellationToken.None).ConfigureAwait(false))
        {
            return new SameAccountReauthenticationResult(
                SameAccountReauthenticationDisposition.RecoveryRequired,
                "LOCAL_SECRET_RECONCILIATION_REQUIRED",
                existing.AccountId,
                existing.AssociationId,
                entitlementResolved);
        }

        return new SameAccountReauthenticationResult(
            SameAccountReauthenticationDisposition.PersistenceFailed,
            productCode,
            existing.AccountId,
            existing.AssociationId,
            entitlementResolved);
    }

    private async Task<bool> CleanupFreshSecretAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _secretStore.DeleteAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (IsSecretPersistenceFailure(exception))
        {
            return false;
        }
    }

    private static SameAccountReauthenticationResult MapAccountFailure(
        ProductApiResult<SplitOSAccountProfile> result,
        UserAccountAssociationRecord existing)
        => result.Disposition switch
        {
            ProductApiDisposition.AuthRequired => new SameAccountReauthenticationResult(
                SameAccountReauthenticationDisposition.AuthRequired,
                result.ProductCode,
                existing.AccountId,
                existing.AssociationId,
                false),
            ProductApiDisposition.AccountDisabled => new SameAccountReauthenticationResult(
                SameAccountReauthenticationDisposition.AccountDisabled,
                result.ProductCode,
                existing.AccountId,
                existing.AssociationId,
                false),
            ProductApiDisposition.BackendUnavailable or ProductApiDisposition.RateLimited =>
                new SameAccountReauthenticationResult(
                    SameAccountReauthenticationDisposition.BackendUnavailable,
                    result.ProductCode,
                    existing.AccountId,
                    existing.AssociationId,
                    false),
            _ => new SameAccountReauthenticationResult(
                SameAccountReauthenticationDisposition.Rejected,
                result.ProductCode,
                existing.AccountId,
                existing.AssociationId,
                false)
        };

    private static SameAccountReauthenticationResult Reject(
        SameAccountReauthenticationDisposition disposition,
        string productCode)
        => new(disposition, productCode, null, null, false);

    private static bool IsLocalPersistenceFailure(Exception exception)
        => exception is IOException or UnauthorizedAccessException or InvalidDataException;

    private static bool IsSecretPersistenceFailure(Exception exception)
        => exception is IOException or UnauthorizedAccessException or CryptographicException or InvalidDataException;
}
