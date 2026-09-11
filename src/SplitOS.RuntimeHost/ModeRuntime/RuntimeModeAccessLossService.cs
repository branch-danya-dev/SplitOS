using System.ComponentModel;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SplitOS.RuntimeHost.ModeRuntime;

/// <summary>Serializes automatic recovery before checking idle managed ownership.</summary>
public sealed class RuntimeModeAccessLossService(RuntimeModeAccessLossCoordinator coordinator,
    RuntimeModeAutomaticRecoveryCoordinator recovery,
    ILogger<RuntimeModeAccessLossService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string? lastCode = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var recovered = await recovery.ReconcileAsync(stoppingToken).ConfigureAwait(false);
                var outcome = recovered.MayCheckAccess
                    ? await coordinator.ReconcileAsync(stoppingToken).ConfigureAwait(false)
                    : new RuntimeModeAccessLossOutcome("RECOVERY_PENDING", recovered.ProductCode);
                if (outcome.ProductCode != lastCode)
                {
                    logger.LogInformation("Mode access reconciliation: {Disposition} {ProductCode}", outcome.Disposition, outcome.ProductCode);
                    lastCode = outcome.ProductCode;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or TimeoutException or Win32Exception
                or System.Data.Common.DbException or InvalidOperationException or System.Security.SecurityException)
            {
                if (lastCode != "MODE_ACCESS_RECONCILIATION_UNAVAILABLE")
                    logger.LogWarning(ex, "Mode access reconciliation is unavailable; preserving durable evidence.");
                lastCode = "MODE_ACCESS_RECONCILIATION_UNAVAILABLE";
            }
            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
