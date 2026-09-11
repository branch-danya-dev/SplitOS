namespace SplitOS.Persistence.Machine;

public interface IModeTransitionCommitStore
{
    Task<ModeTransitionCommitOutcome> CommitTransitionAndModeAsync(
        Guid transitionId,
        int expectedTransitionRevision,
        int expectedModeRevision,
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        bool runtimeAccessPermitsTarget,
        Guid? activationEpochId,
        PersistedModePolicyIdentity? currentPolicyIdentity,
        CancellationToken cancellationToken = default);
}
