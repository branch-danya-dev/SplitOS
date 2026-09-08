using System.Diagnostics;
using System.Net.Sockets;

namespace SplitOS.RuntimeHost.Authentication;

public interface INativeAuthBrowserLauncher
{
    void Launch(Uri authorizationUri);
}

public sealed class SystemNativeAuthBrowserLauncher : INativeAuthBrowserLauncher
{
    public void Launch(Uri authorizationUri)
    {
        ArgumentNullException.ThrowIfNull(authorizationUri);
        if (!authorizationUri.IsAbsoluteUri ||
            !string.Equals(authorizationUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Native authentication browser target must be an absolute HTTPS URI.", nameof(authorizationUri));
        }

        using var process = Process.Start(new ProcessStartInfo(authorizationUri.AbsoluteUri)
        {
            UseShellExecute = true
        });

        if (process is null)
        {
            throw new InvalidOperationException("Windows did not accept the system-browser launch request.");
        }
    }
}

public enum NativeAuthInteractiveDisposition
{
    CallbackReceived,
    AlreadyInProgress,
    TimedOut,
    Cancelled,
    BrowserLaunchFailed,
    LoopbackRejected
}

public sealed record NativeAuthInteractiveResult(
    NativeAuthInteractiveDisposition Disposition,
    string ProductCode,
    Guid? AuthTransactionId,
    NativeAuthCallbackResult? CallbackResult);

public sealed class NativeAuthInteractiveFlow(
    NativeAuthTransactionManager transactionManager,
    INativeAuthLoopbackListenerFactory listenerFactory,
    INativeAuthBrowserLauncher browserLauncher,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private int _running;

    public async Task<NativeAuthInteractiveResult> RunAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            var active = transactionManager.GetActiveSnapshot();
            return new NativeAuthInteractiveResult(
                NativeAuthInteractiveDisposition.AlreadyInProgress,
                "AUTH_ALREADY_IN_PROGRESS",
                active?.AuthTransactionId,
                null);
        }

        Guid? transactionId = null;
        try
        {
            await using var listener = listenerFactory.Bind();
            var start = transactionManager.Start(listener.RedirectUri);
            transactionId = start.Transaction.AuthTransactionId;
            if (start.Disposition == NativeAuthStartDisposition.AlreadyInProgress)
            {
                return new NativeAuthInteractiveResult(
                    NativeAuthInteractiveDisposition.AlreadyInProgress,
                    "AUTH_ALREADY_IN_PROGRESS",
                    transactionId,
                    null);
            }

            try
            {
                browserLauncher.Launch(start.Transaction.AuthorizationUri);
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
            {
                transactionManager.Cancel(transactionId.Value);
                return new NativeAuthInteractiveResult(
                    NativeAuthInteractiveDisposition.BrowserLaunchFailed,
                    "AUTH_BROWSER_LAUNCH_FAILED",
                    transactionId,
                    null);
            }

            var remaining = start.Transaction.ExpiresUtc - _timeProvider.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                transactionManager.Cancel(transactionId.Value);
                return new NativeAuthInteractiveResult(
                    NativeAuthInteractiveDisposition.TimedOut,
                    "AUTH_TIMEOUT",
                    transactionId,
                    null);
            }

            using var receiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var receiveTask = listener.ReceiveAsync(receiveCancellation.Token);
            var timeoutTask = Task.Delay(remaining, _timeProvider, CancellationToken.None);
            var completed = await Task.WhenAny(receiveTask, timeoutTask).ConfigureAwait(false);

            if (completed == timeoutTask)
            {
                receiveCancellation.Cancel();
                await ObserveCancelledReceiveAsync(receiveTask).ConfigureAwait(false);
                transactionManager.Cancel(transactionId.Value);
                return new NativeAuthInteractiveResult(
                    NativeAuthInteractiveDisposition.TimedOut,
                    "AUTH_TIMEOUT",
                    transactionId,
                    null);
            }

            INativeAuthLoopbackRequest request;
            try
            {
                request = await receiveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                transactionManager.Cancel(transactionId.Value);
                return new NativeAuthInteractiveResult(
                    NativeAuthInteractiveDisposition.Cancelled,
                    "AUTH_CANCELLED",
                    transactionId,
                    null);
            }
            catch (InvalidDataException)
            {
                transactionManager.Cancel(transactionId.Value);
                return new NativeAuthInteractiveResult(
                    NativeAuthInteractiveDisposition.LoopbackRejected,
                    "AUTH_RESULT_REJECTED",
                    transactionId,
                    null);
            }
            catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
            {
                transactionManager.Cancel(transactionId.Value);
                return new NativeAuthInteractiveResult(
                    NativeAuthInteractiveDisposition.LoopbackRejected,
                    "AUTH_RESULT_REJECTED",
                    transactionId,
                    null);
            }

            await using (request)
            {
                var callback = transactionManager.ConsumeCallback(request.CallbackUri);
                var protocolAccepted = callback.Disposition is
                    NativeAuthCallbackDisposition.Accepted or
                    NativeAuthCallbackDisposition.Cancelled or
                    NativeAuthCallbackDisposition.LoginRequired or
                    NativeAuthCallbackDisposition.ServerError;
                await request.RespondAsync(protocolAccepted, CancellationToken.None).ConfigureAwait(false);

                return new NativeAuthInteractiveResult(
                    NativeAuthInteractiveDisposition.CallbackReceived,
                    callback.ProductCode,
                    transactionId,
                    callback);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    private static async Task ObserveCancelledReceiveAsync(Task<INativeAuthLoopbackRequest> receiveTask)
    {
        try
        {
            var request = await receiveTask.ConfigureAwait(false);
            await request.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException)
        {
            // Expected when timeout cancels a listener blocked in Accept/Read.
        }
    }
}
