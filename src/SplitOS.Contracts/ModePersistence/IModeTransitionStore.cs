namespace SplitOS.Persistence.Machine;

public interface IModeTransitionStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<ModeTransitionRecord?> GetAsync(Guid transitionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ModeTransitionRecord>> GetIncompleteAsync(CancellationToken cancellationToken = default);
    Task<ModeTransitionCreateOutcome> CreateAsync(
        Guid transitionId,
        Guid operationId,
        Guid correlationId,
        PersistedModeOperationKind operationKind,
        string sourceMode,
        string targetMode,
        int sourceModeRevision,
        string controlSessionKey,
        Guid leaseId,
        long fenceToken,
        CancellationToken cancellationToken = default);
    Task<ModeTransitionAdvanceOutcome> AdvanceAsync(
        Guid transitionId,
        int expectedRevision,
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        PersistedModeTransitionState nextState,
        PersistedModeTransitionStage nextStage,
        bool mandatoryVerified,
        string? terminalOutcome = null,
        CancellationToken cancellationToken = default);
}
