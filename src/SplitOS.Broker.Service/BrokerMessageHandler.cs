using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SplitOS.Contracts.Protocol;
using SplitOS.Persistence.Machine;

namespace SplitOS.Broker.Service;

public sealed class BrokerMessageHandler(
    MachineStateStore machineStateStore,
    BrokerManagedServicePolicyExecutor? managedServicePolicyExecutor = null,
    BrokerManagedServiceSnapshotExecutor? managedServiceSnapshotExecutor = null,
    BrokerManagedServiceVerificationExecutor? managedServiceVerificationExecutor = null,
    BrokerModePersistenceHandler? modePersistenceHandler = null,
    BrokerManagedServiceRollbackExecutor? managedServiceRollbackExecutor = null,
    BrokerManagedServiceSourceVerificationExecutor? managedServiceSourceVerificationExecutor = null,
    BrokerModeBasePolicyResolver? modeBasePolicyResolver = null,
    BrokerModeBaseRecoveryExecutor? modeBaseRecoveryExecutor = null)
{
    private readonly string _componentName = ComponentIdentity.Name;
    private readonly string _componentVersion = ComponentIdentity.Version;

    public async ValueTask<WireMessage> HandleAsync(WireMessage request, CancellationToken cancellationToken)
    {
        if (request.Capability == ModeBaseRecoveryProtocol.Capability)
        {
            if (request.MessageType != nameof(MachineModeBaseRecoveryRequest)) return Unsupported(request);
            if (modeBaseRecoveryExecutor is null) return PersistenceUnavailable(request, "BASE recovery executor is not configured.");
            try
            {
                if (request.ProtocolVersion != ProtocolConstants.CurrentVersion || request.OperationId == Guid.Empty ||
                    request.CorrelationId == Guid.Empty || request.RequestId == Guid.Empty || request.Payload.ValueKind != JsonValueKind.Object)
                    throw new ArgumentException("Invalid BASE recovery envelope.");
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in request.Payload.EnumerateObject())
                    if (!names.Add(property.Name)) throw new JsonException("Duplicate BASE recovery field.");
                var payload = request.Payload.Deserialize<MachineModeBaseRecoveryRequest>(SplitOS.Contracts.ModePersistence.ModePersistenceProtocol.JsonOptions)
                    ?? throw new JsonException("BASE recovery payload is required.");
                var result = await modeBaseRecoveryExecutor.ExecuteAsync(request.OperationId, request.CorrelationId, payload, cancellationToken).ConfigureAwait(false);
                return SplitOS.Contracts.ModePersistence.ModePersistenceProtocol.Respond(request, result);
            }
            catch (Exception ex) when (ex is ArgumentException or JsonException)
            {
                return WireMessage.Respond(request, MessageTypes.ErrorResponse, new ErrorResponse(ErrorCodes.InvalidMessage, ex.Message));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or SqliteException or UnauthorizedAccessException)
            {
                return PersistenceUnavailable(request, ex.Message);
            }
        }

        if (request.Capability == ManagedServiceRollbackProtocol.Capability)
        {
            if (request.MessageType != nameof(MachineServicePolicyRollbackRequest)) return Unsupported(request);
            if (managedServiceRollbackExecutor is null) return PersistenceUnavailable(request, "Rollback executor is not configured.");
            try
            {
                if (request.ProtocolVersion != ProtocolConstants.CurrentVersion || request.OperationId == Guid.Empty ||
                    request.CorrelationId == Guid.Empty || request.RequestId == Guid.Empty || request.Payload.ValueKind != JsonValueKind.Object)
                    throw new ArgumentException("Invalid rollback envelope.");
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in request.Payload.EnumerateObject())
                    if (!names.Add(property.Name)) throw new JsonException("Duplicate rollback field.");
                var payload = request.Payload.Deserialize<MachineServicePolicyRollbackRequest>(SplitOS.Contracts.ModePersistence.ModePersistenceProtocol.JsonOptions)
                    ?? throw new JsonException("Rollback payload is required.");
                var result = await managedServiceRollbackExecutor.ExecuteAsync(request.OperationId, request.CorrelationId, payload, cancellationToken).ConfigureAwait(false);
                return SplitOS.Contracts.ModePersistence.ModePersistenceProtocol.Respond(request, result);
            }
            catch (Exception ex) when (ex is ArgumentException or JsonException)
            {
                return WireMessage.Respond(request, MessageTypes.ErrorResponse, new ErrorResponse(ErrorCodes.InvalidMessage, ex.Message));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or SqliteException or UnauthorizedAccessException)
            {
                return PersistenceUnavailable(request, ex.Message);
            }
        }

        if (request.Capability == ManagedServiceSourceVerificationProtocol.Capability)
        {
            if (request.MessageType != nameof(MachineServiceSourceVerifyRequest)) return Unsupported(request);
            if (managedServiceSourceVerificationExecutor is null) return PersistenceUnavailable(request, "Source verification executor is not configured.");
            try
            {
                if (request.ProtocolVersion != ProtocolConstants.CurrentVersion || request.OperationId == Guid.Empty ||
                    request.CorrelationId == Guid.Empty || request.RequestId == Guid.Empty || request.Payload.ValueKind != JsonValueKind.Object)
                    throw new ArgumentException("Invalid source verification envelope.");
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in request.Payload.EnumerateObject())
                    if (!names.Add(property.Name)) throw new JsonException("Duplicate source verification field.");
                var payload = request.Payload.Deserialize<MachineServiceSourceVerifyRequest>(SplitOS.Contracts.ModePersistence.ModePersistenceProtocol.JsonOptions)
                    ?? throw new JsonException("Source verification payload is required.");
                var result = await managedServiceSourceVerificationExecutor.ExecuteAsync(request.OperationId, request.CorrelationId, payload, cancellationToken).ConfigureAwait(false);
                return SplitOS.Contracts.ModePersistence.ModePersistenceProtocol.Respond(request, result);
            }
            catch (Exception ex) when (ex is ArgumentException or JsonException)
            {
                return WireMessage.Respond(request, MessageTypes.ErrorResponse, new ErrorResponse(ErrorCodes.InvalidMessage, ex.Message));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or SqliteException or UnauthorizedAccessException)
            {
                return PersistenceUnavailable(request, ex.Message);
            }
        }

        if (request.Capability == ModeBasePolicyProtocol.Capability)
        {
            if (request.MessageType != nameof(MachineModeBasePolicyRequest)) return Unsupported(request);
            if (modeBasePolicyResolver is null) return PersistenceUnavailable(request, "BASE policy executor is not configured.");
            try
            {
                if (request.ProtocolVersion != ProtocolConstants.CurrentVersion || request.OperationId == Guid.Empty ||
                    request.CorrelationId == Guid.Empty || request.RequestId == Guid.Empty || request.Payload.ValueKind != JsonValueKind.Object)
                    throw new ArgumentException("Invalid BASE policy envelope.");
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in request.Payload.EnumerateObject())
                    if (!names.Add(property.Name)) throw new JsonException("Duplicate BASE policy field.");
                var payload = request.Payload.Deserialize<MachineModeBasePolicyRequest>(SplitOS.Contracts.ModePersistence.ModePersistenceProtocol.JsonOptions)
                    ?? throw new JsonException("BASE policy payload is required.");
                var result = await modeBasePolicyResolver.ExecuteAsync(request.OperationId, request.CorrelationId, payload, cancellationToken).ConfigureAwait(false);
                return SplitOS.Contracts.ModePersistence.ModePersistenceProtocol.Respond(request, result);
            }
            catch (Exception ex) when (ex is ArgumentException or JsonException)
            {
                return WireMessage.Respond(request, MessageTypes.ErrorResponse, new ErrorResponse(ErrorCodes.InvalidMessage, ex.Message));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or SqliteException or UnauthorizedAccessException)
            {
                return PersistenceUnavailable(request, ex.Message);
            }
        }

        if (request.Capability == SplitOS.Contracts.ModePersistence.ModePersistenceProtocol.Capability)
        {
            if (modePersistenceHandler is null)
                return PersistenceUnavailable(request, "Mode persistence handler is not configured.");
            return await modePersistenceHandler.HandleAsync(request, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(request.Capability, Capabilities.BrokerHealthRead, StringComparison.Ordinal))
        {
            if (!string.Equals(request.MessageType, MessageTypes.HealthReadRequest, StringComparison.Ordinal))
                return Unsupported(request);

            using var process = Process.GetCurrentProcess();
            return WireMessage.Respond(request, MessageTypes.HealthReadResult,
                new HealthReadResult(_componentName, _componentVersion, "HEALTHY", Environment.ProcessId, process.SessionId, DateTimeOffset.UtcNow));
        }

        if (string.Equals(request.Capability, Capabilities.MachineStateStoreRead, StringComparison.Ordinal))
        {
            if (!string.Equals(request.MessageType, MessageTypes.MachineStateReadRequest, StringComparison.Ordinal))
                return Unsupported(request);

            try
            {
                var read = request.ReadPayload<MachineStateReadRequest>();
                if (!string.Equals(read.RecordKind, "OPERATIONAL_MODE", StringComparison.Ordinal) ||
                    !string.Equals(read.RecordId, "singleton", StringComparison.Ordinal))
                {
                    return WireMessage.Respond(request, MessageTypes.ErrorResponse,
                        new ErrorResponse(ErrorCodes.InvalidRecordKind, "Record kind/id is not allowlisted for SLICE-01."));
                }

                var record = await machineStateStore.GetOperationalModeAsync(cancellationToken).ConfigureAwait(false);
                return WireMessage.Respond(request, MessageTypes.MachineStateReadResult,
                    new MachineStateReadResult(
                        "OPERATIONAL_MODE",
                        "singleton",
                        MachineStateStore.SchemaVersion,
                        record.Revision,
                        JsonSerializer.Serialize(record),
                        DateTimeOffset.UtcNow));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or SqliteException)
            {
                return PersistenceUnavailable(request, ex.Message);
            }
        }

        if (string.Equals(request.Capability, Capabilities.MachineServicePolicySnapshot, StringComparison.Ordinal))
        {
            if (!string.Equals(request.MessageType, MessageTypes.MachineServicePolicySnapshotRequest, StringComparison.Ordinal))
                return Unsupported(request);

            if (managedServiceSnapshotExecutor is null)
            {
                return WireMessage.Respond(
                    request,
                    MessageTypes.ErrorResponse,
                    new ErrorResponse(ErrorCodes.InternalError, "Managed-service snapshot executor is not configured."));
            }

            try
            {
                var snapshot = request.ReadPayload<MachineServicePolicySnapshotRequest>();
                var result = await managedServiceSnapshotExecutor.ExecuteAsync(
                    request.OperationId,
                    request.CorrelationId,
                    snapshot,
                    cancellationToken).ConfigureAwait(false);
                return WireMessage.Respond(request, MessageTypes.MachineServicePolicySnapshotResult, result);
            }
            catch (Exception ex) when (ex is ArgumentException or JsonException)
            {
                return WireMessage.Respond(
                    request,
                    MessageTypes.ErrorResponse,
                    new ErrorResponse(ErrorCodes.InvalidMessage, ex.Message));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or SqliteException)
            {
                return PersistenceUnavailable(request, ex.Message);
            }
        }

        if (string.Equals(request.Capability, Capabilities.MachineServicePolicyApply, StringComparison.Ordinal))
        {
            if (!string.Equals(request.MessageType, MessageTypes.MachineServicePolicyApplyRequest, StringComparison.Ordinal))
                return Unsupported(request);

            if (managedServicePolicyExecutor is null)
            {
                return WireMessage.Respond(
                    request,
                    MessageTypes.ErrorResponse,
                    new ErrorResponse(ErrorCodes.InternalError, "Managed-service policy executor is not configured."));
            }

            try
            {
                var apply = request.ReadPayload<MachineServicePolicyApplyRequest>();
                var result = await managedServicePolicyExecutor.ExecuteAsync(
                    request.OperationId,
                    request.CorrelationId,
                    apply,
                    cancellationToken).ConfigureAwait(false);
                return WireMessage.Respond(
                    request,
                    MessageTypes.MachineServicePolicyApplyResult,
                    result);
            }
            catch (Exception ex) when (ex is ArgumentException or JsonException)
            {
                return WireMessage.Respond(
                    request,
                    MessageTypes.ErrorResponse,
                    new ErrorResponse(ErrorCodes.InvalidMessage, ex.Message));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or SqliteException)
            {
                return PersistenceUnavailable(request, ex.Message);
            }
        }

        if (string.Equals(request.Capability, Capabilities.MachineServicePolicyVerify, StringComparison.Ordinal))
        {
            if (!string.Equals(request.MessageType, MessageTypes.MachineServicePolicyVerifyRequest, StringComparison.Ordinal))
                return Unsupported(request);

            if (managedServiceVerificationExecutor is null)
            {
                return WireMessage.Respond(
                    request,
                    MessageTypes.ErrorResponse,
                    new ErrorResponse(ErrorCodes.InternalError, "Managed-service verification executor is not configured."));
            }

            try
            {
                var verify = request.ReadPayload<MachineServicePolicyVerifyRequest>();
                var result = await managedServiceVerificationExecutor.ExecuteAsync(
                    request.OperationId,
                    request.CorrelationId,
                    verify,
                    cancellationToken).ConfigureAwait(false);
                return WireMessage.Respond(
                    request,
                    MessageTypes.MachineServicePolicyVerifyResult,
                    result);
            }
            catch (Exception ex) when (ex is ArgumentException or JsonException)
            {
                return WireMessage.Respond(
                    request,
                    MessageTypes.ErrorResponse,
                    new ErrorResponse(ErrorCodes.InvalidMessage, ex.Message));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or SqliteException)
            {
                return PersistenceUnavailable(request, ex.Message);
            }
        }

        if (string.Equals(request.Capability, Capabilities.MachineOperationalModeWrite, StringComparison.Ordinal))
        {
            if (!string.Equals(request.MessageType, MessageTypes.MachineOperationalModeWriteRequest, StringComparison.Ordinal))
                return Unsupported(request);

            try
            {
                var write = request.ReadPayload<MachineOperationalModeWriteRequest>();
                if (write.ExpectedRevision < 1)
                {
                    return WireMessage.Respond(request, MessageTypes.ErrorResponse,
                        new ErrorResponse(ErrorCodes.InvalidMessage, "ExpectedRevision must be greater than zero."));
                }

                if (!string.Equals(write.TargetMode, "NONE", StringComparison.Ordinal))
                {
                    return WireMessage.Respond(request, MessageTypes.ErrorResponse,
                        new ErrorResponse(
                            ErrorCodes.ManagedModeWriteNotAvailable,
                            "SLICE-01 permits only NONE convergence. WORK/GAME writes require the managed mode engine."));
                }

                var outcome = await machineStateStore.WriteOperationalModeAsync(
                    write.TargetMode,
                    write.ExpectedRevision,
                    request.OperationId,
                    request.CorrelationId,
                    cancellationToken).ConfigureAwait(false);

                if (outcome.Disposition == OperationalModeWriteDisposition.RevisionConflict)
                {
                    return WireMessage.Respond(request, MessageTypes.ErrorResponse,
                        new ErrorResponse(
                            ErrorCodes.PersistenceRevisionConflict,
                            outcome.Detail ?? $"Machine state revision conflict. Actual revision: {outcome.ActualRevision}."));
                }

                if (outcome.Disposition == OperationalModeWriteDisposition.IdempotencyConflict)
                {
                    return WireMessage.Respond(request, MessageTypes.ErrorResponse,
                        new ErrorResponse(
                            ErrorCodes.IdempotencyConflict,
                            outcome.Detail ?? "OperationId conflicts with an already committed request."));
                }

                var record = outcome.Record
                    ?? throw new InvalidDataException("Successful machine-state write did not return a committed record.");
                return WireMessage.Respond(request, MessageTypes.MachineOperationalModeWriteResult,
                    new MachineOperationalModeWriteResult(
                        outcome.Disposition.ToString().ToUpperInvariant(),
                        record.CommittedMode,
                        record.Revision,
                        request.OperationId,
                        request.CorrelationId,
                        record.CommittedUtc));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or SqliteException)
            {
                return PersistenceUnavailable(request, ex.Message);
            }
        }

        return WireMessage.Respond(request, MessageTypes.ErrorResponse,
            new ErrorResponse(ErrorCodes.UnknownCapability, "Broker capability is not allowlisted."));
    }

    private static WireMessage Unsupported(WireMessage request) => WireMessage.Respond(
        request,
        MessageTypes.ErrorResponse,
        new ErrorResponse(ErrorCodes.UnsupportedMessage, "Capability does not support this message type."));

    private static WireMessage PersistenceUnavailable(WireMessage request, string message) => WireMessage.Respond(
        request,
        MessageTypes.ErrorResponse,
        new ErrorResponse(ErrorCodes.PersistenceUnavailable, message));
}
