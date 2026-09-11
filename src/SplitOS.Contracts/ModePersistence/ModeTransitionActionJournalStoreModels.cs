// Shared durable models; namespace retained for source compatibility.
namespace SplitOS.Persistence.Machine;

public enum PersistedModeApplyResult
{
    Applied,
    Failed,
    Unknown
}

public enum PersistedModeVerifyResult
{
    Verified,
    Mismatch,
    Unknown
}

public enum ModeActionAdvanceDisposition
{
    Advanced,
    Replayed,
    Missing,
    RevisionConflict,
    LeaseConflict,
    ReconciliationRequired,
    OwnershipConflict,
    InvalidLifecycle,
    EvidenceConflict
}

public sealed record ModeActionAdvanceOutcome(
    ModeActionAdvanceDisposition Disposition,
    PersistedModeActionRecord? Action,
    string ProductCode,
    int? ActualActionRevision = null,
    string? Detail = null);
