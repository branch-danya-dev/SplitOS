using SplitOS.Persistence.User;

namespace SplitOS.RuntimeHost.ProductIdentity;

public enum AuthenticatedEntitlementRefreshDisposition
{
    Refreshed,
    NotAssociated,
    ReauthRequired,
    AccountMismatch,
    AuthRequired,
    AccountDisabled,
    BackendUnavailable,
    Rejected
}

public sealed record AuthenticatedEntitlementRefreshContext(
    string AccountId,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresUtc,
    string ClientVersion,
    string InstallationId,
    Guid CorrelationId)
{
    public void Validate()
    {
        ValidateText(AccountId, 512, nameof(AccountId));
        ValidateText(AccessToken, 32 * 1024, nameof(AccessToken));
        ValidateText(ClientVersion, 128, nameof(ClientVersion));
        ValidateText(InstallationId, 256, nameof(InstallationId));
        if (CorrelationId == Guid.Empty)
        {
            throw new ArgumentException("Correlation id must not be empty.", nameof(CorrelationId));
        }
    }

    public override string ToString()
        => $"AuthenticatedEntitlementRefreshContext(AccountId={AccountId}, AccessToken=<redacted>, AccessTokenExpiresUtc={AccessTokenExpiresUtc:O}, ClientVersion={ClientVersion}, InstallationId={InstallationId}, CorrelationId={CorrelationId:D})";

    private static void ValidateText(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > maximumLength ||
            value.Any(static character => char.IsControl(character)))
        {
            throw new ArgumentException($"{parameterName} is missing or outside the supported bounds.", parameterName);
        }
    }
}

public sealed record AuthenticatedEntitlementRefreshResult(
    AuthenticatedEntitlementRefreshDisposition Disposition,
    string ProductCode,
    long? EntitlementVersion,
    bool Retryable);

/// <summary>
/// Turns an already-authenticated native access session into fresh RuntimeHost-owned online entitlement evidence.
/// It never creates account associations and never treats a browser/token success as entitlement authority by itself.
/// </summary>
public sealed class AuthenticatedEntitlementRefreshCoordinator
{
    private readonly ProductApiClient _productApiClient;
    private readonly IUserAccountAssociationStore _associationStore;
    private readonly IWindowsUserContext _windowsUserContext;
    private readonly OnlineEntitlementEvidenceState _evidenceState;
    private readonly RuntimeStateRefreshSignal _refreshSignal;
    private readonly TimeProvider _timeProvider;
    private int _refreshInProgress;

    public AuthenticatedEntitlementRefreshCoordinator(
        ProductApiClient productApiClient,
        IUserAccountAssociationStore associationStore,
        IWindowsUserContext windowsUserContext,
        OnlineEntitlementEvidenceState evidenceState,
        RuntimeStateRefreshSignal refreshSignal,
        TimeProvider? timeProvider = null)
    {
        _productApiClient = productApiClient ?? throw new ArgumentNullException(nameof(productApiClient));
        _associationStore = associationStore ?? throw new ArgumentNullException(nameof(associationStore));
        _windowsUserContext = windowsUserContext ?? throw new ArgumentNullException(nameof(windowsUserContext));
        _evidenceState = evidenceState ?? throw new ArgumentNullException(nameof(evidenceState));
        _refreshSignal = refreshSignal ?? throw new ArgumentNullException(nameof(refreshSignal));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<AuthenticatedEntitlementRefreshResult> RefreshAsync(
        AuthenticatedEntitlementRefreshContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Validate();

        if (Interlocked.CompareExchange(ref _refreshInProgress, 1, 0) != 0)
        {
            return Result(
                AuthenticatedEntitlementRefreshDisposition.BackendUnavailable,
                "ENTITLEMENT_REFRESH_ALREADY_IN_PROGRESS",
                retryable: true);
        }

        try
        {
            return await RefreshCoreAsync(context, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _refreshInProgress, 0);
        }
    }

    private async Task<AuthenticatedEntitlementRefreshResult> RefreshCoreAsync(
        AuthenticatedEntitlementRefreshContext context,
        CancellationToken cancellationToken)
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return Result(
                AuthenticatedEntitlementRefreshDisposition.Rejected,
                "LOCAL_ASSOCIATION_STORE_FAILED",
                retryable: false);
        }

        if (association is null)
        {
            _evidenceState.Clear();
            _refreshSignal.RequestRefresh();
            return Result(
                AuthenticatedEntitlementRefreshDisposition.NotAssociated,
                "ACCOUNT_NOT_ASSOCIATED",
                retryable: false);
        }

        var currentSid = _windowsUserContext.GetCurrentUserSid();
        if (!string.Equals(currentSid, association.WindowsUserSid, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(association.AssociationState, "ACTIVE", StringComparison.Ordinal))
        {
            ClearBoundEvidence(association);
            return Result(
                AuthenticatedEntitlementRefreshDisposition.ReauthRequired,
                !string.Equals(currentSid, association.WindowsUserSid, StringComparison.OrdinalIgnoreCase)
                    ? "LOCAL_ASSOCIATION_CONTEXT_MISMATCH"
                    : "REAUTH_REQUIRED",
                retryable: false);
        }

        if (!string.Equals(context.AccountId, association.AccountId, StringComparison.Ordinal))
        {
            ClearBoundEvidence(association);
            return Result(
                AuthenticatedEntitlementRefreshDisposition.AccountMismatch,
                "SESSION_ACCOUNT_MISMATCH",
                retryable: false);
        }

        if (context.AccessTokenExpiresUtc <= _timeProvider.GetUtcNow())
        {
            ClearBoundEvidence(association);
            return Result(
                AuthenticatedEntitlementRefreshDisposition.AuthRequired,
                "AUTH_SESSION_EXPIRED",
                retryable: false);
        }

        var apiContext = new ProductApiRequestContext(
            context.AccessToken,
            context.ClientVersion,
            context.InstallationId,
            context.CorrelationId);
        var entitlementResult = await _productApiClient.GetCurrentEntitlementAsync(
            apiContext,
            association.AccountId,
            cancellationToken).ConfigureAwait(false);

        if (entitlementResult.Disposition == ProductApiDisposition.Accepted &&
            entitlementResult.Value is not null)
        {
            var publish = _evidenceState.Publish(
                association.AssociationId,
                entitlementResult.Value,
                _timeProvider.GetUtcNow());
            if (publish.Disposition == OnlineEntitlementPublishDisposition.StaleRejected)
            {
                _refreshSignal.RequestRefresh();
                return Result(
                    AuthenticatedEntitlementRefreshDisposition.Rejected,
                    "ENTITLEMENT_VERSION_ROLLBACK",
                    retryable: false,
                    publish.Current?.Entitlement.EntitlementVersion);
            }

            _refreshSignal.RequestRefresh();
            return Result(
                AuthenticatedEntitlementRefreshDisposition.Refreshed,
                "ENTITLEMENT_REFRESHED",
                retryable: false,
                entitlementResult.Value.EntitlementVersion);
        }

        if (entitlementResult.Disposition is ProductApiDisposition.BackendUnavailable or ProductApiDisposition.RateLimited)
        {
            return Result(
                AuthenticatedEntitlementRefreshDisposition.BackendUnavailable,
                entitlementResult.ProductCode,
                retryable: true,
                _evidenceState.Read()?.Entitlement.EntitlementVersion);
        }

        ClearBoundEvidence(association);
        return entitlementResult.Disposition switch
        {
            ProductApiDisposition.AuthRequired => Result(
                AuthenticatedEntitlementRefreshDisposition.AuthRequired,
                entitlementResult.ProductCode,
                retryable: false),
            ProductApiDisposition.AccountDisabled => Result(
                AuthenticatedEntitlementRefreshDisposition.AccountDisabled,
                entitlementResult.ProductCode,
                retryable: false),
            _ => Result(
                AuthenticatedEntitlementRefreshDisposition.Rejected,
                entitlementResult.ProductCode,
                retryable: false)
        };
    }

    private void ClearBoundEvidence(UserAccountAssociationRecord association)
    {
        _evidenceState.ClearIfBoundTo(association.AssociationId, association.AccountId);
        _refreshSignal.RequestRefresh();
    }

    private static AuthenticatedEntitlementRefreshResult Result(
        AuthenticatedEntitlementRefreshDisposition disposition,
        string productCode,
        bool retryable,
        long? entitlementVersion = null)
        => new(disposition, productCode, entitlementVersion, retryable);
}
