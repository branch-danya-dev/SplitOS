using System.Globalization;
using System.Security.Cryptography;
using SplitOS.Persistence.ProtectedSecrets;
using SplitOS.Persistence.User;
using SplitOS.RuntimeHost.Authentication;

namespace SplitOS.RuntimeHost.ProductIdentity;

public enum DurableLoginBootstrapDisposition
{
    Associated,
    AssociatedDegraded,
    AlreadyAssociated,
    AccountSwitchRequired,
    AuthRequired,
    AccountDisabled,
    BackendUnavailable,
    ReusableSessionRequired,
    Rejected,
    PersistenceFailed,
    RecoveryRequired
}

public sealed record DurableLoginBootstrapContext(
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

public sealed record DurableLoginBootstrapPolicy(TimeSpan RefreshTokenLocalAbsoluteLifetime)
{
    public static DurableLoginBootstrapPolicy Default { get; } = new(TimeSpan.FromDays(90));

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

public sealed record DurableLoginBootstrapResult(
    DurableLoginBootstrapDisposition Disposition,
    string ProductCode,
    string? AccountId,
    string? AssociationId,
    bool EntitlementResolved)
{
    public bool IsDurablyAssociated
        => Disposition is DurableLoginBootstrapDisposition.Associated or DurableLoginBootstrapDisposition.AssociatedDegraded;
}

public sealed class DurableLoginBootstrapCoordinator
{
    private const string ProtectedSecretReference = "account.v1";

    private readonly ProductApiClient _productApiClient;
    private readonly IUserAccountAssociationStore _associationStore;
    private readonly IAccountSecretStore _secretStore;
    private readonly IWindowsUserContext _windowsUserContext;
    private readonly RuntimeStateRefreshSignal _refreshSignal;
    private readonly DurableLoginBootstrapPolicy _policy;
    private readonly TimeProvider _timeProvider;
    private int _bootstrapInProgress;

    public DurableLoginBootstrapCoordinator(
        ProductApiClient productApiClient,
        IUserAccountAssociationStore associationStore,
        IAccountSecretStore secretStore,
        IWindowsUserContext windowsUserContext,
        RuntimeStateRefreshSignal refreshSignal,
        DurableLoginBootstrapPolicy? policy = null,
        TimeProvider? timeProvider = null)
    {
        _productApiClient = productApiClient ?? throw new ArgumentNullException(nameof(productApiClient));
        _associationStore = associationStore ?? throw new ArgumentNullException(nameof(associationStore));
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        _windowsUserContext = windowsUserContext ?? throw new ArgumentNullException(nameof(windowsUserContext));
        _refreshSignal = refreshSignal ?? throw new ArgumentNullException(nameof(refreshSignal));
        _policy = policy ?? DurableLoginBootstrapPolicy.Default;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _policy.Validate();
    }

    public async Task<DurableLoginBootstrapResult> BootstrapInitialAssociationAsync(
        ValidatedNativeAuthSession session,
        DurableLoginBootstrapContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(context);
        context.Validate();

        if (Interlocked.CompareExchange(ref _bootstrapInProgress, 1, 0) != 0)
        {
            return Reject(
                DurableLoginBootstrapDisposition.Rejected,
                "LOGIN_BOOTSTRAP_ALREADY_IN_PROGRESS");
        }

        try
        {
            return await BootstrapCoreAsync(session, context, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _bootstrapInProgress, 0);
        }
    }

    private async Task<DurableLoginBootstrapResult> BootstrapCoreAsync(
        ValidatedNativeAuthSession session,
        DurableLoginBootstrapContext context,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        if (session.AccessTokenExpiresUtc <= now)
        {
            return Reject(DurableLoginBootstrapDisposition.AuthRequired, "AUTH_SESSION_EXPIRED");
        }

        if (string.IsNullOrWhiteSpace(session.RefreshToken))
        {
            return Reject(
                DurableLoginBootstrapDisposition.ReusableSessionRequired,
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
            return Reject(DurableLoginBootstrapDisposition.PersistenceFailed, "LOCAL_ASSOCIATION_STORE_FAILED");
        }

        var currentSid = _windowsUserContext.GetCurrentUserSid();
        if (existing is not null &&
            !string.Equals(existing.WindowsUserSid, currentSid, StringComparison.OrdinalIgnoreCase))
        {
            return new DurableLoginBootstrapResult(
                DurableLoginBootstrapDisposition.Rejected,
                "LOCAL_ASSOCIATION_CONTEXT_MISMATCH",
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
            return MapAccountFailure(accountResult);
        }

        var account = accountResult.Value;
        if (string.Equals(account.Status, "DISABLED", StringComparison.Ordinal))
        {
            return new DurableLoginBootstrapResult(
                DurableLoginBootstrapDisposition.AccountDisabled,
                "ACCOUNT_DISABLED",
                account.AccountId,
                null,
                false);
        }

        if (existing is not null)
        {
            if (string.Equals(existing.AccountId, account.AccountId, StringComparison.Ordinal))
            {
                return new DurableLoginBootstrapResult(
                    DurableLoginBootstrapDisposition.AlreadyAssociated,
                    "ACCOUNT_ALREADY_ASSOCIATED",
                    existing.AccountId,
                    existing.AssociationId,
                    false);
            }

            return new DurableLoginBootstrapResult(
                DurableLoginBootstrapDisposition.AccountSwitchRequired,
                "ACCOUNT_SWITCH_REQUIRED",
                existing.AccountId,
                existing.AssociationId,
                false);
        }

        var entitlementResult = await _productApiClient.GetCurrentEntitlementAsync(
            apiContext,
            account.AccountId,
            cancellationToken).ConfigureAwait(false);

        if (entitlementResult.Disposition == ProductApiDisposition.AuthRequired)
        {
            return new DurableLoginBootstrapResult(
                DurableLoginBootstrapDisposition.AuthRequired,
                entitlementResult.ProductCode,
                account.AccountId,
                null,
                false);
        }

        if (entitlementResult.Disposition == ProductApiDisposition.AccountDisabled)
        {
            return new DurableLoginBootstrapResult(
                DurableLoginBootstrapDisposition.AccountDisabled,
                entitlementResult.ProductCode,
                account.AccountId,
                null,
                false);
        }

        var entitlementResolved = entitlementResult.Disposition == ProductApiDisposition.Accepted &&
                                  entitlementResult.Value is not null;
        var entitlement = entitlementResolved ? entitlementResult.Value : null;

        try
        {
            // With no canonical association, any existing account secret is an orphan from an interrupted
            // transaction and must not silently survive into a new association bootstrap.
            await _secretStore.DeleteAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsSecretPersistenceFailure(exception))
        {
            return new DurableLoginBootstrapResult(
                DurableLoginBootstrapDisposition.PersistenceFailed,
                "LOCAL_SECRET_CLEANUP_FAILED",
                account.AccountId,
                null,
                entitlementResolved);
        }

        var secret = new AccountSecretEnvelope
        {
            AccountId = account.AccountId,
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
            await _secretStore.WriteAsync(secret, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsSecretPersistenceFailure(exception))
        {
            return new DurableLoginBootstrapResult(
                DurableLoginBootstrapDisposition.PersistenceFailed,
                "LOCAL_SECRET_STORE_FAILED",
                account.AccountId,
                null,
                entitlementResolved);
        }

        var associationId = Guid.NewGuid();
        UserAssociationWriteOutcome associationOutcome;
        try
        {
            associationOutcome = await _associationStore.CreateActiveAssociationAsync(
                associationId,
                currentSid,
                account.AccountId,
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
            await CleanupUncommittedSecretAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception) when (IsLocalPersistenceFailure(exception))
        {
            return await FailAfterAssociationPersistenceAsync(
                "LOCAL_ASSOCIATION_STORE_FAILED",
                account.AccountId,
                entitlementResolved).ConfigureAwait(false);
        }

        if (associationOutcome.Disposition != UserAssociationWriteDisposition.Applied ||
            associationOutcome.Record is null)
        {
            return await FailAfterAssociationPersistenceAsync(
                associationOutcome.Disposition == UserAssociationWriteDisposition.AlreadyExists
                    ? "LOCAL_ASSOCIATION_RACE"
                    : "LOCAL_ASSOCIATION_STORE_FAILED",
                account.AccountId,
                entitlementResolved).ConfigureAwait(false);
        }

        _refreshSignal.RequestRefresh();
        return new DurableLoginBootstrapResult(
            entitlementResolved
                ? DurableLoginBootstrapDisposition.Associated
                : DurableLoginBootstrapDisposition.AssociatedDegraded,
            entitlementResolved ? "ACCOUNT_ASSOCIATED" : entitlementResult.ProductCode,
            associationOutcome.Record.AccountId,
            associationOutcome.Record.AssociationId,
            entitlementResolved);
    }

    private async Task<DurableLoginBootstrapResult> FailAfterAssociationPersistenceAsync(
        string productCode,
        string accountId,
        bool entitlementResolved)
    {
        if (!await CleanupUncommittedSecretAsync(CancellationToken.None).ConfigureAwait(false))
        {
            return new DurableLoginBootstrapResult(
                DurableLoginBootstrapDisposition.RecoveryRequired,
                "LOCAL_SECRET_RECONCILIATION_REQUIRED",
                accountId,
                null,
                entitlementResolved);
        }

        return new DurableLoginBootstrapResult(
            DurableLoginBootstrapDisposition.PersistenceFailed,
            productCode,
            accountId,
            null,
            entitlementResolved);
    }

    private async Task<bool> CleanupUncommittedSecretAsync(CancellationToken cancellationToken)
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

    private static DurableLoginBootstrapResult MapAccountFailure(
        ProductApiResult<SplitOSAccountProfile> result)
        => result.Disposition switch
        {
            ProductApiDisposition.AuthRequired => Reject(
                DurableLoginBootstrapDisposition.AuthRequired,
                result.ProductCode),
            ProductApiDisposition.AccountDisabled => Reject(
                DurableLoginBootstrapDisposition.AccountDisabled,
                result.ProductCode),
            ProductApiDisposition.BackendUnavailable or ProductApiDisposition.RateLimited => Reject(
                DurableLoginBootstrapDisposition.BackendUnavailable,
                result.ProductCode),
            _ => Reject(DurableLoginBootstrapDisposition.Rejected, result.ProductCode)
        };

    private static DurableLoginBootstrapResult Reject(
        DurableLoginBootstrapDisposition disposition,
        string productCode)
        => new(disposition, productCode, null, null, false);

    private static bool IsLocalPersistenceFailure(Exception exception)
        => exception is IOException or UnauthorizedAccessException or InvalidDataException;

    private static bool IsSecretPersistenceFailure(Exception exception)
        => exception is IOException or UnauthorizedAccessException or CryptographicException or InvalidDataException;
}
