using SplitOS.Contracts.Protocol;
using SplitOS.Persistence.Machine;

namespace SplitOS.RuntimeHost.ModeRuntime;

public sealed record ModeActionExecutionCommand(
    Guid TransitionId,
    Guid ActionId,
    int ExpectedActionRevision,
    Guid LeaseId,
    long FenceToken,
    Guid OperationId,
    Guid CorrelationId,
    string ControlSessionKey);

public sealed record ModeActionApplyStageOutcome(
    bool IsApplied,
    string ProductCode,
    int? ActionRevision = null,
    string? Detail = null);

public sealed record ModeActionVerifyStageOutcome(
    bool IsVerified,
    string ProductCode,
    int? ActionRevision = null,
    string? Detail = null);

public interface IModeActionApplyCoordinator
{
    Task<ModeActionApplyStageOutcome> ApplyAsync(
        ModeActionExecutionCommand command,
        CancellationToken cancellationToken = default);
}

public interface IModeActionVerifyCoordinator
{
    Task<ModeActionVerifyStageOutcome> VerifyAsync(
        ModeActionExecutionCommand command,
        CancellationToken cancellationToken = default);
}

public interface IModeActionApplyHandler
{
    bool CanHandle(PersistedModeActionRecord action);

    Task<ModeActionApplyStageOutcome> ApplyAsync(
        ModeActionExecutionCommand command,
        PersistedModeActionRecord action,
        CancellationToken cancellationToken = default);
}

public interface IModeActionVerifyHandler
{
    bool CanHandle(PersistedModeActionRecord action);

    Task<ModeActionVerifyStageOutcome> VerifyAsync(
        ModeActionExecutionCommand command,
        PersistedModeActionRecord action,
        CancellationToken cancellationToken = default);
}

public sealed class ModeActionApplyDispatcher(
    IModeTransitionActionJournalStore actionJournalStore,
    IEnumerable<IModeActionApplyHandler> handlers) : IModeActionApplyCoordinator
{
    private readonly IModeActionApplyHandler[] _handlers = handlers?.ToArray()
        ?? throw new ArgumentNullException(nameof(handlers));

    public async Task<ModeActionApplyStageOutcome> ApplyAsync(
        ModeActionExecutionCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateCommand(command);
        await actionJournalStore.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var action = await actionJournalStore.GetAsync(command.ActionId, cancellationToken).ConfigureAwait(false);
        var validation = ValidateDurableAction(command, action);
        if (validation is not null) return new(false, validation.Value.ProductCode, action?.Revision, validation.Value.Detail);

        var matches = _handlers.Where(handler => handler.CanHandle(action!)).Take(2).ToArray();
        if (matches.Length == 0)
        {
            return new ModeActionApplyStageOutcome(
                false,
                "MODE_ACTION_APPLY_HANDLER_UNAVAILABLE",
                action!.Revision,
                $"No APPLY handler is registered for durable action {action.OwningModule}/{action.ActionType}.");
        }
        if (matches.Length > 1)
        {
            return new ModeActionApplyStageOutcome(
                false,
                "MODE_ACTION_APPLY_HANDLER_AMBIGUOUS",
                action!.Revision,
                $"More than one APPLY handler accepted durable action {action.OwningModule}/{action.ActionType}.");
        }

        return await matches[0].ApplyAsync(command, action!, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateCommand(ModeActionExecutionCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.TransitionId == Guid.Empty || command.ActionId == Guid.Empty || command.LeaseId == Guid.Empty ||
            command.OperationId == Guid.Empty || command.CorrelationId == Guid.Empty)
            throw new ArgumentException("Mode action identifiers must not be empty.", nameof(command));
        if (command.ExpectedActionRevision < 1) throw new ArgumentOutOfRangeException(nameof(command));
        if (command.FenceToken < 1) throw new ArgumentOutOfRangeException(nameof(command));
        if (string.IsNullOrWhiteSpace(command.ControlSessionKey))
            throw new ArgumentException("Control session key must not be empty.", nameof(command));
    }

    internal static (string ProductCode, string Detail)? ValidateDurableAction(
        ModeActionExecutionCommand command,
        PersistedModeActionRecord? action)
    {
        if (action is null)
            return ("MODE_ACTION_MISSING", "Durable action does not exist.");
        if (action.TransitionId != command.TransitionId)
            return ("MODE_ACTION_TRANSITION_MISMATCH", "Durable action belongs to a different transition.");
        if (action.Revision != command.ExpectedActionRevision)
            return ("MODE_ACTION_REVISION_MISMATCH", "Caller action revision is stale.");
        return null;
    }
}

public sealed class ModeActionVerifyDispatcher(
    IModeTransitionActionJournalStore actionJournalStore,
    IEnumerable<IModeActionVerifyHandler> handlers) : IModeActionVerifyCoordinator
{
    private readonly IModeActionVerifyHandler[] _handlers = handlers?.ToArray()
        ?? throw new ArgumentNullException(nameof(handlers));

    public async Task<ModeActionVerifyStageOutcome> VerifyAsync(
        ModeActionExecutionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await actionJournalStore.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var action = await actionJournalStore.GetAsync(command.ActionId, cancellationToken).ConfigureAwait(false);
        var validation = ModeActionApplyDispatcher.ValidateDurableAction(command, action);
        if (validation is not null) return new(false, validation.Value.ProductCode, action?.Revision, validation.Value.Detail);

        var matches = _handlers.Where(handler => handler.CanHandle(action!)).Take(2).ToArray();
        if (matches.Length == 0)
        {
            return new ModeActionVerifyStageOutcome(
                false,
                "MODE_ACTION_VERIFY_HANDLER_UNAVAILABLE",
                action!.Revision,
                $"No VERIFY handler is registered for durable action {action.OwningModule}/{action.ActionType}.");
        }
        if (matches.Length > 1)
        {
            return new ModeActionVerifyStageOutcome(
                false,
                "MODE_ACTION_VERIFY_HANDLER_AMBIGUOUS",
                action!.Revision,
                $"More than one VERIFY handler accepted durable action {action.OwningModule}/{action.ActionType}.");
        }

        return await matches[0].VerifyAsync(command, action!, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class ManagedServiceModeActionApplyHandler(ManagedServiceActionApplyCoordinator inner)
    : IModeActionApplyHandler
{
    public bool CanHandle(PersistedModeActionRecord action) =>
        string.Equals(action.OwningModule, ManagedServicePolicyActionContract.OwningModule, StringComparison.Ordinal) &&
        string.Equals(action.ActionType, ManagedServicePolicyActionContract.ActionType, StringComparison.Ordinal);

    public async Task<ModeActionApplyStageOutcome> ApplyAsync(
        ModeActionExecutionCommand command,
        PersistedModeActionRecord action,
        CancellationToken cancellationToken = default)
    {
        var result = await inner.ApplyAsync(
            new ManagedServiceActionApplyCommand(
                command.TransitionId,
                command.ActionId,
                command.ExpectedActionRevision,
                command.LeaseId,
                command.FenceToken,
                command.OperationId,
                command.CorrelationId,
                command.ControlSessionKey),
            cancellationToken).ConfigureAwait(false);
        return new(result.IsApplied, result.ProductCode, result.ActionRevision, result.Detail);
    }
}

public sealed class ManagedServiceModeActionVerifyHandler(ManagedServiceActionVerifyCoordinator inner)
    : IModeActionVerifyHandler
{
    public bool CanHandle(PersistedModeActionRecord action) =>
        string.Equals(action.OwningModule, ManagedServicePolicyActionContract.OwningModule, StringComparison.Ordinal) &&
        string.Equals(action.ActionType, ManagedServicePolicyActionContract.ActionType, StringComparison.Ordinal);

    public async Task<ModeActionVerifyStageOutcome> VerifyAsync(
        ModeActionExecutionCommand command,
        PersistedModeActionRecord action,
        CancellationToken cancellationToken = default)
    {
        var result = await inner.VerifyAsync(
            new ManagedServiceActionVerifyCommand(
                command.TransitionId,
                command.ActionId,
                command.ExpectedActionRevision,
                command.LeaseId,
                command.FenceToken,
                command.OperationId,
                command.CorrelationId,
                command.ControlSessionKey),
            cancellationToken).ConfigureAwait(false);
        return new(result.IsVerified, result.ProductCode, result.ActionRevision, result.Detail);
    }
}
