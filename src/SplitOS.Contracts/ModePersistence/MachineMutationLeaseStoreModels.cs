// Shared durable models; namespace retained for source compatibility.
namespace SplitOS.Persistence.Machine;

public enum MachineMutationType
{
    Mode,
    Update,
    Recovery
}

public sealed record MachineMutationLeaseRecord(
    Guid? LeaseId,
    MachineMutationType? MutationType,
    Guid? OwnerOperationId,
    Guid? OwnerCorrelationId,
    string? OwnerControlSessionKey,
    long FenceToken,
    DateTimeOffset? AcquiredUtc,
    DateTimeOffset? HeartbeatUtc,
    DateTimeOffset? ExpiresUtc,
    int Revision)
{
    public bool IsHeld => LeaseId.HasValue;
}

public enum MachineMutationLeaseAcquireDisposition
{
    Acquired,
    AlreadyOwned,
    Busy,
    ReconciliationRequired
}

public sealed record MachineMutationLeaseAcquireOutcome(
    MachineMutationLeaseAcquireDisposition Disposition,
    MachineMutationLeaseRecord Lease,
    string ProductCode);

public enum MachineMutationLeaseRenewDisposition
{
    Renewed,
    StaleOwner,
    ReconciliationRequired
}

public sealed record MachineMutationLeaseRenewOutcome(
    MachineMutationLeaseRenewDisposition Disposition,
    MachineMutationLeaseRecord Lease,
    string ProductCode);

public enum MachineMutationLeaseReleaseDisposition
{
    Released,
    AlreadyReleased,
    StaleOwner,
    ReconciliationRequired
}

public sealed record MachineMutationLeaseReleaseOutcome(
    MachineMutationLeaseReleaseDisposition Disposition,
    MachineMutationLeaseRecord Lease,
    string ProductCode);
