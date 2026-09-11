namespace SplitOS.Persistence.Machine;

public enum PersistedModeRollbackResult
{
    RolledBack,
    Failed,
    Unknown
}

public enum ModeRollbackAdvanceDisposition
{
    Advanced,
    Replayed,
    Missing,
    RevisionConflict,
    LeaseConflict,
    ReconciliationRequired,
    OwnershipConflict,
    InvalidLifecycle
}

public sealed record ModeRollbackAdvanceOutcome(
    ModeRollbackAdvanceDisposition Disposition,
    PersistedModeActionRecord? Action,
    string ProductCode,
    int? ActualActionRevision = null,
    string? Detail = null);

