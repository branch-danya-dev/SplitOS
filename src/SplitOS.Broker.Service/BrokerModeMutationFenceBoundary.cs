using SplitOS.Persistence.Machine;

namespace SplitOS.Broker.Service;

public enum BrokerModeMutationExecutionDisposition
{
    Executed,
    Rejected
}

public sealed record BrokerModeMutationExecutionOutcome<T>(
    BrokerModeMutationExecutionDisposition Disposition,
    string ProductCode,
    T? Result = default,
    string? Detail = null)
{
    public bool Executed => Disposition == BrokerModeMutationExecutionDisposition.Executed;
}

/// <summary>
/// Single privileged execution boundary for future MODE machine-mutation capabilities.
/// A capability handler must enter through this boundary immediately before invoking its adapter.
/// The adapter delegate is never invoked when canonical lease/fence/transition/action evidence is stale.
/// </summary>
public sealed class BrokerModeMutationFenceBoundary(ModeMutationFenceStore fenceStore)
{
    public async ValueTask<BrokerModeMutationExecutionOutcome<T>> ExecuteAsync<T>(
        ModeMutationFenceContext context,
        Func<CancellationToken, ValueTask<T>> mutation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(mutation);

        var validation = await fenceStore.ValidateAsync(context, cancellationToken).ConfigureAwait(false);
        if (!validation.IsAuthorized)
        {
            return new BrokerModeMutationExecutionOutcome<T>(
                BrokerModeMutationExecutionDisposition.Rejected,
                validation.ProductCode,
                default,
                validation.Detail);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var result = await mutation(cancellationToken).ConfigureAwait(false);
        return new BrokerModeMutationExecutionOutcome<T>(
            BrokerModeMutationExecutionDisposition.Executed,
            "MODE_MUTATION_EXECUTED",
            result);
    }
}
