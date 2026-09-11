namespace SplitOS.Persistence.Machine;

public interface IModeTransitionActionJournalStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<PersistedModeActionRecord?> GetAsync(Guid actionId, CancellationToken cancellationToken = default);
    Task<ModeActionAdvanceOutcome> BeginApplyAsync(
        Guid transitionId,
        Guid actionId,
        int expectedActionRevision,
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        string? preStateJson = null,
        string? preStateDigest = null,
        CancellationToken cancellationToken = default);
    Task<ModeActionAdvanceOutcome> RecordApplyResultAsync(
        Guid transitionId,
        Guid actionId,
        int expectedActionRevision,
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        PersistedModeApplyResult result,
        CancellationToken cancellationToken = default);
    Task<ModeActionAdvanceOutcome> BeginVerifyAsync(
        Guid transitionId,
        Guid actionId,
        int expectedActionRevision,
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        CancellationToken cancellationToken = default);
    Task<ModeActionAdvanceOutcome> RecordVerifyResultAsync(
        Guid transitionId,
        Guid actionId,
        int expectedActionRevision,
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        PersistedModeVerifyResult result,
        CancellationToken cancellationToken = default);
}
