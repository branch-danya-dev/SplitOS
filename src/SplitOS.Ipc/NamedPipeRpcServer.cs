using System.IO.Pipes;
using SplitOS.Contracts.Protocol;

namespace SplitOS.Ipc;

public sealed record HandshakeDecision(bool Accepted, string? Reason)
{
    public static HandshakeDecision Allow() => new(true, null);
    public static HandshakeDecision Deny(string reason) => new(false, reason);
}

public static class NamedPipeRpcServer
{
    public static NamedPipeServerStream Create(string pipeName, int maxInstances = 8)
        => new(
            pipeName,
            PipeDirection.InOut,
            maxInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

    public static async Task HandleConnectionAsync(
        NamedPipeServerStream server,
        string serverComponent,
        string serverVersion,
        Func<NamedPipeServerStream, ProtocolHello, CancellationToken, ValueTask<HandshakeDecision>> authorize,
        Func<WireMessage, CancellationToken, ValueTask<WireMessage>> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(authorize);
        ArgumentNullException.ThrowIfNull(handler);

        var helloMessage = await IpcFrameCodec.ReadAsync(server, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(helloMessage.MessageType, MessageTypes.ProtocolHello, StringComparison.Ordinal))
        {
            await WriteHelloAckAsync(server, helloMessage, serverComponent, serverVersion, false, "First frame must be ProtocolHello.", cancellationToken).ConfigureAwait(false);
            return;
        }

        ProtocolHello hello;
        try
        {
            hello = helloMessage.ReadPayload<ProtocolHello>();
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            await WriteHelloAckAsync(server, helloMessage, serverComponent, serverVersion, false, "Invalid ProtocolHello payload.", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (helloMessage.ProtocolVersion != ProtocolConstants.CurrentVersion)
        {
            await WriteHelloAckAsync(server, helloMessage, serverComponent, serverVersion, false, ErrorCodes.ProtocolUnsupported, cancellationToken).ConfigureAwait(false);
            return;
        }

        var decision = await authorize(server, hello, cancellationToken).ConfigureAwait(false);
        await WriteHelloAckAsync(server, helloMessage, serverComponent, serverVersion, decision.Accepted, decision.Reason, cancellationToken).ConfigureAwait(false);
        if (!decision.Accepted)
        {
            return;
        }

        while (server.IsConnected && !cancellationToken.IsCancellationRequested)
        {
            WireMessage request;
            try
            {
                request = await IpcFrameCodec.ReadAsync(server, cancellationToken).ConfigureAwait(false);
            }
            catch (EndOfStreamException)
            {
                break;
            }

            WireMessage response;
            try
            {
                response = await handler(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                response = WireMessage.Respond(
                    request,
                    MessageTypes.ErrorResponse,
                    new ErrorResponse(ErrorCodes.InternalError, "The peer could not complete the request."));
            }

            await IpcFrameCodec.WriteAsync(server, response, cancellationToken).ConfigureAwait(false);
        }
    }

    private static ValueTask WriteHelloAckAsync(
        Stream stream,
        WireMessage helloMessage,
        string serverComponent,
        string serverVersion,
        bool accepted,
        string? reason,
        CancellationToken cancellationToken)
        => IpcFrameCodec.WriteAsync(
            stream,
            WireMessage.Respond(
                helloMessage,
                MessageTypes.ProtocolHelloAck,
                new ProtocolHelloAck(
                    serverComponent,
                    serverVersion,
                    ProtocolConstants.CurrentVersion,
                    accepted,
                    reason)),
            cancellationToken);
}
