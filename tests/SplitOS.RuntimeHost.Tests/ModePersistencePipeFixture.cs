using SplitOS.Broker.Service;
using SplitOS.Contracts.Protocol;
using SplitOS.Ipc;

namespace SplitOS.RuntimeHost.Tests;

// Disposable transport fixture: real named-pipe framing and Broker dispatch, but not a
// LocalSystem installation or proof of release-path/physical-console authorization.
internal sealed class ModePersistencePipeFixture(BrokerMessageHandler handler)
{
    public async Task<WireMessage> SendAsync(WireMessage request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var pipeName = "SplitOS.ModePersistence.Tests." + Guid.NewGuid().ToString("N");
        await using var server = NamedPipeRpcServer.Create(pipeName, 1);
        var serving = ServeAsync();
        try
        {
            var client = new NamedPipeRpcClient(pipeName, "SplitOS.RuntimeHost", "test");
            return await client.SendAsync(request, timeout.Token);
        }
        finally
        {
            await timeout.CancelAsync();
            try { await serving; }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
        }

        async Task ServeAsync()
        {
            await server.WaitForConnectionAsync(timeout.Token);
            await NamedPipeRpcServer.HandleConnectionAsync(server, "SplitOS.Broker.Service", "test",
                (_, _, _) => ValueTask.FromResult(HandshakeDecision.Allow()), handler.HandleAsync, timeout.Token);
        }
    }
}
