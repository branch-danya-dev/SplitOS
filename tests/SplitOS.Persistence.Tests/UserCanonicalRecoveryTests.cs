using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.User;

namespace SplitOS.Persistence.Tests;

[TestClass]
public sealed class UserCanonicalRecoveryTests
{
    [TestMethod]
    public async Task CorruptCanonicalRestoresVerifiedBackupAndPreservesForensicCopy()
    {
        using var storage = new TestStorage();
        var db = storage.PathFor("user.db");
        var backups = storage.PathFor("backups");
        await CreateLegacyV1StoreAsync(db);

        var initial = new UserStateStore(db, backups);
        await initial.InitializeAsync();
        SqliteConnection.ClearAllPools();

        await File.WriteAllBytesAsync(db, Encoding.UTF8.GetBytes("not-a-sqlite-database"));

        var recovered = new UserStateStore(db, backups);
        await recovered.InitializeAsync();

        var association = await recovered.GetAccountAssociationAsync();
        Assert.IsNotNull(association);
        Assert.AreEqual("acc_legacy", association.AccountId);
        Assert.AreEqual("REAUTH_REQUIRED", association.AssociationState);
        Assert.IsTrue(File.Exists(storage.PathFor("user-store.initialized")));
        Assert.IsFalse(File.Exists(storage.PathFor("user-store.quarantined.json")));

        var incidents = Directory.GetDirectories(storage.PathFor("quarantine"));
        Assert.AreEqual(1, incidents.Length);
        Assert.IsTrue(File.Exists(Path.Combine(incidents[0], "user.db")));
        Assert.IsTrue(File.Exists(Path.Combine(incidents[0], "quarantine-manifest.json")));
    }

    [TestMethod]
    public async Task MissingCanonicalAfterBootstrapRestoresLatestVerifiedBackup()
    {
        using var storage = new TestStorage();
        var db = storage.PathFor("user.db");
        var backups = storage.PathFor("backups");
        await CreateLegacyV1StoreAsync(db);

        var initial = new UserStateStore(db, backups);
        await initial.InitializeAsync();
        SqliteConnection.ClearAllPools();
        DeleteSqliteFiles(db);

        var recovered = new UserStateStore(db, backups);
        await recovered.InitializeAsync();

        var association = await recovered.GetAccountAssociationAsync();
        Assert.IsNotNull(association);
        Assert.AreEqual("acc_legacy", association.AccountId);
        Assert.AreEqual("REAUTH_REQUIRED", association.AssociationState);
    }

    [TestMethod]
    public async Task MissingCanonicalWithoutBackupFailsClosedAndDoesNotFabricateUnassociatedState()
    {
        using var storage = new TestStorage();
        var db = storage.PathFor("user.db");
        var store = new UserStateStore(db);
        await store.InitializeAsync();
        SqliteConnection.ClearAllPools();
        DeleteSqliteFiles(db);

        var restarted = new UserStateStore(db);
        await AssertThrowsAsync<InvalidDataException>(() => restarted.InitializeAsync());

        Assert.IsFalse(File.Exists(db));
        Assert.IsTrue(File.Exists(storage.PathFor("user-store.initialized")));
    }

    [TestMethod]
    public async Task CorruptCanonicalWithoutValidBackupQuarantinesAndBlocksSubsequentInitialization()
    {
        using var storage = new TestStorage();
        var db = storage.PathFor("user.db");
        var backups = storage.PathFor("backups");
        var store = new UserStateStore(db, backups);
        await store.InitializeAsync();
        SqliteConnection.ClearAllPools();

        Directory.CreateDirectory(backups);
        await File.WriteAllTextAsync(Path.Combine(backups, "bogus.db"), "not a usable backup");
        await File.WriteAllBytesAsync(db, Encoding.UTF8.GetBytes("corrupt-canonical"));

        var firstRestart = new UserStateStore(db, backups);
        await AssertThrowsAsync<InvalidDataException>(() => firstRestart.InitializeAsync());

        Assert.IsTrue(File.Exists(storage.PathFor("user-store.quarantined.json")));
        var incidents = Directory.GetDirectories(storage.PathFor("quarantine"));
        Assert.AreEqual(1, incidents.Length);
        Assert.IsTrue(File.Exists(Path.Combine(incidents[0], "user.db")));

        var secondRestart = new UserStateStore(db, backups);
        await AssertThrowsAsync<InvalidDataException>(() => secondRestart.InitializeAsync());
    }

    private static void DeleteSqliteFiles(string databasePath)
    {
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var path = databasePath + suffix;
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static async Task CreateLegacyV1StoreAsync(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        await using var connection = await OpenUnpooledAsync(path);
        var now = DateTimeOffset.UtcNow.ToString("O");
        var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = FULL;
            CREATE TABLE schema_metadata (
                component_key TEXT PRIMARY KEY,
                schema_version INTEGER NOT NULL,
                created_utc TEXT NOT NULL,
                last_migrated_utc TEXT NOT NULL,
                release_id TEXT NULL
            );
            CREATE TABLE account_association (
                singleton_id INTEGER PRIMARY KEY CHECK(singleton_id = 1),
                windows_user_sid TEXT NOT NULL,
                splitos_account_id TEXT NULL,
                association_state TEXT NOT NULL,
                associated_utc TEXT NULL,
                last_validated_utc TEXT NULL,
                secret_reference TEXT NULL,
                revision INTEGER NOT NULL CHECK(revision >= 1),
                updated_utc TEXT NOT NULL
            );
            INSERT INTO schema_metadata(component_key, schema_version, created_utc, last_migrated_utc, release_id)
            VALUES('user', 1, $now, $now, 'legacy-v1');
            INSERT INTO account_association(
                singleton_id, windows_user_sid, splitos_account_id, association_state,
                associated_utc, last_validated_utc, secret_reference, revision, updated_utc)
            VALUES(1, 'S-1-5-21-test', 'acc_legacy', 'ACTIVE', $now, $now, 'legacy-secret', 7, $now);
            PRAGMA user_version = 1;
            """;
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync();
        await connection.CloseAsync();
    }

    private static async Task<SqliteConnection> OpenUnpooledAsync(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ConnectionString);
        await connection.OpenAsync();
        return connection;
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
