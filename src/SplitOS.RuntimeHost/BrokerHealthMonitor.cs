using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SplitOS.Contracts.Protocol;
using SplitOS.Ipc;

namespace SplitOS.RuntimeHost;

public sealed partial class BrokerHealthMonitor(
    ILogger<BrokerHealthMonitor> logger,
    BrokerHealthState healthState) : BackgroundService
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
            using var process = Process.GetCurrentProcess();
            var sessionId = process.SessionId;
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
                healthState.ReportHealthy(health.ProcessId, health.SessionId);
                LogHealth(logger, health.Status, health.ProcessId, health.SessionId);
                return;
            }

            var error = response.ReadPayload<ErrorResponse>();
            healthState.ReportUnavailable($"{error.Code}: {error.Message}");
            LogProbeError(logger, error.Code, error.Message);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or InvalidDataException)
        {
            healthState.ReportUnavailable(ex.Message);
            LogUnavailable(logger, ex.Message);
        }
    }

    [LoggerMessage(2100, LogLevel.Information, "Broker health {Status}. BrokerPID={ProcessId} Session={SessionId}.")]
    private static partial void LogHealth(ILogger logger, string status, int processId, int sessionId);

    [LoggerMessage(2101, LogLevel.Warning, "Broker health probe returned {Code}: {Message}")]
    private static partial void LogProbeError(ILogger logger, string code, string message);

    [LoggerMessage(2102, LogLevel.Warning, "Broker health probe unavailable: {Message}")]
    private static partial void LogUnavailable(ILogger logger, string message);
}
