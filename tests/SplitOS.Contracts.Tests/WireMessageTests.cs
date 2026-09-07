using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Contracts.Protocol;

namespace SplitOS.Contracts.Tests;

[TestClass]
public sealed class WireMessageTests
{
    [TestMethod]
    public void ResponsePreservesRequestOperationAndCorrelationIdentity()
    {
        var request = WireMessage.Create(
            MessageTypes.HealthReadRequest,
            new HealthReadRequest(),
            Capabilities.BrokerHealthRead);

        var response = WireMessage.Respond(
            request,
            MessageTypes.HealthReadResult,
            new HealthReadResult("test", "1.0", "HEALTHY", 1, 1, DateTimeOffset.UtcNow));

        Assert.AreEqual(request.RequestId, response.RequestId);
        Assert.AreEqual(request.OperationId, response.OperationId);
        Assert.AreEqual(request.CorrelationId, response.CorrelationId);
        Assert.AreEqual(request.Capability, response.Capability);
    }
}
