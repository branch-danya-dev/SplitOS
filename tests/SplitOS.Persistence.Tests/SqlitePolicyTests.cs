using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SplitOS.Persistence.Tests;

[TestClass]
public sealed class SqlitePolicyTests
{
    [TestMethod]
    public async Task CanonicalStoreUsesWalAndFull()
    {
        using var storage = new TestStorage();
        var database = new SqliteDatabase(new SqliteDatabaseOptions(storage.PathFor("canonical.db"), SplitOSDatabaseRole.User, 1, "test"));
        await using var connection = await database.OpenAsync();
        await database.InitializeMetadataAsync(connection, "test");

        Assert.AreEqual("wal", Convert.ToString(await ScalarAsync(connection, "PRAGMA journal_mode;"))?.ToLowerInvariant());
        Assert.AreEqual(2, Convert.ToInt32(await ScalarAsync(connection, "PRAGMA synchronous;")));
        Assert.AreEqual(1, Convert.ToInt32(await ScalarAsync(connection, "PRAGMA foreign_keys;")));
    }

    [TestMethod]
    public async Task ProjectionStoreUsesWalAndNormal()
    {
        using var storage = new TestStorage();
        var database = new SqliteDatabase(new SqliteDatabaseOptions(storage.PathFor("projection.db"), SplitOSDatabaseRole.Projection, 1, "test"));
        await using var connection = await database.OpenAsync();
        await database.InitializeMetadataAsync(connection, "test");

        Assert.AreEqual("wal", Convert.ToString(await ScalarAsync(connection, "PRAGMA journal_mode;"))?.ToLowerInvariant());
        Assert.AreEqual(1, Convert.ToInt32(await ScalarAsync(connection, "PRAGMA synchronous;")));
    }

    [TestMethod]
    public async Task NewerPhysicalSchemaFailsClosed()
    {
        using var storage = new TestStorage();
        var path = storage.PathFor("newer.db");
        await using (var raw = new SqliteConnection($"Data Source={path}"))
        {
            await raw.OpenAsync();
            var command = raw.CreateCommand();
            command.CommandText = "PRAGMA user_version = 2;";
            await command.ExecuteNonQueryAsync();
        }
        SqliteConnection.ClearAllPools();

        var database = new SqliteDatabase(new SqliteDatabaseOptions(path, SplitOSDatabaseRole.User, 1, "test"));
        await using var connection = await database.OpenAsync();
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => database.InitializeMetadataAsync(connection, "test"));
    }

    private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }
}
