namespace SplitOS.RuntimeHost;

public interface IRuntimeAccessEvaluator
{
    ValueTask<string> EvaluateAsync(CancellationToken cancellationToken = default);
}

public sealed class DeterministicFreeRuntimeAccessEvaluator : IRuntimeAccessEvaluator
{
    public ValueTask<string> EvaluateAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult("DISABLED");
}
