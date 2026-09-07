using System.Diagnostics;
using System.Text.Json;
using SplitOS.Contracts.Protocol;
using SplitOS.Ipc;

namespace SplitOS.RuntimeHost;

public sealed record MachineOperationalModeSnapshot(string CommittedMode, int SchemaVersion, int Revision);

public sealed class MachineStateClient
{
    public async Task<MachineOperationalModeSnapshot> ReadOperationalModeAsync(CancellationToken cancellationToken = default)
    {
        var sessionId = Process.GetCurrentProcess().SessionId;
        var client = new NamedPipeRpcClient(
            NamedPipeNames.BrokerForSession(sessionId),
            ComponentIdentity.Name,
            ComponentIdentity.Version,
            connectTimeoutMilliseconds: 1_500);

        var request = WireMessage.Create(
            MessageTypes.MachineStateReadRequest,
            new MachineStateReadRequest("OPERATIONAL_MODE", "singleton"),
            Capabilities.MachineStateStoreRead);
        var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (string.Equals(response.MessageType, MessageTypes.ErrorResponse, StringComparison.Ordinal))
        {
            var error = response.ReadPayload<ErrorResponse>();
            throw new InvalidDataException($"Broker machine-state read failed: {error.Code}: {error.Message}");
        }

        if (!string.Equals(response.MessageType, MessageTypes.MachineStateReadResult, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unexpected Broker machine-state response type {response.MessageType}.");
        }

        var result = response.ReadPayload<MachineStateReadResult>();
        using var document = JsonDocument.Parse(result.PayloadJson);
        if (!document.RootElement.TryGetProperty("CommittedMode", out var modeProperty))
        {
            throw new InvalidDataException("Broker operational-mode payload does not contain CommittedMode.");
        }

        var mode = modeProperty.GetString() ?? throw new InvalidDataException("Broker returned an empty committed mode.");
        return new MachineOperationalModeSnapshot(mode, result.SchemaVersion, result.Revision);
    }
}
