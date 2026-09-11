using SplitOS.Persistence.Machine;

namespace SplitOS.RuntimeHost.ModeRuntime;

/// <summary>
/// Runtime acceptance boundary for machine-wide MODE commands. The caller-supplied control-session
/// key is treated only as continuity evidence: the current physical-console owner is derived again
/// from OS-owned facts immediately before the durable orchestrator is entered.
/// </summary>
public sealed class RuntimeModeCommandAuthorityExecutor(
    IMachineStateStore machineStateStore,
    IControlSessionIdentity controlSessionIdentity,
    IRuntimeModeCommandExecutor inner) : IRuntimeModeCommandExecutor
{
    public async Task<RuntimeModeExecutionOutcome> ExecuteAsync(
        RuntimeModeExecutionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        // WindowsControlSessionIdentity fails closed when this RuntimeHost no longer belongs to the
        // active physical console. Do not translate that failure into a guessed canonical outcome.
        var currentControlSessionKey = controlSessionIdentity.GetCurrentKey();
        if (string.Equals(currentControlSessionKey, command.ControlSessionKey, StringComparison.Ordinal))
            return await inner.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);

        await machineStateStore.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var canonical = await machineStateStore.GetOperationalModeAsync(cancellationToken).ConfigureAwait(false);
        return new RuntimeModeExecutionOutcome(
            RuntimeModeExecutionDisposition.AuthorityDenied,
            "MODE_CONTROL_CONTEXT_STALE",
            canonical,
            Detail: "The MODE command belongs to a control session that is no longer the active physical-console owner.");
    }
}
