using SplitOS.Persistence.ProtectedSecrets;
using SplitOS.Persistence.User;
using SplitOS.RuntimeHost.ProductIdentity;

namespace SplitOS.RuntimeHost.Authentication;

/// <summary>
/// Constructs the complete RuntimeHost-owned interactive identity/account lifecycle from one already-verified
/// release metadata object. The factory accepts no user/UI endpoint input and uses one long-lived HttpClient so
/// authority rotation does not leak per-command connection pools.
/// </summary>
public sealed class RuntimeIdentityAuthStartCommandFactory : IRuntimeAuthStartCommandFactory
{
    private readonly HttpClient _httpClient;
    private readonly IWindowsUserContext _windowsUserContext;
    private readonly AccountAssociationCoordinator _accountAssociationCoordinator;
    private readonly IUserAccountAssociationStore _associationStore;
    private readonly IUserAccountAssociationReactivationStore _reactivationStore;
    private readonly IAccountSecretStore _secretStore;
    private readonly RuntimeStateRefreshSignal _refreshSignal;
    private readonly OnlineEntitlementEvidenceState _evidenceState;
    private readonly IInstallationIdentityProvider _installationIdentityProvider;
    private readonly INativeAuthLoopbackListenerFactory _loopbackListenerFactory;
    private readonly INativeAuthBrowserLauncher _browserLauncher;
    private readonly TimeProvider _timeProvider;

    public RuntimeIdentityAuthStartCommandFactory(
        HttpClient httpClient,
        IWindowsUserContext windowsUserContext,
        AccountAssociationCoordinator accountAssociationCoordinator,
        IUserAccountAssociationStore associationStore,
        IUserAccountAssociationReactivationStore reactivationStore,
        IAccountSecretStore secretStore,
        RuntimeStateRefreshSignal refreshSignal,
        OnlineEntitlementEvidenceState evidenceState,
        IInstallationIdentityProvider installationIdentityProvider,
        INativeAuthLoopbackListenerFactory? loopbackListenerFactory = null,
        INativeAuthBrowserLauncher? browserLauncher = null,
        TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _windowsUserContext = windowsUserContext ?? throw new ArgumentNullException(nameof(windowsUserContext));
        _accountAssociationCoordinator = accountAssociationCoordinator ?? throw new ArgumentNullException(nameof(accountAssociationCoordinator));
        _associationStore = associationStore ?? throw new ArgumentNullException(nameof(associationStore));
        _reactivationStore = reactivationStore ?? throw new ArgumentNullException(nameof(reactivationStore));
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        _refreshSignal = refreshSignal ?? throw new ArgumentNullException(nameof(refreshSignal));
        _evidenceState = evidenceState ?? throw new ArgumentNullException(nameof(evidenceState));
        _installationIdentityProvider = installationIdentityProvider ?? throw new ArgumentNullException(nameof(installationIdentityProvider));
        _loopbackListenerFactory = loopbackListenerFactory ?? new TcpNativeAuthLoopbackListenerFactory();
        _browserLauncher = browserLauncher ?? new SystemNativeAuthBrowserLauncher();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public IRuntimeAuthStartCommand Create(VerifiedNativeAuthAuthorityMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(metadata.Authority);
        ArgumentNullException.ThrowIfNull(metadata.ProductApi);
        metadata.Authority.Validate();
        metadata.ProductApi.Validate();

        var transactionManager = new NativeAuthTransactionManager(
            metadata.Authority,
            _windowsUserContext,
            _timeProvider);
        var interactiveFlow = new NativeAuthInteractiveFlow(
            transactionManager,
            _loopbackListenerFactory,
            _browserLauncher,
            _timeProvider);
        var tokenExchange = new NativeAuthTokenExchangeService(
            _httpClient,
            metadata.Authority,
            _timeProvider);
        var identityFlow = new InteractiveIdentityAuthenticationFlow(interactiveFlow, tokenExchange);

        var productApiClient = new ProductApiClient(_httpClient, metadata.ProductApi);
        var bootstrap = new DurableLoginBootstrapCoordinator(
            productApiClient,
            _associationStore,
            _secretStore,
            _windowsUserContext,
            _refreshSignal,
            timeProvider: _timeProvider);
        var reauthentication = new SameAccountReauthenticationCoordinator(
            productApiClient,
            _associationStore,
            _reactivationStore,
            _secretStore,
            _windowsUserContext,
            _refreshSignal,
            timeProvider: _timeProvider);
        var entitlementRefresh = new AuthenticatedEntitlementRefreshCoordinator(
            productApiClient,
            _associationStore,
            _windowsUserContext,
            _evidenceState,
            _refreshSignal,
            _timeProvider);
        var lifecycle = new InteractiveSessionEntitlementLifecycleCoordinator(
            new AccountAssociationEvaluationFlow(_accountAssociationCoordinator),
            new DurableLoginBootstrapFlow(bootstrap),
            new SameAccountReauthenticationFlow(reauthentication),
            new AuthenticatedEntitlementRefreshFlow(entitlementRefresh),
            _evidenceState,
            _refreshSignal);

        return new RuntimeAuthStartCoordinator(
            identityFlow,
            new InteractiveSessionLifecycleCompletionFlow(lifecycle),
            _installationIdentityProvider);
    }
}
