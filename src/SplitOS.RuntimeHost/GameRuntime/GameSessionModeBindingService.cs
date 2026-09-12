using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SplitOS.RuntimeHost.GameRuntime;

/// <summary>
/// Maintains the top-level GameSession invariant against canonical committed mode after Runtime startup
/// and mode changes. It does not infer game process state: it only enters LAUNCHER from INACTIVE after
/// committed GAME is authoritative, or forces INACTIVE after GAME is no longer committed.
/// </summary>
public sealed partial class GameSessionModeBindingService(
    ILogger<GameSessionModeBindingService> logger,
    RuntimeStateState runtimeState,
    GameSessionStateMachine gameSession) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var runtime = runtimeState.Snapshot;
            if (string.Equals(runtime.Status, "READY", StringComparison.Ordinal))
            {
                var gameCommitted = string.Equals(runtime.OperationalMode, "GAME", StringComparison.Ordinal);
                var session = gameSession.Snapshot;

                if (gameCommitted && session.State == GameSessionState.Inactive)
                {
                    var result = gameSession.Apply(new GameSessionTransitionRequest(
                        GameSessionSignal.LauncherReady,
                        IsGameModeCommitted: true));
                    LogBinding(logger, result.PreviousState.ToString(), result.Snapshot.State.ToString(), result.ReasonCode);
                }
                else if (!gameCommitted && session.State != GameSessionState.Inactive)
                {
                    var result = gameSession.Apply(new GameSessionTransitionRequest(
                        GameSessionSignal.ModeDeactivated,
                        IsGameModeCommitted: false));
                    LogBinding(logger, result.PreviousState.ToString(), result.Snapshot.State.ToString(), result.ReasonCode);
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(2710, LogLevel.Information, "GameSession mode binding {PreviousState}->{State} reason={Reason}.")]
    private static partial void LogBinding(ILogger logger, string previousState, string state, string reason);
}
