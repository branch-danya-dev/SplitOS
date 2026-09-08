using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.User;

namespace SplitOS.Persistence.Tests;

[TestClass]
public sealed class UserCanonicalInvariantRecoveryTests
{
    [TestMethod]
    public async Task MissingCurrentCanonicalTableIsQuarantinedInsteadOfRecreated()
    {
        using var storage = new TestStorage();
        var db = storage.PathFor("user.db");
        var store = new UserStateStore(db);
        await store.InitializeAsync();
        SqliteConnection.ClearAllPools();

        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = db,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ConnectionString))
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE account_association;";
            await command.ExecuteNonQueryAsync();
        }
        SqliteConnection.ClearAllPools();

        var restarted = new UserStateStore(db);
        await AssertThrowsAsync<InvalidDataException>(() => restarted.InitializeAsync());

        Assert.IsTrue(File.Exists(storage.PathFor("user-store.quarantined.json")));
        await using var verify = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = db,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ConnectionString);
        await verify.OpenAsync();
        var exists = verify.CreateCommand();
        exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='account_association';";
        Assert.AreEqual(0, Convert.ToInt32(await exists.ExecuteScalarAsync()));
    }

    private static async Task<TException> AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException exception)
        {
            return exception;
        }
        catch (Exception exception)
        {
            Assert.Fail($"Expected {typeof(TException).Name}, got {exception.GetType().Name}.");
            throw;
        }

        Assert.Fail($"Expected {typeof(TException).Name}, but no exception was thrown.");
        throw new InvalidOperationException("Unreachable after failed assertion.");
    }
}
