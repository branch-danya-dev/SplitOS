namespace SplitOS.Contracts.Protocol;

public sealed record MachineServicePolicyVerifyRequest(
    Guid TransitionId,
    Guid ActionId,
    Guid LeaseId,
    long FenceToken,
    string ControlSessionKey,
    int ExpectedActionRevision,
    IReadOnlyList<ManagedServicePolicyEntry> Entries);

public sealed record ManagedServicePolicyVerificationEntryResult(
    string ManagedServiceId,
    string DesiredState,
    string ActualStateObserved,
    string VerificationStatus,
    string? ErrorCode);

public sealed record MachineServicePolicyVerifyResult(
    string Disposition,
    string ProductCode,
    IReadOnlyList<ManagedServicePolicyVerificationEntryResult> Entries);
