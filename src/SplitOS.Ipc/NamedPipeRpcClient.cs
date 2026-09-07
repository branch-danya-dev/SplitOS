using System.IO.Pipes;
using SplitOS.Contracts.Protocol;

namespace SplitOS.Ipc;

public sealed class NamedPipeRpcClient
{
    private readonly string _pipeName;
    private readonly string _component;
    private readonly string _componentVersion;
    private readonly int _connectTimeoutMilliseconds;

    public NamedPipeRpcClient(
        string pipeName,
        string component,
        string componentVersion,
        int connectTimeoutMilliseconds = 3_000)
    {
        _pipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));
        _component = component ?? throw new ArgumentNullException(nameof(component));
        _componentVersion = componentVersion ?? throw new ArgumentNullException(nameof(componentVersion));
        _connectTimeoutMilliseconds = connectTimeoutMilliseconds;
    }

    public async Task<WireMessage> SendAsync(WireMessage request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var client = new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        await client.ConnectAsync(_connectTimeoutMilliseconds, cancellationToken).ConfigureAwait(false);

        var hello = WireMessage.Create(
            MessageTypes.ProtocolHello,
            new ProtocolHello(_component, _componentVersion));

        await IpcFrameCodec.WriteAsync(client, hello, cancellationToken).ConfigureAwait(false);
        var ackMessage = await IpcFrameCodec.ReadAsync(client, cancellationToken).ConfigureAwait(false);

        if (!string.Equals(ackMessage.MessageType, MessageTypes.ProtocolHelloAck, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Peer did not return ProtocolHelloAck.");
        }

        var ack = ackMessage.ReadPayload<ProtocolHelloAck>();
        if (!ack.Accepted)
        {
            throw new UnauthorizedAccessException(ack.Reason ?? "IPC peer rejected the caller.");
        }

        if (ack.NegotiatedProtocolVersion != ProtocolConstants.CurrentVersion)
        {
            throw new InvalidDataException($"Peer negotiated unsupported protocol {ack.NegotiatedProtocolVersion}.");
        }

        await IpcFrameCodec.WriteAsync(client, request, cancellationToken).ConfigureAwait(false);
        return await IpcFrameCodec.ReadAsync(client, cancellationToken).ConfigureAwait(false);
    }
}
