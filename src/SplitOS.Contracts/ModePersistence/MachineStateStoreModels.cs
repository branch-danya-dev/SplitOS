// Shared durable models; namespace retained for source compatibility.
namespace SplitOS.Persistence.Machine;

public sealed record OperationalModeRecord(
    string CommittedMode,
    DateTimeOffset CommittedUtc,
    string CommittedByOperationId,
    string? CorrelationId,
    int Revision,
    DateTimeOffset UpdatedUtc,
    string? ControlSessionKey = null,
    Guid? ActivationEpochId = null,
    PersistedModePolicyIdentity? PolicyIdentity = null,
    PersistedModePolicyTarget? PolicyTarget = null,
    string? ResolvedPolicyDigest = null);

public enum OperationalModeWriteDisposition
{
    Applied,
    Replayed,
    RevisionConflict,
    IdempotencyConflict
}

public sealed record OperationalModeWriteOutcome(
    OperationalModeWriteDisposition Disposition,
    OperationalModeRecord? Record,
    int? ActualRevision,
    string? Detail);
