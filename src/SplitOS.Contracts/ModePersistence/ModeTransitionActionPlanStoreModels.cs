// Shared durable models; namespace retained for source compatibility.
namespace SplitOS.Persistence.Machine;

public enum PersistedModeActionState
{
    Planned,
    Applying,
    Applied,
    Verifying,
    Verified,
    Failed,
    RollingBack,
    RolledBack,
    RollbackFailed,
    Skipped
}

public sealed record PersistedModeActionDefinition(
    Guid ActionId,
    int SequenceNo,
    string OwningModule,
    string ActionType,
    string? TargetRef,
    int DesiredSchemaVersion,
    string? DesiredStateJson,
    string DesiredStateDigest,
    bool Mandatory,
    string RollbackClass,
    string VerificationClass);

public sealed record PersistedModeActionRecord(
    Guid ActionId,
    Guid TransitionId,
    int SequenceNo,
    string OwningModule,
    string ActionType,
    string? TargetRef,
    int DesiredSchemaVersion,
    string? DesiredStateJson,
    string DesiredStateDigest,
    bool Mandatory,
    string RollbackClass,
    string VerificationClass,
    PersistedModeActionState State,
    string? PreStateJson,
    string? PreStateDigest,
    string? ApplyResultCode,
    string? VerifyResultCode,
    string? RollbackResultCode,
    DateTimeOffset? StartedUtc,
    DateTimeOffset? AppliedUtc,
    DateTimeOffset? VerifiedUtc,
    DateTimeOffset UpdatedUtc,
    int Revision);

public sealed record ModeTransitionActionPlan(
    Guid TransitionId,
    int PlanSchemaVersion,
    string PlanDigest,
    int ActionCount,
    DateTimeOffset PersistedUtc,
    int TransitionRevision,
    IReadOnlyList<PersistedModeActionRecord> Actions);

public enum ModeTransitionActionPlanPersistDisposition
{
    Persisted,
    Replayed,
    Missing,
    RevisionConflict,
    LeaseConflict,
    ReconciliationRequired,
    InvalidLifecycle,
    MissingPolicyBinding,
    PlanConflict
}

public sealed record ModeTransitionActionPlanPersistOutcome(
    ModeTransitionActionPlanPersistDisposition Disposition,
    ModeTransitionActionPlan? Plan,
    string ProductCode,
    int? ActualTransitionRevision = null,
    string? Detail = null);
