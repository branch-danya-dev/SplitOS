namespace SplitOS.Persistence.Machine;

public interface IModeTransitionActionPlanStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<ModeTransitionActionPlan?> GetAsync(Guid transitionId, CancellationToken cancellationToken = default);
    Task<ModeTransitionActionPlanPersistOutcome> PersistActionPlanAsync(
        Guid transitionId,
        int expectedTransitionRevision,
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        IReadOnlyCollection<PersistedModeActionDefinition> actions,
        CancellationToken cancellationToken = default);
}
