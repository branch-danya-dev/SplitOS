using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SplitOS.Broker.Service;
using SplitOS.Persistence.Machine;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "SplitOS Privileged Broker");
builder.Services.AddSingleton<MachineStateStore>();
builder.Services.AddSingleton<ModeMutationFenceStore>();
builder.Services.AddSingleton<ModePreMutationEvidenceStore>();
builder.Services.AddSingleton<ModeVerificationEvidenceStore>();
builder.Services.AddSingleton<ModeTransitionActionJournalStore>();
builder.Services.AddSingleton<BrokerModeMutationFenceBoundary>();
builder.Services.AddSingleton<IManagedServiceCatalog, ReleaseManagedServiceCatalog>();
builder.Services.AddSingleton<IManagedServiceAdapter, WindowsManagedServiceAdapter>();
builder.Services.AddSingleton<BrokerManagedServiceSnapshotExecutor>();
builder.Services.AddSingleton<BrokerManagedServicePolicyExecutor>();
builder.Services.AddSingleton<BrokerManagedServiceVerificationExecutor>();
builder.Services.AddSingleton<BrokerCallerValidator>();
builder.Services.AddSingleton<BrokerMessageHandler>();
builder.Services.AddHostedService<BrokerPipeService>();

await builder.Build().RunAsync().ConfigureAwait(false);
