using System.Diagnostics;
using System.Text.Json;
using SplitOS.Contracts.ModePersistence;
using SplitOS.Contracts.Protocol;
using SplitOS.Ipc;
using SplitOS.Persistence.Machine;
namespace SplitOS.RuntimeHost.ModeRuntime;

/// <summary>Typed Broker proxy. Never opens or initializes a local machine database.</summary>
public sealed class NamedPipeModePersistenceClient : IMachineStateStore, IMachineMutationLeaseStore, IModeTransitionStore, IModeTransitionPolicyStore, IModeTransitionActionPlanStore, IModeTransitionActionJournalStore, IModeTransitionCommitStore, IModeTransitionRollbackStore, IModeTransitionReconciliationStore, IManagedServiceRollbackClient, IManagedServiceSourceVerificationClient, IModeBasePolicyClient, IModeBaseRecoveryClient
{
    private readonly Func<WireMessage, CancellationToken, Task<WireMessage>> _send;
    public NamedPipeModePersistenceClient() : this(SendPipeAsync) { }
    public NamedPipeModePersistenceClient(Func<WireMessage, CancellationToken, Task<WireMessage>> send)
        => _send = send ?? throw new ArgumentNullException(nameof(send));

    async Task IMachineStateStore.InitializeAsync(CancellationToken cancellationToken)
    {
        var payload = new MachineStateInitializeRequest();
        _ = await CallAsync<MachineStateInitializeRequest, bool>(payload, null, null, cancellationToken).ConfigureAwait(false);
    }

    async Task<OperationalModeRecord> IMachineStateStore.GetOperationalModeAsync(CancellationToken cancellationToken)
    {
        var payload = new MachineStateGetOperationalModeRequest();
        return await CallAsync<MachineStateGetOperationalModeRequest, OperationalModeRecord>(payload, null, null, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Broker omitted the required persistence value.");
    }

    async Task IMachineMutationLeaseStore.InitializeAsync(CancellationToken cancellationToken)
    {
        var payload = new MachineMutationLeaseInitializeRequest();
        _ = await CallAsync<MachineMutationLeaseInitializeRequest, bool>(payload, null, null, cancellationToken).ConfigureAwait(false);
    }

    async Task<MachineMutationLeaseRecord> IMachineMutationLeaseStore.GetAsync(CancellationToken cancellationToken)
    {
        var payload = new MachineMutationLeaseGetRequest();
        return await CallAsync<MachineMutationLeaseGetRequest, MachineMutationLeaseRecord>(payload, null, null, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Broker omitted the required persistence value.");
    }

    async Task<MachineMutationLeaseAcquireOutcome> IMachineMutationLeaseStore.TryAcquireAsync(
        MachineMutationType mutationType,
        Guid ownerOperationId,
        Guid ownerCorrelationId,
        string ownerControlSessionKey,
        TimeSpan leaseLifetime,
        CancellationToken cancellationToken)
    {
        var payload = new MachineMutationLeaseTryAcquireRequest(mutationType, ownerOperationId, ownerCorrelationId, ownerControlSessionKey, leaseLifetime);
        return await CallAsync<MachineMutationLeaseTryAcquireRequest, MachineMutationLeaseAcquireOutcome>(payload, ownerOperationId, ownerCorrelationId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Broker omitted the required persistence value.");
    }

    async Task<MachineMutationLeaseRenewOutcome> IMachineMutationLeaseStore.RenewAsync(
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        TimeSpan leaseLifetime,
        CancellationToken cancellationToken)
    {
        var payload = new MachineMutationLeaseRenewRequest(leaseId, fenceToken, ownerOperationId, leaseLifetime);
        return await CallAsync<MachineMutationLeaseRenewRequest, MachineMutationLeaseRenewOutcome>(payload, ownerOperationId, null, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Broker omitted the required persistence value.");
    }

    async Task<MachineMutationLeaseReleaseOutcome> IMachineMutationLeaseStore.ReleaseAsync(
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        CancellationToken cancellationToken)
    {
        var payload = new MachineMutationLeaseReleaseRequest(leaseId, fenceToken, ownerOperationId);
        return await CallAsync<MachineMutationLeaseReleaseRequest, MachineMutationLeaseReleaseOutcome>(payload, ownerOperationId, null, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Broker omitted the required persistence value.");
    }

    async Task IModeTransitionStore.InitializeAsync(CancellationToken cancellationToken)
    {
        var payload = new ModeTransitionInitializeRequest();
        _ = await CallAsync<ModeTransitionInitializeRequest, bool>(payload, null, null, cancellationToken).ConfigureAwait(false);
    }

    async Task<ModeTransitionRecord?> IModeTransitionStore.GetAsync(Guid transitionId, CancellationToken cancellationToken)
    {
        var payload = new ModeTransitionGetRequest(transitionId);
        return await CallAsync<ModeTransitionGetRequest, ModeTransitionRecord?>(payload, null, null, cancellationToken).ConfigureAwait(false);
    }

    async Task<IReadOnlyList<ModeTransitionRecord>> IModeTransitionStore.GetIncompleteAsync(
        CancellationToken cancellationToken)
    {
        var payload = new ModeTransitionGetIncompleteRequest();
        return await CallAsync<ModeTransitionGetIncompleteRequest, IReadOnlyList<ModeTransitionRecord>>(payload, null, null, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Broker omitted the required persistence value.");
    }

    async Task<ModeTransitionCreateOutcome> IModeTransitionStore.CreateAsync(
        Guid transitionId,
        Guid operationId,
        Guid correlationId,
        PersistedModeOperationKind operationKind,
        string sourceMode,
        string targetMode,
        int sourceModeRevision,
        string controlSessionKey,
        Guid leaseId,
        long fenceToken,
        CancellationToken cancellationToken)
    {
        var payload = new ModeTransitionCreateRequest(transitionId, operationId, correlationId, operationKind, sourceMode, targetMode, sourceModeRevision, controlSessionKey, leaseId, fenceToken);
        return await CallAsync<ModeTransitionCreateRequest, ModeTransitionCreateOutcome>(payload, operationId, correlationId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Broker omitted the required persistence value.");
    }

    async Task<ModeTransitionAdvanceOutcome> IModeTransitionStore.AdvanceAsync(
        Guid transitionId,
        int expectedRevision,
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        PersistedModeTransitionState nextState,
        PersistedModeTransitionStage nextStage,
        bool mandatoryVerified,
        string? terminalOutcome,
        CancellationToken cancellationToken)
    {
        var payload = new ModeTransitionAdvanceRequest(transitionId, expectedRevision, leaseId, fenceToken, ownerOperationId, nextState, nextStage, mandatoryVerified, terminalOutcome);
        return await CallAsync<ModeTransitionAdvanceRequest, ModeTransitionAdvanceOutcome>(payload, ownerOperationId, null, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Broker omitted the required persistence value.");
    }

    async Task IModeTransitionPolicyStore.InitializeAsync(CancellationToken cancellationToken)
    {
        var payload = new ModeTransitionPolicyInitializeRequest();
        _ = await CallAsync<ModeTransitionPolicyInitializeRequest, bool>(payload, null, null, cancellationToken).ConfigureAwait(false);
    }

    async Task<ModeTransitionPolicyBinding?> IModeTransitionPolicyStore.GetAsync(
        Guid transitionId,
        CancellationToken cancellationToken)
    {
        var payload = new ModeTransitionPolicyGetRequest(transitionId);
        return await CallAsync<ModeTransitionPolicyGetRequest, ModeTransitionPolicyBinding?>(payload, null, null, cancellationToken).ConfigureAwait(false);
    }

    async Task<ModeTransitionPolicyBindOutcome> IModeTransitionPolicyStore.BindResolvedPolicyAsync(
        Guid transitionId,
        int expectedTransitionRevision,
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        PersistedModePolicyIdentity identity,
        PersistedModePolicyTarget target,
        string resolvedDigest,
        IReadOnlyCollection<PersistedModePolicyFallbackSelection>? selectedFallbacks,
        CancellationToken cancellationToken)
    {
        var payload = new ModeTransitionPolicyBindResolvedPolicyRequest(transitionId, expectedTransitionRevision, leaseId, fenceToken, ownerOperationId, identity, target, resolvedDigest, selectedFallbacks);
        return await CallAsync<ModeTransitionPolicyBindResolvedPolicyRequest, ModeTransitionPolicyBindOutcome>(payload, ownerOperationId, null, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Broker omitted the required persistence value.");
    }

    async Task IModeTransitionActionPlanStore.InitializeAsync(CancellationToken cancellationToken)
    {
        var payload = new ModeTransitionActionPlanInitializeRequest();
        _ = await CallAsync<ModeTransitionActionPlanInitializeRequest, bool>(payload, null, null, cancellationToken).ConfigureAwait(false);
    }

    async Task<ModeTransitionActionPlan?> IModeTransitionActionPlanStore.GetAsync(
        Guid transitionId,
        CancellationToken cancellationToken)
    {
        var payload = new ModeTransitionActionPlanGetRequest(transitionId);
        return await CallAsync<ModeTransitionActionPlanGetRequest, ModeTransitionActionPlan?>(payload, null, null, cancellationToken).ConfigureAwait(false);
    }

    async Task<ModeTransitionActionPlanPersistOutcome> IModeTransitionActionPlanStore.PersistActionPlanAsync(
        Guid transitionId,
        int expectedTransitionRevision,
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        IReadOnlyCollection<PersistedModeActionDefinition> actions,
        CancellationToken cancellationToken)
    {
        var payload = new ModeTransitionActionPlanPersistActionPlanRequest(transitionId, expectedTransitionRevision, leaseId, fenceToken, ownerOperationId, actions);
        return await CallAsync<ModeTransitionActionPlanPersistActionPlanRequest, ModeTransitionActionPlanPersistOutcome>(payload, ownerOperationId, null, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Broker omitted the required persistence value.");
    }

    async Task IModeTransitionActionJournalStore.InitializeAsync(CancellationToken cancellationToken)
    {
        var payload = new ModeTransitionActionJournalInitializeRequest();
        _ = await CallAsync<ModeTransitionActionJournalInitializeRequest, bool>(payload, null, null, cancellationToken).ConfigureAwait(false);
    }

    async Task<PersistedModeActionRecord?> IModeTransitionActionJournalStore.GetAsync(
        Guid actionId,
        CancellationToken cancellationToken)
    {
        var payload = new ModeTransitionActionJournalGetRequest(actionId);
        return await CallAsync<ModeTransitionActionJournalGetRequest, PersistedModeActionRecord?>(payload, null, null, cancellationToken).ConfigureAwait(false);
    }

    async Task<ModeActionAdvanceOutcome> IModeTransitionActionJournalStore.BeginApplyAsync(
        Guid transitionId,
        Guid actionId,
        int expectedActionRevision,
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        string? preStateJson,
        string? preStateDigest,
        CancellationToken cancellationToken)
    {
        var payload = new ModeTransitionActionJournalBeginApplyRequest(transitionId, actionId, expectedActionRevision, leaseId, fenceToken, ownerOperationId, preStateJson, preStateDigest);
        return await CallAsync<ModeTransitionActionJournalBeginApplyRequest, ModeActionAdvanceOutcome>(payload, ownerOperationId, null, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Broker omitted the required persistence value.");
    }

    async Task<ModeActionAdvanceOutcome> IModeTransitionActionJournalStore.RecordApplyResultAsync(
        Guid transitionId,
        Guid actionId,
        int expectedActionRevision,
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        PersistedModeApplyResult result,
        CancellationToken cancellationToken)
    {
        var payload = new ModeTransitionActionJournalRecordApplyResultRequest(transitionId, actionId, expectedActionRevision, leaseId, fenceToken, ownerOperationId, result);
        return await CallAsync<ModeTransitionActionJournalRecordApplyResultRequest, ModeActionAdvanceOutcome>(payload, ownerOperationId, null, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Broker omitted the required persistence value.");
    }

    async Task<ModeActionAdvanceOutcome> IModeTransitionActionJournalStore.BeginVerifyAsync(
        Guid transitionId,
        Guid actionId,
        int expectedActionRevision,
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        CancellationToken cancellationToken)
    {
        var payload = new ModeTransitionActionJournalBeginVerifyRequest(transitionId, actionId, expectedActionRevision, leaseId, fenceToken, ownerOperationId);
        return await CallAsync<ModeTransitionActionJournalBeginVerifyRequest, ModeActionAdvanceOutcome>(payload, ownerOperationId, null, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Broker omitted the required persistence value.");
    }

    async Task<ModeActionAdvanceOutcome> IModeTransitionActionJournalStore.RecordVerifyResultAsync(
        Guid transitionId,
        Guid actionId,
        int expectedActionRevision,
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        PersistedModeVerifyResult result,
        CancellationToken cancellationToken)
    {
        var payload = new ModeTransitionActionJournalRecordVerifyResultRequest(transitionId, actionId, expectedActionRevision, leaseId, fenceToken, ownerOperationId, result);
        return await CallAsync<ModeTransitionActionJournalRecordVerifyResultRequest, ModeActionAdvanceOutcome>(payload, ownerOperationId, null, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Broker omitted the required persistence value.");
    }

    async Task<ModeTransitionCommitOutcome> IModeTransitionCommitStore.CommitTransitionAndModeAsync(
        Guid transitionId,
        int expectedTransitionRevision,
        int expectedModeRevision,
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        bool runtimeAccessPermitsTarget,
        Guid? activationEpochId,
        PersistedModePolicyIdentity? currentPolicyIdentity,
        CancellationToken cancellationToken)
    {
        var payload = new ModeTransitionCommitCommitTransitionAndModeRequest(transitionId, expectedTransitionRevision, expectedModeRevision, leaseId, fenceToken, ownerOperationId, runtimeAccessPermitsTarget, activationEpochId, currentPolicyIdentity);
        return await CallAsync<ModeTransitionCommitCommitTransitionAndModeRequest, ModeTransitionCommitOutcome>(payload, ownerOperationId, null, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Broker omitted the required persistence value.");
    }

    async Task IModeTransitionRollbackStore.InitializeAsync(CancellationToken cancellationToken)
    {
        var payload = new ModeTransitionRollbackInitializeRequest();
        _ = await CallAsync<ModeTransitionRollbackInitializeRequest, bool>(payload, null, null, cancellationToken).ConfigureAwait(false);
    }
    async Task<PersistedModeActionRecord?> IModeTransitionRollbackStore.GetNextRollbackCandidateAsync(Guid transitionId, CancellationToken cancellationToken)
    {
        var payload = new ModeTransitionRollbackGetNextRollbackCandidateRequest(transitionId);
        return await CallAsync<ModeTransitionRollbackGetNextRollbackCandidateRequest, PersistedModeActionRecord?>(payload, null, null, cancellationToken).ConfigureAwait(false);
    }
    async Task<ModeRollbackAdvanceOutcome> IModeTransitionRollbackStore.BeginRollbackAsync(Guid transitionId, Guid actionId, int expectedActionRevision, Guid leaseId, long fenceToken, Guid ownerOperationId, CancellationToken cancellationToken)
    {
        var payload = new ModeTransitionRollbackBeginRollbackRequest(transitionId, actionId, expectedActionRevision, leaseId, fenceToken, ownerOperationId);
        return await CallAsync<ModeTransitionRollbackBeginRollbackRequest, ModeRollbackAdvanceOutcome>(payload, ownerOperationId, null, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Broker omitted the required persistence value.");
    }
    async Task<ModeRollbackAdvanceOutcome> IModeTransitionRollbackStore.RecordRollbackResultAsync(Guid transitionId, Guid actionId, int expectedActionRevision, Guid leaseId, long fenceToken, Guid ownerOperationId, PersistedModeRollbackResult result, CancellationToken cancellationToken)
    {
        var payload = new ModeTransitionRollbackRecordRollbackResultRequest(transitionId, actionId, expectedActionRevision, leaseId, fenceToken, ownerOperationId, result);
        return await CallAsync<ModeTransitionRollbackRecordRollbackResultRequest, ModeRollbackAdvanceOutcome>(payload, ownerOperationId, null, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Broker omitted the required persistence value.");
    }
    async Task IModeTransitionReconciliationStore.InitializeAsync(CancellationToken cancellationToken)
    {
        var payload = new ModeTransitionReconciliationInitializeRequest();
        _ = await CallAsync<ModeTransitionReconciliationInitializeRequest, bool>(payload, null, null, cancellationToken).ConfigureAwait(false);
    }
    async Task<ModeCrashReconciliationSnapshot> IModeTransitionReconciliationStore.InspectAsync(string currentControlSessionKey, CancellationToken cancellationToken)
    {
        var payload = new ModeTransitionReconciliationInspectRequest(currentControlSessionKey);
        return await CallAsync<ModeTransitionReconciliationInspectRequest, ModeCrashReconciliationSnapshot>(payload, null, null, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Broker omitted the required persistence value.");
    }
    async Task<ModeReconciliationTakeoverOutcome> IModeTransitionReconciliationStore.TakeOverAsync(Guid transitionId, int expectedTransitionRevision, string currentControlSessionKey, TimeSpan leaseLifetime, CancellationToken cancellationToken)
    {
        var payload = new ModeTransitionReconciliationTakeOverRequest(transitionId, expectedTransitionRevision, currentControlSessionKey, leaseLifetime);
        var transition = await ((IModeTransitionStore)this).GetAsync(transitionId, cancellationToken).ConfigureAwait(false);
        return await CallAsync<ModeTransitionReconciliationTakeOverRequest, ModeReconciliationTakeoverOutcome>(payload, transition?.OperationId, null, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Broker omitted the required persistence value.");
    }
    async Task<ModeTerminalLeaseCleanupOutcome> IModeTransitionReconciliationStore.ReleaseTerminalLeaseAsync(CancellationToken cancellationToken)
    {
        var payload = new ModeTransitionReconciliationReleaseTerminalLeaseRequest();
        return await CallAsync<ModeTransitionReconciliationReleaseTerminalLeaseRequest, ModeTerminalLeaseCleanupOutcome>(payload, null, null, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Broker omitted the required persistence value.");
    }

    public async Task<MachineModeBasePolicyResult> ResolveBaseAsync(Guid operationId, Guid correlationId,
        MachineModeBasePolicyRequest request, CancellationToken cancellationToken = default)
        => await CallAsync<MachineModeBasePolicyRequest, MachineModeBasePolicyResult>(request,
            operationId, correlationId, cancellationToken, ModeBasePolicyProtocol.Capability).ConfigureAwait(false)
            ?? throw new InvalidDataException("Broker omitted BASE policy.");

    public async Task<MachineModeBaseRecoveryResult> RecoverBaseAsync(Guid operationId, Guid correlationId,
        MachineModeBaseRecoveryRequest request, CancellationToken cancellationToken = default)
        => await CallAsync<MachineModeBaseRecoveryRequest, MachineModeBaseRecoveryResult>(request,
            operationId, correlationId, cancellationToken, ModeBaseRecoveryProtocol.Capability).ConfigureAwait(false)
            ?? throw new InvalidDataException("Broker omitted BASE recovery result.");

    public async Task<MachineServiceSourceVerifyResult> VerifySourceAsync(Guid operationId, Guid correlationId,
        MachineServiceSourceVerifyRequest request, CancellationToken cancellationToken = default)
        => await CallAsync<MachineServiceSourceVerifyRequest, MachineServiceSourceVerifyResult>(request,
            operationId, correlationId, cancellationToken, ManagedServiceSourceVerificationProtocol.Capability).ConfigureAwait(false)
            ?? throw new InvalidDataException("Broker omitted source verification result.");

    public async Task<MachineServicePolicyRollbackResult> RollbackAsync(Guid operationId, Guid correlationId,
        MachineServicePolicyRollbackRequest request, CancellationToken cancellationToken = default)
        => await CallAsync<MachineServicePolicyRollbackRequest, MachineServicePolicyRollbackResult>(request,
            operationId, correlationId, cancellationToken, ManagedServiceRollbackProtocol.Capability).ConfigureAwait(false)
            ?? throw new InvalidDataException("Broker omitted rollback result.");

    private async Task<TResult> CallAsync<TRequest, TResult>(TRequest payload, Guid? operationId, Guid? correlationId, CancellationToken cancellationToken,
        string capability = ModePersistenceProtocol.Capability)
    {
        // Repository methods carry ownerOperationId; obtain the original correlation from
        // durable state instead of inventing a new mode-operation trace for each IPC call.
        if (operationId.HasValue && !correlationId.HasValue)
        {
            if (payload is ITransitionPersistenceRequest transitionRequest)
            {
                var transition = await ((IModeTransitionStore)this).GetAsync(transitionRequest.TransitionId, cancellationToken).ConfigureAwait(false);
                if (transition?.OperationId == operationId) correlationId = transition.CorrelationId;
            }
            else
            {
                var lease = await ((IMachineMutationLeaseStore)this).GetAsync(cancellationToken).ConfigureAwait(false);
                if (lease.OwnerOperationId == operationId) correlationId = lease.OwnerCorrelationId;
            }
            // Missing/previously released state can still produce a typed rejection or replay.
            correlationId ??= operationId;
        }
        var request = ModePersistenceProtocol.CreateRequest(payload, operationId, correlationId) with { Capability = capability };
        var response = await _send(request, cancellationToken).ConfigureAwait(false);
        if (response.RequestId != request.RequestId || response.OperationId != request.OperationId ||
            response.CorrelationId != request.CorrelationId || response.Capability != request.Capability ||
            response.ProtocolVersion != request.ProtocolVersion)
            throw new InvalidDataException("Broker persistence response identity mismatch.");
        if (response.MessageType == MessageTypes.ErrorResponse)
        {
            var error = response.ReadPayload<ErrorResponse>();
            throw new InvalidDataException($"Broker persistence failed: {error.Code}: {error.Message}");
        }
        if (response.MessageType != request.MessageType + "Result")
            throw new InvalidDataException("Unexpected Broker persistence response type.");
        var result = response.Payload.Deserialize<ModePersistenceResult<TResult>>(ModePersistenceProtocol.JsonOptions)
            ?? throw new InvalidDataException("Missing Broker persistence result.");
        return result.Value;
    }

    private static Task<WireMessage> SendPipeAsync(WireMessage request, CancellationToken cancellationToken)
    {
        using var process = Process.GetCurrentProcess();
        var client = new NamedPipeRpcClient(NamedPipeNames.BrokerForSession(process.SessionId),
            ComponentIdentity.Name, ComponentIdentity.Version, connectTimeoutMilliseconds: 1500);
        return client.SendAsync(request, cancellationToken);
    }
}
