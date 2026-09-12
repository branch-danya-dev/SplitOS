using System.Text.Json;
using Microsoft.Data.Sqlite;
using SplitOS.Contracts.ModePersistence;
using SplitOS.Contracts.Protocol;
using SplitOS.Persistence.Machine;
namespace SplitOS.Broker.Service;

/// <summary>Allowlisted persistence execution. Semantic ordering remains in RuntimeHost.</summary>
public sealed class BrokerModePersistenceHandler(
    MachineStateStore machineStateStore,
    MachineMutationLeaseStore machineMutationLeaseStore,
    ModeTransitionStore modeTransitionStore,
    ModeTransitionPolicyStore modeTransitionPolicyStore,
    ModeTransitionActionPlanStore modeTransitionActionPlanStore,
    ModeTransitionActionJournalStore modeTransitionActionJournalStore,
    ModeTransitionCommitStore modeTransitionCommitStore,
    ModeTransitionRollbackStore? modeTransitionRollbackStore = null,
    ModeTransitionReconciliationStore? modeTransitionReconciliationStore = null)
{
    public async Task<WireMessage> HandleAsync(WireMessage request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.Capability != ModePersistenceProtocol.Capability)
                return Error(request, ErrorCodes.UnknownCapability, "Unknown persistence capability.");
            if (request.ProtocolVersion != ProtocolConstants.CurrentVersion || request.OperationId == Guid.Empty || request.CorrelationId == Guid.Empty || request.RequestId == Guid.Empty)
                return Error(request, ErrorCodes.InvalidMessage, "Invalid persistence envelope.");
            switch (request.MessageType)
            {
                case nameof(MachineStateInitializeRequest):
                {
                    var payload = Read<MachineStateInitializeRequest>(request);
                    _ = await machineStateStore.GetOperationalModeAsync(cancellationToken).ConfigureAwait(false);
                    var value = true;
                    return ModePersistenceProtocol.Respond(request, value);
                }
                case nameof(MachineStateGetOperationalModeRequest):
                {
                    var payload = Read<MachineStateGetOperationalModeRequest>(request);
                    var value = await machineStateStore.GetOperationalModeAsync(cancellationToken).ConfigureAwait(false);
                    return ModePersistenceProtocol.Respond(request, value);
                }
                case nameof(MachineMutationLeaseInitializeRequest):
                {
                    var payload = Read<MachineMutationLeaseInitializeRequest>(request);
                    await machineMutationLeaseStore.InitializeAsync(cancellationToken).ConfigureAwait(false);
                    var value = true;
                    return ModePersistenceProtocol.Respond(request, value);
                }
                case nameof(MachineMutationLeaseGetRequest):
                {
                    var payload = Read<MachineMutationLeaseGetRequest>(request);
                    var value = await machineMutationLeaseStore.GetAsync(cancellationToken).ConfigureAwait(false);
                    return ModePersistenceProtocol.Respond(request, value);
                }
                case nameof(MachineMutationLeaseTryAcquireRequest):
                {
                    var payload = Read<MachineMutationLeaseTryAcquireRequest>(request);
                    if (payload.OwnerOperationId != request.OperationId)
                        throw new ArgumentException("Payload operation does not match envelope.");
                    if (payload.OwnerCorrelationId != request.CorrelationId)
                        throw new ArgumentException("Payload correlation does not match envelope.");
                    if (payload.MutationType != MachineMutationType.Mode)
                        throw new ArgumentException("Only MODE leases are allowed by this capability.");
                    var value = await machineMutationLeaseStore.TryAcquireAsync(payload.MutationType, payload.OwnerOperationId, payload.OwnerCorrelationId, payload.OwnerControlSessionKey, payload.LeaseLifetime, cancellationToken).ConfigureAwait(false);
                    return ModePersistenceProtocol.Respond(request, value);
                }
                case nameof(MachineMutationLeaseRenewRequest):
                {
                    var payload = Read<MachineMutationLeaseRenewRequest>(request);
                    if (payload.OwnerOperationId != request.OperationId)
                        throw new ArgumentException("Payload operation does not match envelope.");
                    var currentLease = await machineMutationLeaseStore.GetAsync(cancellationToken).ConfigureAwait(false);
                    if (currentLease.IsHeld && currentLease.MutationType != MachineMutationType.Mode)
                        throw new ArgumentException("This capability cannot change UPDATE or RECOVERY leases.");
                    var value = await machineMutationLeaseStore.RenewAsync(payload.LeaseId, payload.FenceToken, payload.OwnerOperationId, payload.LeaseLifetime, cancellationToken).ConfigureAwait(false);
                    return ModePersistenceProtocol.Respond(request, value);
                }
                case nameof(MachineMutationLeaseReleaseRequest):
                {
                    var payload = Read<MachineMutationLeaseReleaseRequest>(request);
                    if (payload.OwnerOperationId != request.OperationId)
                        throw new ArgumentException("Payload operation does not match envelope.");
                    var currentLease = await machineMutationLeaseStore.GetAsync(cancellationToken).ConfigureAwait(false);
                    if (currentLease.IsHeld && currentLease.MutationType != MachineMutationType.Mode)
                        throw new ArgumentException("This capability cannot change UPDATE or RECOVERY leases.");
                    var value = await machineMutationLeaseStore.ReleaseAsync(payload.LeaseId, payload.FenceToken, payload.OwnerOperationId, cancellationToken).ConfigureAwait(false);
                    return ModePersistenceProtocol.Respond(request, value);
                }
                case nameof(ModeTransitionInitializeRequest):
                {
                    var payload = Read<ModeTransitionInitializeRequest>(request);
                    await modeTransitionStore.InitializeAsync(cancellationToken).ConfigureAwait(false);
                    var value = true;
                    return ModePersistenceProtocol.Respond(request, value);
                }
                case nameof(ModeTransitionGetRequest):
                {
                    var payload = Read<ModeTransitionGetRequest>(request);
                    var value = await modeTransitionStore.GetAsync(payload.TransitionId, cancellationToken).ConfigureAwait(false);
                    return ModePersistenceProtocol.Respond(request, value);
                }
                case nameof(ModeTransitionGetIncompleteRequest):
                {
                    var payload = Read<ModeTransitionGetIncompleteRequest>(request);
                    var value = await modeTransitionStore.GetIncompleteAsync(cancellationToken).ConfigureAwait(false);
                    return ModePersistenceProtocol.Respond(request, value);
                }
                case nameof(ModeTransitionCreateRequest):
                {
                    var payload = Read<ModeTransitionCreateRequest>(request);
                    if (payload.OperationId != request.OperationId)
                        throw new ArgumentException("Payload operation does not match envelope.");
                    if (payload.CorrelationId != request.CorrelationId)
                        throw new ArgumentException("Payload correlation does not match envelope.");
                    var value = await modeTransitionStore.CreateAsync(payload.TransitionId, payload.OperationId, payload.CorrelationId, payload.OperationKind, payload.SourceMode, payload.TargetMode, payload.SourceModeRevision, payload.ControlSessionKey, payload.LeaseId, payload.FenceToken, cancellationToken).ConfigureAwait(false);
                    return ModePersistenceProtocol.Respond(request, value);
                }
                case nameof(ModeTransitionAdvanceRequest):
                {
                    var payload = Read<ModeTransitionAdvanceRequest>(request);
                    if (payload.OwnerOperationId != request.OperationId)
                        throw new ArgumentException("Payload operation does not match envelope.");
                    var value = await modeTransitionStore.AdvanceAsync(payload.TransitionId, payload.ExpectedRevision, payload.LeaseId, payload.FenceToken, payload.OwnerOperationId, payload.NextState, payload.NextStage, payload.MandatoryVerified, payload.TerminalOutcome, cancellationToken).ConfigureAwait(false);
                    return ModePersistenceProtocol.Respond(request, value);
                }
                case nameof(ModeTransitionPolicyInitializeRequest):
                {
                    var payload = Read<ModeTransitionPolicyInitializeRequest>(request);
                    await modeTransitionPolicyStore.InitializeAsync(cancellationToken).ConfigureAwait(false);
                    var value = true;
                    return ModePersistenceProtocol.Respond(request, value);
                }
                case nameof(ModeTransitionPolicyGetRequest):
                {
                    var payload = Read<ModeTransitionPolicyGetRequest>(request);
                    var value = await modeTransitionPolicyStore.GetAsync(payload.TransitionId, cancellationToken).ConfigureAwait(false);
                    return ModePersistenceProtocol.Respond(request, value);
                }
                case nameof(ModeTransitionPolicyBindResolvedPolicyRequest):
                {
                    var payload = Read<ModeTransitionPolicyBindResolvedPolicyRequest>(request);
                    if (payload.OwnerOperationId != request.OperationId)
                        throw new ArgumentException("Payload operation does not match envelope.");
                    var value = await modeTransitionPolicyStore.BindResolvedPolicyAsync(payload.TransitionId, payload.ExpectedTransitionRevision, payload.LeaseId, payload.FenceToken, payload.OwnerOperationId, payload.Identity, payload.Target, payload.ResolvedDigest, payload.SelectedFallbacks, cancellationToken).ConfigureAwait(false);
                    return ModePersistenceProtocol.Respond(request, value);
                }
                case nameof(ModeTransitionActionPlanInitializeRequest):
                {
                    var payload = Read<ModeTransitionActionPlanInitializeRequest>(request);
                    await modeTransitionActionPlanStore.InitializeAsync(cancellationToken).ConfigureAwait(false);
                    var value = true;
                    return ModePersistenceProtocol.Respond(request, value);
                }
                case nameof(ModeTransitionActionPlanGetRequest):
                {
                    var payload = Read<ModeTransitionActionPlanGetRequest>(request);
                    var value = await modeTransitionActionPlanStore.GetAsync(payload.TransitionId, cancellationToken).ConfigureAwait(false);
                    return ModePersistenceProtocol.Respond(request, value);
                }
                case nameof(ModeTransitionActionPlanPersistActionPlanRequest):
                {
                    var payload = Read<ModeTransitionActionPlanPersistActionPlanRequest>(request);
                    if (payload.OwnerOperationId != request.OperationId)
                        throw new ArgumentException("Payload operation does not match envelope.");
                    // Persistence is action-domain agnostic. The durable store validates the generic
                    // envelope (ids, semantic bounds, schema, JSON and SHA-256 digest); RuntimeHost
                    // owns semantic admission and executable-handler selection for each action type.
                    var value = await modeTransitionActionPlanStore.PersistActionPlanAsync(payload.TransitionId, payload.ExpectedTransitionRevision, payload.LeaseId, payload.FenceToken, payload.OwnerOperationId, payload.Actions, cancellationToken).ConfigureAwait(false);
                    return ModePersistenceProtocol.Respond(request, value);
                }
                case nameof(ModeTransitionActionJournalInitializeRequest):
                {
                    var payload = Read<ModeTransitionActionJournalInitializeRequest>(request);
                    await modeTransitionActionJournalStore.InitializeAsync(cancellationToken).ConfigureAwait(false);
                    var value = true;
                    return ModePersistenceProtocol.Respond(request, value);
                }
                case nameof(ModeTransitionActionJournalGetRequest):
                {
                    var payload = Read<ModeTransitionActionJournalGetRequest>(request);
                    var value = await modeTransitionActionJournalStore.GetAsync(payload.ActionId, cancellationToken).ConfigureAwait(false);
                    return ModePersistenceProtocol.Respond(request, value);
                }
                case nameof(ModeTransitionActionJournalBeginApplyRequest):
                {
                    var payload = Read<ModeTransitionActionJournalBeginApplyRequest>(request);
                    if (payload.OwnerOperationId != request.OperationId)
                        throw new ArgumentException("Payload operation does not match envelope.");
                    // Pre-state evidence is likewise an opaque durable envelope here. The journal
                    // validates JSON shape/size and its SHA-256 digest without assuming an action domain.
                    var value = await modeTransitionActionJournalStore.BeginApplyAsync(payload.TransitionId, payload.ActionId, payload.ExpectedActionRevision, payload.LeaseId, payload.FenceToken, payload.OwnerOperationId, payload.PreStateJson, payload.PreStateDigest, cancellationToken).ConfigureAwait(false);
                    return ModePersistenceProtocol.Respond(request, value);
                }
                case nameof(ModeTransitionActionJournalRecordApplyResultRequest):
                {
                    var payload = Read<ModeTransitionActionJournalRecordApplyResultRequest>(request);
                    if (payload.OwnerOperationId != request.OperationId)
                        throw new ArgumentException("Payload operation does not match envelope.");
                    var value = await modeTransitionActionJournalStore.RecordApplyResultAsync(payload.TransitionId, payload.ActionId, payload.ExpectedActionRevision, payload.LeaseId, payload.FenceToken, payload.OwnerOperationId, payload.Result, cancellationToken).ConfigureAwait(false);
                    return ModePersistenceProtocol.Respond(request, value);
                }
                case nameof(ModeTransitionActionJournalBeginVerifyRequest):
                {
                    var payload = Read<ModeTransitionActionJournalBeginVerifyRequest>(request);
                    if (payload.OwnerOperationId != request.OperationId)
                        throw new ArgumentException("Payload operation does not match envelope.");
                    var value = await modeTransitionActionJournalStore.BeginVerifyAsync(payload.TransitionId, payload.ActionId, payload.ExpectedActionRevision, payload.LeaseId, payload.FenceToken, payload.OwnerOperationId, cancellationToken).ConfigureAwait(false);
                    return ModePersistenceProtocol.Respond(request, value);
                }
                case nameof(ModeTransitionActionJournalRecordVerifyResultRequest):
                {
                    var payload = Read<ModeTransitionActionJournalRecordVerifyResultRequest>(request);
                    if (payload.OwnerOperationId != request.OperationId)
                        throw new ArgumentException("Payload operation does not match envelope.");
                    var value = await modeTransitionActionJournalStore.RecordVerifyResultAsync(payload.TransitionId, payload.ActionId, payload.ExpectedActionRevision, payload.LeaseId, payload.FenceToken, payload.OwnerOperationId, payload.Result, cancellationToken).ConfigureAwait(false);
                    return ModePersistenceProtocol.Respond(request, value);
                }
                case nameof(ModeTransitionCommitCommitTransitionAndModeRequest):
                {
                    var payload = Read<ModeTransitionCommitCommitTransitionAndModeRequest>(request);
                    if (payload.OwnerOperationId != request.OperationId)
                        throw new ArgumentException("Payload operation does not match envelope.");
                    var value = await modeTransitionCommitStore.CommitTransitionAndModeAsync(payload.TransitionId, payload.ExpectedTransitionRevision, payload.ExpectedModeRevision, payload.LeaseId, payload.FenceToken, payload.OwnerOperationId, payload.RuntimeAccessPermitsTarget, payload.ActivationEpochId, payload.CurrentPolicyIdentity, cancellationToken).ConfigureAwait(false);
                    return ModePersistenceProtocol.Respond(request, value);
                }
                case nameof(ModeTransitionRollbackInitializeRequest):
                {
                    var payload = Read<ModeTransitionRollbackInitializeRequest>(request);
                    var store = modeTransitionRollbackStore ?? throw new IOException("Recovery persistence is unavailable.");
                    await store.InitializeAsync(cancellationToken).ConfigureAwait(false);
                    var value = true;
                    return ModePersistenceProtocol.Respond(request, value);
                }
                case nameof(ModeTransitionRollbackGetNextRollbackCandidateRequest):
                {
                    var payload = Read<ModeTransitionRollbackGetNextRollbackCandidateRequest>(request);
                    var store = modeTransitionRollbackStore ?? throw new IOException("Recovery persistence is unavailable.");
                    var value = await store.GetNextRollbackCandidateAsync(payload.TransitionId, cancellationToken).ConfigureAwait(false);
                    return ModePersistenceProtocol.Respond(request, value);
                }
                case nameof(ModeTransitionRollbackBeginRollbackRequest):
                {
                    var payload = Read<ModeTransitionRollbackBeginRollbackRequest>(request);
                    var store = modeTransitionRollbackStore ?? throw new IOException("Recovery persistence is unavailable.");
                    if (payload.OwnerOperationId != request.OperationId)
                        throw new ArgumentException("Payload operation does not match envelope.");
                    var value = await store.BeginRollbackAsync(payload.TransitionId, payload.ActionId, payload.ExpectedActionRevision, payload.LeaseId, payload.FenceToken, payload.OwnerOperationId, cancellationToken).ConfigureAwait(false);
                    return ModePersistenceProtocol.Respond(request, value);
                }
                case nameof(ModeTransitionRollbackRecordRollbackResultRequest):
                {
                    var payload = Read<ModeTransitionRollbackRecordRollbackResultRequest>(request);
                    var store = modeTransitionRollbackStore ?? throw new IOException("Recovery persistence is unavailable.");
                    if (payload.OwnerOperationId != request.OperationId)
                        throw new ArgumentException("Payload operation does not match envelope.");
                    var value = await store.RecordRollbackResultAsync(payload.TransitionId, payload.ActionId, payload.ExpectedActionRevision, payload.LeaseId, payload.FenceToken, payload.OwnerOperationId, payload.Result, cancellationToken).ConfigureAwait(false);
                    return ModePersistenceProtocol.Respond(request, value);
                }
                case nameof(ModeTransitionReconciliationInitializeRequest):
                {
                    var payload = Read<ModeTransitionReconciliationInitializeRequest>(request);
                    var store = modeTransitionReconciliationStore ?? throw new IOException("Recovery persistence is unavailable.");
                    await store.InitializeAsync(cancellationToken).ConfigureAwait(false);
                    var value = true;
                    return ModePersistenceProtocol.Respond(request, value);
                }
                case nameof(ModeTransitionReconciliationInspectRequest):
                {
                    var payload = Read<ModeTransitionReconciliationInspectRequest>(request);
                    var store = modeTransitionReconciliationStore ?? throw new IOException("Recovery persistence is unavailable.");
                    var value = await store.InspectAsync(payload.CurrentControlSessionKey, cancellationToken).ConfigureAwait(false);
                    return ModePersistenceProtocol.Respond(request, value);
                }
                case nameof(ModeTransitionReconciliationTakeOverRequest):
                {
                    var payload = Read<ModeTransitionReconciliationTakeOverRequest>(request);
                    var store = modeTransitionReconciliationStore ?? throw new IOException("Recovery persistence is unavailable.");
                    var transition = await modeTransitionStore.GetAsync(payload.TransitionId, cancellationToken).ConfigureAwait(false);
                    if (transition is not null && (transition.OperationId != request.OperationId || transition.CorrelationId != request.CorrelationId))
                        throw new ArgumentException("Takeover envelope does not match durable transition ownership.");
                    var value = await store.TakeOverAsync(payload.TransitionId, payload.ExpectedTransitionRevision, payload.CurrentControlSessionKey, payload.LeaseLifetime, cancellationToken).ConfigureAwait(false);
                    return ModePersistenceProtocol.Respond(request, value);
                }
                case nameof(ModeTransitionReconciliationReleaseTerminalLeaseRequest):
                {
                    var payload = Read<ModeTransitionReconciliationReleaseTerminalLeaseRequest>(request);
                    var store = modeTransitionReconciliationStore ?? throw new IOException("Recovery persistence is unavailable.");
                    var value = await store.ReleaseTerminalLeaseAsync(cancellationToken).ConfigureAwait(false);
                    return ModePersistenceProtocol.Respond(request, value);
                }
                default:
                    return Error(request, ErrorCodes.UnsupportedMessage, "Persistence operation is not allowlisted.");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException)
        {
            return Error(request, ErrorCodes.InvalidMessage, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or SqliteException or UnauthorizedAccessException)
        {
            return Error(request, ErrorCodes.PersistenceUnavailable, ex.Message);
        }
    }

    private static T Read<T>(WireMessage request)
    {
        if (request.Payload.ValueKind != JsonValueKind.Object)
            throw new JsonException("Typed persistence object is required.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in request.Payload.EnumerateObject())
            if (!names.Add(property.Name))
                throw new JsonException("Duplicate persistence field.");
        return request.Payload.Deserialize<T>(ModePersistenceProtocol.JsonOptions)
            ?? throw new JsonException("Typed persistence payload is required.");
    }

    private static WireMessage Error(WireMessage request, string code, string message)
        => WireMessage.Respond(request, MessageTypes.ErrorResponse, new ErrorResponse(code, message));

    private static void ValidateActions(IReadOnlyCollection<PersistedModeActionDefinition> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        foreach (var action in actions)
        {
            ArgumentNullException.ThrowIfNull(action);
            if (action.OwningModule != ManagedServicePolicyActionContract.OwningModule ||
                action.ActionType != ManagedServicePolicyActionContract.ActionType ||
                action.TargetRef != ManagedServicePolicyActionContract.TargetRef ||
                action.DesiredSchemaVersion != ManagedServicePolicyActionContract.DesiredSchemaVersion ||
                string.IsNullOrWhiteSpace(action.DesiredStateJson))
                throw new ArgumentException("Only typed managed-service actions are executable in this slice.");
            var entries = ManagedServicePolicyActionContract.DeserializeDesiredState(action.DesiredStateJson);
            if (!string.Equals(ManagedServicePolicyActionContract.ComputeDesiredStateDigest(entries), action.DesiredStateDigest, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Managed-service intent digest mismatch.");
        }
    }
}
