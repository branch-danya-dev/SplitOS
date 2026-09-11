namespace SplitOS.Persistence.Machine;

public interface IModeTransitionRollbackStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<PersistedModeActionRecord?> GetNextRollbackCandidateAsync(Guid transitionId, CancellationToken cancellationToken = default);
    Task<ModeRollbackAdvanceOutcome> BeginRollbackAsync(Guid transitionId, Guid actionId, int expectedActionRevision, Guid leaseId, long fenceToken, Guid ownerOperationId, CancellationToken cancellationToken = default);
    Task<ModeRollbackAdvanceOutcome> RecordRollbackResultAsync(Guid transitionId, Guid actionId, int expectedActionRevision, Guid leaseId, long fenceToken, Guid ownerOperationId, PersistedModeRollbackResult result, CancellationToken cancellationToken = default);
}
