namespace SplitOS.Persistence.Machine;

public interface IModeTransitionPolicyStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<ModeTransitionPolicyBinding?> GetAsync(Guid transitionId, CancellationToken cancellationToken = default);
    Task<ModeTransitionPolicyBindOutcome> BindResolvedPolicyAsync(
        Guid transitionId,
        int expectedTransitionRevision,
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        PersistedModePolicyIdentity identity,
        PersistedModePolicyTarget target,
        string resolvedDigest,
        IReadOnlyCollection<PersistedModePolicyFallbackSelection>? selectedFallbacks = null,
        CancellationToken cancellationToken = default);
}
