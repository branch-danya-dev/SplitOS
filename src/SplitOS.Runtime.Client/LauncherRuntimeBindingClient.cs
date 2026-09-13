using System.Diagnostics;
using SplitOS.Contracts.Protocol;
using SplitOS.Ipc;

namespace SplitOS.Runtime.Client;

/// <summary>
/// Typed Game Launcher client for the Runtime-owned coherent snapshot and readiness handshake.
/// It never accepts caller-supplied mode/session truth; the only write is acknowledgement of an
/// operation/correlation pair Runtime already exposed in its snapshot.
/// </summary>
public sealed class LauncherRuntimeBindingClient(string componentName, string componentVersion)
{
    public async Task<LauncherRuntimeSnapshotResult> ReadSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        var request = WireMessage.Create(
            MessageTypes.LauncherRuntimeSnapshotRequest,
            new LauncherRuntimeSnapshotRequest(),
            Capabilities.LauncherRuntimeSnapshotRead);

        var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return ReadResponse<LauncherRuntimeSnapshotResult>(
            response,
            MessageTypes.LauncherRuntimeSnapshotResult);
    }

    public async Task<LauncherReadyForGameModeResult> ReportReadyForGameModeAsync(
        Guid operationId,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty)
            throw new ArgumentException("Operation ID cannot be empty.", nameof(operationId));
        if (correlationId == Guid.Empty)
            throw new ArgumentException("Correlation ID cannot be empty.", nameof(correlationId));

        var client = CreateClient();
        var request = WireMessage.Create(
            MessageTypes.LauncherReadyForGameModeRequest,
            new LauncherReadyForGameModeRequest(operationId, correlationId),
            Capabilities.LauncherReadyForGameMode);

        var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return ReadResponse<LauncherReadyForGameModeResult>(
            response,
            MessageTypes.LauncherReadyForGameModeResult);
    }

    private NamedPipeRpcClient CreateClient()
    {
        using var process = Process.GetCurrentProcess();
        return new NamedPipeRpcClient(
            NamedPipeNames.RuntimeForSession(process.SessionId),
            componentName,
            componentVersion);
    }

    private static T ReadResponse<T>(WireMessage response, string expectedMessageType)
    {
        if (string.Equals(response.MessageType, MessageTypes.ErrorResponse, StringComparison.Ordinal))
        {
            var error = response.ReadPayload<ErrorResponse>();
            throw new InvalidOperationException($"Runtime returned {error.Code}: {error.Message}");
        }

        if (!string.Equals(response.MessageType, expectedMessageType, StringComparison.Ordinal))
            throw new InvalidDataException($"Unexpected Runtime response type {response.MessageType}.");

        return response.ReadPayload<T>();
    }
}
