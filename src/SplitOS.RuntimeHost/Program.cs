using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SplitOS.Persistence.Machine;
using SplitOS.Persistence.ProtectedSecrets;
using SplitOS.Persistence.Projection;
using SplitOS.Persistence.User;
using SplitOS.RuntimeHost;
using SplitOS.RuntimeHost.Authentication;
using SplitOS.RuntimeHost.GameRuntime;
using SplitOS.RuntimeHost.ModeRuntime;
using SplitOS.RuntimeHost.ProductIdentity;
using SplitOS.RuntimeHost.WindowsContext;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<RuntimeUiCallerValidator>();
builder.Services.AddSingleton<BrokerHealthState>();
builder.Services.AddSingleton<RuntimeStateState>();
builder.Services.AddSingleton<RuntimeStateRefreshSignal>();
builder.Services.AddSingleton<GameSessionStateMachine>();
builder.Services.AddSingleton<LauncherReadinessState>();
builder.Services.AddSingleton<LauncherRuntimeSnapshotProvider>();
builder.Services.AddSingleton<ILauncherProcessPlatform, WindowsLauncherProcessPlatform>();
builder.Services.AddSingleton<LauncherProcessSupervisor>();
builder.Services.AddSingleton<IDisplayGenerationTracker, DisplayGenerationTracker>();
builder.Services.AddSingleton<IDisplayDeviceInstanceIdResolver, SetupApiDisplayDeviceInstanceIdResolver>();
builder.Services.AddSingleton<WindowsDisplayConfigInterop>();
builder.Services.AddSingleton<IWindowsDisplayConfigInterop>(services => services.GetRequiredService<WindowsDisplayConfigInterop>());
builder.Services.AddSingleton<IWindowsDisplayConnectionInterop>(services => services.GetRequiredService<WindowsDisplayConfigInterop>());
builder.Services.AddSingleton<IDisplayConfigQuery, WindowsDisplayConfigQuery>();
builder.Services.AddSingleton<IDisplaySnapshotReader, DisplaySnapshotReader>();
builder.Services.AddSingleton<IDisplayConnectionCandidateQuery, WindowsDisplayConnectionCandidateQuery>();
builder.Services.AddSingleton<IDisplayConnectionCandidateReader, DisplayConnectionCandidateReader>();
builder.Services.AddSingleton<IDisplayNativeTargetApplier, WindowsDisplayConfigTargetApplier>();
builder.Services.AddSingleton<DisplayTargetApplyCoordinator>();
builder.Services.AddSingleton<PersistentDisplaySelectorResolver>();
builder.Services.AddSingleton<DisplayExtendCandidateResolver>();
builder.Services.AddSingleton<IDisplayNativeExtendApplier, WindowsDisplayConfigExtendApplier>();
builder.Services.AddSingleton<DisplayExtendApplyCoordinator>();
builder.Services.AddSingleton<IDisplayNativeTopologyRollbackApplier, WindowsDisplayConfigTopologyRollbackApplier>();
builder.Services.AddSingleton<DisplayExtendTopologyRollbackCoordinator>();
builder.Services.AddSingleton<IDisplayExtendTransactionTopologyStage, DisplayExtendTransactionTopologyStage>();
builder.Services.AddSingleton<IDisplayExtendTransactionModeStage, DisplayExtendTransactionModeStage>();
builder.Services.AddSingleton<IDisplayExtendTransactionRollbackStage, DisplayExtendTransactionRollbackStage>();
builder.Services.AddSingleton<DisplayExtendModeTransactionCoordinator>();
builder.Services.AddSingleton<PowrProfPowerSchemeInterop>();
builder.Services.AddSingleton<IWindowsPowerSchemeInterop>(services => services.GetRequiredService<PowrProfPowerSchemeInterop>());
builder.Services.AddSingleton<IPowerSchemeQuery, WindowsPowerSchemeQuery>();
builder.Services.AddSingleton<IPowerSchemeSetter, WindowsPowerSchemeSetter>();
builder.Services.AddSingleton<IPowerSchemeSnapshotReader, PowerSchemeSnapshotReader>();
builder.Services.AddSingleton<WindowsProcessEvidenceInterop>();
builder.Services.AddSingleton<IWindowsProcessEvidenceInterop>(services => services.GetRequiredService<WindowsProcessEvidenceInterop>());
builder.Services.AddSingleton<IProcessEvidenceSnapshotReader, ProcessEvidenceSnapshotReader>();
builder.Services.AddSingleton<IHardwareGenerationTracker, HardwareGenerationTracker>();
builder.Services.AddSingleton<PnpHardwareGenerationInvalidator>();
builder.Services.AddSingleton<ConfigurationManagerPnpInterop>();
builder.Services.AddSingleton<IConfigurationManagerPnpInterop>(services => services.GetRequiredService<ConfigurationManagerPnpInterop>());
builder.Services.AddSingleton<WindowsPnpHardwareNotificationSource>();
builder.Services.AddSingleton<IPnpHardwareNotificationSource>(services => services.GetRequiredService<WindowsPnpHardwareNotificationSource>());
builder.Services.AddSingleton<GameInputEvidenceState>();
builder.Services.AddSingleton<IInputGenerationTracker>(services => services.GetRequiredService<GameInputEvidenceState>());
builder.Services.AddSingleton<IInputSnapshotReader>(services => services.GetRequiredService<GameInputEvidenceState>());
builder.Services.AddSingleton<IGameInputSessionFactory, WindowsGameInputSessionFactory>();
builder.Services.AddSingleton<IAudioGenerationTracker, AudioGenerationTracker>();
builder.Services.AddSingleton<ICoreAudioSnapshotQuery, WindowsCoreAudioSnapshotQuery>();
builder.Services.AddSingleton<IAudioSnapshotReader, AudioSnapshotReader>();
builder.Services.AddSingleton<ICoreAudioNotificationSessionFactory, WindowsCoreAudioNotificationSessionFactory>();
// Release power-policy mappings are not provisioned yet. Keep the production catalog empty and
// fail closed; BaselineModeTargetPreparationProvider also does not emit power actions in this slice.
builder.Services.AddSingleton<IPowerPolicyCatalogResolver>(_ =>
    new PowerPolicyCatalogResolver(Array.Empty<PowerPolicyCatalogEntry>()));
builder.Services.AddSingleton<PowerSchemeApplyCoordinator>();
builder.Services.AddSingleton<UserStateStore>();
builder.Services.AddSingleton<IUserAccountAssociationStore>(services => services.GetRequiredService<UserStateStore>());
builder.Services.AddSingleton<IUserAccountAssociationSignOutStore, UserAccountAssociationSignOutStore>();
builder.Services.AddSingleton<ProjectionStore>();
builder.Services.AddSingleton<IAccountSecretStore, DpapiAccountSecretStore>();
builder.Services.AddSingleton<IWindowsUserContext, WindowsUserContext>();
builder.Services.AddSingleton<IInstallationIdentityProvider, MachineInstallationIdentityProvider>();
builder.Services.AddSingleton<AccountAssociationCoordinator>();
builder.Services.AddSingleton<OnlineEntitlementEvidenceState>();
builder.Services.AddSingleton<LocalSignOutCoordinator>();
builder.Services.AddSingleton<ILocalSignOutFlow, LocalSignOutFlow>();
builder.Services.AddSingleton<IRuntimeSignOutCommand, RuntimeSignOutCommand>();
builder.Services.AddSingleton<MachineStateClient>();
builder.Services.AddSingleton<NamedPipeModePersistenceClient>();
builder.Services.AddSingleton<IMachineStateStore>(services => services.GetRequiredService<NamedPipeModePersistenceClient>());
builder.Services.AddSingleton<IMachineMutationLeaseStore>(services => services.GetRequiredService<NamedPipeModePersistenceClient>());
builder.Services.AddSingleton<IModeTransitionStore>(services => services.GetRequiredService<NamedPipeModePersistenceClient>());
builder.Services.AddSingleton<IModeTransitionPolicyStore>(services => services.GetRequiredService<NamedPipeModePersistenceClient>());
builder.Services.AddSingleton<IModeTransitionActionPlanStore>(services => services.GetRequiredService<NamedPipeModePersistenceClient>());
builder.Services.AddSingleton<IModeActionPlanReader, ModeActionPlanReader>();
builder.Services.AddSingleton<IModeTransitionActionJournalStore>(services => services.GetRequiredService<NamedPipeModePersistenceClient>());
builder.Services.AddSingleton<IModeActionRecordReader, ModeActionJournalRecordReader>();
builder.Services.AddSingleton<IModeTransitionRollbackStore>(services => services.GetRequiredService<NamedPipeModePersistenceClient>());
builder.Services.AddSingleton<IModeTransitionReconciliationStore>(services => services.GetRequiredService<NamedPipeModePersistenceClient>());
builder.Services.AddSingleton<RuntimeModeRecoveryCoordinator>();
builder.Services.AddSingleton<IManagedServiceRollbackClient>(services => services.GetRequiredService<NamedPipeModePersistenceClient>());
builder.Services.AddSingleton<IModeActionRollbackHandler, ManagedServiceModeActionRollbackHandler>();
builder.Services.AddSingleton<IModeActionRollbackHandler, DisplayModeActionRollbackHandler>();
builder.Services.AddSingleton<IModeActionRollbackHandler, PowerModeActionRollbackHandler>();
builder.Services.AddSingleton<IModeActionRollbackExecutor, ModeActionRollbackDispatcher>();
builder.Services.AddSingleton<ManagedServiceActionRollbackCoordinator>();
builder.Services.AddSingleton<IControlSessionIdentity, WindowsControlSessionIdentity>();
builder.Services.AddSingleton<ICurrentModeAccess, CurrentModeAccess>();
builder.Services.AddSingleton<IModeSourceAuthority, RuntimeModeSourceAuthority>();
builder.Services.AddSingleton<IManagedServiceSourceVerificationClient>(services => services.GetRequiredService<NamedPipeModePersistenceClient>());
builder.Services.AddSingleton<IModeSourceVerificationHandler, ManagedServiceModeSourceVerificationHandler>();
builder.Services.AddSingleton<IModeSourceVerificationHandler, DisplayModeSourceVerificationHandler>();
builder.Services.AddSingleton<IModeSourceVerificationHandler, PowerModeSourceVerificationHandler>();
builder.Services.AddSingleton<IModeSourceVerificationCoordinator, ModeSourceVerificationDispatcher>();
builder.Services.AddSingleton<RuntimeModeRollbackCompletionCoordinator>(services => new RuntimeModeRollbackCompletionCoordinator(
    services.GetRequiredService<IModeTransitionStore>(),
    services.GetRequiredService<IModeTransitionReconciliationStore>(),
    services.GetRequiredService<IModeSourceAuthority>(),
    services.GetRequiredService<IModeSourceVerificationCoordinator>()));
builder.Services.AddSingleton<RuntimeModeAutomaticRecoveryCoordinator>();
builder.Services.AddSingleton<IModeBaseRecoveryClient>(services => services.GetRequiredService<NamedPipeModePersistenceClient>());
builder.Services.AddSingleton<IModeTransitionCommitStore>(services => services.GetRequiredService<NamedPipeModePersistenceClient>());
builder.Services.AddSingleton<IManagedServiceActionBrokerClient, NamedPipeManagedServiceActionBrokerClient>();
builder.Services.AddSingleton<ManagedServiceActionApplyCoordinator>();
builder.Services.AddSingleton<IManagedServiceActionVerificationBrokerClient, NamedPipeManagedServiceActionVerificationBrokerClient>();
builder.Services.AddSingleton<ManagedServiceActionVerifyCoordinator>();
builder.Services.AddSingleton<IModeActionApplyHandler, ManagedServiceModeActionApplyHandler>();
builder.Services.AddSingleton<IModeActionVerifyHandler, ManagedServiceModeActionVerifyHandler>();
builder.Services.AddSingleton<IModeActionApplyHandler, DisplayModeActionApplyHandler>();
builder.Services.AddSingleton<IModeActionVerifyHandler, DisplayModeActionVerifyHandler>();
builder.Services.AddSingleton<IModeActionApplyHandler, PowerModeActionApplyHandler>();
builder.Services.AddSingleton<IModeActionVerifyHandler, PowerModeActionVerifyHandler>();
builder.Services.AddSingleton<IModeActionApplyCoordinator, ModeActionApplyDispatcher>();
builder.Services.AddSingleton<IModeActionVerifyCoordinator, ModeActionVerifyDispatcher>();
builder.Services.AddSingleton<IModeBasePolicyClient>(services => services.GetRequiredService<NamedPipeModePersistenceClient>());
builder.Services.AddSingleton<BaselineModeTargetPreparationProvider>();
// The durable state machine remains platform-independent. Runtime acceptance re-derives the active
// physical-console owner immediately before entering it; Broker independently repeats OS validation
// for every privileged request.
builder.Services.AddSingleton<RuntimeModeOrchestrator>(services => new RuntimeModeOrchestrator(
    services.GetRequiredService<IMachineStateStore>(), services.GetRequiredService<IMachineMutationLeaseStore>(),
    services.GetRequiredService<IModeTransitionStore>(), services.GetRequiredService<IModeTransitionPolicyStore>(),
    services.GetRequiredService<IModeTransitionActionPlanStore>(), services.GetRequiredService<IModeActionApplyCoordinator>(),
    services.GetRequiredService<IModeActionVerifyCoordinator>(), services.GetRequiredService<IModeTransitionCommitStore>(),
    new ModeBlockerEngine(Array.Empty<IModeBlockerProvider>()), services.GetRequiredService<BaselineModeTargetPreparationProvider>()));
builder.Services.AddSingleton<IRuntimeModeCommandExecutor>(services => new RuntimeModeCommandAuthorityExecutor(
    services.GetRequiredService<IMachineStateStore>(), services.GetRequiredService<IControlSessionIdentity>(),
    services.GetRequiredService<RuntimeModeOrchestrator>()));
builder.Services.AddSingleton<RuntimeModeAccessLossCoordinator>();
builder.Services.AddSingleton<IRuntimeAccessEvaluator, OnlineEntitlementRuntimeAccessEvaluator>();

// Auth.Start is now a stable semantic IPC capability, but production OAuth authority metadata is not
// provisioned in this slice yet. Keep the runtime fail-closed rather than accepting endpoints or keys
// from Manager/user configuration. A release-owned authority provider will replace this registration.
builder.Services.AddSingleton<IRuntimeAuthStartCommand, UnavailableRuntimeAuthStartCommand>();

builder.Services.AddHostedService<PnpHardwareGenerationMonitor>();
builder.Services.AddHostedService<GameInputControllerMonitor>();
builder.Services.AddHostedService<CoreAudioEndpointMonitor>();
builder.Services.AddHostedService<RuntimeStateCoordinator>();
builder.Services.AddHostedService<GameSessionModeBindingService>();
builder.Services.AddHostedService<LauncherProcessSupervisorService>();
builder.Services.AddHostedService<RuntimeModeAccessLossService>();
builder.Services.AddHostedService<RuntimeUiPipeService>();
builder.Services.AddHostedService<BrokerHealthMonitor>();

await builder.Build().RunAsync().ConfigureAwait(false);