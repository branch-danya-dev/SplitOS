using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Broker.Service;
using SplitOS.Contracts.Protocol;
using SplitOS.Persistence.Machine;

namespace SplitOS.Broker.Tests;

[TestClass]
public sealed class BrokerMessageHandlerTests
{
    private string? _root;

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (_root is not null && Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public async Task UnknownCapabilityFailsClosed()
    {
        var handler = await CreateHandlerAsync();
        var request = WireMessage.Create(MessageTypes.HealthReadRequest, new HealthReadRequest(), "RunCommand");
        var response = await handler.HandleAsync(request, CancellationToken.None);
        var error = response.ReadPayload<ErrorResponse>();
        Assert.AreEqual(MessageTypes.ErrorResponse, response.MessageType);
        Assert.AreEqual(ErrorCodes.UnknownCapability, error.Code);
    }

    [TestMethod]
    public async Task HealthReadIsAllowlisted()
    {
        var handler = await CreateHandlerAsync();
        var request = WireMessage.Create(MessageTypes.HealthReadRequest, new HealthReadRequest(), Capabilities.BrokerHealthRead);
        var response = await handler.HandleAsync(request, CancellationToken.None);
        var health = response.ReadPayload<HealthReadResult>();
        Assert.AreEqual(MessageTypes.HealthReadResult, response.MessageType);
        Assert.AreEqual("HEALTHY", health.Status);
        Assert.AreEqual("SplitOS.Broker.Service", health.Component);
    }

    [TestMethod]
    public async Task MachineStateReadReturnsBootstrapNone()
    {
        var handler = await CreateHandlerAsync();
        var request = WireMessage.Create(
            MessageTypes.MachineStateReadRequest,
            new MachineStateReadRequest("OPERATIONAL_MODE", "singleton"),
            Capabilities.MachineStateStoreRead);
        var response = await handler.HandleAsync(request, CancellationToken.None);
        var result = response.ReadPayload<MachineStateReadResult>();
        StringAssert.Contains(result.PayloadJson, "\"CommittedMode\":\"NONE\"");
        Assert.AreEqual(MachineStateStore.SchemaVersion, result.SchemaVersion);
        Assert.AreEqual(1, result.Revision);
    }

    private async Task<BrokerMessageHandler> CreateHandlerAsync()
    {
        _root = Path.Combine(Path.GetTempPath(), "SplitOS.Broker.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var store = new MachineStateStore(Path.Combine(_root, "machine.db"), Path.Combine(_root, "marker"));
        await store.InitializeAsync();
        return new BrokerMessageHandler(store);
    }
}
