using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.Projection;
using SplitOS.Persistence.User;

namespace SplitOS.Persistence.Tests;

[TestClass]
public sealed class UserAndProjectionStoreTests
{
    [TestMethod]
    public async Task NewUserStoreStartsUnassociated()
    {
        using var storage = new TestStorage();
        var store = new UserStateStore(storage.PathFor("user.db"));
        await store.InitializeAsync();
        Assert.AreEqual("UNASSOCIATED", await store.GetAssociationStateAsync());
    }

    [TestMethod]
    public async Task ProjectionStoreRebuildsIncompatibleCache()
    {
        using var storage = new TestStorage();
        var path = storage.PathFor("projection.db");
        var store = new ProjectionStore(path);
        await store.InitializeOrRebuildAsync();
        SqliteConnection.ClearAllPools();

        await using (var raw = new SqliteConnection($"Data Source={path}"))
        {
            await raw.OpenAsync();
            var command = raw.CreateCommand();
            command.CommandText = "PRAGMA user_version = 2;";
            await command.ExecuteNonQueryAsync();
        }
        SqliteConnection.ClearAllPools();

        await store.InitializeOrRebuildAsync();
        await using var verified = new SqliteConnection($"Data Source={path}");
        await verified.OpenAsync();
        var versionCommand = verified.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version;";
        Assert.AreEqual(ProjectionStore.SchemaVersion, Convert.ToInt32(await versionCommand.ExecuteScalarAsync()));
    }
}
