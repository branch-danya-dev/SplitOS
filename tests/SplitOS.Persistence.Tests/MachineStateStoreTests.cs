using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.Machine;

namespace SplitOS.Persistence.Tests;

[TestClass]
public sealed class MachineStateStoreTests
{
    [TestMethod]
    public async Task FirstBootstrapCreatesDurableNone()
    {
        using var storage = new TestStorage();
        var db = storage.PathFor("machine.db");
        var marker = storage.PathFor("machine-store.initialized");

        var first = new MachineStateStore(db, marker);
        await first.InitializeAsync();
        var initial = await first.GetOperationalModeAsync();
        Assert.AreEqual("NONE", initial.CommittedMode);
        Assert.AreEqual(1, initial.Revision);

        SqliteConnection.ClearAllPools();
        var reopened = new MachineStateStore(db, marker);
        await reopened.InitializeAsync();
        var restored = await reopened.GetOperationalModeAsync();
        Assert.AreEqual("NONE", restored.CommittedMode);
        Assert.AreEqual(initial.Revision, restored.Revision);
    }

    [TestMethod]
    public async Task MissingCanonicalDatabaseAfterBootstrapDoesNotFabricateNone()
    {
        using var storage = new TestStorage();
        var db = storage.PathFor("machine.db");
        var marker = storage.PathFor("machine-store.initialized");
        var store = new MachineStateStore(db, marker);
        await store.InitializeAsync();
        SqliteConnection.ClearAllPools();
        DeleteSqliteFiles(db);

        var reopened = new MachineStateStore(db, marker);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => reopened.InitializeAsync());
    }

    private static void DeleteSqliteFiles(string databasePath)
    {
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var path = databasePath + suffix;
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
