using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SplitOS.Persistence.Machine;
using SplitOS.Persistence.ProtectedSecrets;
using SplitOS.Persistence.Projection;
using SplitOS.Persistence.User;
using SplitOS.RuntimeHost;
using SplitOS.RuntimeHost.Authentication;
using SplitOS.RuntimeHost.ModeRuntime;
using SplitOS.RuntimeHost.ProductIdentity;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<RuntimeUiCallerValidator>();
builder.Services.AddSingleton<BrokerHealthState>();
builder.Services.AddSingleton<RuntimeStateState>();
builder.Services.AddSingleton<RuntimeStateRefreshSignal>();
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
builder.Services.AddSingleton<ModeTransitionActionJournalStore>();
builder.Services.AddSingleton<IManagedServiceActionBrokerClient, NamedPipeManagedServiceActionBrokerClient>();
builder.Services.AddSingleton<ManagedServiceActionApplyCoordinator>();
builder.Services.AddSingleton<IRuntimeAccessEvaluator, OnlineEntitlementRuntimeAccessEvaluator>();

// Auth.Start is now a stable semantic IPC capability, but production OAuth authority metadata is not
// provisioned in this slice yet. Keep the runtime fail-closed rather than accepting endpoints or keys
// from Manager/user configuration. A release-owned authority provider will replace this registration.
builder.Services.AddSingleton<IRuntimeAuthStartCommand, UnavailableRuntimeAuthStartCommand>();

builder.Services.AddHostedService<RuntimeStateCoordinator>();
builder.Services.AddHostedService<RuntimeUiPipeService>();
builder.Services.AddHostedService<BrokerHealthMonitor>();

await builder.Build().RunAsync().ConfigureAwait(false);
