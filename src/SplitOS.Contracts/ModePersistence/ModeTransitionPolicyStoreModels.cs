// Shared durable models; namespace retained for source compatibility.
namespace SplitOS.Persistence.Machine;

public enum PersistedModePolicyTarget
{
    Base,
    Work,
    Game
}

public enum PersistedModePolicyFallbackClass
{
    ReleaseDefault,
    ApprovedAlternate,
    PreserveCurrent
}

public sealed record PersistedModePolicyIdentity(
    string PolicyCatalogId,
    long PolicyVersion,
    string ReleaseId,
    string CatalogDigest);

public sealed record PersistedModePolicyFallbackSelection(
    string RuleId,
    PersistedModePolicyFallbackClass FallbackClass,
    string? TargetId);

public sealed record ModeTransitionPolicyBinding(
    Guid TransitionId,
    PersistedModePolicyIdentity Identity,
    PersistedModePolicyTarget Target,
    string ResolvedDigest,
    IReadOnlyList<PersistedModePolicyFallbackSelection> SelectedFallbacks,
    DateTimeOffset BoundUtc,
    int TransitionRevision);

public enum ModeTransitionPolicyBindDisposition
{
    Bound,
    Replayed,
    Missing,
    RevisionConflict,
    LeaseConflict,
    ReconciliationRequired,
    InvalidLifecycle,
    TargetMismatch,
    BindingConflict
}

public sealed record ModeTransitionPolicyBindOutcome(
    ModeTransitionPolicyBindDisposition Disposition,
    ModeTransitionPolicyBinding? Binding,
    string ProductCode,
    int? ActualTransitionRevision = null,
    string? Detail = null);
