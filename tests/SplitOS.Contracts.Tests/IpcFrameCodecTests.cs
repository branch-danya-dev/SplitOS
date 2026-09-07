using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Contracts.Protocol;
using SplitOS.Ipc;

namespace SplitOS.Contracts.Tests;

[TestClass]
public sealed class IpcFrameCodecTests
{
    [TestMethod]
    public async Task FrameRoundTripPreservesMessageAndPayload()
    {
        var original = WireMessage.Create(
            MessageTypes.HealthReadRequest,
            new HealthReadRequest(),
            Capabilities.BrokerHealthRead);

        await using var stream = new MemoryStream();
        await IpcFrameCodec.WriteAsync(stream, original);
        stream.Position = 0;

        var restored = await IpcFrameCodec.ReadAsync(stream);

        Assert.AreEqual(original.MessageType, restored.MessageType);
        Assert.AreEqual(original.RequestId, restored.RequestId);
        Assert.AreEqual(original.OperationId, restored.OperationId);
        Assert.AreEqual(original.CorrelationId, restored.CorrelationId);
        Assert.AreEqual(original.Capability, restored.Capability);
        _ = restored.ReadPayload<HealthReadRequest>();
    }

    [TestMethod]
    public async Task OversizedFrameIsRejectedBeforeWrite()
    {
        var message = WireMessage.Create("Oversized", new { Data = new string('x', ProtocolConstants.MaxFrameBytes) });
        await using var stream = new MemoryStream();

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => IpcFrameCodec.WriteAsync(stream, message).AsTask());
    }
}
