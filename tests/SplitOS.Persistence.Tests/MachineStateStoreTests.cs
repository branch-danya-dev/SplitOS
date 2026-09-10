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
    public async Task DirectManagedModeWriteIsRejected()
    {
        using var storage = new TestStorage();
        var store = CreateStore(storage);
        await store.InitializeAsync();

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
            store.WriteOperationalModeAsync("WORK", 1, Guid.NewGuid(), Guid.NewGuid()));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
            store.WriteOperationalModeAsync("GAME", 1, Guid.NewGuid(), Guid.NewGuid()));

        var current = await store.GetOperationalModeAsync();
        Assert.AreEqual("NONE", current.CommittedMode);
        Assert.AreEqual(1, current.Revision);
        Assert.IsNull(current.ControlSessionKey);
        Assert.IsNull(current.ActivationEpochId);
        Assert.IsNull(current.PolicyIdentity);
    }

    [TestMethod]
    public async Task OperationalModeWriteSurvivesRestartAndReplaysIdempotently()
    {
        using var storage = new TestStorage();
        var operationId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var store = CreateStore(storage);
        await store.InitializeAsync();

        var applied = await store.WriteOperationalModeAsync("NONE", 1, operationId, correlationId);
        Assert.AreEqual(OperationalModeWriteDisposition.Applied, applied.Disposition);
        Assert.AreEqual(2, applied.Record?.Revision);
        Assert.AreEqual("NONE", applied.Record?.CommittedMode);

        SqliteConnection.ClearAllPools();
        var reopened = CreateStore(storage);
        await reopened.InitializeAsync();
        var restored = await reopened.GetOperationalModeAsync();
        Assert.AreEqual("NONE", restored.CommittedMode);
        Assert.AreEqual(2, restored.Revision);
        Assert.AreEqual(operationId.ToString("D"), restored.CommittedByOperationId);

        var replay = await reopened.WriteOperationalModeAsync("NONE", 1, operationId, correlationId);
        Assert.AreEqual(OperationalModeWriteDisposition.Replayed, replay.Disposition);
        Assert.AreEqual(2, replay.Record?.Revision);

        var afterReplay = await reopened.GetOperationalModeAsync();
        Assert.AreEqual(2, afterReplay.Revision);
        Assert.AreEqual("NONE", afterReplay.CommittedMode);
    }

    [TestMethod]
    public async Task StaleRevisionDoesNotOverwriteNewerCanonicalState()
    {
        using var storage = new TestStorage();
        var store = CreateStore(storage);
        await store.InitializeAsync();
        await store.WriteOperationalModeAsync("NONE", 1, Guid.NewGuid(), Guid.NewGuid());

        var stale = await store.WriteOperationalModeAsync("NONE", 1, Guid.NewGuid(), Guid.NewGuid());
        Assert.AreEqual(OperationalModeWriteDisposition.RevisionConflict, stale.Disposition);
        Assert.AreEqual(2, stale.ActualRevision);

        var current = await store.GetOperationalModeAsync();
        Assert.AreEqual("NONE", current.CommittedMode);
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
        await store.WriteOperationalModeAsync("NONE", 1, operationId, correlationId);

        var conflict = await store.WriteOperationalModeAsync("NONE", 2, operationId, correlationId);
        Assert.AreEqual(OperationalModeWriteDisposition.IdempotencyConflict, conflict.Disposition);

        var current = await store.GetOperationalModeAsync();
        Assert.AreEqual("NONE", current.CommittedMode);
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
        Assert.IsNull(migrated.ControlSessionKey);
        Assert.IsNull(migrated.ActivationEpochId);
        Assert.IsNull(migrated.PolicyIdentity);

        SqliteConnection.ClearAllPools();
        var v1Backup = GetSingleBackupByVersion(backupRoot, 1);
        var v2Backup = GetSingleBackupByVersion(backupRoot, 2);

        await using (var backup = await OpenUnpooledAsync(v1Backup))
        {
            Assert.AreEqual(1, await ScalarIntAsync(backup, "PRAGMA user_version;"));
            Assert.AreEqual(7, await ScalarIntAsync(backup, "SELECT revision FROM operational_mode_state WHERE singleton_id = 1;"));
            Assert.IsFalse(await TableExistsAsync(backup, "machine_mutation_lease"));
        }

        await using (var backup = await OpenUnpooledAsync(v2Backup))
        {
            Assert.AreEqual(2, await ScalarIntAsync(backup, "PRAGMA user_version;"));
            Assert.AreEqual(7, await ScalarIntAsync(backup, "SELECT revision FROM operational_mode_state WHERE singleton_id = 1;"));
            Assert.IsFalse(await TableExistsAsync(backup, "mode_transition"));
        }

        await using var current = await OpenUnpooledAsync(db);
        Assert.AreEqual(MachineStateStore.SchemaVersion, await ScalarIntAsync(current, "PRAGMA user_version;"));
        Assert.AreEqual(MachineStateStore.SchemaVersion, await ScalarIntAsync(current, "SELECT schema_version FROM schema_metadata WHERE component_key = 'machine';"));
        Assert.AreEqual(1, await ScalarIntAsync(current, "SELECT COUNT(*) FROM machine_schema_migration_history WHERE migration_id = 'machine-v1-v2';"));
        Assert.AreEqual(1, await ScalarIntAsync(current, "SELECT COUNT(*) FROM machine_schema_migration_history WHERE migration_id = 'machine-v2-v3-slice03-foundation';"));
        Assert.AreEqual(1, await ScalarIntAsync(current, "SELECT COUNT(*) FROM machine_mutation_lease WHERE singleton_id = 1 AND lease_id IS NULL AND fence_token = 0;"));
        Assert.IsTrue(await TableExistsAsync(current, "mode_transition"));
    }

    [TestMethod]
    public async Task SchemaV2MigratesToV3PreservesStateAndExistingLease()
    {
        using var storage = new TestStorage();
        var db = storage.PathFor("machine.db");
        var marker = storage.PathFor("machine-store.initialized");
        var backupRoot = storage.PathFor("maintenance", "backups");
        var leaseId = Guid.NewGuid();
        var ownerOperationId = Guid.NewGuid();
        var ownerCorrelationId = Guid.NewGuid();
        await CreateSchemaV2StoreAsync(
            db,
            marker,
            "GAME",
            4,
            leaseId,
            ownerOperationId,
            ownerCorrelationId,
            fenceToken: 9);

        var store = new MachineStateStore(
            db,
            marker,
            backupRoot,
            storage.PathFor("maintenance", "quarantine"),
            storage.PathFor("machine-store.quarantined.json"));
        await store.InitializeAsync();

        var migrated = await store.GetOperationalModeAsync();
        Assert.AreEqual("GAME", migrated.CommittedMode);
        Assert.AreEqual(4, migrated.Revision);
        Assert.IsNull(migrated.ControlSessionKey);
        Assert.IsNull(migrated.ActivationEpochId);
        Assert.IsNull(migrated.PolicyIdentity);

        SqliteConnection.ClearAllPools();
        var v2Backup = GetSingleBackupByVersion(backupRoot, 2);
        await using (var backup = await OpenUnpooledAsync(v2Backup))
        {
            Assert.AreEqual(2, await ScalarIntAsync(backup, "PRAGMA user_version;"));
            Assert.AreEqual(9L, await ScalarLongAsync(backup, "SELECT fence_token FROM machine_mutation_lease WHERE singleton_id = 1;"));
            Assert.AreEqual(leaseId.ToString("D"), await ScalarStringAsync(backup, "SELECT lease_id FROM machine_mutation_lease WHERE singleton_id = 1;"));
            Assert.IsFalse(await TableExistsAsync(backup, "mode_transition"));
        }

        await using var current = await OpenUnpooledAsync(db);
        Assert.AreEqual(MachineStateStore.SchemaVersion, await ScalarIntAsync(current, "PRAGMA user_version;"));
        Assert.AreEqual(MachineStateStore.SchemaVersion, await ScalarIntAsync(current, "SELECT schema_version FROM schema_metadata WHERE component_key = 'machine';"));
        Assert.AreEqual(9L, await ScalarLongAsync(current, "SELECT fence_token FROM machine_mutation_lease WHERE singleton_id = 1;"));
        Assert.AreEqual(leaseId.ToString("D"), await ScalarStringAsync(current, "SELECT lease_id FROM machine_mutation_lease WHERE singleton_id = 1;"));
        Assert.AreEqual(ownerOperationId.ToString("D"), await ScalarStringAsync(current, "SELECT owner_operation_id FROM machine_mutation_lease WHERE singleton_id = 1;"));
        Assert.AreEqual(1, await ScalarIntAsync(current, "SELECT COUNT(*) FROM machine_schema_migration_history WHERE migration_id = 'machine-v2-v3-slice03-foundation';"));
        Assert.IsTrue(await TableExistsAsync(current, "mode_transition"));
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

    private static async Task CreateSchemaV2StoreAsync(
        string databasePath,
        string markerPath,
        string mode,
        int revision,
        Guid leaseId,
        Guid ownerOperationId,
        Guid ownerCorrelationId,
        long fenceToken)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        await using var connection = await OpenUnpooledAsync(databasePath);

        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(2);
        var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = FULL;
            CREATE TABLE schema_metadata (
                component_key TEXT PRIMARY KEY,
                schema_version INTEGER NOT NULL CHECK(schema_version >= 1),
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
            CREATE TABLE machine_operation_idempotency (
                operation_id TEXT PRIMARY KEY,
                request_kind TEXT NOT NULL,
                target_mode TEXT NOT NULL,
                expected_revision INTEGER NOT NULL CHECK(expected_revision >= 1),
                correlation_id TEXT NOT NULL,
                result_mode TEXT NOT NULL,
                result_revision INTEGER NOT NULL CHECK(result_revision >= 1),
                result_committed_utc TEXT NOT NULL,
                result_updated_utc TEXT NOT NULL,
                created_utc TEXT NOT NULL
            );
            CREATE TABLE machine_schema_migration_history (
                migration_id TEXT PRIMARY KEY,
                from_schema_version INTEGER NOT NULL,
                to_schema_version INTEGER NOT NULL,
                backup_path TEXT NOT NULL,
                migrated_utc TEXT NOT NULL,
                release_id TEXT NOT NULL
            );
            CREATE TABLE machine_mutation_lease (
                singleton_id INTEGER PRIMARY KEY CHECK(singleton_id = 1),
                lease_id TEXT NULL,
                mutation_type TEXT NULL CHECK(mutation_type IS NULL OR mutation_type IN ('MODE','UPDATE','RECOVERY')),
                owner_operation_id TEXT NULL,
                owner_correlation_id TEXT NULL,
                owner_control_session_key TEXT NULL,
                fence_token INTEGER NOT NULL DEFAULT 0 CHECK(fence_token >= 0),
                acquired_utc TEXT NULL,
                heartbeat_utc TEXT NULL,
                expires_utc TEXT NULL,
                revision INTEGER NOT NULL CHECK(revision >= 1)
            );
            INSERT INTO schema_metadata(component_key, schema_version, created_utc, last_migrated_utc, release_id)
            VALUES ('machine', 2, $now, $now, 'schema-v2');
            INSERT INTO operational_mode_state(
                singleton_id, committed_mode, committed_utc, committed_by_operation_id, correlation_id, revision, updated_utc)
            VALUES (1, $mode, $now, 'v2-operation', NULL, $revision, $now);
            INSERT INTO machine_mutation_lease(
                singleton_id, lease_id, mutation_type, owner_operation_id, owner_correlation_id,
                owner_control_session_key, fence_token, acquired_utc, heartbeat_utc, expires_utc, revision)
            VALUES (1, $lease, 'MODE', $operation, $correlation, 'session:test-v2', $fence, $now, $now, $expires, 5);
            PRAGMA user_version = 2;
            """;
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.Parameters.AddWithValue("$expires", expires.ToString("O"));
        command.Parameters.AddWithValue("$mode", mode);
        command.Parameters.AddWithValue("$revision", revision);
        command.Parameters.AddWithValue("$lease", leaseId.ToString("D"));
        command.Parameters.AddWithValue("$operation", ownerOperationId.ToString("D"));
        command.Parameters.AddWithValue("$correlation", ownerCorrelationId.ToString("D"));
        command.Parameters.AddWithValue("$fence", fenceToken);
        await command.ExecuteNonQueryAsync();
        await connection.CloseAsync();
        await File.WriteAllTextAsync(markerPath, now.ToString("O"));
    }

    private static string GetSingleBackupByVersion(string backupRoot, int schemaVersion)
    {
        var matches = Directory.GetFiles(
            backupRoot,
            $"machine.schema-v{schemaVersion}.*.db",
            SearchOption.TopDirectoryOnly);
        Assert.AreEqual(1, matches.Length, $"Expected exactly one verified schema-v{schemaVersion} backup.");
        return matches[0];
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

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string tableName)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task<int> ScalarIntAsync(SqliteConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<string?> ScalarStringAsync(SqliteConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync());
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
