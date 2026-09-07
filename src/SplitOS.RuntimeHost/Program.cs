using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SplitOS.Persistence.Projection;
using SplitOS.Persistence.User;
using SplitOS.RuntimeHost;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<RuntimeUiCallerValidator>();
builder.Services.AddSingleton<BrokerHealthState>();
builder.Services.AddSingleton<RuntimeStateState>();
builder.Services.AddSingleton<UserStateStore>();
builder.Services.AddSingleton<ProjectionStore>();
builder.Services.AddSingleton<MachineStateClient>();
builder.Services.AddSingleton<IRuntimeAccessEvaluator, DeterministicFreeRuntimeAccessEvaluator>();
builder.Services.AddHostedService<RuntimeStateCoordinator>();
builder.Services.AddHostedService<RuntimeUiPipeService>();
builder.Services.AddHostedService<BrokerHealthMonitor>();

await builder.Build().RunAsync().ConfigureAwait(false);
