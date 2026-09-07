using System.Diagnostics;
using SplitOS.Contracts.Protocol;

namespace SplitOS.Broker.Service;

public sealed class BrokerMessageHandler
{
    public ValueTask<WireMessage> HandleAsync(WireMessage request, CancellationToken _)
    {
        if (!string.Equals(request.Capability, Capabilities.BrokerHealthRead, StringComparison.Ordinal))
        {
            return ValueTask.FromResult(WireMessage.Respond(
                request,
                MessageTypes.ErrorResponse,
                new ErrorResponse(ErrorCodes.UnknownCapability, "Broker capability is not allowlisted.")));
        }

        if (!string.Equals(request.MessageType, MessageTypes.HealthReadRequest, StringComparison.Ordinal))
        {
            return ValueTask.FromResult(WireMessage.Respond(
                request,
                MessageTypes.ErrorResponse,
                new ErrorResponse(ErrorCodes.UnsupportedMessage, "Capability does not support this message type.")));
        }

        var process = Process.GetCurrentProcess();
        return ValueTask.FromResult(WireMessage.Respond(
            request,
            MessageTypes.HealthReadResult,
            new HealthReadResult(
                ComponentIdentity.Name,
                ComponentIdentity.Version,
                "HEALTHY",
                Environment.ProcessId,
                process.SessionId,
                DateTimeOffset.UtcNow)));
    }
}
