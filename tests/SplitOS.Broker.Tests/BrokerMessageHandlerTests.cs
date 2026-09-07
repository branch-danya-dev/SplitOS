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

    [TestMethod]
    public async Task FreeRuntimeCanConvergeNoneAndReplaySameOperation()
    {
        var handler = await CreateHandlerAsync();
        var operationId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var request = WireMessage.Create(
            MessageTypes.MachineOperationalModeWriteRequest,
            new MachineOperationalModeWriteRequest("NONE", 1),
            Capabilities.MachineOperationalModeWrite,
            operationId,
            correlationId);

        var first = await handler.HandleAsync(request, CancellationToken.None);
        var applied = first.ReadPayload<MachineOperationalModeWriteResult>();
        Assert.AreEqual(MessageTypes.MachineOperationalModeWriteResult, first.MessageType);
        Assert.AreEqual("APPLIED", applied.Disposition);
        Assert.AreEqual("NONE", applied.CommittedMode);
        Assert.AreEqual(2, applied.Revision);

        var replay = await handler.HandleAsync(request, CancellationToken.None);
        var replayed = replay.ReadPayload<MachineOperationalModeWriteResult>();
        Assert.AreEqual("REPLAYED", replayed.Disposition);
        Assert.AreEqual(2, replayed.Revision);
    }

    [TestMethod]
    public async Task FreeRuntimeCannotWriteManagedModes()
    {
        var handler = await CreateHandlerAsync();
        var request = WireMessage.Create(
            MessageTypes.MachineOperationalModeWriteRequest,
            new MachineOperationalModeWriteRequest("WORK", 1),
            Capabilities.MachineOperationalModeWrite);

        var response = await handler.HandleAsync(request, CancellationToken.None);
        var error = response.ReadPayload<ErrorResponse>();
        Assert.AreEqual(MessageTypes.ErrorResponse, response.MessageType);
        Assert.AreEqual(ErrorCodes.ManagedModeWriteNotAvailable, error.Code);
    }

    [TestMethod]
    public async Task StaleExpectedRevisionReturnsConflict()
    {
        var handler = await CreateHandlerAsync();
        var first = WireMessage.Create(
            MessageTypes.MachineOperationalModeWriteRequest,
            new MachineOperationalModeWriteRequest("NONE", 1),
            Capabilities.MachineOperationalModeWrite);
        await handler.HandleAsync(first, CancellationToken.None);

        var stale = WireMessage.Create(
            MessageTypes.MachineOperationalModeWriteRequest,
            new MachineOperationalModeWriteRequest("NONE", 1),
            Capabilities.MachineOperationalModeWrite);
        var response = await handler.HandleAsync(stale, CancellationToken.None);
        var error = response.ReadPayload<ErrorResponse>();
        Assert.AreEqual(ErrorCodes.PersistenceRevisionConflict, error.Code);
    }

    [TestMethod]
    public async Task OperationIdReuseWithDifferentCorrelationFailsClosed()
    {
        var handler = await CreateHandlerAsync();
        var operationId = Guid.NewGuid();
        var first = WireMessage.Create(
            MessageTypes.MachineOperationalModeWriteRequest,
            new MachineOperationalModeWriteRequest("NONE", 1),
            Capabilities.MachineOperationalModeWrite,
            operationId,
            Guid.NewGuid());
        await handler.HandleAsync(first, CancellationToken.None);

        var conflictingReplay = WireMessage.Create(
            MessageTypes.MachineOperationalModeWriteRequest,
            new MachineOperationalModeWriteRequest("NONE", 1),
            Capabilities.MachineOperationalModeWrite,
            operationId,
            Guid.NewGuid());
        var response = await handler.HandleAsync(conflictingReplay, CancellationToken.None);
        var error = response.ReadPayload<ErrorResponse>();
        Assert.AreEqual(ErrorCodes.IdempotencyConflict, error.Code);
    }

    private async Task<BrokerMessageHandler> CreateHandlerAsync()
    {
        _root = Path.Combine(Path.GetTempPath(), "SplitOS.Broker.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var store = new MachineStateStore(
            Path.Combine(_root, "machine.db"),
            Path.Combine(_root, "marker"),
            Path.Combine(_root, "maintenance", "backups"),
            Path.Combine(_root, "maintenance", "quarantine"),
            Path.Combine(_root, "quarantine-marker.json"));
        await store.InitializeAsync();
        return new BrokerMessageHandler(store);
    }
}
