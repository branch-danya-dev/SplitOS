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
        var store = CreateStore(storage);
        await store.InitializeAsync();
        var initial = await store.GetOperationalModeAsync();
        Assert.AreEqual("NONE", initial.CommittedMode);
        Assert.AreEqual(1, initial.Revision);

        SqliteConnection.ClearAllPools();
        var reopened = CreateStore(storage);
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
        var store = CreateStore(storage);
        await store.InitializeAsync();
        SqliteConnection.ClearAllPools();
        DeleteSqliteFiles(db);

        var reopened = CreateStore(storage);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => reopened.InitializeAsync());
    }

    [TestMethod]
    public async Task OperationalModeWriteSurvivesRestartAndReplaysIdempotently()
    {
        using var storage = new TestStorage();
        var operationId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var store = CreateStore(storage);
        await store.InitializeAsync();

        var applied = await store.WriteOperationalModeAsync("WORK", 1, operationId, correlationId);
        Assert.AreEqual(OperationalModeWriteDisposition.Applied, applied.Disposition);
        Assert.AreEqual(2, applied.Record?.Revision);
        Assert.AreEqual("WORK", applied.Record?.CommittedMode);

        SqliteConnection.ClearAllPools();
        var reopened = CreateStore(storage);
        await reopened.InitializeAsync();
        var restored = await reopened.GetOperationalModeAsync();
        Assert.AreEqual("WORK", restored.CommittedMode);
        Assert.AreEqual(2, restored.Revision);
        Assert.AreEqual(operationId.ToString("D"), restored.CommittedByOperationId);

        var replay = await reopened.WriteOperationalModeAsync("WORK", 1, operationId, correlationId);
        Assert.AreEqual(OperationalModeWriteDisposition.Replayed, replay.Disposition);
        Assert.AreEqual(2, replay.Record?.Revision);

        var afterReplay = await reopened.GetOperationalModeAsync();
        Assert.AreEqual(2, afterReplay.Revision);
        Assert.AreEqual("WORK", afterReplay.CommittedMode);
    }

    [TestMethod]
    public async Task StaleRevisionDoesNotOverwriteNewerCanonicalState()
    {
        using var storage = new TestStorage();
        var store = CreateStore(storage);
        await store.InitializeAsync();
        await store.WriteOperationalModeAsync("WORK", 1, Guid.NewGuid(), Guid.NewGuid());

        var stale = await store.WriteOperationalModeAsync("GAME", 1, Guid.NewGuid(), Guid.NewGuid());
        Assert.AreEqual(OperationalModeWriteDisposition.RevisionConflict, stale.Disposition);
        Assert.AreEqual(2, stale.ActualRevision);

        var current = await store.GetOperationalModeAsync();
        Assert.AreEqual("WORK", current.CommittedMode);
        Assert.AreEqual(2, current.Revision);
    }

    [TestMethod]
    public async Task ReusedOperationIdWithDifferentRequestIsRejected()
    {
        using var storage = new TestStorage();
        var store = CreateStore(storage);
        await store.InitializeAsync();
        var operationId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        await store.WriteOperationalModeAsync("WORK", 1, operationId, correlationId);

        var conflict = await store.WriteOperationalModeAsync("GAME", 2, operationId, correlationId);
        Assert.AreEqual(OperationalModeWriteDisposition.IdempotencyConflict, conflict.Disposition);

        var current = await store.GetOperationalModeAsync();
        Assert.AreEqual("WORK", current.CommittedMode);
        Assert.AreEqual(2, current.Revision);
    }

    [TestMethod]
    public async Task LegacyV1MigratesOnlyAfterVerifiedBackupAndPreservesState()
    {
        using var storage = new TestStorage();
        var db = storage.PathFor("machine.db");
        var marker = storage.PathFor("machine-store.initialized");
        var backupRoot = storage.PathFor("maintenance", "backups");
        await CreateLegacyV1StoreAsync(db, marker, "WORK", 7);

        var store = new MachineStateStore(
            db,
            marker,
            backupRoot,
            storage.PathFor("maintenance", "quarantine"),
            storage.PathFor("machine-store.quarantined.json"));
        await store.InitializeAsync();

        var migrated = await store.GetOperationalModeAsync();
        Assert.AreEqual("WORK", migrated.CommittedMode);
        Assert.AreEqual(7, migrated.Revision);

        SqliteConnection.ClearAllPools();
        var backups = Directory.GetFiles(backupRoot, "*.db", SearchOption.TopDirectoryOnly);
        Assert.AreEqual(1, backups.Length);
        await using (var backup = await OpenUnpooledAsync(backups[0]))
        {
            Assert.AreEqual(1, await ScalarIntAsync(backup, "PRAGMA user_version;"));
            Assert.AreEqual(7, await ScalarIntAsync(backup, "SELECT revision FROM operational_mode_state WHERE singleton_id = 1;"));
        }

        await using var current = await OpenUnpooledAsync(db);
        Assert.AreEqual(MachineStateStore.SchemaVersion, await ScalarIntAsync(current, "PRAGMA user_version;"));
        Assert.AreEqual(1, await ScalarIntAsync(current, "SELECT COUNT(*) FROM machine_schema_migration_history WHERE migration_id = 'machine-v1-v2';"));
    }

    [TestMethod]
    public async Task CorruptCanonicalStoreIsPreservedAndQuarantined()
    {
        using var storage = new TestStorage();
        var db = storage.PathFor("machine.db");
        var quarantineRoot = storage.PathFor("maintenance", "quarantine");
        var quarantineMarker = storage.PathFor("machine-store.quarantined.json");
        var store = new MachineStateStore(
            db,
            storage.PathFor("machine-store.initialized"),
            storage.PathFor("maintenance", "backups"),
            quarantineRoot,
            quarantineMarker);
        await store.InitializeAsync();

        SqliteConnection.ClearAllPools();
        DeleteSqliteFiles(db);
        await File.WriteAllBytesAsync(db, new byte[] { 0x53, 0x50, 0x4C, 0x49, 0x54, 0x4F, 0x53 });

        var corrupted = new MachineStateStore(
            db,
            storage.PathFor("machine-store.initialized"),
            storage.PathFor("maintenance", "backups"),
            quarantineRoot,
            quarantineMarker);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => corrupted.InitializeAsync());

        Assert.IsTrue(File.Exists(quarantineMarker));
        Assert.IsTrue(Directory.Exists(quarantineRoot));
        Assert.IsTrue(Directory.GetFiles(quarantineRoot, "machine.db", SearchOption.AllDirectories).Length > 0);

        var blocked = new MachineStateStore(
            db,
            storage.PathFor("machine-store.initialized"),
            storage.PathFor("maintenance", "backups"),
            quarantineRoot,
            quarantineMarker);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => blocked.InitializeAsync());
    }

    private static MachineStateStore CreateStore(TestStorage storage)
        => new(
            storage.PathFor("machine.db"),
            storage.PathFor("machine-store.initialized"),
            storage.PathFor("maintenance", "backups"),
            storage.PathFor("maintenance", "quarantine"),
            storage.PathFor("machine-store.quarantined.json"));

    private static async Task CreateLegacyV1StoreAsync(
        string databasePath,
        string markerPath,
        string mode,
        int revision)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        await using var connection = await OpenUnpooledAsync(databasePath);

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
            CREATE TABLE operational_mode_state (
                singleton_id INTEGER PRIMARY KEY CHECK(singleton_id = 1),
                committed_mode TEXT NOT NULL CHECK(committed_mode IN ('NONE','WORK','GAME')),
                committed_utc TEXT NOT NULL,
                committed_by_operation_id TEXT NOT NULL,
                correlation_id TEXT NULL,
                revision INTEGER NOT NULL CHECK(revision >= 1),
                updated_utc TEXT NOT NULL
            );
            INSERT INTO schema_metadata(component_key, schema_version, created_utc, last_migrated_utc, release_id)
            VALUES ('machine', 1, $now, $now, 'legacy-v1');
            INSERT INTO operational_mode_state(
                singleton_id, committed_mode, committed_utc, committed_by_operation_id, correlation_id, revision, updated_utc)
            VALUES (1, $mode, $now, 'legacy-operation', NULL, $revision, $now);
            PRAGMA user_version = 1;
            """;
        command.Parameters.AddWithValue("$now", now);
        command.Parameters.AddWithValue("$mode", mode);
        command.Parameters.AddWithValue("$revision", revision);
        await command.ExecuteNonQueryAsync();
        await connection.CloseAsync();
        await File.WriteAllTextAsync(markerPath, now);
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

    private static void DeleteSqliteFiles(string databasePath)
    {
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var path = databasePath + suffix;
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
