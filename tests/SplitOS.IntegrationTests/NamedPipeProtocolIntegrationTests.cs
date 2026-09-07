using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Contracts.Protocol;
using SplitOS.Ipc;

namespace SplitOS.IntegrationTests;

[TestClass]
public sealed class NamedPipeProtocolIntegrationTests
{
    [TestMethod]
    public async Task HelloThenHealthRequestRoundTripsOverRealNamedPipe()
    {
        var pipeName = $"SplitOS.Test.{Guid.NewGuid():N}";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var serverTask = Task.Run(async () =>
        {
            await using var server = NamedPipeRpcServer.Create(pipeName, 1);
            await server.WaitForConnectionAsync(timeout.Token);
            await NamedPipeRpcServer.HandleConnectionAsync(
                server,
                "SplitOS.TestServer",
                "1.0.0",
                (_, _, _) => ValueTask.FromResult(HandshakeDecision.Allow()),
                (request, _) => ValueTask.FromResult(WireMessage.Respond(
                    request,
                    MessageTypes.HealthReadResult,
                    new HealthReadResult("SplitOS.TestServer", "1.0.0", "HEALTHY", Environment.ProcessId, 0, DateTimeOffset.UtcNow))),
                timeout.Token);
        }, timeout.Token);

        var client = new NamedPipeRpcClient(pipeName, "SplitOS.IntegrationTests", "1.0.0");
        var request = WireMessage.Create(
            MessageTypes.HealthReadRequest,
            new HealthReadRequest(),
            Capabilities.RuntimeHealthRead);

        var response = await client.SendAsync(request, timeout.Token);
        var health = response.ReadPayload<HealthReadResult>();

        Assert.AreEqual("HEALTHY", health.Status);
        Assert.AreEqual(request.RequestId, response.RequestId);
        Assert.AreEqual(request.OperationId, response.OperationId);
        Assert.AreEqual(request.CorrelationId, response.CorrelationId);

        await serverTask;
    }
}
