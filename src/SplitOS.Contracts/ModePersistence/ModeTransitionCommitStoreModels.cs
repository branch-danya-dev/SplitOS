// Shared durable models; namespace retained for source compatibility.
namespace SplitOS.Persistence.Machine;

public enum ModeTransitionCommitDisposition
{
    Committed,
    Replayed,
    Missing,
    TransitionRevisionConflict,
    ModeRevisionConflict,
    LeaseConflict,
    ReconciliationRequired,
    InvalidTransition,
    AuthorityDenied,
    ConcurrencyConflict
}

public sealed record ModeTransitionCommitOutcome(
    ModeTransitionCommitDisposition Disposition,
    string ProductCode,
    OperationalModeRecord? OperationalMode,
    ModeTransitionRecord? Transition,
    int? ActualModeRevision = null,
    int? ActualTransitionRevision = null,
    string? Detail = null);
