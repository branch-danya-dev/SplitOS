using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.User;

namespace SplitOS.Persistence.Tests;

[TestClass]
public sealed class UserAssociationStoreTests
{
    [TestMethod]
    public async Task LegacyV1AssociationMigratesOnlyAfterVerifiedBackupAndRequiresReauth()
    {
        using var storage = new TestStorage();
        var db = storage.PathFor("user.db");
        var backups = storage.PathFor("backups");
        await CreateLegacyV1StoreAsync(db);

        var store = new UserStateStore(db, backups);
        await store.InitializeAsync();

        var backupFiles = Directory.GetFiles(backups, "*.db", SearchOption.TopDirectoryOnly);
        Assert.AreEqual(1, backupFiles.Length);
        await using (var backup = await OpenUnpooledAsync(backupFiles[0]))
        {
            Assert.AreEqual(1, await ScalarIntAsync(backup, "PRAGMA user_version;"));
            Assert.AreEqual(7, await ScalarIntAsync(backup, "SELECT revision FROM account_association WHERE singleton_id = 1;"));
        }

        var association = await store.GetAccountAssociationAsync();
        Assert.IsNotNull(association);
        Assert.AreEqual("acc_legacy", association.AccountId);
        Assert.AreEqual("S-1-5-21-test", association.WindowsUserSid);
        Assert.AreEqual("REAUTH_REQUIRED", association.AssociationState);
        Assert.AreEqual(8, association.Revision);
        Assert.IsTrue(Guid.TryParse(association.AssociationId, out _));

        await using var current = await OpenUnpooledAsync(db);
        Assert.AreEqual(UserStateStore.SchemaVersion, await ScalarIntAsync(current, "PRAGMA user_version;"));
        Assert.AreEqual(1, await ScalarIntAsync(current, "SELECT COUNT(*) FROM user_schema_migration_history WHERE migration_id = 'user-v1-v2';"));
    }

    [TestMethod]
    public async Task ReauthConvergenceUsesOptimisticRevisionAndDoesNotOverwriteStaleState()
    {
        using var storage = new TestStorage();
        var store = new UserStateStore(storage.PathFor("user.db"));
        await store.InitializeAsync();

        var created = await store.CreateActiveAssociationAsync(
            Guid.NewGuid(),
            "S-1-5-21-test",
            "acc_one",
            DateTimeOffset.UtcNow,
            "account.v1",
            "entitlement-1",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            Guid.NewGuid());
        Assert.AreEqual(UserAssociationWriteDisposition.Applied, created.Disposition);
        Assert.AreEqual(1, created.Record!.Revision);

        var stale = await store.MarkReauthRequiredAsync(2, Guid.NewGuid());
        Assert.AreEqual(UserAssociationWriteDisposition.RevisionConflict, stale.Disposition);
        Assert.AreEqual("ACTIVE", (await store.GetAccountAssociationAsync())!.AssociationState);

        var applied = await store.MarkReauthRequiredAsync(1, Guid.NewGuid());
        Assert.AreEqual(UserAssociationWriteDisposition.Applied, applied.Disposition);
        Assert.AreEqual(2, applied.Record!.Revision);
        Assert.AreEqual("REAUTH_REQUIRED", applied.Record.AssociationState);
    }

    [TestMethod]
    public async Task SecondActiveAssociationCreationIsRejected()
    {
        using var storage = new TestStorage();
        var store = new UserStateStore(storage.PathFor("user.db"));
        await store.InitializeAsync();

        await store.CreateActiveAssociationAsync(
            Guid.NewGuid(), "S-1-5-21-test", "acc_one", DateTimeOffset.UtcNow,
            "account.v1", null, null, null, Guid.NewGuid());
        var duplicate = await store.CreateActiveAssociationAsync(
            Guid.NewGuid(), "S-1-5-21-test", "acc_two", DateTimeOffset.UtcNow,
            "account.v1", null, null, null, Guid.NewGuid());

        Assert.AreEqual(UserAssociationWriteDisposition.AlreadyExists, duplicate.Disposition);
        Assert.AreEqual("acc_one", (await store.GetAccountAssociationAsync())!.AccountId);
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

    private static async Task<int> ScalarIntAsync(SqliteConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
