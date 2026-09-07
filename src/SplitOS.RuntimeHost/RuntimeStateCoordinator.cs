using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SplitOS.Contracts.Protocol;
using SplitOS.Persistence.Projection;
using SplitOS.Persistence.User;

namespace SplitOS.RuntimeHost;

public sealed partial class RuntimeStateCoordinator(
    ILogger<RuntimeStateCoordinator> logger,
    UserStateStore userStateStore,
    ProjectionStore projectionStore,
    MachineStateClient machineStateClient,
    IRuntimeAccessEvaluator accessEvaluator,
    AccountAssociationCoordinator associationCoordinator,
    RuntimeStateState state) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await userStateStore.InitializeAsync(stoppingToken).ConfigureAwait(false);
        await projectionStore.InitializeOrRebuildAsync(stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var mode = await machineStateClient.ReadOperationalModeAsync(stoppingToken).ConfigureAwait(false);
                var access = await accessEvaluator.EvaluateAsync(stoppingToken).ConfigureAwait(false);
                var association = await associationCoordinator.EvaluateAsync(stoppingToken).ConfigureAwait(false);

                state.Report(new RuntimeStateReadResult(
                    "READY",
                    access,
                    mode.CommittedMode,
                    association.AssociationState,
                    mode.SchemaVersion,
                    UserStateStore.SchemaVersion,
                    ProjectionStore.SchemaVersion,
                    DateTimeOffset.UtcNow));
                LogReady(logger, access, mode.CommittedMode, association.AssociationState);
                return;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or InvalidDataException)
            {
                state.ReportUnavailable();
                LogWaiting(logger, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
            }
        }
    }

    [LoggerMessage(2200, LogLevel.Information, "Runtime state READY. ManagedRuntime={Access} OperationalMode={Mode} Association={Association}.")]
    private static partial void LogReady(ILogger logger, string access, string mode, string association);

    [LoggerMessage(2201, LogLevel.Warning, "Runtime state waiting for canonical persistence: {Message}")]
    private static partial void LogWaiting(ILogger logger, string message);
}
