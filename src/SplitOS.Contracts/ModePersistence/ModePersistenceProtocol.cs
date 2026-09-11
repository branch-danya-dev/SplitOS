using System.Text.Json;
using System.Text.Json.Serialization;
using SplitOS.Persistence.Machine;
namespace SplitOS.Contracts.ModePersistence;

public static class ModePersistenceProtocol
{
    public const string Capability = "Machine.Mode.Persistence@1";
    public static readonly TimeSpan MaximumLeaseLifetime = TimeSpan.FromMinutes(5);
    public static JsonSerializerOptions JsonOptions { get; } = new(SplitOS.Contracts.Protocol.ProtocolJson.Options)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true
    };

    public static SplitOS.Contracts.Protocol.WireMessage CreateRequest<T>(T payload, Guid? operationId = null, Guid? correlationId = null)
        => SplitOS.Contracts.Protocol.WireMessage.Create(typeof(T).Name, payload, Capability, operationId, correlationId)
            with { Payload = JsonSerializer.SerializeToElement(payload, JsonOptions) };

    public static SplitOS.Contracts.Protocol.WireMessage Respond<T>(SplitOS.Contracts.Protocol.WireMessage request, T value)
    {
        var result = new ModePersistenceResult<T>(value);
        return SplitOS.Contracts.Protocol.WireMessage.Respond(request, request.MessageType + "Result", result)
            with { Payload = JsonSerializer.SerializeToElement(result, JsonOptions) };
    }
}
public sealed record ModePersistenceResult<T>(T Value);

public interface ITransitionPersistenceRequest
{
    Guid TransitionId { get; }
}

public sealed record MachineStateInitializeRequest();
public sealed record MachineStateGetOperationalModeRequest();
public sealed record MachineMutationLeaseInitializeRequest();
public sealed record MachineMutationLeaseGetRequest();
public sealed record MachineMutationLeaseTryAcquireRequest(
    MachineMutationType MutationType,
    Guid OwnerOperationId,
    Guid OwnerCorrelationId,
    string OwnerControlSessionKey,
    TimeSpan LeaseLifetime);
public sealed record MachineMutationLeaseRenewRequest(
    Guid LeaseId,
    long FenceToken,
    Guid OwnerOperationId,
    TimeSpan LeaseLifetime);
public sealed record MachineMutationLeaseReleaseRequest(Guid LeaseId, long FenceToken, Guid OwnerOperationId);
public sealed record ModeTransitionInitializeRequest();
public sealed record ModeTransitionGetRequest(Guid TransitionId);
public sealed record ModeTransitionGetIncompleteRequest();
public sealed record ModeTransitionCreateRequest(
    Guid TransitionId,
    Guid OperationId,
    Guid CorrelationId,
    PersistedModeOperationKind OperationKind,
    string SourceMode,
    string TargetMode,
    int SourceModeRevision,
    string ControlSessionKey,
    Guid LeaseId,
    long FenceToken);
public sealed record ModeTransitionAdvanceRequest(
    Guid TransitionId,
    int ExpectedRevision,
    Guid LeaseId,
    long FenceToken,
    Guid OwnerOperationId,
    PersistedModeTransitionState NextState,
    PersistedModeTransitionStage NextStage,
    bool MandatoryVerified,
    string? TerminalOutcome) : ITransitionPersistenceRequest;
public sealed record ModeTransitionPolicyInitializeRequest();
public sealed record ModeTransitionPolicyGetRequest(Guid TransitionId);
public sealed record ModeTransitionPolicyBindResolvedPolicyRequest(
    Guid TransitionId,
    int ExpectedTransitionRevision,
    Guid LeaseId,
    long FenceToken,
    Guid OwnerOperationId,
    PersistedModePolicyIdentity Identity,
    PersistedModePolicyTarget Target,
    string ResolvedDigest,
    IReadOnlyCollection<PersistedModePolicyFallbackSelection>? SelectedFallbacks) : ITransitionPersistenceRequest;
public sealed record ModeTransitionActionPlanInitializeRequest();
public sealed record ModeTransitionActionPlanGetRequest(Guid TransitionId);
public sealed record ModeTransitionActionPlanPersistActionPlanRequest(
    Guid TransitionId,
    int ExpectedTransitionRevision,
    Guid LeaseId,
    long FenceToken,
    Guid OwnerOperationId,
    IReadOnlyCollection<PersistedModeActionDefinition> Actions) : ITransitionPersistenceRequest;
public sealed record ModeTransitionActionJournalInitializeRequest();
public sealed record ModeTransitionActionJournalGetRequest(Guid ActionId);
public sealed record ModeTransitionActionJournalBeginApplyRequest(
    Guid TransitionId,
    Guid ActionId,
    int ExpectedActionRevision,
    Guid LeaseId,
    long FenceToken,
    Guid OwnerOperationId,
    string? PreStateJson,
    string? PreStateDigest) : ITransitionPersistenceRequest;
public sealed record ModeTransitionActionJournalRecordApplyResultRequest(
    Guid TransitionId,
    Guid ActionId,
    int ExpectedActionRevision,
    Guid LeaseId,
    long FenceToken,
    Guid OwnerOperationId,
    PersistedModeApplyResult Result) : ITransitionPersistenceRequest;
public sealed record ModeTransitionActionJournalBeginVerifyRequest(
    Guid TransitionId,
    Guid ActionId,
    int ExpectedActionRevision,
    Guid LeaseId,
    long FenceToken,
    Guid OwnerOperationId);
public sealed record ModeTransitionActionJournalRecordVerifyResultRequest(
    Guid TransitionId,
    Guid ActionId,
    int ExpectedActionRevision,
    Guid LeaseId,
    long FenceToken,
    Guid OwnerOperationId,
    PersistedModeVerifyResult Result) : ITransitionPersistenceRequest;
public sealed record ModeTransitionCommitCommitTransitionAndModeRequest(
    Guid TransitionId,
    int ExpectedTransitionRevision,
    int ExpectedModeRevision,
    Guid LeaseId,
    long FenceToken,
    Guid OwnerOperationId,
    bool RuntimeAccessPermitsTarget,
    Guid? ActivationEpochId,
    PersistedModePolicyIdentity? CurrentPolicyIdentity) : ITransitionPersistenceRequest;

public sealed record ModeTransitionRollbackInitializeRequest();
public sealed record ModeTransitionRollbackGetNextRollbackCandidateRequest(Guid TransitionId) : ITransitionPersistenceRequest;
public sealed record ModeTransitionRollbackBeginRollbackRequest(Guid TransitionId, Guid ActionId, int ExpectedActionRevision, Guid LeaseId, long FenceToken, Guid OwnerOperationId) : ITransitionPersistenceRequest;
public sealed record ModeTransitionRollbackRecordRollbackResultRequest(Guid TransitionId, Guid ActionId, int ExpectedActionRevision, Guid LeaseId, long FenceToken, Guid OwnerOperationId, PersistedModeRollbackResult Result) : ITransitionPersistenceRequest;
public sealed record ModeTransitionReconciliationInitializeRequest();
public sealed record ModeTransitionReconciliationInspectRequest(string CurrentControlSessionKey);
public sealed record ModeTransitionReconciliationTakeOverRequest(Guid TransitionId, int ExpectedTransitionRevision, string CurrentControlSessionKey, TimeSpan LeaseLifetime) : ITransitionPersistenceRequest;
public sealed record ModeTransitionReconciliationReleaseTerminalLeaseRequest();
