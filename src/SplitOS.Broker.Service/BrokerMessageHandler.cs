using System.Diagnostics;
using System.Text.Json;
using SplitOS.Contracts.Protocol;
using SplitOS.Persistence.Machine;

namespace SplitOS.Broker.Service;

public sealed class BrokerMessageHandler(MachineStateStore machineStateStore)
{
    private readonly string _componentName = ComponentIdentity.Name;
    private readonly string _componentVersion = ComponentIdentity.Version;

    public async ValueTask<WireMessage> HandleAsync(WireMessage request, CancellationToken cancellationToken)
    {
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
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
                return WireMessage.Respond(request, MessageTypes.ErrorResponse,
                    new ErrorResponse(ErrorCodes.PersistenceUnavailable, ex.Message));
            }
        }

        return WireMessage.Respond(request, MessageTypes.ErrorResponse,
            new ErrorResponse(ErrorCodes.UnknownCapability, "Broker capability is not allowlisted."));
    }

    private static WireMessage Unsupported(WireMessage request) => WireMessage.Respond(
        request,
        MessageTypes.ErrorResponse,
        new ErrorResponse(ErrorCodes.UnsupportedMessage, "Capability does not support this message type."));
}
