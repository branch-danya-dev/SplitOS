using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SplitOS.Contracts.Protocol;
using SplitOS.Ipc;

namespace SplitOS.RuntimeHost;

public sealed class BrokerHealthMonitor(ILogger<BrokerHealthMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ProbeOnceAsync(stoppingToken).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ProbeOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var sessionId = Process.GetCurrentProcess().SessionId;
            var client = new NamedPipeRpcClient(
                NamedPipeNames.BrokerForSession(sessionId),
                ComponentIdentity.Name,
                ComponentIdentity.Version,
                connectTimeoutMilliseconds: 1_500);

            var request = WireMessage.Create(
                MessageTypes.HealthReadRequest,
                new HealthReadRequest(),
                Capabilities.BrokerHealthRead);

            var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (string.Equals(response.MessageType, MessageTypes.HealthReadResult, StringComparison.Ordinal))
            {
                var health = response.ReadPayload<HealthReadResult>();
                logger.LogInformation(
                    "Broker health {Status}. BrokerPID={ProcessId} Session={SessionId}.",
                    health.Status,
                    health.ProcessId,
                    health.SessionId);
                return;
            }

            var error = response.ReadPayload<ErrorResponse>();
            logger.LogWarning("Broker health probe returned {Code}: {Message}", error.Code, error.Message);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or InvalidDataException)
        {
            logger.LogWarning("Broker health probe unavailable: {Message}", ex.Message);
        }
    }
}
