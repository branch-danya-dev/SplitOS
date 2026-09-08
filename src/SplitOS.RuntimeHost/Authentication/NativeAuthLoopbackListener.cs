using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SplitOS.RuntimeHost.Authentication;

public interface INativeAuthLoopbackListenerFactory
{
    INativeAuthLoopbackListener Bind();
}

public interface INativeAuthLoopbackListener : IAsyncDisposable
{
    Uri RedirectUri { get; }
    Task<INativeAuthLoopbackRequest> ReceiveAsync(CancellationToken cancellationToken = default);
}

public interface INativeAuthLoopbackRequest : IAsyncDisposable
{
    Uri CallbackUri { get; }
    Task RespondAsync(bool protocolAccepted, CancellationToken cancellationToken = default);
}

public sealed class TcpNativeAuthLoopbackListenerFactory : INativeAuthLoopbackListenerFactory
{
    public INativeAuthLoopbackListener Bind() => TcpNativeAuthLoopbackListener.Bind();
}

public sealed class TcpNativeAuthLoopbackListener : INativeAuthLoopbackListener
{
    private const int MaximumHeaderBytes = 8192;
    private readonly TcpListener _listener;
    private int _receiveStarted;
    private int _disposed;

    private TcpNativeAuthLoopbackListener(TcpListener listener, Uri redirectUri)
    {
        _listener = listener;
        RedirectUri = redirectUri;
    }

    public Uri RedirectUri { get; }

    public static TcpNativeAuthLoopbackListener Bind()
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(1);
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            if (endpoint.Port > 1024)
            {
                return new TcpNativeAuthLoopbackListener(
                    listener,
                    new Uri($"http://127.0.0.1:{endpoint.Port}/oauth/callback", UriKind.Absolute));
            }

            listener.Stop();
        }

        throw new InvalidOperationException("Windows did not allocate a valid ephemeral loopback port for native authentication.");
    }

    public async Task<INativeAuthLoopbackRequest> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _receiveStarted, 1) != 0)
        {
            throw new InvalidOperationException("The native-auth loopback listener accepts exactly one callback request.");
        }

        TcpClient? client = null;
        try
        {
            client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            _listener.Stop();
            var stream = client.GetStream();
            try
            {
                var callbackUri = await ReadAndValidateRequestAsync(stream, cancellationToken).ConfigureAwait(false);
                return new TcpNativeAuthLoopbackRequest(client, stream, callbackUri);
            }
            catch
            {
                await TryWriteRejectionAsync(stream, cancellationToken).ConfigureAwait(false);
                await stream.DisposeAsync().ConfigureAwait(false);
                client.Dispose();
                client = null;
                throw;
            }
        }
        catch
        {
            client?.Dispose();
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _listener.Stop();
        }

        return ValueTask.CompletedTask;
    }

    private async Task<Uri> ReadAndValidateRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaximumHeaderBytes];
        var count = 0;
        var headerEnd = -1;

        while (count < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(count, buffer.Length - count), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            count += read;
            headerEnd = FindHeaderEnd(buffer.AsSpan(0, count));
            if (headerEnd >= 0)
            {
                break;
            }
        }

        if (headerEnd < 0)
        {
            throw new InvalidDataException("Native-auth loopback request headers were incomplete or exceeded the allowed size.");
        }

        for (var index = 0; index < headerEnd; index++)
        {
            if (buffer[index] is 0 or > 127)
            {
                throw new InvalidDataException("Native-auth loopback request headers must be ASCII text.");
            }
        }

        var headerText = Encoding.ASCII.GetString(buffer, 0, headerEnd);
        var lines = headerText.Split("\r\n", StringSplitOptions.None);
        if (lines.Length == 0)
        {
            throw new InvalidDataException("Native-auth loopback request line is missing.");
        }

        var requestLine = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requestLine.Length != 3 ||
            !string.Equals(requestLine[0], "GET", StringComparison.Ordinal) ||
            requestLine[2] is not ("HTTP/1.1" or "HTTP/1.0"))
        {
            throw new InvalidDataException("Native-auth loopback accepts only a bounded HTTP GET callback.");
        }

        var target = requestLine[1];
        if (!target.StartsWith('/', StringComparison.Ordinal) ||
            target.StartsWith("//", StringComparison.Ordinal) ||
            target.Contains('#', StringComparison.Ordinal) ||
            target.Any(static character => char.IsControl(character) || char.IsWhiteSpace(character)))
        {
            throw new InvalidDataException("Native-auth loopback request target is invalid.");
        }

        string? host = null;
        for (var index = 1; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.Length == 0)
            {
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                throw new InvalidDataException("Native-auth loopback request contains a malformed HTTP header.");
            }

            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (string.Equals(name, "Host", StringComparison.OrdinalIgnoreCase))
            {
                if (host is not null)
                {
                    throw new InvalidDataException("Native-auth loopback request contains duplicate Host headers.");
                }

                host = value;
            }
        }

        var expectedHost = $"127.0.0.1:{RedirectUri.Port}";
        if (!string.Equals(host, expectedHost, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Native-auth loopback Host header does not target the active listener.");
        }

        if (!Uri.TryCreate($"http://{expectedHost}{target}", UriKind.Absolute, out var callbackUri))
        {
            throw new InvalidDataException("Native-auth loopback callback URI could not be parsed.");
        }

        return callbackUri;
    }

    private static int FindHeaderEnd(ReadOnlySpan<byte> buffer)
    {
        for (var index = 3; index < buffer.Length; index++)
        {
            if (buffer[index - 3] == '\r' &&
                buffer[index - 2] == '\n' &&
                buffer[index - 1] == '\r' &&
                buffer[index] == '\n')
            {
                return index + 1;
            }
        }

        return -1;
    }

    private static async Task TryWriteRejectionAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        try
        {
            await TcpNativeAuthLoopbackRequest.WriteResponseAsync(
                stream,
                protocolAccepted: false,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            // The callback was already rejected; failure to render the local browser message is non-authoritative.
        }
    }
}

internal sealed class TcpNativeAuthLoopbackRequest(
    TcpClient client,
    NetworkStream stream,
    Uri callbackUri) : INativeAuthLoopbackRequest
{
    private int _responded;
    private int _disposed;

    public Uri CallbackUri { get; } = callbackUri;

    public async Task RespondAsync(bool protocolAccepted, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _responded, 1) != 0)
        {
            return;
        }

        await WriteResponseAsync(stream, protocolAccepted, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await stream.DisposeAsync().ConfigureAwait(false);
        client.Dispose();
    }

    internal static async Task WriteResponseAsync(
        NetworkStream stream,
        bool protocolAccepted,
        CancellationToken cancellationToken)
    {
        var bodyText = protocolAccepted
            ? "Authentication result received. You can return to SplitOS."
            : "Authentication callback rejected. You can return to SplitOS.";
        var body = Encoding.UTF8.GetBytes(bodyText);
        var status = protocolAccepted ? "200 OK" : "400 Bad Request";
        var headers = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\n" +
            "Content-Type: text/plain; charset=utf-8\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Cache-Control: no-store\r\n" +
            "Pragma: no-cache\r\n" +
            "Referrer-Policy: no-referrer\r\n" +
            "X-Content-Type-Options: nosniff\r\n" +
            "Connection: close\r\n\r\n");

        await stream.WriteAsync(headers, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
