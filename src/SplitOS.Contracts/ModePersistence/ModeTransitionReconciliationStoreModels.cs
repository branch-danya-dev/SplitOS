namespace SplitOS.Persistence.Machine;

public enum ModeCrashReconciliationAction
{
    None,
    ReleaseTerminalLease,
    CancelBeforeMutation,
    ReconcileApplyingOutcome,
    RollbackSource,
    ReconcileRollbackOutcome,
    VerifySourceAfterRollback,
    VerifyCommittedTarget,
    ConvergeBaseForFreshSession,
    WaitForMutationOwner,
    EscalateRecovery
}

public enum ModeCrashReconciliationLeaseState
{
    None,
    Current,
    TakeoverRequired,
    Busy,
    SessionMismatch,
    RecoveryRequired
}

public sealed record ModeCrashReconciliationSnapshot(
    ModeCrashReconciliationAction Action,
    ModeCrashReconciliationLeaseState LeaseState,
    OperationalModeRecord CanonicalMode,
    ModeTransitionRecord? Transition,
    MachineMutationLeaseRecord Lease,
    PersistedModeActionRecord? FocusAction,
    bool HasMutationEvidence,
    string ProductCode,
    string? Detail = null);

public enum ModeReconciliationTakeoverDisposition
{
    Acquired,
    AlreadyCurrent,
    Missing,
    RevisionConflict,
    SessionConflict,
    Busy,
    RecoveryRequired
}

public sealed record ModeReconciliationTakeoverOutcome(
    ModeReconciliationTakeoverDisposition Disposition,
    ModeTransitionRecord? Transition,
    MachineMutationLeaseRecord Lease,
    string ProductCode,
    int? ActualTransitionRevision = null,
    string? Detail = null);

public enum ModeTerminalLeaseCleanupDisposition
{
    Released,
    AlreadyReleased,
    Busy,
    RecoveryRequired
}

public sealed record ModeTerminalLeaseCleanupOutcome(
    ModeTerminalLeaseCleanupDisposition Disposition,
    MachineMutationLeaseRecord Lease,
    string ProductCode,
    string? Detail = null);

