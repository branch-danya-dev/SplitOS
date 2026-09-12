using SplitOS.Contracts.Protocol;
using SplitOS.Persistence.Machine;

namespace SplitOS.RuntimeHost.ModeRuntime;

public sealed record ModeActionRollbackCommand(
    Guid TransitionId,
    Guid ActionId,
    int ActionRevision,
    Guid LeaseId,
    long FenceToken,
    Guid OperationId,
    Guid CorrelationId,
    string ControlSessionKey);

public interface IModeActionRollbackHandler
{
    bool CanHandle(PersistedModeActionRecord action);

    Task<RuntimeModeRollbackStepOutcome> RollbackAsync(
        ModeActionRollbackCommand command,
        PersistedModeActionRecord action,
        CancellationToken cancellationToken = default);
}

public interface IModeActionRollbackExecutor
{
    Task<RuntimeModeRollbackStepOutcome> RollbackAsync(
        ModeActionRollbackCommand command,
        PersistedModeActionRecord action,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Selects exactly one compensation handler from durable action semantics. Rollback ordering,
/// lease/fence ownership, lifecycle advancement and durable result recording stay outside this
/// dispatcher so handlers cannot reorder or self-commit recovery work.
/// </summary>
public sealed class ModeActionRollbackDispatcher(IEnumerable<IModeActionRollbackHandler> handlers)
    : IModeActionRollbackExecutor
{
    private readonly IModeActionRollbackHandler[] _handlers = handlers?.ToArray()
        ?? throw new ArgumentNullException(nameof(handlers));

    public async Task<RuntimeModeRollbackStepOutcome> RollbackAsync(
        ModeActionRollbackCommand command,
        PersistedModeActionRecord action,
        CancellationToken cancellationToken = default)
    {
        Validate(command, action);

        var matches = _handlers.Where(handler => handler.CanHandle(action)).Take(2).ToArray();
        if (matches.Length == 0)
            return new("RECONCILIATION_REQUIRED", "MODE_ACTION_ROLLBACK_HANDLER_UNAVAILABLE");
        if (matches.Length > 1)
            return new("RECONCILIATION_REQUIRED", "MODE_ACTION_ROLLBACK_HANDLER_AMBIGUOUS");

        return await matches[0].RollbackAsync(command, action, cancellationToken).ConfigureAwait(false);
    }

    private static void Validate(ModeActionRollbackCommand command, PersistedModeActionRecord action)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(action);
        if (command.TransitionId == Guid.Empty || command.ActionId == Guid.Empty || command.LeaseId == Guid.Empty ||
            command.OperationId == Guid.Empty || command.CorrelationId == Guid.Empty)
            throw new ArgumentException("Mode rollback identifiers must not be empty.", nameof(command));
        if (command.ActionRevision < 1) throw new ArgumentOutOfRangeException(nameof(command));
        if (command.FenceToken < 1) throw new ArgumentOutOfRangeException(nameof(command));
        if (string.IsNullOrWhiteSpace(command.ControlSessionKey) || command.ControlSessionKey.Length > 256 ||
            command.ControlSessionKey.Any(static character => char.IsControl(character)))
            throw new ArgumentException("ControlSessionKey is outside supported bounds.", nameof(command));
        if (action.ActionId != command.ActionId || action.TransitionId != command.TransitionId)
            throw new InvalidDataException("Rollback action identity does not match durable recovery command.");
        if (action.Revision != command.ActionRevision)
            throw new InvalidDataException("Rollback action revision changed before compensation dispatch.");
        if (action.State != PersistedModeActionState.RollingBack)
            throw new InvalidDataException($"Rollback dispatch requires ROLLING_BACK action state; actual state is {action.State}.");
    }
}

public sealed class ManagedServiceModeActionRollbackHandler(IManagedServiceRollbackClient broker)
    : IModeActionRollbackHandler
{
    public bool CanHandle(PersistedModeActionRecord action) =>
        string.Equals(action.OwningModule, ManagedServicePolicyActionContract.OwningModule, StringComparison.Ordinal) &&
        string.Equals(action.ActionType, ManagedServicePolicyActionContract.ActionType, StringComparison.Ordinal);

    public async Task<RuntimeModeRollbackStepOutcome> RollbackAsync(
        ModeActionRollbackCommand command,
        PersistedModeActionRecord action,
        CancellationToken cancellationToken = default)
    {
        var result = await broker.RollbackAsync(
            command.OperationId,
            command.CorrelationId,
            new MachineServicePolicyRollbackRequest(
                command.TransitionId,
                command.ActionId,
                command.LeaseId,
                command.FenceToken,
                command.ControlSessionKey,
                command.ActionRevision),
            cancellationToken).ConfigureAwait(false);
        return new RuntimeModeRollbackStepOutcome(result.Disposition, result.ProductCode);
    }
}
