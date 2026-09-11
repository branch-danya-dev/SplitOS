namespace SplitOS.Persistence.Machine;

public interface IModeTransitionReconciliationStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<ModeCrashReconciliationSnapshot> InspectAsync(string currentControlSessionKey, CancellationToken cancellationToken = default);
    Task<ModeReconciliationTakeoverOutcome> TakeOverAsync(Guid transitionId, int expectedTransitionRevision, string currentControlSessionKey, TimeSpan leaseLifetime, CancellationToken cancellationToken = default);
    Task<ModeTerminalLeaseCleanupOutcome> ReleaseTerminalLeaseAsync(CancellationToken cancellationToken = default);
}
