using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Broker.Service;
using SplitOS.Contracts.Protocol;

namespace SplitOS.Broker.Tests;

[TestClass]
public sealed class BrokerMessageHandlerTests
{
    [TestMethod]
    public async Task UnknownCapabilityFailsClosed()
    {
        var handler = new BrokerMessageHandler();
        var request = WireMessage.Create(
            MessageTypes.HealthReadRequest,
            new HealthReadRequest(),
            "RunCommand");

        var response = await handler.HandleAsync(request, CancellationToken.None);
        var error = response.ReadPayload<ErrorResponse>();

        Assert.AreEqual(MessageTypes.ErrorResponse, response.MessageType);
        Assert.AreEqual(ErrorCodes.UnknownCapability, error.Code);
    }

    [TestMethod]
    public async Task HealthReadIsAllowlisted()
    {
        var handler = new BrokerMessageHandler();
        var request = WireMessage.Create(
            MessageTypes.HealthReadRequest,
            new HealthReadRequest(),
            Capabilities.BrokerHealthRead);

        var response = await handler.HandleAsync(request, CancellationToken.None);
        var health = response.ReadPayload<HealthReadResult>();

        Assert.AreEqual(MessageTypes.HealthReadResult, response.MessageType);
        Assert.AreEqual("HEALTHY", health.Status);
        Assert.AreEqual("SplitOS.Broker.Service", health.Component);
    }
}
