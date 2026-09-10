using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.Machine;

namespace SplitOS.Persistence.Tests;

[TestClass]
public sealed class MachineSchemaV6Tests
{
    [TestMethod]
    public async Task SchemaV5MigratesToV6WithoutFabricatingManagedIdentityAndPreservesRuntimeEvidence()
    {
        using var storage = new TestStorage();
        var db = storage.PathFor("machine.db");
        var marker = storage.PathFor("machine-store.initialized");
        var backupRoot = storage.PathFor("maintenance", "backups");
        var leaseId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var transitionId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        await CreateSchemaV5StoreAsync(db, marker, leaseId, operationId, correlationId, transitionId, actionId);

        var store = new MachineStateStore(
            db,
            marker,
            backupRoot,
            storage.PathFor("maintenance", "quarantine"),
            storage.PathFor("machine-store.quarantined.json"));
        await store.InitializeAsync();

        SqliteConnection.ClearAllPools();
        var backups = Directory.GetFiles(backupRoot, "machine.schema-v5.*.db", SearchOption.TopDirectoryOnly);
        Assert.AreEqual(1, backups.Length);
        await using (var backup = await OpenUnpooledAsync(backups[0]))
        {
            Assert.AreEqual(5, await ScalarIntAsync(backup, "PRAGMA user_version;"));
            Assert.IsFalse(await ColumnExistsAsync(backup, "operational_mode_state", "activation_epoch_id"));
            Assert.AreEqual(actionId.ToString("D"), await ScalarStringAsync(
                backup, "SELECT action_id FROM mode_transition_action LIMIT 1;"));
            Assert.AreEqual(17L, await ScalarLongAsync(
                backup, "SELECT fence_token FROM machine_mutation_lease WHERE singleton_id=1;"));
        }

        var mode = await store.GetOperationalModeAsync();
        Assert.AreEqual("NONE", mode.CommittedMode);
        Assert.IsNull(mode.ControlSessionKey);
        Assert.IsNull(mode.ActivationEpochId);
        Assert.IsNull(mode.PolicyIdentity);
        Assert.IsNull(mode.PolicyTarget);
        Assert.IsNull(mode.ResolvedPolicyDigest);

        await using var current = await OpenUnpooledAsync(db);
        Assert.AreEqual(6, await ScalarIntAsync(current, "PRAGMA user_version;"));
        Assert.AreEqual(6, await ScalarIntAsync(
            current, "SELECT schema_version FROM schema_metadata WHERE component_key='machine';"));
        foreach (var column in new[]
                 {
                     "control_session_key", "activation_epoch_id", "policy_catalog_id", "policy_version",
                     "policy_release_id", "policy_catalog_digest", "policy_target", "resolved_policy_digest"
                 })
        {
            Assert.IsTrue(await ColumnExistsAsync(current, "operational_mode_state", column), column);
        }
        Assert.AreEqual(leaseId.ToString("D"), await ScalarStringAsync(
            current, "SELECT lease_id FROM machine_mutation_lease WHERE singleton_id=1;"));
        Assert.AreEqual(transitionId.ToString("D"), await ScalarStringAsync(
            current, "SELECT transition_id FROM mode_transition_policy_binding LIMIT 1;"));
        Assert.AreEqual(actionId.ToString("D"), await ScalarStringAsync(
            current, "SELECT action_id FROM mode_transition_action LIMIT 1;"));
        Assert.AreEqual(1, await ScalarIntAsync(
            current, "SELECT COUNT(*) FROM machine_schema_migration_history WHERE migration_id='machine-v5-v6-operational-mode-identity';"));
    }

    private static async Task CreateSchemaV5StoreAsync(
        string databasePath,
        string markerPath,
        Guid leaseId,
        Guid operationId,
        Guid correlationId,
        Guid transitionId,
        Guid actionId)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        await using var connection = await OpenUnpooledAsync(databasePath);
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(5);
        var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=FULL;
            CREATE TABLE schema_metadata(component_key TEXT PRIMARY KEY, schema_version INTEGER NOT NULL, created_utc TEXT NOT NULL, last_migrated_utc TEXT NOT NULL, release_id TEXT NULL);
            CREATE TABLE operational_mode_state(singleton_id INTEGER PRIMARY KEY, committed_mode TEXT NOT NULL, committed_utc TEXT NOT NULL, committed_by_operation_id TEXT NOT NULL, correlation_id TEXT NULL, revision INTEGER NOT NULL, updated_utc TEXT NOT NULL);
            CREATE TABLE machine_operation_idempotency(operation_id TEXT PRIMARY KEY, request_kind TEXT NOT NULL, target_mode TEXT NOT NULL, expected_revision INTEGER NOT NULL, correlation_id TEXT NOT NULL, result_mode TEXT NOT NULL, result_revision INTEGER NOT NULL, result_committed_utc TEXT NOT NULL, result_updated_utc TEXT NOT NULL, created_utc TEXT NOT NULL);
            CREATE TABLE machine_schema_migration_history(migration_id TEXT PRIMARY KEY, from_schema_version INTEGER NOT NULL, to_schema_version INTEGER NOT NULL, backup_path TEXT NOT NULL, migrated_utc TEXT NOT NULL, release_id TEXT NOT NULL);
            CREATE TABLE machine_mutation_lease(singleton_id INTEGER PRIMARY KEY, lease_id TEXT NULL, mutation_type TEXT NULL, owner_operation_id TEXT NULL, owner_correlation_id TEXT NULL, owner_control_session_key TEXT NULL, fence_token INTEGER NOT NULL, acquired_utc TEXT NULL, heartbeat_utc TEXT NULL, expires_utc TEXT NULL, revision INTEGER NOT NULL);
            CREATE TABLE mode_transition(transition_id TEXT PRIMARY KEY, operation_id TEXT NOT NULL UNIQUE, correlation_id TEXT NOT NULL, operation_kind TEXT NOT NULL, source_mode TEXT NOT NULL, target_mode TEXT NOT NULL, source_mode_revision INTEGER NOT NULL, control_session_key TEXT NOT NULL, lease_id TEXT NOT NULL, fence_token INTEGER NOT NULL, transition_state TEXT NOT NULL, stage_code TEXT NOT NULL, started_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, mandatory_verified INTEGER NOT NULL, commit_durable INTEGER NOT NULL, terminal_outcome TEXT NULL, recovery_context_id TEXT NULL, revision INTEGER NOT NULL);
            CREATE TABLE mode_transition_policy_binding(transition_id TEXT PRIMARY KEY, policy_catalog_id TEXT NOT NULL, policy_version INTEGER NOT NULL, policy_release_id TEXT NOT NULL, policy_catalog_digest TEXT NOT NULL, policy_target TEXT NOT NULL, resolved_policy_digest TEXT NOT NULL, bound_utc TEXT NOT NULL);
            CREATE TABLE mode_transition_policy_fallback(transition_id TEXT NOT NULL, rule_id TEXT NOT NULL, fallback_class TEXT NOT NULL, target_id TEXT NULL, PRIMARY KEY(transition_id, rule_id));
            CREATE TABLE mode_transition_action_plan(transition_id TEXT PRIMARY KEY, plan_schema_version INTEGER NOT NULL, plan_digest TEXT NOT NULL, action_count INTEGER NOT NULL, persisted_utc TEXT NOT NULL, transition_revision INTEGER NOT NULL);
            CREATE TABLE mode_transition_action(action_id TEXT PRIMARY KEY, transition_id TEXT NOT NULL, sequence_no INTEGER NOT NULL, owning_module TEXT NOT NULL, action_type TEXT NOT NULL, target_ref TEXT NULL, desired_schema_version INTEGER NOT NULL, desired_state_json TEXT NULL, desired_state_digest TEXT NOT NULL, mandatory INTEGER NOT NULL, rollback_class TEXT NOT NULL, verification_class TEXT NOT NULL, action_state TEXT NOT NULL, pre_state_json TEXT NULL, pre_state_digest TEXT NULL, apply_result_code TEXT NULL, verify_result_code TEXT NULL, rollback_result_code TEXT NULL, started_utc TEXT NULL, applied_utc TEXT NULL, verified_utc TEXT NULL, updated_utc TEXT NOT NULL, revision INTEGER NOT NULL);
            INSERT INTO schema_metadata VALUES('machine',5,$now,$now,'schema-v5');
            INSERT INTO operational_mode_state VALUES(1,'NONE',$now,'bootstrap',NULL,1,$now);
            INSERT INTO machine_mutation_lease VALUES(1,$lease,'MODE',$operation,$correlation,'console-session-v5',17,$now,$now,$expires,5);
            INSERT INTO mode_transition VALUES($transition,$operation,$correlation,'ACTIVATE','NONE','WORK',1,'console-session-v5',$lease,17,'RESOLVING','ACTION_PLAN_READY',$now,$now,0,0,NULL,NULL,5);
            INSERT INTO mode_transition_policy_binding VALUES($transition,'mode-policy.v5',5,'development',$catalog_digest,'WORK',$resolved_digest,$now);
            INSERT INTO mode_transition_action_plan VALUES($transition,1,$plan_digest,1,$now,4);
            INSERT INTO mode_transition_action VALUES($action,$transition,100,'test','noop.prepare',NULL,1,'{}',$desired_digest,1,'no_mutation','test.ready','PLANNED',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,$now,1);
            PRAGMA user_version=5;
            """;
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.Parameters.AddWithValue("$expires", expires.ToString("O"));
        command.Parameters.AddWithValue("$lease", leaseId.ToString("D"));
        command.Parameters.AddWithValue("$operation", operationId.ToString("D"));
        command.Parameters.AddWithValue("$correlation", correlationId.ToString("D"));
        command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
        command.Parameters.AddWithValue("$action", actionId.ToString("D"));
        command.Parameters.AddWithValue("$catalog_digest", new string('a',64));
        command.Parameters.AddWithValue("$resolved_digest", new string('b',64));
        command.Parameters.AddWithValue("$plan_digest", new string('c',64));
        command.Parameters.AddWithValue("$desired_digest", "44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a");
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

    private static async Task<bool> ColumnExistsAsync(SqliteConnection connection, string table, string column)
    {
        var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name=$column;";
        command.Parameters.AddWithValue("$column", column);
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
