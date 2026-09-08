using SplitOS.RuntimeHost.Authentication;

namespace SplitOS.RuntimeHost.ProductIdentity;

public interface IAccountAssociationEvaluationFlow
{
    Task<AccountAssociationEvaluation> EvaluateAsync(CancellationToken cancellationToken = default);
}

public sealed class AccountAssociationEvaluationFlow(AccountAssociationCoordinator coordinator)
    : IAccountAssociationEvaluationFlow
{
    public Task<AccountAssociationEvaluation> EvaluateAsync(CancellationToken cancellationToken = default)
        => coordinator.EvaluateAsync(cancellationToken);
}

public interface IDurableLoginBootstrapFlow
{
    Task<DurableLoginBootstrapResult> BootstrapInitialAssociationAsync(
        ValidatedNativeAuthSession session,
        DurableLoginBootstrapContext context,
        CancellationToken cancellationToken = default);
}

public sealed class DurableLoginBootstrapFlow(DurableLoginBootstrapCoordinator coordinator)
    : IDurableLoginBootstrapFlow
{
    public Task<DurableLoginBootstrapResult> BootstrapInitialAssociationAsync(
        ValidatedNativeAuthSession session,
        DurableLoginBootstrapContext context,
        CancellationToken cancellationToken = default)
        => coordinator.BootstrapInitialAssociationAsync(session, context, cancellationToken);
}

public interface ISameAccountReauthenticationFlow
{
    Task<SameAccountReauthenticationResult> ReactivateAsync(
        ValidatedNativeAuthSession session,
        SameAccountReauthenticationContext context,
        CancellationToken cancellationToken = default);
}

public sealed class SameAccountReauthenticationFlow(SameAccountReauthenticationCoordinator coordinator)
    : ISameAccountReauthenticationFlow
{
    public Task<SameAccountReauthenticationResult> ReactivateAsync(
        ValidatedNativeAuthSession session,
        SameAccountReauthenticationContext context,
        CancellationToken cancellationToken = default)
        => coordinator.ReactivateAsync(session, context, cancellationToken);
}

public interface IAuthenticatedEntitlementRefreshFlow
{
    Task<AuthenticatedEntitlementRefreshResult> RefreshAsync(
        AuthenticatedEntitlementRefreshContext context,
        CancellationToken cancellationToken = default);
}

public sealed class AuthenticatedEntitlementRefreshFlow(AuthenticatedEntitlementRefreshCoordinator coordinator)
    : IAuthenticatedEntitlementRefreshFlow
{
    public Task<AuthenticatedEntitlementRefreshResult> RefreshAsync(
        AuthenticatedEntitlementRefreshContext context,
        CancellationToken cancellationToken = default)
        => coordinator.RefreshAsync(context, cancellationToken);
}

public interface INativeSessionRefreshFlow
{
    Task<NativeSessionRefreshResult> RefreshAsync(CancellationToken cancellationToken = default);
}

public sealed class NativeSessionRefreshFlow(NativeSessionRefreshService service) : INativeSessionRefreshFlow
{
    public Task<NativeSessionRefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
        => service.RefreshAsync(cancellationToken);
}

public sealed record InteractiveSessionLifecycleContext(
    string ClientVersion,
    string InstallationId,
    Guid CorrelationId,
    Guid OperationId)
{
    public void Validate()
    {
        ValidateText(ClientVersion, 128, nameof(ClientVersion));
        ValidateText(InstallationId, 256, nameof(InstallationId));
        if (CorrelationId == Guid.Empty)
        {
            throw new ArgumentException("Correlation id must not be empty.", nameof(CorrelationId));
        }

        if (OperationId == Guid.Empty)
        {
            throw new ArgumentException("Operation id must not be empty.", nameof(OperationId));
        }
    }

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

public enum InteractiveSessionLifecycleDisposition
{
    Associated,
    AssociatedDegraded,
    Reactivated,
    ReactivatedDegraded,
    AlreadyActive,
    AccountSwitchRequired,
    AuthRequired,
    AccountDisabled,
    BackendUnavailable,
    ReusableSessionRequired,
    Rejected,
    PersistenceFailed,
    RecoveryRequired
}

public sealed record InteractiveSessionLifecycleResult(
    InteractiveSessionLifecycleDisposition Disposition,
    string ProductCode,
    string? AccountId,
    string? AssociationId,
    AuthenticatedEntitlementRefreshDisposition? EntitlementRefreshDisposition)
{
    public bool HasFreshOnlineEntitlement
        => EntitlementRefreshDisposition == AuthenticatedEntitlementRefreshDisposition.Refreshed;
}

/// <summary>
/// Owns the post-interactive-auth ordering boundary. A pre-commit entitlement response may be useful to
/// persist account metadata, but it is never published as RuntimeAccess authority. Fresh online authority is
/// obtained only after the canonical association transaction has committed.
/// </summary>
public sealed class InteractiveSessionEntitlementLifecycleCoordinator
{
    private readonly IAccountAssociationEvaluationFlow _associationFlow;
    private readonly IDurableLoginBootstrapFlow _bootstrapFlow;
    private readonly ISameAccountReauthenticationFlow _reauthenticationFlow;
    private readonly IAuthenticatedEntitlementRefreshFlow _entitlementRefreshFlow;
    private readonly OnlineEntitlementEvidenceState _evidenceState;
    private readonly RuntimeStateRefreshSignal _refreshSignal;
    private int _interactiveCompletionInProgress;

    public InteractiveSessionEntitlementLifecycleCoordinator(
        IAccountAssociationEvaluationFlow associationFlow,
        IDurableLoginBootstrapFlow bootstrapFlow,
        ISameAccountReauthenticationFlow reauthenticationFlow,
        IAuthenticatedEntitlementRefreshFlow entitlementRefreshFlow,
        OnlineEntitlementEvidenceState evidenceState,
        RuntimeStateRefreshSignal refreshSignal)
    {
        _associationFlow = associationFlow ?? throw new ArgumentNullException(nameof(associationFlow));
        _bootstrapFlow = bootstrapFlow ?? throw new ArgumentNullException(nameof(bootstrapFlow));
        _reauthenticationFlow = reauthenticationFlow ?? throw new ArgumentNullException(nameof(reauthenticationFlow));
        _entitlementRefreshFlow = entitlementRefreshFlow ?? throw new ArgumentNullException(nameof(entitlementRefreshFlow));
        _evidenceState = evidenceState ?? throw new ArgumentNullException(nameof(evidenceState));
        _refreshSignal = refreshSignal ?? throw new ArgumentNullException(nameof(refreshSignal));
    }

    public async Task<InteractiveSessionLifecycleResult> CompleteAsync(
        ValidatedNativeAuthSession session,
        InteractiveSessionLifecycleContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(context);
        context.Validate();

        if (Interlocked.CompareExchange(ref _interactiveCompletionInProgress, 1, 0) != 0)
        {
            return Result(
                InteractiveSessionLifecycleDisposition.Rejected,
                "INTERACTIVE_LOGIN_ALREADY_IN_PROGRESS");
        }

        try
        {
            var association = await _associationFlow.EvaluateAsync(cancellationToken).ConfigureAwait(false);
            return association.AssociationState switch
            {
                "UNASSOCIATED" => await CompleteInitialAssociationAsync(session, context, cancellationToken).ConfigureAwait(false),
                "REAUTH_REQUIRED" => await CompleteReauthenticationAsync(
                    association,
                    session,
                    context,
                    cancellationToken).ConfigureAwait(false),
                "ACTIVE" => new InteractiveSessionLifecycleResult(
                    InteractiveSessionLifecycleDisposition.AlreadyActive,
                    "ACCOUNT_ALREADY_ACTIVE",
                    association.AccountId,
                    association.AssociationId,
                    null),
                _ => RejectUnknownAssociationState(association)
            };
        }
        finally
        {
            Volatile.Write(ref _interactiveCompletionInProgress, 0);
        }
    }

    private async Task<InteractiveSessionLifecycleResult> CompleteInitialAssociationAsync(
        ValidatedNativeAuthSession session,
        InteractiveSessionLifecycleContext context,
        CancellationToken cancellationToken)
    {
        // No canonical association means any process-memory entitlement is necessarily orphaned evidence.
        _evidenceState.Clear();
        _refreshSignal.RequestRefresh();

        var bootstrap = await _bootstrapFlow.BootstrapInitialAssociationAsync(
            session,
            new DurableLoginBootstrapContext(
                context.ClientVersion,
                context.InstallationId,
                context.CorrelationId,
                context.OperationId),
            cancellationToken).ConfigureAwait(false);

        if (!bootstrap.IsDurablyAssociated ||
            string.IsNullOrWhiteSpace(bootstrap.AccountId) ||
            string.IsNullOrWhiteSpace(bootstrap.AssociationId))
        {
            return MapBootstrapFailure(bootstrap);
        }

        return await RefreshAfterCommitAsync(
            session,
            context,
            bootstrap.AccountId,
            bootstrap.AssociationId,
            InteractiveSessionLifecycleDisposition.Associated,
            InteractiveSessionLifecycleDisposition.AssociatedDegraded,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<InteractiveSessionLifecycleResult> CompleteReauthenticationAsync(
        AccountAssociationEvaluation association,
        ValidatedNativeAuthSession session,
        InteractiveSessionLifecycleContext context,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(association.AssociationId) &&
            !string.IsNullOrWhiteSpace(association.AccountId))
        {
            _evidenceState.ClearIfBoundTo(association.AssociationId, association.AccountId);
        }
        else
        {
            _evidenceState.Clear();
        }

        _refreshSignal.RequestRefresh();

        var reauthentication = await _reauthenticationFlow.ReactivateAsync(
            session,
            new SameAccountReauthenticationContext(
                context.ClientVersion,
                context.InstallationId,
                context.CorrelationId,
                context.OperationId),
            cancellationToken).ConfigureAwait(false);

        if (!reauthentication.IsActive ||
            string.IsNullOrWhiteSpace(reauthentication.AccountId) ||
            string.IsNullOrWhiteSpace(reauthentication.AssociationId))
        {
            return MapReauthenticationFailure(reauthentication);
        }

        return await RefreshAfterCommitAsync(
            session,
            context,
            reauthentication.AccountId,
            reauthentication.AssociationId,
            InteractiveSessionLifecycleDisposition.Reactivated,
            InteractiveSessionLifecycleDisposition.ReactivatedDegraded,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<InteractiveSessionLifecycleResult> RefreshAfterCommitAsync(
        ValidatedNativeAuthSession session,
        InteractiveSessionLifecycleContext context,
        string accountId,
        string associationId,
        InteractiveSessionLifecycleDisposition onlineDisposition,
        InteractiveSessionLifecycleDisposition degradedDisposition,
        CancellationToken cancellationToken)
    {
        var entitlement = await _entitlementRefreshFlow.RefreshAsync(
            new AuthenticatedEntitlementRefreshContext(
                accountId,
                session.AccessToken,
                session.AccessTokenExpiresUtc,
                context.ClientVersion,
                context.InstallationId,
                context.CorrelationId),
            cancellationToken).ConfigureAwait(false);

        var fresh = entitlement.Disposition == AuthenticatedEntitlementRefreshDisposition.Refreshed;
        return new InteractiveSessionLifecycleResult(
            fresh ? onlineDisposition : degradedDisposition,
            fresh ? "ACCOUNT_AND_ENTITLEMENT_READY" : entitlement.ProductCode,
            accountId,
            associationId,
            entitlement.Disposition);
    }

    private static InteractiveSessionLifecycleResult MapBootstrapFailure(DurableLoginBootstrapResult result)
        => result.Disposition switch
        {
            DurableLoginBootstrapDisposition.AccountSwitchRequired => Result(
                InteractiveSessionLifecycleDisposition.AccountSwitchRequired,
                result.ProductCode,
                result.AccountId,
                result.AssociationId),
            DurableLoginBootstrapDisposition.AuthRequired => Result(
                InteractiveSessionLifecycleDisposition.AuthRequired,
                result.ProductCode,
                result.AccountId,
                result.AssociationId),
            DurableLoginBootstrapDisposition.AccountDisabled => Result(
                InteractiveSessionLifecycleDisposition.AccountDisabled,
                result.ProductCode,
                result.AccountId,
                result.AssociationId),
            DurableLoginBootstrapDisposition.BackendUnavailable => Result(
                InteractiveSessionLifecycleDisposition.BackendUnavailable,
                result.ProductCode,
                result.AccountId,
                result.AssociationId),
            DurableLoginBootstrapDisposition.ReusableSessionRequired => Result(
                InteractiveSessionLifecycleDisposition.ReusableSessionRequired,
                result.ProductCode,
                result.AccountId,
                result.AssociationId),
            DurableLoginBootstrapDisposition.PersistenceFailed => Result(
                InteractiveSessionLifecycleDisposition.PersistenceFailed,
                result.ProductCode,
                result.AccountId,
                result.AssociationId),
            DurableLoginBootstrapDisposition.RecoveryRequired => Result(
                InteractiveSessionLifecycleDisposition.RecoveryRequired,
                result.ProductCode,
                result.AccountId,
                result.AssociationId),
            _ => Result(
                InteractiveSessionLifecycleDisposition.Rejected,
                result.ProductCode,
                result.AccountId,
                result.AssociationId)
        };

    private static InteractiveSessionLifecycleResult MapReauthenticationFailure(SameAccountReauthenticationResult result)
        => result.Disposition switch
        {
            SameAccountReauthenticationDisposition.NotRequired => Result(
                InteractiveSessionLifecycleDisposition.AlreadyActive,
                result.ProductCode,
                result.AccountId,
                result.AssociationId),
            SameAccountReauthenticationDisposition.AccountSwitchRequired => Result(
                InteractiveSessionLifecycleDisposition.AccountSwitchRequired,
                result.ProductCode,
                result.AccountId,
                result.AssociationId),
            SameAccountReauthenticationDisposition.AuthRequired => Result(
                InteractiveSessionLifecycleDisposition.AuthRequired,
                result.ProductCode,
                result.AccountId,
                result.AssociationId),
            SameAccountReauthenticationDisposition.AccountDisabled => Result(
                InteractiveSessionLifecycleDisposition.AccountDisabled,
                result.ProductCode,
                result.AccountId,
                result.AssociationId),
            SameAccountReauthenticationDisposition.BackendUnavailable => Result(
                InteractiveSessionLifecycleDisposition.BackendUnavailable,
                result.ProductCode,
                result.AccountId,
                result.AssociationId),
            SameAccountReauthenticationDisposition.ReusableSessionRequired => Result(
                InteractiveSessionLifecycleDisposition.ReusableSessionRequired,
                result.ProductCode,
                result.AccountId,
                result.AssociationId),
            SameAccountReauthenticationDisposition.PersistenceFailed => Result(
                InteractiveSessionLifecycleDisposition.PersistenceFailed,
                result.ProductCode,
                result.AccountId,
                result.AssociationId),
            SameAccountReauthenticationDisposition.RecoveryRequired => Result(
                InteractiveSessionLifecycleDisposition.RecoveryRequired,
                result.ProductCode,
                result.AccountId,
                result.AssociationId),
            _ => Result(
                InteractiveSessionLifecycleDisposition.Rejected,
                result.ProductCode,
                result.AccountId,
                result.AssociationId)
        };

    private InteractiveSessionLifecycleResult RejectUnknownAssociationState(AccountAssociationEvaluation association)
    {
        _evidenceState.Clear();
        _refreshSignal.RequestRefresh();
        return Result(
            InteractiveSessionLifecycleDisposition.Rejected,
            "LOCAL_ASSOCIATION_STATE_REJECTED",
            association.AccountId,
            association.AssociationId);
    }

    private static InteractiveSessionLifecycleResult Result(
        InteractiveSessionLifecycleDisposition disposition,
        string productCode,
        string? accountId = null,
        string? associationId = null)
        => new(disposition, productCode, accountId, associationId, null);
}

public sealed record SessionRefreshLifecycleContext(
    string ClientVersion,
    string InstallationId,
    Guid CorrelationId)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientVersion) || ClientVersion.Length > 128 ||
            ClientVersion.Any(static character => char.IsControl(character)))
        {
            throw new ArgumentException("ClientVersion is missing or outside the supported bounds.", nameof(ClientVersion));
        }

        if (string.IsNullOrWhiteSpace(InstallationId) || InstallationId.Length > 256 ||
            InstallationId.Any(static character => char.IsControl(character)))
        {
            throw new ArgumentException("InstallationId is missing or outside the supported bounds.", nameof(InstallationId));
        }

        if (CorrelationId == Guid.Empty)
        {
            throw new ArgumentException("Correlation id must not be empty.", nameof(CorrelationId));
        }
    }
}

public enum SessionRefreshLifecycleDisposition
{
    RefreshedOnline,
    RefreshedDegraded,
    AlreadyInProgress,
    ReauthRequired,
    BackendUnavailable,
    Rejected,
    PersistenceFailed,
    RecoveryRequired
}

public sealed record SessionRefreshLifecycleResult(
    SessionRefreshLifecycleDisposition Disposition,
    string SessionProductCode,
    string? EntitlementProductCode,
    AuthenticatedEntitlementRefreshDisposition? EntitlementRefreshDisposition)
{
    public bool HasFreshOnlineEntitlement
        => Disposition == SessionRefreshLifecycleDisposition.RefreshedOnline;
}

/// <summary>
/// Couples refresh-token rotation to a post-refresh entitlement fetch. Auth-invalidating refresh failures clear
/// process-memory premium evidence immediately; retryable backend failures preserve only the already-bounded evidence.
/// </summary>
public sealed class SessionRefreshEntitlementLifecycleCoordinator
{
    private readonly INativeSessionRefreshFlow _sessionRefreshFlow;
    private readonly IAuthenticatedEntitlementRefreshFlow _entitlementRefreshFlow;
    private readonly OnlineEntitlementEvidenceState _evidenceState;
    private readonly RuntimeStateRefreshSignal _refreshSignal;

    public SessionRefreshEntitlementLifecycleCoordinator(
        INativeSessionRefreshFlow sessionRefreshFlow,
        IAuthenticatedEntitlementRefreshFlow entitlementRefreshFlow,
        OnlineEntitlementEvidenceState evidenceState,
        RuntimeStateRefreshSignal refreshSignal)
    {
        _sessionRefreshFlow = sessionRefreshFlow ?? throw new ArgumentNullException(nameof(sessionRefreshFlow));
        _entitlementRefreshFlow = entitlementRefreshFlow ?? throw new ArgumentNullException(nameof(entitlementRefreshFlow));
        _evidenceState = evidenceState ?? throw new ArgumentNullException(nameof(evidenceState));
        _refreshSignal = refreshSignal ?? throw new ArgumentNullException(nameof(refreshSignal));
    }

    public async Task<SessionRefreshLifecycleResult> RefreshAsync(
        SessionRefreshLifecycleContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Validate();

        var sessionRefresh = await _sessionRefreshFlow.RefreshAsync(cancellationToken).ConfigureAwait(false);
        if (sessionRefresh.Disposition == NativeSessionRefreshDisposition.Refreshed &&
            sessionRefresh.Session is not null)
        {
            var session = sessionRefresh.Session;
            var entitlement = await _entitlementRefreshFlow.RefreshAsync(
                new AuthenticatedEntitlementRefreshContext(
                    session.AccountId,
                    session.AccessToken,
                    session.AccessTokenExpiresUtc,
                    context.ClientVersion,
                    context.InstallationId,
                    context.CorrelationId),
                cancellationToken).ConfigureAwait(false);

            return new SessionRefreshLifecycleResult(
                entitlement.Disposition == AuthenticatedEntitlementRefreshDisposition.Refreshed
                    ? SessionRefreshLifecycleDisposition.RefreshedOnline
                    : SessionRefreshLifecycleDisposition.RefreshedDegraded,
                sessionRefresh.ProductCode,
                entitlement.ProductCode,
                entitlement.Disposition);
        }

        if (sessionRefresh.Disposition is NativeSessionRefreshDisposition.ReauthRequired or
            NativeSessionRefreshDisposition.Rejected or
            NativeSessionRefreshDisposition.PersistenceFailed or
            NativeSessionRefreshDisposition.RecoveryRequired ||
            (sessionRefresh.Disposition == NativeSessionRefreshDisposition.Refreshed && sessionRefresh.Session is null))
        {
            _evidenceState.Clear();
            _refreshSignal.RequestRefresh();
        }

        return new SessionRefreshLifecycleResult(
            sessionRefresh.Disposition switch
            {
                NativeSessionRefreshDisposition.AlreadyInProgress => SessionRefreshLifecycleDisposition.AlreadyInProgress,
                NativeSessionRefreshDisposition.ReauthRequired => SessionRefreshLifecycleDisposition.ReauthRequired,
                NativeSessionRefreshDisposition.BackendUnavailable => SessionRefreshLifecycleDisposition.BackendUnavailable,
                NativeSessionRefreshDisposition.PersistenceFailed => SessionRefreshLifecycleDisposition.PersistenceFailed,
                NativeSessionRefreshDisposition.RecoveryRequired => SessionRefreshLifecycleDisposition.RecoveryRequired,
                _ => SessionRefreshLifecycleDisposition.Rejected
            },
            sessionRefresh.ProductCode,
            null,
            null);
    }
}
