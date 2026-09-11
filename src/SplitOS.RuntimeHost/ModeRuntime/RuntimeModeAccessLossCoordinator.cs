using SplitOS.Persistence.Machine;

namespace SplitOS.RuntimeHost.ModeRuntime;

public sealed record RuntimeModeAccessLossOutcome(string Disposition, string ProductCode, RuntimeModeExecutionOutcome? Execution = null);

/// <summary>Converges an idle, same-logon managed mode through the ordinary verified DEACTIVATE path.</summary>
public sealed class RuntimeModeAccessLossCoordinator(
    IMachineStateStore machine, IModeTransitionStore transitions,
    IControlSessionIdentity session, ICurrentModeAccess access,
    IRuntimeModeCommandExecutor executor, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<RuntimeModeAccessLossOutcome> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sessionKey = session.GetCurrentKey();
            var source = await machine.GetOperationalModeAsync(cancellationToken).ConfigureAwait(false);
            // NONE is not proof of BASE while an interrupted activation still owns mutation evidence.
            if ((await transitions.GetIncompleteAsync(cancellationToken).ConfigureAwait(false)).Count != 0)
                return new("RECONCILIATION_REQUIRED", "MODE_ACCESS_LOSS_INCOMPLETE_TRANSITION");
            if (source.CommittedMode == "NONE") return new("NO_CHANGE", "MODE_ACCESS_LOSS_ALREADY_NONE");
            if (source.CommittedMode is not ("WORK" or "GAME") || source.ControlSessionKey != sessionKey)
                return new("RECONCILIATION_REQUIRED", "MODE_ACCESS_LOSS_SOURCE_SESSION_MISMATCH");
            var evaluation = await access.EvaluateAsync(cancellationToken).ConfigureAwait(false);
            if (evaluation.IsEnabled) return new("NO_CHANGE", "MODE_ACCESS_LOSS_ACCESS_RETAINED");
            if (source != await machine.GetOperationalModeAsync(cancellationToken).ConfigureAwait(false) || session.GetCurrentKey() != sessionKey)
                return new("RECONCILIATION_REQUIRED", "MODE_ACCESS_LOSS_CONTEXT_CHANGED");
            var command = new RuntimeModeExecutionCommand(OperationalMode.None, Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), sessionKey, evaluation, _time.GetUtcNow(), null, TimeSpan.FromMinutes(2));
            var result = await executor.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
            // A late NONE->NONE no-op is not verification of the requested BASE transition.
            return result.Disposition == RuntimeModeExecutionDisposition.Completed
                ? new("DEACTIVATED", result.ProductCode, result)
                : new("RECONCILIATION_REQUIRED", result.ProductCode, result);
        }
        finally { _gate.Release(); }
    }
}
