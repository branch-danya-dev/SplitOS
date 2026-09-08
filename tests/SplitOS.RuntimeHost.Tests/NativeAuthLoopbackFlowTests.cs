using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.Authentication;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class NativeAuthLoopbackFlowTests
{
    [TestMethod]
    public async Task TcpListenerBindsIpv4EphemeralPortAndReturnsSecretFreeBrowserResponse()
    {
        await using var listener = TcpNativeAuthLoopbackListener.Bind();
        Assert.AreEqual("http", listener.RedirectUri.Scheme);
        Assert.AreEqual("127.0.0.1", listener.RedirectUri.Host);
        Assert.AreEqual("/oauth/callback", listener.RedirectUri.AbsolutePath);
        Assert.IsTrue(listener.RedirectUri.Port > 1024);

        var receiveTask = listener.ReceiveAsync();
        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, listener.RedirectUri.Port);
        var secretCode = "authorization-code-must-not-be-rendered";
        var secretState = "state-must-not-be-rendered";
        var requestText =
            $"GET /oauth/callback?code={secretCode}&state={secretState} HTTP/1.1\r\n" +
            $"Host: 127.0.0.1:{listener.RedirectUri.Port}\r\n" +
            "Connection: close\r\n\r\n";
        var requestBytes = Encoding.ASCII.GetBytes(requestText);
        await client.GetStream().WriteAsync(requestBytes);

        await using (var callback = await receiveTask)
        {
            Assert.AreEqual(secretCode, GetQueryValue(callback.CallbackUri, "code"));
            Assert.AreEqual(secretState, GetQueryValue(callback.CallbackUri, "state"));
            await callback.RespondAsync(protocolAccepted: true);
        }

        using var reader = new StreamReader(client.GetStream(), Encoding.UTF8, leaveOpen: true);
        var response = await reader.ReadToEndAsync();
        Assert.IsTrue(response.StartsWith("HTTP/1.1 200 OK", StringComparison.Ordinal));
        Assert.IsTrue(response.Contains("Cache-Control: no-store", StringComparison.Ordinal));
        Assert.IsFalse(response.Contains(secretCode, StringComparison.Ordinal));
        Assert.IsFalse(response.Contains(secretState, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task TcpListenerRejectsWrongHostAndDoesNotProduceCallback()
    {
        await using var listener = TcpNativeAuthLoopbackListener.Bind();
        var receiveTask = listener.ReceiveAsync();
        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, listener.RedirectUri.Port);
        var bytes = Encoding.ASCII.GetBytes(
            "GET /oauth/callback?code=x&state=y HTTP/1.1\r\n" +
            "Host: attacker.invalid\r\nConnection: close\r\n\r\n");
        await client.GetStream().WriteAsync(bytes);

        await AssertThrowsAsync<InvalidDataException>(async () => await receiveTask);
        using var reader = new StreamReader(client.GetStream(), Encoding.UTF8, leaveOpen: true);
        var response = await reader.ReadToEndAsync();
        Assert.IsTrue(response.StartsWith("HTTP/1.1 400 Bad Request", StringComparison.Ordinal));
        Assert.IsFalse(response.Contains("code=x", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task InteractiveFlowBindsBeforeBrowserLaunchAndConsumesValidatedCallback()
    {
        var listener = new FakeLoopbackListener(new Uri("http://127.0.0.1:49160/oauth/callback"));
        var factory = new FakeLoopbackListenerFactory(listener);
        var browser = new CallbackDeliveringBrowserLauncher(listener);
        var manager = CreateManager(TimeSpan.FromMinutes(10));
        var flow = new NativeAuthInteractiveFlow(manager, factory, browser);

        var result = await flow.RunAsync();

        Assert.AreEqual(NativeAuthInteractiveDisposition.CallbackReceived, result.Disposition);
        Assert.AreEqual("AUTH_CODE_ACCEPTED", result.ProductCode);
        Assert.IsNotNull(result.CallbackResult?.ExchangeContext);
        Assert.AreEqual("one-time-code", result.CallbackResult.ExchangeContext.AuthorizationCode);
        Assert.IsTrue(browser.LaunchedAfterListenerWasBound);
        Assert.IsTrue(listener.ResponseAccepted);
        Assert.IsNull(manager.GetActiveSnapshot());
    }

    [TestMethod]
    public async Task BrowserLaunchFailureCancelsTransactionWithoutFabricatingLogin()
    {
        var listener = new FakeLoopbackListener(new Uri("http://127.0.0.1:49161/oauth/callback"));
        var manager = CreateManager(TimeSpan.FromMinutes(10));
        var flow = new NativeAuthInteractiveFlow(
            manager,
            new FakeLoopbackListenerFactory(listener),
            new ThrowingBrowserLauncher());

        var result = await flow.RunAsync();

        Assert.AreEqual(NativeAuthInteractiveDisposition.BrowserLaunchFailed, result.Disposition);
        Assert.AreEqual("AUTH_BROWSER_LAUNCH_FAILED", result.ProductCode);
        Assert.IsNull(manager.GetActiveSnapshot());
        Assert.AreEqual(0, listener.ReceiveCount);
    }

    [TestMethod]
    public async Task FlowTimeoutCancelsActiveTransactionAndListenerWait()
    {
        var listener = new FakeLoopbackListener(new Uri("http://127.0.0.1:49162/oauth/callback"));
        var manager = CreateManager(TimeSpan.FromMilliseconds(40));
        var flow = new NativeAuthInteractiveFlow(
            manager,
            new FakeLoopbackListenerFactory(listener),
            new RecordingBrowserLauncher());

        var result = await flow.RunAsync();

        Assert.AreEqual(NativeAuthInteractiveDisposition.TimedOut, result.Disposition);
        Assert.AreEqual("AUTH_TIMEOUT", result.ProductCode);
        Assert.IsNull(manager.GetActiveSnapshot());
        Assert.IsTrue(listener.ReceiveWasCancelled);
    }

    [TestMethod]
    public async Task LoopbackTransportFailureClearsActiveTransaction()
    {
        var listener = new FailingLoopbackListener(new Uri("http://127.0.0.1:49163/oauth/callback"));
        var manager = CreateManager(TimeSpan.FromMinutes(10));
        var flow = new NativeAuthInteractiveFlow(
            manager,
            new SingleListenerFactory(listener),
            new RecordingBrowserLauncher());

        var result = await flow.RunAsync();

        Assert.AreEqual(NativeAuthInteractiveDisposition.LoopbackRejected, result.Disposition);
        Assert.AreEqual("AUTH_RESULT_REJECTED", result.ProductCode);
        Assert.IsNull(manager.GetActiveSnapshot());
    }

    private static NativeAuthTransactionManager CreateManager(TimeSpan lifetime)
        => new(
            new NativeAuthAuthorityConfiguration(
                new Uri("https://auth.example.test/"),
                new Uri("https://auth.example.test/.well-known/openid-configuration"),
                new Uri("https://auth.example.test/authorize"),
                new Uri("https://auth.example.test/token"),
                new Uri("https://auth.example.test/jwks"),
                "splitos-windows-native-v1",
                ["openid", "profile", "email"],
                ["RS256"],
                TimeSpan.FromMinutes(1),
                lifetime),
            new FakeWindowsUserContext());

    private static string? GetQueryValue(Uri uri, string key)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var candidate = WebUtility.UrlDecode(separator >= 0 ? pair[..separator] : pair);
            if (string.Equals(candidate, key, StringComparison.Ordinal))
            {
                return WebUtility.UrlDecode(separator >= 0 ? pair[(separator + 1)..] : string.Empty);
            }
        }

        return null;
    }

    private static async Task<TException> AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException exception)
        {
            return exception;
        }
        catch (Exception exception)
        {
            Assert.Fail($"Expected {typeof(TException).Name}, got {exception.GetType().Name}.");
            throw;
        }

        Assert.Fail($"Expected {typeof(TException).Name}, but no exception was thrown.");
        throw new InvalidOperationException("Unreachable after failed assertion.");
    }

    private sealed class FakeWindowsUserContext : IWindowsUserContext
    {
        public string GetCurrentUserSid() => "S-1-5-21-native-auth-test";
    }

    private sealed class FakeLoopbackListenerFactory(FakeLoopbackListener listener) : INativeAuthLoopbackListenerFactory
    {
        public INativeAuthLoopbackListener Bind()
        {
            listener.WasBound = true;
            return listener;
        }
    }

    private sealed class SingleListenerFactory(INativeAuthLoopbackListener listener) : INativeAuthLoopbackListenerFactory
    {
        public INativeAuthLoopbackListener Bind() => listener;
    }

    private sealed class FakeLoopbackListener(Uri redirectUri) : INativeAuthLoopbackListener
    {
        private readonly TaskCompletionSource<INativeAuthLoopbackRequest> _callback =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Uri RedirectUri { get; } = redirectUri;
        public bool WasBound { get; set; }
        public int ReceiveCount { get; private set; }
        public bool ReceiveWasCancelled { get; private set; }
        public bool ResponseAccepted { get; private set; }

        public async Task<INativeAuthLoopbackRequest> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            ReceiveCount++;
            try
            {
                return await _callback.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ReceiveWasCancelled = true;
                throw;
            }
        }

        public void Deliver(Uri callbackUri)
            => _callback.TrySetResult(new FakeLoopbackRequest(callbackUri, accepted => ResponseAccepted = accepted));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FailingLoopbackListener(Uri redirectUri) : INativeAuthLoopbackListener
    {
        public Uri RedirectUri { get; } = redirectUri;
        public Task<INativeAuthLoopbackRequest> ReceiveAsync(CancellationToken cancellationToken = default)
            => Task.FromException<INativeAuthLoopbackRequest>(new IOException("Synthetic loopback transport failure."));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeLoopbackRequest(Uri callbackUri, Action<bool> onResponse) : INativeAuthLoopbackRequest
    {
        public Uri CallbackUri { get; } = callbackUri;
        public Task RespondAsync(bool protocolAccepted, CancellationToken cancellationToken = default)
        {
            onResponse(protocolAccepted);
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CallbackDeliveringBrowserLauncher(FakeLoopbackListener listener) : INativeAuthBrowserLauncher
    {
        public bool LaunchedAfterListenerWasBound { get; private set; }

        public void Launch(Uri authorizationUri)
        {
            LaunchedAfterListenerWasBound = listener.WasBound;
            var state = GetQueryValue(authorizationUri, "state")
                ?? throw new InvalidDataException("Authorization URI state was missing in test.");
            listener.Deliver(new Uri(
                $"{listener.RedirectUri}?code=one-time-code&state={Uri.EscapeDataString(state)}"));
        }
    }

    private sealed class RecordingBrowserLauncher : INativeAuthBrowserLauncher
    {
        public void Launch(Uri authorizationUri) { }
    }

    private sealed class ThrowingBrowserLauncher : INativeAuthBrowserLauncher
    {
        public void Launch(Uri authorizationUri)
            => throw new InvalidOperationException("Synthetic browser launch failure.");
    }
}
