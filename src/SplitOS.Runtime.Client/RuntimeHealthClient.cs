using System.Diagnostics;
using SplitOS.Contracts.Protocol;
using SplitOS.Ipc;

namespace SplitOS.Runtime.Client;

public sealed class RuntimeHealthClient(string componentName, string componentVersion)
{
    public async Task<HealthReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        var sessionId = Process.GetCurrentProcess().SessionId;
        var client = new NamedPipeRpcClient(
            NamedPipeNames.RuntimeForSession(sessionId),
            componentName,
            componentVersion);

        var request = WireMessage.Create(
            MessageTypes.HealthReadRequest,
            new HealthReadRequest(),
            Capabilities.RuntimeHealthRead);

        var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (string.Equals(response.MessageType, MessageTypes.ErrorResponse, StringComparison.Ordinal))
        {
            var error = response.ReadPayload<ErrorResponse>();
            throw new InvalidOperationException($"Runtime returned {error.Code}: {error.Message}");
        }

        if (!string.Equals(response.MessageType, MessageTypes.HealthReadResult, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unexpected Runtime response type {response.MessageType}.");
        }

        return response.ReadPayload<HealthReadResult>();
    }
}
