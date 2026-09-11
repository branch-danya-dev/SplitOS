// Shared durable models; namespace retained for source compatibility.
namespace SplitOS.Persistence.Machine;

public enum PersistedModeOperationKind
{
    Activate,
    Switch,
    Deactivate
}

public enum PersistedModeTransitionState
{
    Requested,
    Inspecting,
    Blocked,
    AwaitingUser,
    Resolving,
    Applying,
    Verifying,
    Committing,
    RollingBack,
    Completed,
    Cancelled,
    FailedWithSafeFallback
}

public enum PersistedModeTransitionStage
{
    Accepted,
    InspectionStarted,
    InspectionComplete,
    WaitingForUser,
    ResolutionStarted,
    ActionPlanReady,
    ApplyStarted,
    ApplyComplete,
    VerifyStarted,
    VerifyComplete,
    CommitStarted,
    CommitDurable,
    FinalizationStarted,
    RollbackStarted,
    RollbackVerify,
    Terminal
}

public sealed record ModeTransitionRecord(
    Guid TransitionId,
    Guid OperationId,
    Guid CorrelationId,
    PersistedModeOperationKind OperationKind,
    string SourceMode,
    string TargetMode,
    int SourceModeRevision,
    string ControlSessionKey,
    Guid LeaseId,
    long FenceToken,
    PersistedModeTransitionState TransitionState,
    PersistedModeTransitionStage Stage,
    DateTimeOffset StartedUtc,
    DateTimeOffset UpdatedUtc,
    bool MandatoryVerified,
    bool CommitDurable,
    string? TerminalOutcome,
    string? RecoveryContextId,
    int Revision);

public enum ModeTransitionCreateDisposition
{
    Created,
    Replayed,
    IdempotencyConflict,
    SourceConflict,
    LeaseConflict,
    ReconciliationRequired
}

public sealed record ModeTransitionCreateOutcome(
    ModeTransitionCreateDisposition Disposition,
    ModeTransitionRecord? Transition,
    string ProductCode,
    string? Detail = null);

public enum ModeTransitionAdvanceDisposition
{
    Advanced,
    Unchanged,
    Missing,
    RevisionConflict,
    LeaseConflict,
    InvalidLifecycle
}

public sealed record ModeTransitionAdvanceOutcome(
    ModeTransitionAdvanceDisposition Disposition,
    ModeTransitionRecord? Transition,
    string ProductCode,
    int? ActualRevision = null,
    string? Detail = null);
