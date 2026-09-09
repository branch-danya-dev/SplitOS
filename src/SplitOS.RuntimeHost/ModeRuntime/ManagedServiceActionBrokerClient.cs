using System.Diagnostics;
using SplitOS.Contracts.Protocol;
using SplitOS.Ipc;

namespace SplitOS.RuntimeHost.ModeRuntime;

public interface IManagedServiceActionBrokerClient
{
    Task<MachineServicePolicySnapshotResult> SnapshotAsync(
        Guid operationId,
        Guid correlationId,
        MachineServicePolicySnapshotRequest request,
        CancellationToken cancellationToken = default);

    Task<MachineServicePolicyApplyResult> ApplyAsync(
        Guid operationId,
        Guid correlationId,
        MachineServicePolicyApplyRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Runtime-side semantic Broker client for managed-service action execution. The caller supplies
/// durable operation/correlation identities; this transport never invents a second action identity.
/// </summary>
public sealed class NamedPipeManagedServiceActionBrokerClient : IManagedServiceActionBrokerClient
{
    public async Task<MachineServicePolicySnapshotResult> SnapshotAsync(
        Guid operationId,
        Guid correlationId,
        MachineServicePolicySnapshotRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            WireMessage.Create(
                MessageTypes.MachineServicePolicySnapshotRequest,
                request,
                Capabilities.MachineServicePolicySnapshot,
                operationId,
                correlationId),
            cancellationToken).ConfigureAwait(false);
        EnsureNotError(response, "managed-service snapshot");
        if (!string.Equals(response.MessageType, MessageTypes.MachineServicePolicySnapshotResult, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Unexpected Broker managed-service snapshot response type {response.MessageType}.");
        }

        return response.ReadPayload<MachineServicePolicySnapshotResult>();
    }

    public async Task<MachineServicePolicyApplyResult> ApplyAsync(
        Guid operationId,
        Guid correlationId,
        MachineServicePolicyApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            WireMessage.Create(
                MessageTypes.MachineServicePolicyApplyRequest,
                request,
                Capabilities.MachineServicePolicyApply,
                operationId,
                correlationId),
            cancellationToken).ConfigureAwait(false);
        EnsureNotError(response, "managed-service apply");
        if (!string.Equals(response.MessageType, MessageTypes.MachineServicePolicyApplyResult, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Unexpected Broker managed-service apply response type {response.MessageType}.");
        }

        return response.ReadPayload<MachineServicePolicyApplyResult>();
    }

    private static Task<WireMessage> SendAsync(
        WireMessage request,
        CancellationToken cancellationToken)
    {
        var sessionId = Process.GetCurrentProcess().SessionId;
        var client = new NamedPipeRpcClient(
            NamedPipeNames.BrokerForSession(sessionId),
            ComponentIdentity.Name,
            ComponentIdentity.Version,
            connectTimeoutMilliseconds: 1_500);
        return client.SendAsync(request, cancellationToken);
    }

    private static void EnsureNotError(WireMessage response, string operation)
    {
        if (!string.Equals(response.MessageType, MessageTypes.ErrorResponse, StringComparison.Ordinal)) return;
        var error = response.ReadPayload<ErrorResponse>();
        throw new InvalidDataException(
            $"Broker {operation} failed: {error.Code}: {error.Message}");
    }
}
