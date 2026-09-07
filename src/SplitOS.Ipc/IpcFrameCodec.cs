using System.Buffers.Binary;
using System.Text.Json;
using SplitOS.Contracts.Protocol;

namespace SplitOS.Ipc;

public static class IpcFrameCodec
{
    public static async ValueTask WriteAsync(Stream stream, WireMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(message);

        var payload = JsonSerializer.SerializeToUtf8Bytes(message, ProtocolJson.Options);
        if (payload.Length is <= 0 or > ProtocolConstants.MaxFrameBytes)
        {
            throw new InvalidDataException($"IPC frame size {payload.Length} is outside the allowed range.");
        }

        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);

        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<WireMessage> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var header = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);

        if (length is <= 0 or > ProtocolConstants.MaxFrameBytes)
        {
            throw new InvalidDataException($"IPC frame length {length} is outside the allowed range.");
        }

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);

        return JsonSerializer.Deserialize<WireMessage>(payload, ProtocolJson.Options)
               ?? throw new InvalidDataException("IPC frame did not contain a valid WireMessage.");
    }
}
