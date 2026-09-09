using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.Machine;

namespace SplitOS.Persistence.Tests;

[TestClass]
public sealed class MachineSchemaV4Tests
{
    [TestMethod]
    public async Task SchemaV3MigratesToV4PreservesLeaseAndTransition()
    {
        using var storage = new TestStorage();
        var db = storage.PathFor("machine.db");
        var marker = storage.PathFor("machine-store.initialized");
        var backupRoot = storage.PathFor("maintenance", "backups");
        var leaseId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var transitionId = Guid.NewGuid();
        await CreateSchemaV3StoreAsync(db, marker, leaseId, operationId, correlationId, transitionId);

        var store = new MachineStateStore(
            db,
            marker,
            backupRoot,
            storage.PathFor("maintenance", "quarantine"),
            storage.PathFor("machine-store.quarantined.json"));
        await store.InitializeAsync();

        SqliteConnection.ClearAllPools();
        var backups = Directory.GetFiles(backupRoot, "machine.schema-v3.*.db", SearchOption.TopDirectoryOnly);
        Assert.AreEqual(1, backups.Length);
        await using (var backup = await OpenUnpooledAsync(backups[0]))
        {
            Assert.AreEqual(3, await ScalarIntAsync(backup, "PRAGMA user_version;"));
            Assert.AreEqual(transitionId.ToString("D"), await ScalarStringAsync(
                backup,
                "SELECT transition_id FROM mode_transition LIMIT 1;"));
            Assert.IsFalse(await TableExistsAsync(backup, "mode_transition_policy_binding"));
        }

        await using var current = await OpenUnpooledAsync(db);
        Assert.AreEqual(MachineStateStore.SchemaVersion, await ScalarIntAsync(current, "PRAGMA user_version;"));
        Assert.AreEqual(MachineStateStore.SchemaVersion, await ScalarIntAsync(
            current,
            "SELECT schema_version FROM schema_metadata WHERE component_key='machine';"));
        Assert.AreEqual(leaseId.ToString("D"), await ScalarStringAsync(
            current,
            "SELECT lease_id FROM machine_mutation_lease WHERE singleton_id=1;"));
        Assert.AreEqual(11L, await ScalarLongAsync(
            current,
            "SELECT fence_token FROM machine_mutation_lease WHERE singleton_id=1;"));
        Assert.AreEqual(transitionId.ToString("D"), await ScalarStringAsync(
            current,
            "SELECT transition_id FROM mode_transition LIMIT 1;"));
        Assert.IsTrue(await TableExistsAsync(current, "mode_transition_policy_binding"));
        Assert.IsTrue(await TableExistsAsync(current, "mode_transition_policy_fallback"));
        Assert.AreEqual(0, await ScalarIntAsync(current, "SELECT COUNT(*) FROM mode_transition_policy_binding;"));
        Assert.AreEqual(1, await ScalarIntAsync(
            current,
            "SELECT COUNT(*) FROM machine_schema_migration_history WHERE migration_id='machine-v3-v4-mode-policy-binding';"));
    }

    private static async Task CreateSchemaV3StoreAsync(
        string databasePath,
        string markerPath,
        Guid leaseId,
        Guid operationId,
        Guid correlationId,
        Guid transitionId)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        await using var connection = await OpenUnpooledAsync(databasePath);
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(5);
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
                committed_mode TEXT NOT NULL,
                committed_utc TEXT NOT NULL,
                committed_by_operation_id TEXT NOT NULL,
                correlation_id TEXT NULL,
                revision INTEGER NOT NULL,
                updated_utc TEXT NOT NULL
            );
            CREATE TABLE machine_operation_idempotency (
                operation_id TEXT PRIMARY KEY,
                request_kind TEXT NOT NULL,
                target_mode TEXT NOT NULL,
                expected_revision INTEGER NOT NULL,
                correlation_id TEXT NOT NULL,
                result_mode TEXT NOT NULL,
                result_revision INTEGER NOT NULL,
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
                mutation_type TEXT NULL,
                owner_operation_id TEXT NULL,
                owner_correlation_id TEXT NULL,
                owner_control_session_key TEXT NULL,
                fence_token INTEGER NOT NULL,
                acquired_utc TEXT NULL,
                heartbeat_utc TEXT NULL,
                expires_utc TEXT NULL,
                revision INTEGER NOT NULL
            );
            CREATE TABLE mode_transition (
                transition_id TEXT PRIMARY KEY,
                operation_id TEXT NOT NULL UNIQUE,
                correlation_id TEXT NOT NULL,
                operation_kind TEXT NOT NULL,
                source_mode TEXT NOT NULL,
                target_mode TEXT NOT NULL,
                source_mode_revision INTEGER NOT NULL,
                control_session_key TEXT NOT NULL,
                lease_id TEXT NOT NULL,
                fence_token INTEGER NOT NULL,
                transition_state TEXT NOT NULL,
                stage_code TEXT NOT NULL,
                started_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                mandatory_verified INTEGER NOT NULL,
                commit_durable INTEGER NOT NULL,
                terminal_outcome TEXT NULL,
                recovery_context_id TEXT NULL,
                revision INTEGER NOT NULL
            );
            INSERT INTO schema_metadata(component_key, schema_version, created_utc, last_migrated_utc, release_id)
            VALUES('machine', 3, $now, $now, 'schema-v3');
            INSERT INTO operational_mode_state(
                singleton_id, committed_mode, committed_utc, committed_by_operation_id,
                correlation_id, revision, updated_utc)
            VALUES(1, 'NONE', $now, 'bootstrap', NULL, 1, $now);
            INSERT INTO machine_mutation_lease(
                singleton_id, lease_id, mutation_type, owner_operation_id, owner_correlation_id,
                owner_control_session_key, fence_token, acquired_utc, heartbeat_utc, expires_utc, revision)
            VALUES(1, $lease, 'MODE', $operation, $correlation, 'console-session-v3', 11, $now, $now, $expires, 4);
            INSERT INTO mode_transition(
                transition_id, operation_id, correlation_id, operation_kind,
                source_mode, target_mode, source_mode_revision, control_session_key,
                lease_id, fence_token, transition_state, stage_code,
                started_utc, updated_utc, mandatory_verified, commit_durable,
                terminal_outcome, recovery_context_id, revision)
            VALUES(
                $transition, $operation, $correlation, 'ACTIVATE',
                'NONE', 'WORK', 1, 'console-session-v3',
                $lease, 11, 'RESOLVING', 'RESOLUTION_STARTED',
                $now, $now, 0, 0,
                NULL, NULL, 3);
            PRAGMA user_version = 3;
            """;
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.Parameters.AddWithValue("$expires", expires.ToString("O"));
        command.Parameters.AddWithValue("$lease", leaseId.ToString("D"));
        command.Parameters.AddWithValue("$operation", operationId.ToString("D"));
        command.Parameters.AddWithValue("$correlation", correlationId.ToString("D"));
        command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
        await command.ExecuteNonQueryAsync();
        await connection.CloseAsync();
        await File.WriteAllTextAsync(markerPath, now.ToString("O"));
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
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
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
}
