using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SplitOS.Broker.Service;
using SplitOS.Persistence.Machine;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "SplitOS Privileged Broker");
builder.Services.AddSingleton<MachineStateStore>();
builder.Services.AddSingleton<BrokerCallerValidator>();
builder.Services.AddSingleton<BrokerMessageHandler>();
builder.Services.AddHostedService<BrokerPipeService>();

await builder.Build().RunAsync().ConfigureAwait(false);
