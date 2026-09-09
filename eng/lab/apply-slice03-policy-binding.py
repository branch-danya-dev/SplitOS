from pathlib import Path
import subprocess


def read(path):
    return Path(path).read_text(encoding='utf-8')


def write(path, text):
    Path(path).write_text(text, encoding='utf-8', newline='\n')


def replace_once(text, old, new, label):
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f'{label}: expected exactly one match, found {count}')
    return text.replace(old, new, 1)

# Restore the accidentally replaced file from main before patching.
subprocess.run(['git', 'fetch', 'origin', 'main', '--no-tags'], check=True)
restored = subprocess.check_output(
    ['git', 'show', 'origin/main:src/SplitOS.Persistence.Machine/MachineStateStore.cs'],
    text=True,
    encoding='utf-8')
write('src/SplitOS.Persistence.Machine/MachineStateStore.cs', restored)

# ---- MachineStateStore: v3 -> v4 ----
p = 'src/SplitOS.Persistence.Machine/MachineStateStore.cs'
s = read(p)
s = replace_once(s,
'''    public const int SchemaVersion = 3;\n    private const int LegacySchemaVersion = 1;\n    private const int PreviousSchemaVersion = 2;''',
'''    public const int SchemaVersion = 4;\n    private const int LegacySchemaVersion = 1;\n    private const int PreviousSchemaVersion = 2;\n    private const int Slice03FoundationSchemaVersion = 3;''',
'constants')
s = replace_once(s,
'''    private const string V2ToV3MigrationId = "machine-v2-v3-slice03-foundation";''',
'''    private const string V2ToV3MigrationId = "machine-v2-v3-slice03-foundation";\n    private const string V3ToV4MigrationId = "machine-v3-v4-mode-policy-binding";''',
'migration id')
s = s.replace('await CreateSchemaV3Async(connection, null, cancellationToken).ConfigureAwait(false);',
              'await CreateSchemaV4Async(connection, null, cancellationToken).ConfigureAwait(false);', 2)
# The v2 migration must still land on physical v3, not jump directly to current v4.
s = replace_once(s,
'''                        await MigrateV2ToV3Async(connection, backup.BackupPath, cancellationToken).ConfigureAwait(false);\n                        currentVersion = SchemaVersion;''',
'''                        await MigrateV2ToV3Async(connection, backup.BackupPath, cancellationToken).ConfigureAwait(false);\n                        currentVersion = Slice03FoundationSchemaVersion;''',
'v2 current version')
anchor = '''                if (corruptionReason is null && currentVersion != SchemaVersion)\n                {\n                    throw new InvalidDataException(\n                        $"Machine canonical schema version {currentVersion} is not supported by runtime schema {SchemaVersion}.");\n                }\n'''
insert = '''                if (corruptionReason is null && currentVersion == Slice03FoundationSchemaVersion)\n                {\n                    try\n                    {\n                        await _database.VerifyQuickCheckAsync(connection, cancellationToken).ConfigureAwait(false);\n                        await VerifyV3CanonicalAsync(connection, cancellationToken).ConfigureAwait(false);\n                    }\n                    catch (InvalidDataException ex)\n                    {\n                        corruptionReason = ex;\n                    }\n\n                    if (corruptionReason is null)\n                    {\n                        var backup = await CanonicalStoreRecovery.CreateVerifiedBackupAsync(\n                            connection,\n                            _databasePath,\n                            _backupDirectory,\n                            Slice03FoundationSchemaVersion,\n                            cancellationToken).ConfigureAwait(false);\n                        await MigrateV3ToV4Async(connection, backup.BackupPath, cancellationToken).ConfigureAwait(false);\n                        currentVersion = SchemaVersion;\n                    }\n                }\n\n''' + anchor
s = replace_once(s, anchor, insert, 'v3 migration branch')

anchor = '''    private static async Task EnsureInitialStateAsync(SqliteConnection connection, CancellationToken cancellationToken)\n'''
v4 = '''    private static async Task CreateSchemaV4Async(\n        SqliteConnection connection,\n        SqliteTransaction? transaction,\n        CancellationToken cancellationToken)\n    {\n        await CreateSchemaV3Async(connection, transaction, cancellationToken).ConfigureAwait(false);\n\n        var command = connection.CreateCommand();\n        command.Transaction = transaction;\n        command.CommandText = """\n            CREATE TABLE IF NOT EXISTS mode_transition_policy_binding (\n                transition_id TEXT PRIMARY KEY,\n                policy_catalog_id TEXT NOT NULL,\n                policy_version INTEGER NOT NULL CHECK(policy_version >= 1),\n                policy_release_id TEXT NOT NULL,\n                policy_catalog_digest TEXT NOT NULL CHECK(length(policy_catalog_digest) = 64),\n                policy_target TEXT NOT NULL CHECK(policy_target IN ('BASE','WORK','GAME')),\n                resolved_policy_digest TEXT NOT NULL CHECK(length(resolved_policy_digest) = 64),\n                bound_utc TEXT NOT NULL,\n                FOREIGN KEY(transition_id) REFERENCES mode_transition(transition_id) ON DELETE CASCADE\n            );\n\n            CREATE TABLE IF NOT EXISTS mode_transition_policy_fallback (\n                transition_id TEXT NOT NULL,\n                rule_id TEXT NOT NULL,\n                fallback_class TEXT NOT NULL CHECK(fallback_class IN ('RELEASE_DEFAULT','APPROVED_ALTERNATE','PRESERVE_CURRENT')),\n                target_id TEXT NULL,\n                PRIMARY KEY(transition_id, rule_id),\n                FOREIGN KEY(transition_id) REFERENCES mode_transition_policy_binding(transition_id) ON DELETE CASCADE,\n                CHECK(\n                    (fallback_class = 'APPROVED_ALTERNATE' AND target_id IS NOT NULL) OR\n                    (fallback_class != 'APPROVED_ALTERNATE' AND target_id IS NULL)\n                )\n            );\n\n            CREATE TRIGGER IF NOT EXISTS trg_mode_transition_policy_before_action_plan\n            BEFORE UPDATE OF stage_code ON mode_transition\n            WHEN NEW.stage_code = 'ACTION_PLAN_READY'\n             AND NOT EXISTS (\n                 SELECT 1\n                 FROM mode_transition_policy_binding binding\n                 WHERE binding.transition_id = NEW.transition_id\n             )\n            BEGIN\n                SELECT RAISE(ABORT, 'MODE_POLICY_BINDING_REQUIRED');\n            END;\n            """;\n        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);\n    }\n\n'''
s = replace_once(s, anchor, v4 + anchor, 'v4 schema insert')

anchor = '''    private static async Task VerifyLegacyV1CanonicalAsync(\n'''
migration = '''    private static async Task MigrateV3ToV4Async(\n        SqliteConnection connection,\n        string backupPath,\n        CancellationToken cancellationToken)\n    {\n        using var transaction = connection.BeginTransaction();\n        await CreateSchemaV4Async(connection, transaction, cancellationToken).ConfigureAwait(false);\n        var now = DateTimeOffset.UtcNow;\n\n        var command = connection.CreateCommand();\n        command.Transaction = transaction;\n        command.CommandText = """\n            UPDATE schema_metadata\n            SET schema_version = 4,\n                last_migrated_utc = $now,\n                release_id = $release\n            WHERE component_key = 'machine' AND schema_version = 3;\n            """;\n        command.Parameters.AddWithValue("$now", now.ToString("O"));\n        command.Parameters.AddWithValue("$release", ReleaseId);\n        var updated = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);\n        if (updated != 1)\n        {\n            throw new InvalidDataException("Machine schema metadata could not be advanced from v3 to v4.");\n        }\n\n        command = connection.CreateCommand();\n        command.Transaction = transaction;\n        command.CommandText = "PRAGMA user_version = 4;";\n        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);\n\n        command = connection.CreateCommand();\n        command.Transaction = transaction;\n        command.CommandText = """\n            INSERT INTO machine_schema_migration_history(\n                migration_id, from_schema_version, to_schema_version, backup_path, migrated_utc, release_id)\n            VALUES ($migration, 3, 4, $backup, $now, $release);\n            """;\n        command.Parameters.AddWithValue("$migration", V3ToV4MigrationId);\n        command.Parameters.AddWithValue("$backup", backupPath);\n        command.Parameters.AddWithValue("$now", now.ToString("O"));\n        command.Parameters.AddWithValue("$release", ReleaseId);\n        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);\n\n        transaction.Commit();\n    }\n\n'''
s = replace_once(s, anchor, migration + anchor, 'v3 v4 migration insert')

anchor = '''    private static async Task VerifyCanonicalInvariantsAsync(\n'''
verify_v3 = '''    private static async Task VerifyV3CanonicalAsync(\n        SqliteConnection connection,\n        CancellationToken cancellationToken)\n    {\n        foreach (var table in new[]\n                 {\n                     "schema_metadata",\n                     "operational_mode_state",\n                     "machine_operation_idempotency",\n                     "machine_schema_migration_history",\n                     "machine_mutation_lease",\n                     "mode_transition"\n                 })\n        {\n            if (!await TableExistsAsync(connection, table, cancellationToken).ConfigureAwait(false))\n            {\n                throw new InvalidDataException($"Machine v3 canonical table {table} is missing.");\n            }\n        }\n\n        var command = connection.CreateCommand();\n        command.CommandText = "SELECT schema_version FROM schema_metadata WHERE component_key = 'machine';";\n        var metadataVersion = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);\n        if (metadataVersion is null or DBNull || Convert.ToInt32(metadataVersion) != Slice03FoundationSchemaVersion)\n        {\n            throw new InvalidDataException("Machine schema metadata does not match physical schema v3.");\n        }\n\n        command = connection.CreateCommand();\n        command.CommandText = "SELECT COUNT(*) FROM operational_mode_state WHERE singleton_id = 1;";\n        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 1)\n        {\n            throw new InvalidDataException("Machine v3 canonical OperationalModeState singleton is missing.");\n        }\n\n        command = connection.CreateCommand();\n        command.CommandText = "SELECT COUNT(*) FROM machine_mutation_lease WHERE singleton_id = 1;";\n        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 1)\n        {\n            throw new InvalidDataException("Machine v3 canonical major mutation lease singleton is missing.");\n        }\n    }\n\n'''
s = replace_once(s, anchor, verify_v3 + anchor, 'v3 verifier insert')

head, marker, tail = s.partition("    private static async Task VerifyCanonicalInvariantsAsync(\n")
if not marker:
    raise RuntimeError("final invariant verifier marker missing")
tail = replace_once(tail,
'''                     "machine_mutation_lease",\n                     "mode_transition"''',
'''                     "machine_mutation_lease",\n                     "mode_transition",\n                     "mode_transition_policy_binding",\n                     "mode_transition_policy_fallback"''',
'v4 required tables')
tail = replace_once(tail,
'''            throw new InvalidDataException("Machine schema metadata does not match physical schema v3.");''',
'''            throw new InvalidDataException("Machine schema metadata does not match physical schema v4.");''',
'v4 metadata message')
tail = replace_once(tail,
'''            throw new InvalidDataException("Machine canonical major mutation lease violates v3 invariants.");\n        }\n    }''',
'''            throw new InvalidDataException("Machine canonical major mutation lease violates v4 invariants.");\n        }\n\n        command = connection.CreateCommand();\n        command.CommandText = """\n            SELECT COUNT(*)\n            FROM mode_transition_policy_binding binding\n            JOIN mode_transition transition_row ON transition_row.transition_id = binding.transition_id\n            WHERE trim(binding.policy_catalog_id) = ''\n               OR binding.policy_version < 1\n               OR trim(binding.policy_release_id) = ''\n               OR length(binding.policy_catalog_digest) != 64\n               OR length(binding.resolved_policy_digest) != 64\n               OR binding.policy_target NOT IN ('BASE','WORK','GAME')\n               OR (transition_row.target_mode = 'NONE' AND binding.policy_target != 'BASE')\n               OR (transition_row.target_mode = 'WORK' AND binding.policy_target != 'WORK')\n               OR (transition_row.target_mode = 'GAME' AND binding.policy_target != 'GAME');\n            """;\n        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0)\n        {\n            throw new InvalidDataException("Machine canonical resolved mode policy binding violates v4 invariants.");\n        }\n    }''',
'v4 policy invariants')
s = head + marker + tail
write(p, s)

p = 'tests/SplitOS.Persistence.Tests/MachineStateStoreTests.cs'
s = read(p)
s = s.replace('Assert.AreEqual(3, await ScalarIntAsync(current, "SELECT schema_version FROM schema_metadata WHERE component_key = \'machine\';"));',
              'Assert.AreEqual(MachineStateStore.SchemaVersion, await ScalarIntAsync(current, "SELECT schema_version FROM schema_metadata WHERE component_key = \'machine\';"));')
s = s.replace('Assert.AreEqual(3, await ScalarIntAsync(current, "PRAGMA user_version;"));',
              'Assert.AreEqual(MachineStateStore.SchemaVersion, await ScalarIntAsync(current, "PRAGMA user_version;"));')
write(p, s)

p = 'tests/SplitOS.Persistence.Tests/ModeTransitionStoreTests.cs'
s = read(p)
old = '''    private static async Task<int> AdvanceAsync(\n        TestContext context,\n        Guid transitionId,\n        int revision,\n        PersistedModeTransitionState state,\n        PersistedModeTransitionStage stage,\n        bool mandatoryVerified)\n    {\n        var outcome = await context.Transitions.AdvanceAsync(\n            transitionId,\n            revision,\n            context.Lease.LeaseId!.Value,\n            context.Lease.FenceToken,\n            context.OperationId,\n            state,\n            stage,\n            mandatoryVerified);\n        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, outcome.Disposition, outcome.Detail);\n        return outcome.Transition!.Revision;\n    }\n'''
new = '''    private static async Task<int> AdvanceAsync(\n        TestContext context,\n        Guid transitionId,\n        int revision,\n        PersistedModeTransitionState state,\n        PersistedModeTransitionStage stage,\n        bool mandatoryVerified)\n    {\n        if (state == PersistedModeTransitionState.Resolving &&\n            stage == PersistedModeTransitionStage.ActionPlanReady)\n        {\n            var resolving = await context.Transitions.AdvanceAsync(\n                transitionId,\n                revision,\n                context.Lease.LeaseId!.Value,\n                context.Lease.FenceToken,\n                context.OperationId,\n                PersistedModeTransitionState.Resolving,\n                PersistedModeTransitionStage.ResolutionStarted,\n                mandatoryVerified: false);\n            Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, resolving.Disposition, resolving.Detail);\n            revision = resolving.Transition!.Revision;\n\n            var policies = new ModeTransitionPolicyStore(\n                context.DatabasePath,\n                context.MarkerPath,\n                context.QuarantineMarkerPath,\n                context.Time);\n            await policies.InitializeAsync();\n            var bound = await policies.BindResolvedPolicyAsync(\n                transitionId,\n                revision,\n                context.Lease.LeaseId.Value,\n                context.Lease.FenceToken,\n                context.OperationId,\n                new PersistedModePolicyIdentity("mode-policy.test", 1, "development", new string('a', 64)),\n                PersistedModePolicyTarget.Work,\n                new string('b', 64));\n            Assert.AreEqual(ModeTransitionPolicyBindDisposition.Bound, bound.Disposition, bound.Detail);\n            revision = bound.Binding!.TransitionRevision;\n        }\n\n        var outcome = await context.Transitions.AdvanceAsync(\n            transitionId,\n            revision,\n            context.Lease.LeaseId!.Value,\n            context.Lease.FenceToken,\n            context.OperationId,\n            state,\n            stage,\n            mandatoryVerified);\n        Assert.AreEqual(ModeTransitionAdvanceDisposition.Advanced, outcome.Disposition, outcome.Detail);\n        return outcome.Transition!.Revision;\n    }\n'''
s = replace_once(s, old, new, 'transition test helper')
write(p, s)

p = 'tests/SplitOS.Persistence.Tests/ModeTransitionCommitStoreTests.cs'
s = read(p)
old = '''        revision = await AdvanceAsync(context, transitionId, revision,\n            PersistedModeTransitionState.Resolving,\n            PersistedModeTransitionStage.ActionPlanReady,\n            false);\n'''
new = '''        revision = await AdvanceAsync(context, transitionId, revision,\n            PersistedModeTransitionState.Resolving,\n            PersistedModeTransitionStage.ResolutionStarted,\n            false);\n\n        var policies = new ModeTransitionPolicyStore(\n            context.DatabasePath,\n            context.MarkerPath,\n            context.QuarantineMarkerPath,\n            context.Time);\n        await policies.InitializeAsync();\n        var policyTarget = targetMode switch\n        {\n            "NONE" => PersistedModePolicyTarget.Base,\n            "WORK" => PersistedModePolicyTarget.Work,\n            "GAME" => PersistedModePolicyTarget.Game,\n            _ => throw new InvalidOperationException("Unsupported policy target.")\n        };\n        var bound = await policies.BindResolvedPolicyAsync(\n            transitionId,\n            revision,\n            context.Lease.LeaseId!.Value,\n            context.Lease.FenceToken,\n            context.OperationId,\n            new PersistedModePolicyIdentity("mode-policy.test", 1, "development", new string('a', 64)),\n            policyTarget,\n            new string('b', 64));\n        Assert.AreEqual(ModeTransitionPolicyBindDisposition.Bound, bound.Disposition, bound.Detail);\n        revision = bound.Binding!.TransitionRevision;\n\n        revision = await AdvanceAsync(context, transitionId, revision,\n            PersistedModeTransitionState.Resolving,\n            PersistedModeTransitionStage.ActionPlanReady,\n            false);\n'''
s = replace_once(s, old, new, 'commit test policy binding')
write(p, s)

p = '.github/workflows/build.yml'
s = read(p)
s = replace_once(s,
'''      - name: Test machine schema migration\n        run: dotnet test tests/SplitOS.Persistence.Tests/SplitOS.Persistence.Tests.csproj -c Release --no-build --no-restore -p:Platform=x64 --filter "Name=LegacyV1MigratesOnlyAfterVerifiedBackupAndPreservesState|Name=SchemaV2MigratesToV3PreservesStateAndExistingLease"\n''',
'''      - name: Test machine schema migration\n        run: dotnet test tests/SplitOS.Persistence.Tests/SplitOS.Persistence.Tests.csproj -c Release --no-build --no-restore -p:Platform=x64 --filter "Name=LegacyV1MigratesOnlyAfterVerifiedBackupAndPreservesState|Name=SchemaV2MigratesToV3PreservesStateAndExistingLease|Name=SchemaV3MigratesToV4PreservesLeaseAndTransition"\n''',
'ci migration filter')
s = replace_once(s,
'''      - name: Test durable mode transition journal\n        run: dotnet test tests/SplitOS.Persistence.Tests/SplitOS.Persistence.Tests.csproj -c Release --no-build --no-restore -p:Platform=x64 --filter FullyQualifiedName~ModeTransitionStoreTests\n''',
'''      - name: Test durable mode transition journal\n        run: dotnet test tests/SplitOS.Persistence.Tests/SplitOS.Persistence.Tests.csproj -c Release --no-build --no-restore -p:Platform=x64 --filter FullyQualifiedName~ModeTransitionStoreTests\n\n      - name: Test durable resolved mode policy binding\n        run: dotnet test tests/SplitOS.Persistence.Tests/SplitOS.Persistence.Tests.csproj -c Release --no-build --no-restore -p:Platform=x64 --filter FullyQualifiedName~ModeTransitionPolicyStoreTests\n''',
'ci policy gate')
write(p, s)

print('Slice 03 durable mode policy patch applied successfully.')
