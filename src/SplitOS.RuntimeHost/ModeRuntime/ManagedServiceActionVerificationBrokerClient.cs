using System.Diagnostics;
using SplitOS.Contracts.Protocol;
using SplitOS.Ipc;

namespace SplitOS.RuntimeHost.ModeRuntime;

public interface IManagedServiceActionVerificationBrokerClient
{
    Task<MachineServicePolicyVerifyResult> VerifyAsync(
        Guid operationId,
        Guid correlationId,
        MachineServicePolicyVerifyRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class NamedPipeManagedServiceActionVerificationBrokerClient
    : IManagedServiceActionVerificationBrokerClient
{
    public async Task<MachineServicePolicyVerifyResult> VerifyAsync(
        Guid operationId,
        Guid correlationId,
        MachineServicePolicyVerifyRequest request,
        CancellationToken cancellationToken = default)
    {
        var sessionId = Process.GetCurrentProcess().SessionId;
        var client = new NamedPipeRpcClient(
            NamedPipeNames.BrokerForSession(sessionId),
            ComponentIdentity.Name,
            ComponentIdentity.Version,
            connectTimeoutMilliseconds: 1_500);
        var response = await client.SendAsync(
            WireMessage.Create(
                MessageTypes.MachineServicePolicyVerifyRequest,
                request,
                Capabilities.MachineServicePolicyVerify,
                operationId,
                correlationId),
            cancellationToken).ConfigureAwait(false);

        if (string.Equals(response.MessageType, MessageTypes.ErrorResponse, StringComparison.Ordinal))
        {
            var error = response.ReadPayload<ErrorResponse>();
            throw new InvalidDataException(
                $"Broker managed-service verify failed: {error.Code}: {error.Message}");
        }

        if (!string.Equals(response.MessageType, MessageTypes.MachineServicePolicyVerifyResult, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Unexpected Broker managed-service verify response type {response.MessageType}.");
        }

        return response.ReadPayload<MachineServicePolicyVerifyResult>();
    }
}
