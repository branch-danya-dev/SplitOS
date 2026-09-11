namespace SplitOS.Persistence.Machine;

public interface IMachineMutationLeaseStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<MachineMutationLeaseRecord> GetAsync(CancellationToken cancellationToken = default);
    Task<MachineMutationLeaseAcquireOutcome> TryAcquireAsync(
        MachineMutationType mutationType,
        Guid ownerOperationId,
        Guid ownerCorrelationId,
        string ownerControlSessionKey,
        TimeSpan leaseLifetime,
        CancellationToken cancellationToken = default);
    Task<MachineMutationLeaseRenewOutcome> RenewAsync(
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        TimeSpan leaseLifetime,
        CancellationToken cancellationToken = default);
    Task<MachineMutationLeaseReleaseOutcome> ReleaseAsync(
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        CancellationToken cancellationToken = default);
}
