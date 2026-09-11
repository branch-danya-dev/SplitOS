namespace SplitOS.RuntimeHost.ModeRuntime;

/// <summary>
/// Keeps existing direct managed-service orchestrator tests source-compatible while production DI
/// routes through the durable action dispatcher. This adapter is intentionally not registered in
/// production; it exists only as an explicit compatibility bridge for callers that still construct
/// RuntimeModeOrchestrator with the legacy concrete coordinators.
/// </summary>
public sealed class ManagedServiceDirectActionApplyCoordinatorAdapter(
    ManagedServiceActionApplyCoordinator inner) : IModeActionApplyCoordinator
{
    public async Task<ModeActionApplyStageOutcome> ApplyAsync(
        ModeActionExecutionCommand command,
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

        return new ModeActionApplyStageOutcome(
            result.IsApplied,
            result.ProductCode,
            result.ActionRevision,
            result.Detail);
    }
}

public sealed class ManagedServiceDirectActionVerifyCoordinatorAdapter(
    ManagedServiceActionVerifyCoordinator inner) : IModeActionVerifyCoordinator
{
    public async Task<ModeActionVerifyStageOutcome> VerifyAsync(
        ModeActionExecutionCommand command,
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

        return new ModeActionVerifyStageOutcome(
            result.IsVerified,
            result.ProductCode,
            result.ActionRevision,
            result.Detail);
    }
}
