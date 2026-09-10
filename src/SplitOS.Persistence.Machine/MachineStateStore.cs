using Microsoft.Data.Sqlite;
using SplitOS.Persistence;

namespace SplitOS.Persistence.Machine;

public sealed record OperationalModeRecord(
    string CommittedMode,
    DateTimeOffset CommittedUtc,
    string CommittedByOperationId,
    string? CorrelationId,
    int Revision,
    DateTimeOffset UpdatedUtc,
    string? ControlSessionKey = null,
    Guid? ActivationEpochId = null,
    PersistedModePolicyIdentity? PolicyIdentity = null,
    PersistedModePolicyTarget? PolicyTarget = null,
    string? ResolvedPolicyDigest = null);

public enum OperationalModeWriteDisposition
{
    Applied,
    Replayed,
    RevisionConflict,
    IdempotencyConflict
}

public sealed record OperationalModeWriteOutcome(
    OperationalModeWriteDisposition Disposition,
    OperationalModeRecord? Record,
    int? ActualRevision,
    string? Detail);

public sealed class MachineStateStore
{
    public const int SchemaVersion = 6;
    private const int LegacySchemaVersion = 1;
    private const int PreviousSchemaVersion = 2;
    private const int Slice03FoundationSchemaVersion = 3;
    private const int Slice03PolicySchemaVersion = 4;
    private const int Slice03ActionSchemaVersion = 5;
    private const string ReleaseId = "development";
    private const string OperationRequestKind = "OPERATIONAL_MODE_WRITE";
    private const string V1ToV2MigrationId = "machine-v1-v2";
    private const string V2ToV3MigrationId = "machine-v2-v3-slice03-foundation";
    private const string V3ToV4MigrationId = "machine-v3-v4-mode-policy-binding";
    private const string V4ToV5MigrationId = "machine-v4-v5-mode-action-plan";
    private const string V5ToV6MigrationId = "machine-v5-v6-operational-mode-identity";

    private readonly SqliteDatabase _database;
    private readonly string _databasePath;
    private readonly string _markerPath;
    private readonly string _backupDirectory;
    private readonly string _quarantineDirectory;
    private readonly string _quarantineMarkerPath;

    public MachineStateStore(
        string? databasePath = null,
        string? markerPath = null,
        string? backupDirectory = null,
        string? quarantineDirectory = null,
        string? quarantineMarkerPath = null)
    {
        _databasePath = databasePath ?? StoragePaths.MachineDatabase;
        var customRoot = databasePath is null ? null : Path.GetDirectoryName(Path.GetFullPath(_databasePath));
        _markerPath = markerPath ?? (customRoot is null
            ? StoragePaths.MachineBootstrapMarker
            : Path.Combine(customRoot, "machine-store.initialized"));
        _backupDirectory = backupDirectory ?? (customRoot is null
            ? StoragePaths.MachineBackupRoot
            : Path.Combine(customRoot, "maintenance", "backups"));
        _quarantineDirectory = quarantineDirectory ?? (customRoot is null
            ? StoragePaths.MachineQuarantineRoot
            : Path.Combine(customRoot, "maintenance", "quarantine"));
        _quarantineMarkerPath = quarantineMarkerPath ?? (customRoot is null
            ? StoragePaths.MachineQuarantineMarker
            : Path.Combine(customRoot, "machine-store.quarantined.json"));
        _database = new SqliteDatabase(new SqliteDatabaseOptions(
            _databasePath,
            SplitOSDatabaseRole.Machine,
            SchemaVersion,
            ReleaseId));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        EnsureNotQuarantined();
        var dbExists = File.Exists(_databasePath);
        var markerExists = File.Exists(_markerPath);
        if (!dbExists && markerExists)
        {
            throw new InvalidDataException(
                "machine.db is missing after prior machine-store initialization. Recovery is required; canonical NONE must not be fabricated.");
        }

        Exception? corruptionReason = null;
        SqliteConnection? connection = null;
        try
        {
            connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
            var currentVersion = await _database.ReadSchemaVersionAsync(connection, cancellationToken).ConfigureAwait(false);

            if (!markerExists)
            {
                if (currentVersion is not 0 && currentVersion != SchemaVersion)
                {
                    throw new InvalidDataException(
                        $"Unmarked machine store has unsupported schema version {currentVersion}; automatic adoption is forbidden.");
                }

                await _database.InitializeMetadataAsync(connection, "machine", cancellationToken).ConfigureAwait(false);
                await CreateSchemaV6Async(connection, null, cancellationToken).ConfigureAwait(false);
                await EnsureInitialStateAsync(connection, cancellationToken).ConfigureAwait(false);
                await EnsureMutationLeaseSingletonAsync(connection, null, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                if (currentVersion == LegacySchemaVersion)
                {
                    try
                    {
                        await _database.VerifyQuickCheckAsync(connection, cancellationToken).ConfigureAwait(false);
                        await VerifyLegacyV1CanonicalAsync(connection, cancellationToken).ConfigureAwait(false);
                    }
                    catch (InvalidDataException ex)
                    {
                        corruptionReason = ex;
                    }

                    if (corruptionReason is null)
                    {
                        var backup = await CanonicalStoreRecovery.CreateVerifiedBackupAsync(
                            connection,
                            _databasePath,
                            _backupDirectory,
                            LegacySchemaVersion,
                            cancellationToken).ConfigureAwait(false);
                        await MigrateV1ToV2Async(connection, backup.BackupPath, cancellationToken).ConfigureAwait(false);
                        currentVersion = PreviousSchemaVersion;
                    }
                }

                if (corruptionReason is null && currentVersion == PreviousSchemaVersion)
                {
                    try
                    {
                        await _database.VerifyQuickCheckAsync(connection, cancellationToken).ConfigureAwait(false);
                        await VerifyV2CanonicalAsync(connection, cancellationToken).ConfigureAwait(false);
                    }
                    catch (InvalidDataException ex)
                    {
                        corruptionReason = ex;
                    }

                    if (corruptionReason is null)
                    {
                        var backup = await CanonicalStoreRecovery.CreateVerifiedBackupAsync(
                            connection,
                            _databasePath,
                            _backupDirectory,
                            PreviousSchemaVersion,
                            cancellationToken).ConfigureAwait(false);
                        await MigrateV2ToV3Async(connection, backup.BackupPath, cancellationToken).ConfigureAwait(false);
                        currentVersion = Slice03FoundationSchemaVersion;
                    }
                }

                if (corruptionReason is null && currentVersion == Slice03FoundationSchemaVersion)
                {
                    try
                    {
                        await _database.VerifyQuickCheckAsync(connection, cancellationToken).ConfigureAwait(false);
                        await VerifyV3CanonicalAsync(connection, cancellationToken).ConfigureAwait(false);
                    }
                    catch (InvalidDataException ex)
                    {
                        corruptionReason = ex;
                    }

                    if (corruptionReason is null)
                    {
                        var backup = await CanonicalStoreRecovery.CreateVerifiedBackupAsync(
                            connection,
                            _databasePath,
                            _backupDirectory,
                            Slice03FoundationSchemaVersion,
                            cancellationToken).ConfigureAwait(false);
                        await MigrateV3ToV4Async(connection, backup.BackupPath, cancellationToken).ConfigureAwait(false);
                        currentVersion = Slice03PolicySchemaVersion;
                    }
                }

                if (corruptionReason is null && currentVersion == Slice03PolicySchemaVersion)
                {
                    try
                    {
                        await _database.VerifyQuickCheckAsync(connection, cancellationToken).ConfigureAwait(false);
                        await VerifyV4CanonicalAsync(connection, cancellationToken).ConfigureAwait(false);
                    }
                    catch (InvalidDataException ex)
                    {
                        corruptionReason = ex;
                    }

                    if (corruptionReason is null)
                    {
                        var backup = await CanonicalStoreRecovery.CreateVerifiedBackupAsync(
                            connection,
                            _databasePath,
                            _backupDirectory,
                            Slice03PolicySchemaVersion,
                            cancellationToken).ConfigureAwait(false);
                        await MigrateV4ToV5Async(connection, backup.BackupPath, cancellationToken).ConfigureAwait(false);
                        currentVersion = Slice03ActionSchemaVersion;
                    }
                }

                if (corruptionReason is null && currentVersion == Slice03ActionSchemaVersion)
                {
                    try
                    {
                        await _database.VerifyQuickCheckAsync(connection, cancellationToken).ConfigureAwait(false);
                        await VerifyV5CanonicalAsync(connection, cancellationToken).ConfigureAwait(false);
                    }
                    catch (InvalidDataException ex)
                    {
                        corruptionReason = ex;
                    }

                    if (corruptionReason is null)
                    {
                        var backup = await CanonicalStoreRecovery.CreateVerifiedBackupAsync(
                            connection,
                            _databasePath,
                            _backupDirectory,
                            Slice03ActionSchemaVersion,
                            cancellationToken).ConfigureAwait(false);
                        await MigrateV5ToV6Async(connection, backup.BackupPath, cancellationToken).ConfigureAwait(false);
                        currentVersion = SchemaVersion;
                    }
                }

                if (corruptionReason is null && currentVersion != SchemaVersion)
                {
                    throw new InvalidDataException(
                        $"Machine canonical schema version {currentVersion} is not supported by runtime schema {SchemaVersion}.");
                }

                if (corruptionReason is null)
                {
                    try
                    {
                        await _database.InitializeMetadataAsync(connection, "machine", cancellationToken).ConfigureAwait(false);
                        await CreateSchemaV6Async(connection, null, cancellationToken).ConfigureAwait(false);
                        await EnsureMutationLeaseSingletonAsync(connection, null, cancellationToken).ConfigureAwait(false);
                    }
                    catch (InvalidDataException ex)
                    {
                        corruptionReason = ex;
                    }
                }
            }

            if (corruptionReason is null)
            {
                try
                {
                    await _database.VerifyIntegrityAsync(connection, cancellationToken).ConfigureAwait(false);
                    await VerifyCanonicalInvariantsAsync(connection, cancellationToken).ConfigureAwait(false);
                }
                catch (InvalidDataException ex)
                {
                    corruptionReason = ex;
                }
            }
        }
        catch (SqliteException ex) when (IsSqliteCorruption(ex))
        {
            corruptionReason = ex;
        }
        finally
        {
            if (connection is not null)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }

        if (corruptionReason is not null)
        {
            var quarantine = await CanonicalStoreRecovery.PreserveForensicCopyAsync(
                _databasePath,
                _quarantineDirectory,
                _quarantineMarkerPath,
                corruptionReason.Message,
                cancellationToken).ConfigureAwait(false);
            throw new InvalidDataException(
                $"Machine canonical storage failed integrity validation and was quarantined at {quarantine.QuarantineDirectory}. Recovery is required.",
                corruptionReason);
        }

        if (!markerExists)
        {
            await EnsureBootstrapMarkerAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<OperationalModeRecord> GetOperationalModeAsync(CancellationToken cancellationToken = default)
    {
        EnsureReadyForAccess();
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ReadOperationalModeAsync(connection, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationalModeWriteOutcome> WriteOperationalModeAsync(
        string targetMode,
        int expectedRevision,
        Guid operationId,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        EnsureReadyForAccess();
        ValidateTargetMode(targetMode);
        if (expectedRevision < 1) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        if (operationId == Guid.Empty) throw new ArgumentException("Operation id must not be empty.", nameof(operationId));
        if (correlationId == Guid.Empty) throw new ArgumentException("Correlation id must not be empty.", nameof(correlationId));

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        var schemaVersion = await _database.ReadSchemaVersionAsync(connection, cancellationToken).ConfigureAwait(false);
        if (schemaVersion != SchemaVersion)
        {
            throw new InvalidDataException(
                $"Machine write requires schema {SchemaVersion}, got {schemaVersion}.");
        }

        using var transaction = connection.BeginTransaction();
        var operationKey = operationId.ToString("D");
        var correlationKey = correlationId.ToString("D");
        var replay = await ReadIdempotencyEntryAsync(
            connection,
            transaction,
            operationKey,
            cancellationToken).ConfigureAwait(false);

        if (replay is not null)
        {
            if (!string.Equals(replay.RequestKind, OperationRequestKind, StringComparison.Ordinal) ||
                !string.Equals(replay.TargetMode, targetMode, StringComparison.Ordinal) ||
                replay.ExpectedRevision != expectedRevision ||
                !string.Equals(replay.CorrelationId, correlationKey, StringComparison.Ordinal))
            {
                return new OperationalModeWriteOutcome(
                    OperationalModeWriteDisposition.IdempotencyConflict,
                    null,
                    replay.ResultRevision,
                    "OperationId was already used for a different machine-state request.");
            }

            return new OperationalModeWriteOutcome(
                OperationalModeWriteDisposition.Replayed,
                replay.ToOperationalModeRecord(operationKey),
                replay.ResultRevision,
                null);
        }

        var current = await ReadOperationalModeAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        if (current.Revision != expectedRevision)
        {
            return new OperationalModeWriteOutcome(
                OperationalModeWriteDisposition.RevisionConflict,
                null,
                current.Revision,
                $"Expected revision {expectedRevision}, actual revision {current.Revision}.");
        }

        var now = DateTimeOffset.UtcNow;
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE operational_mode_state
            SET committed_mode = $mode,
                committed_utc = $now,
                committed_by_operation_id = $operation,
                correlation_id = $correlation,
                control_session_key = NULL,
                activation_epoch_id = NULL,
                policy_catalog_id = NULL,
                policy_version = NULL,
                policy_release_id = NULL,
                policy_catalog_digest = NULL,
                policy_target = NULL,
                resolved_policy_digest = NULL,
                revision = revision + 1,
                updated_utc = $now
            WHERE singleton_id = 1 AND revision = $expected_revision;
            """;
        command.Parameters.AddWithValue("$mode", targetMode);
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.Parameters.AddWithValue("$operation", operationKey);
        command.Parameters.AddWithValue("$correlation", correlationKey);
        command.Parameters.AddWithValue("$expected_revision", expectedRevision);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            var actual = await ReadOperationalModeAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            return new OperationalModeWriteOutcome(
                OperationalModeWriteDisposition.RevisionConflict,
                null,
                actual.Revision,
                $"Expected revision {expectedRevision}, actual revision {actual.Revision}.");
        }

        var committed = new OperationalModeRecord(
            targetMode,
            now,
            operationKey,
            correlationKey,
            checked(expectedRevision + 1),
            now);

        command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO machine_operation_idempotency(
                operation_id, request_kind, target_mode, expected_revision, correlation_id,
                result_mode, result_revision, result_committed_utc, result_updated_utc, created_utc)
            VALUES (
                $operation, $kind, $target_mode, $expected_revision, $correlation,
                $result_mode, $result_revision, $committed_utc, $updated_utc, $created_utc);
            """;
        command.Parameters.AddWithValue("$operation", operationKey);
        command.Parameters.AddWithValue("$kind", OperationRequestKind);
        command.Parameters.AddWithValue("$target_mode", targetMode);
        command.Parameters.AddWithValue("$expected_revision", expectedRevision);
        command.Parameters.AddWithValue("$correlation", correlationKey);
        command.Parameters.AddWithValue("$result_mode", committed.CommittedMode);
        command.Parameters.AddWithValue("$result_revision", committed.Revision);
        command.Parameters.AddWithValue("$committed_utc", committed.CommittedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updated_utc", committed.UpdatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$created_utc", now.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        transaction.Commit();
        return new OperationalModeWriteOutcome(
            OperationalModeWriteDisposition.Applied,
            committed,
            committed.Revision,
            null);
    }

    private void EnsureReadyForAccess()
    {
        EnsureNotQuarantined();
        if (!File.Exists(_markerPath) || !File.Exists(_databasePath))
        {
            throw new InvalidDataException("Machine canonical store is not initialized or is missing. Broker recovery is required.");
        }
    }

    private void EnsureNotQuarantined()
    {
        if (File.Exists(_quarantineMarkerPath))
        {
            throw new InvalidDataException(
                $"Machine canonical store is quarantined. Marker: {_quarantineMarkerPath}");
        }
    }

    private async Task EnsureBootstrapMarkerAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(_markerPath)) return;
        var markerDirectory = Path.GetDirectoryName(_markerPath);
        if (!string.IsNullOrWhiteSpace(markerDirectory)) Directory.CreateDirectory(markerDirectory);
        await File.WriteAllTextAsync(_markerPath, DateTimeOffset.UtcNow.ToString("O"), cancellationToken).ConfigureAwait(false);
    }

    private static async Task CreateSchemaV2Async(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS operational_mode_state (
                singleton_id INTEGER PRIMARY KEY CHECK(singleton_id = 1),
                committed_mode TEXT NOT NULL CHECK(committed_mode IN ('NONE','WORK','GAME')),
                committed_utc TEXT NOT NULL,
                committed_by_operation_id TEXT NOT NULL,
                correlation_id TEXT NULL,
                revision INTEGER NOT NULL CHECK(revision >= 1),
                updated_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS machine_operation_idempotency (
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

            CREATE TABLE IF NOT EXISTS machine_schema_migration_history (
                migration_id TEXT PRIMARY KEY,
                from_schema_version INTEGER NOT NULL,
                to_schema_version INTEGER NOT NULL,
                backup_path TEXT NOT NULL,
                migrated_utc TEXT NOT NULL,
                release_id TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task CreateSchemaV3Async(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await CreateSchemaV2Async(connection, transaction, cancellationToken).ConfigureAwait(false);

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS machine_mutation_lease (
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
                revision INTEGER NOT NULL CHECK(revision >= 1),
                CHECK(
                    (lease_id IS NULL AND mutation_type IS NULL AND owner_operation_id IS NULL AND
                     owner_correlation_id IS NULL AND owner_control_session_key IS NULL AND acquired_utc IS NULL AND
                     heartbeat_utc IS NULL AND expires_utc IS NULL)
                    OR
                    (lease_id IS NOT NULL AND mutation_type IS NOT NULL AND owner_operation_id IS NOT NULL AND
                     owner_correlation_id IS NOT NULL AND owner_control_session_key IS NOT NULL AND acquired_utc IS NOT NULL AND
                     heartbeat_utc IS NOT NULL AND expires_utc IS NOT NULL)
                )
            );

            CREATE TABLE IF NOT EXISTS mode_transition (
                transition_id TEXT PRIMARY KEY,
                operation_id TEXT NOT NULL UNIQUE,
                correlation_id TEXT NOT NULL,
                operation_kind TEXT NOT NULL CHECK(operation_kind IN ('ACTIVATE','SWITCH','DEACTIVATE')),
                source_mode TEXT NOT NULL CHECK(source_mode IN ('NONE','WORK','GAME')),
                target_mode TEXT NOT NULL CHECK(target_mode IN ('NONE','WORK','GAME')),
                source_mode_revision INTEGER NOT NULL CHECK(source_mode_revision >= 1),
                control_session_key TEXT NOT NULL,
                lease_id TEXT NOT NULL,
                fence_token INTEGER NOT NULL CHECK(fence_token >= 1),
                transition_state TEXT NOT NULL CHECK(transition_state IN (
                    'REQUESTED','INSPECTING','BLOCKED','AWAITING_USER','RESOLVING','APPLYING','VERIFYING',
                    'COMMITTING','ROLLING_BACK','COMPLETED','CANCELLED','FAILED_WITH_SAFE_FALLBACK')),
                stage_code TEXT NOT NULL CHECK(stage_code IN (
                    'ACCEPTED','INSPECTION_STARTED','INSPECTION_COMPLETE','WAITING_FOR_USER','RESOLUTION_STARTED',
                    'ACTION_PLAN_READY','APPLY_STARTED','APPLY_COMPLETE','VERIFY_STARTED','VERIFY_COMPLETE',
                    'COMMIT_STARTED','COMMIT_DURABLE','FINALIZATION_STARTED','ROLLBACK_STARTED','ROLLBACK_VERIFY','TERMINAL')),
                started_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                mandatory_verified INTEGER NOT NULL DEFAULT 0 CHECK(mandatory_verified IN (0,1)),
                commit_durable INTEGER NOT NULL DEFAULT 0 CHECK(commit_durable IN (0,1)),
                terminal_outcome TEXT NULL CHECK(terminal_outcome IS NULL OR terminal_outcome IN (
                    'COMPLETED','CANCELLED','FAILED_WITH_SAFE_FALLBACK')),
                recovery_context_id TEXT NULL,
                revision INTEGER NOT NULL CHECK(revision >= 1),
                CHECK(
                    (operation_kind = 'ACTIVATE' AND source_mode = 'NONE' AND target_mode IN ('WORK','GAME')) OR
                    (operation_kind = 'SWITCH' AND ((source_mode = 'WORK' AND target_mode = 'GAME') OR
                                                     (source_mode = 'GAME' AND target_mode = 'WORK'))) OR
                    (operation_kind = 'DEACTIVATE' AND source_mode IN ('WORK','GAME') AND target_mode = 'NONE')
                ),
                CHECK(commit_durable = 0 OR transition_state IN ('COMMITTING','COMPLETED'))
            );

            CREATE INDEX IF NOT EXISTS ix_mode_transition_state
                ON mode_transition(transition_state, updated_utc);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task CreateSchemaV4Async(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await CreateSchemaV3Async(connection, transaction, cancellationToken).ConfigureAwait(false);

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS mode_transition_policy_binding (
                transition_id TEXT PRIMARY KEY,
                policy_catalog_id TEXT NOT NULL,
                policy_version INTEGER NOT NULL CHECK(policy_version >= 1),
                policy_release_id TEXT NOT NULL,
                policy_catalog_digest TEXT NOT NULL CHECK(length(policy_catalog_digest) = 64),
                policy_target TEXT NOT NULL CHECK(policy_target IN ('BASE','WORK','GAME')),
                resolved_policy_digest TEXT NOT NULL CHECK(length(resolved_policy_digest) = 64),
                bound_utc TEXT NOT NULL,
                FOREIGN KEY(transition_id) REFERENCES mode_transition(transition_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS mode_transition_policy_fallback (
                transition_id TEXT NOT NULL,
                rule_id TEXT NOT NULL,
                fallback_class TEXT NOT NULL CHECK(fallback_class IN ('RELEASE_DEFAULT','APPROVED_ALTERNATE','PRESERVE_CURRENT')),
                target_id TEXT NULL,
                PRIMARY KEY(transition_id, rule_id),
                FOREIGN KEY(transition_id) REFERENCES mode_transition_policy_binding(transition_id) ON DELETE CASCADE,
                CHECK(
                    (fallback_class = 'APPROVED_ALTERNATE' AND target_id IS NOT NULL) OR
                    (fallback_class != 'APPROVED_ALTERNATE' AND target_id IS NULL)
                )
            );

            CREATE TRIGGER IF NOT EXISTS trg_mode_transition_policy_before_action_plan
            BEFORE UPDATE OF stage_code ON mode_transition
            WHEN NEW.stage_code = 'ACTION_PLAN_READY'
             AND NOT EXISTS (
                 SELECT 1
                 FROM mode_transition_policy_binding binding
                 WHERE binding.transition_id = NEW.transition_id
             )
            BEGIN
                SELECT RAISE(ABORT, 'MODE_POLICY_BINDING_REQUIRED');
            END;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task CreateSchemaV5Async(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await CreateSchemaV4Async(connection, transaction, cancellationToken).ConfigureAwait(false);

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS mode_transition_action_plan (
                transition_id TEXT PRIMARY KEY,
                plan_schema_version INTEGER NOT NULL CHECK(plan_schema_version >= 1),
                plan_digest TEXT NOT NULL CHECK(length(plan_digest) = 64),
                action_count INTEGER NOT NULL CHECK(action_count BETWEEN 1 AND 512),
                persisted_utc TEXT NOT NULL,
                transition_revision INTEGER NOT NULL CHECK(transition_revision >= 1),
                FOREIGN KEY(transition_id) REFERENCES mode_transition(transition_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS mode_transition_action (
                action_id TEXT PRIMARY KEY,
                transition_id TEXT NOT NULL,
                sequence_no INTEGER NOT NULL CHECK(sequence_no >= 1),
                owning_module TEXT NOT NULL,
                action_type TEXT NOT NULL,
                target_ref TEXT NULL,
                desired_schema_version INTEGER NOT NULL CHECK(desired_schema_version >= 1),
                desired_state_json TEXT NULL,
                desired_state_digest TEXT NOT NULL CHECK(length(desired_state_digest) = 64),
                mandatory INTEGER NOT NULL CHECK(mandatory IN (0,1)),
                rollback_class TEXT NOT NULL,
                verification_class TEXT NOT NULL,
                action_state TEXT NOT NULL CHECK(action_state IN (
                    'PLANNED','APPLYING','APPLIED','VERIFYING','VERIFIED','FAILED',
                    'ROLLING_BACK','ROLLED_BACK','ROLLBACK_FAILED','SKIPPED')),
                pre_state_json TEXT NULL,
                pre_state_digest TEXT NULL CHECK(pre_state_digest IS NULL OR length(pre_state_digest) = 64),
                apply_result_code TEXT NULL,
                verify_result_code TEXT NULL,
                rollback_result_code TEXT NULL,
                started_utc TEXT NULL,
                applied_utc TEXT NULL,
                verified_utc TEXT NULL,
                updated_utc TEXT NOT NULL,
                revision INTEGER NOT NULL CHECK(revision >= 1),
                FOREIGN KEY(transition_id) REFERENCES mode_transition(transition_id) ON DELETE CASCADE,
                UNIQUE(transition_id, sequence_no)
            );

            CREATE INDEX IF NOT EXISTS ix_mode_transition_action_order
                ON mode_transition_action(transition_id, sequence_no);

            CREATE TRIGGER IF NOT EXISTS trg_mode_transition_action_plan_before_ready
            BEFORE UPDATE OF stage_code ON mode_transition
            WHEN NEW.stage_code = 'ACTION_PLAN_READY'
             AND NOT EXISTS (
                 SELECT 1
                 FROM mode_transition_action_plan plan
                 WHERE plan.transition_id = NEW.transition_id
                   AND plan.action_count = (
                       SELECT COUNT(*)
                       FROM mode_transition_action action_row
                       WHERE action_row.transition_id = NEW.transition_id
                   )
             )
            BEGIN
                SELECT RAISE(ABORT, 'MODE_ACTION_PLAN_REQUIRED');
            END;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task CreateSchemaV6Async(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await CreateSchemaV5Async(connection, transaction, cancellationToken).ConfigureAwait(false);

        foreach (var (columnName, definition) in new[]
                 {
                     ("control_session_key", "TEXT NULL"),
                     ("activation_epoch_id", "TEXT NULL"),
                     ("policy_catalog_id", "TEXT NULL"),
                     ("policy_version", "INTEGER NULL"),
                     ("policy_release_id", "TEXT NULL"),
                     ("policy_catalog_digest", "TEXT NULL"),
                     ("policy_target", "TEXT NULL"),
                     ("resolved_policy_digest", "TEXT NULL")
                 })
        {
            if (await ColumnExistsAsync(connection, transaction, columnName, cancellationToken).ConfigureAwait(false))
                continue;

            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"ALTER TABLE operational_mode_state ADD COLUMN {columnName} {definition};";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task EnsureInitialStateAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO operational_mode_state(
                singleton_id, committed_mode, committed_utc, committed_by_operation_id, correlation_id, revision, updated_utc)
            VALUES (1, 'NONE', $now, 'bootstrap', NULL, 1, $now)
            ON CONFLICT(singleton_id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureMutationLeaseSingletonAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO machine_mutation_lease(
                singleton_id, lease_id, mutation_type, owner_operation_id, owner_correlation_id,
                owner_control_session_key, fence_token, acquired_utc, heartbeat_utc, expires_utc, revision)
            VALUES (1, NULL, NULL, NULL, NULL, NULL, 0, NULL, NULL, NULL, 1)
            ON CONFLICT(singleton_id) DO NOTHING;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task MigrateV1ToV2Async(
        SqliteConnection connection,
        string backupPath,
        CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();
        await CreateSchemaV2Async(connection, transaction, cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE schema_metadata
            SET schema_version = 2,
                last_migrated_utc = $now,
                release_id = $release
            WHERE component_key = 'machine' AND schema_version = 1;
            """;
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.Parameters.AddWithValue("$release", ReleaseId);
        var updated = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (updated != 1)
        {
            throw new InvalidDataException("Legacy machine schema metadata could not be advanced from v1 to v2.");
        }

        command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version = 2;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO machine_schema_migration_history(
                migration_id, from_schema_version, to_schema_version, backup_path, migrated_utc, release_id)
            VALUES ($migration, 1, 2, $backup, $now, $release);
            """;
        command.Parameters.AddWithValue("$migration", V1ToV2MigrationId);
        command.Parameters.AddWithValue("$backup", backupPath);
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.Parameters.AddWithValue("$release", ReleaseId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        transaction.Commit();
    }

    private static async Task MigrateV2ToV3Async(
        SqliteConnection connection,
        string backupPath,
        CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();
        await CreateSchemaV3Async(connection, transaction, cancellationToken).ConfigureAwait(false);
        await EnsureMutationLeaseSingletonAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE schema_metadata
            SET schema_version = 3,
                last_migrated_utc = $now,
                release_id = $release
            WHERE component_key = 'machine' AND schema_version = 2;
            """;
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.Parameters.AddWithValue("$release", ReleaseId);
        var updated = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (updated != 1)
        {
            throw new InvalidDataException("Machine schema metadata could not be advanced from v2 to v3.");
        }

        command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version = 3;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO machine_schema_migration_history(
                migration_id, from_schema_version, to_schema_version, backup_path, migrated_utc, release_id)
            VALUES ($migration, 2, 3, $backup, $now, $release);
            """;
        command.Parameters.AddWithValue("$migration", V2ToV3MigrationId);
        command.Parameters.AddWithValue("$backup", backupPath);
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.Parameters.AddWithValue("$release", ReleaseId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        transaction.Commit();
    }

    private static async Task MigrateV3ToV4Async(
        SqliteConnection connection,
        string backupPath,
        CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();
        await CreateSchemaV4Async(connection, transaction, cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE schema_metadata
            SET schema_version = 4,
                last_migrated_utc = $now,
                release_id = $release
            WHERE component_key = 'machine' AND schema_version = 3;
            """;
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.Parameters.AddWithValue("$release", ReleaseId);
        var updated = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (updated != 1)
        {
            throw new InvalidDataException("Machine schema metadata could not be advanced from v3 to v4.");
        }

        command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version = 4;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO machine_schema_migration_history(
                migration_id, from_schema_version, to_schema_version, backup_path, migrated_utc, release_id)
            VALUES ($migration, 3, 4, $backup, $now, $release);
            """;
        command.Parameters.AddWithValue("$migration", V3ToV4MigrationId);
        command.Parameters.AddWithValue("$backup", backupPath);
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.Parameters.AddWithValue("$release", ReleaseId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        transaction.Commit();
    }

    private static async Task MigrateV4ToV5Async(
        SqliteConnection connection,
        string backupPath,
        CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();
        await CreateSchemaV5Async(connection, transaction, cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE schema_metadata
            SET schema_version = 5,
                last_migrated_utc = $now,
                release_id = $release
            WHERE component_key = 'machine' AND schema_version = 4;
            """;
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.Parameters.AddWithValue("$release", ReleaseId);
        var updated = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (updated != 1)
        {
            throw new InvalidDataException("Machine schema metadata could not be advanced from v4 to v5.");
        }

        command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version = 5;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO machine_schema_migration_history(
                migration_id, from_schema_version, to_schema_version, backup_path, migrated_utc, release_id)
            VALUES ($migration, 4, 5, $backup, $now, $release);
            """;
        command.Parameters.AddWithValue("$migration", V4ToV5MigrationId);
        command.Parameters.AddWithValue("$backup", backupPath);
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.Parameters.AddWithValue("$release", ReleaseId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        transaction.Commit();
    }

    private static async Task MigrateV5ToV6Async(
        SqliteConnection connection,
        string backupPath,
        CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();
        await CreateSchemaV6Async(connection, transaction, cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE schema_metadata
            SET schema_version = 6,
                last_migrated_utc = $now,
                release_id = $release
            WHERE component_key = 'machine' AND schema_version = 5;
            """;
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.Parameters.AddWithValue("$release", ReleaseId);
        var updated = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (updated != 1)
            throw new InvalidDataException("Machine schema metadata could not be advanced from v5 to v6.");

        command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version = 6;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO machine_schema_migration_history(
                migration_id, from_schema_version, to_schema_version, backup_path, migrated_utc, release_id)
            VALUES ($migration, 5, 6, $backup, $now, $release);
            """;
        command.Parameters.AddWithValue("$migration", V5ToV6MigrationId);
        command.Parameters.AddWithValue("$backup", backupPath);
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.Parameters.AddWithValue("$release", ReleaseId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        transaction.Commit();
    }

    private static async Task VerifyLegacyV1CanonicalAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, "schema_metadata", cancellationToken).ConfigureAwait(false) ||
            !await TableExistsAsync(connection, "operational_mode_state", cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException("Legacy machine schema is missing required canonical tables.");
        }

        var command = connection.CreateCommand();
        command.CommandText = "SELECT schema_version FROM schema_metadata WHERE component_key = 'machine';";
        var metadataVersion = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (metadataVersion is null or DBNull || Convert.ToInt32(metadataVersion) != LegacySchemaVersion)
        {
            throw new InvalidDataException("Legacy machine schema metadata does not match physical schema v1.");
        }

        command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM operational_mode_state WHERE singleton_id = 1;";
        var singletonCount = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        if (singletonCount != 1)
        {
            throw new InvalidDataException("Legacy machine canonical OperationalModeState singleton is missing.");
        }
    }

    private static async Task VerifyV2CanonicalAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        foreach (var table in new[]
                 {
                     "schema_metadata",
                     "operational_mode_state",
                     "machine_operation_idempotency",
                     "machine_schema_migration_history"
                 })
        {
            if (!await TableExistsAsync(connection, table, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidDataException($"Machine v2 canonical table {table} is missing.");
            }
        }

        var command = connection.CreateCommand();
        command.CommandText = "SELECT schema_version FROM schema_metadata WHERE component_key = 'machine';";
        var metadataVersion = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (metadataVersion is null or DBNull || Convert.ToInt32(metadataVersion) != PreviousSchemaVersion)
        {
            throw new InvalidDataException("Machine schema metadata does not match physical schema v2.");
        }

        command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM operational_mode_state WHERE singleton_id = 1;";
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 1)
        {
            throw new InvalidDataException("Machine v2 canonical OperationalModeState singleton is missing.");
        }
    }

    private static async Task VerifyV3CanonicalAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        foreach (var table in new[]
                 {
                     "schema_metadata",
                     "operational_mode_state",
                     "machine_operation_idempotency",
                     "machine_schema_migration_history",
                     "machine_mutation_lease",
                     "mode_transition"
                 })
        {
            if (!await TableExistsAsync(connection, table, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidDataException($"Machine v3 canonical table {table} is missing.");
            }
        }

        var command = connection.CreateCommand();
        command.CommandText = "SELECT schema_version FROM schema_metadata WHERE component_key = 'machine';";
        var metadataVersion = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (metadataVersion is null or DBNull || Convert.ToInt32(metadataVersion) != Slice03FoundationSchemaVersion)
        {
            throw new InvalidDataException("Machine schema metadata does not match physical schema v3.");
        }

        command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM operational_mode_state WHERE singleton_id = 1;";
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 1)
        {
            throw new InvalidDataException("Machine v3 canonical OperationalModeState singleton is missing.");
        }

        command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM machine_mutation_lease WHERE singleton_id = 1;";
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 1)
        {
            throw new InvalidDataException("Machine v3 canonical major mutation lease singleton is missing.");
        }
    }

    private static async Task VerifyV4CanonicalAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        foreach (var table in new[]
                 {
                     "schema_metadata",
                     "operational_mode_state",
                     "machine_operation_idempotency",
                     "machine_schema_migration_history",
                     "machine_mutation_lease",
                     "mode_transition",
                     "mode_transition_policy_binding",
                     "mode_transition_policy_fallback"
                 })
        {
            if (!await TableExistsAsync(connection, table, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidDataException($"Machine v4 canonical table {table} is missing.");
            }
        }

        var command = connection.CreateCommand();
        command.CommandText = "SELECT schema_version FROM schema_metadata WHERE component_key = 'machine';";
        var metadataVersion = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (metadataVersion is null or DBNull || Convert.ToInt32(metadataVersion) != Slice03PolicySchemaVersion)
        {
            throw new InvalidDataException("Machine schema metadata does not match physical schema v4.");
        }

        command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM operational_mode_state WHERE singleton_id = 1;";
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 1)
        {
            throw new InvalidDataException("Machine v4 canonical OperationalModeState singleton is missing.");
        }

        command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM machine_mutation_lease WHERE singleton_id = 1;";
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 1)
        {
            throw new InvalidDataException("Machine v4 canonical major mutation lease singleton is missing.");
        }

        command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM mode_transition_policy_binding binding
            JOIN mode_transition transition_row ON transition_row.transition_id = binding.transition_id
            WHERE trim(binding.policy_catalog_id) = ''
               OR binding.policy_version < 1
               OR trim(binding.policy_release_id) = ''
               OR length(binding.policy_catalog_digest) != 64
               OR length(binding.resolved_policy_digest) != 64
               OR binding.policy_target NOT IN ('BASE','WORK','GAME')
               OR (transition_row.target_mode = 'NONE' AND binding.policy_target != 'BASE')
               OR (transition_row.target_mode = 'WORK' AND binding.policy_target != 'WORK')
               OR (transition_row.target_mode = 'GAME' AND binding.policy_target != 'GAME');
            """;
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0)
        {
            throw new InvalidDataException("Machine canonical resolved mode policy binding violates v4 invariants.");
        }
    }

    private static async Task VerifyV5CanonicalAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        foreach (var table in new[]
                 {
                     "schema_metadata", "operational_mode_state", "machine_operation_idempotency",
                     "machine_schema_migration_history", "machine_mutation_lease", "mode_transition",
                     "mode_transition_policy_binding", "mode_transition_policy_fallback",
                     "mode_transition_action_plan", "mode_transition_action"
                 })
        {
            if (!await TableExistsAsync(connection, table, cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException($"Machine v5 canonical table {table} is missing.");
        }

        var command = connection.CreateCommand();
        command.CommandText = "SELECT schema_version FROM schema_metadata WHERE component_key = 'machine';";
        var metadataVersion = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (metadataVersion is null or DBNull || Convert.ToInt32(metadataVersion) != Slice03ActionSchemaVersion)
            throw new InvalidDataException("Machine schema metadata does not match physical schema v6.");

        command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM operational_mode_state WHERE singleton_id = 1;";
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 1)
            throw new InvalidDataException("Machine v5 canonical OperationalModeState singleton is missing.");

        command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM machine_mutation_lease WHERE singleton_id = 1;";
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 1)
            throw new InvalidDataException("Machine v5 canonical major mutation lease singleton is missing.");

        command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM mode_transition_action_plan plan
            LEFT JOIN (
                SELECT transition_id, COUNT(*) AS actual_count
                FROM mode_transition_action
                GROUP BY transition_id
            ) actions ON actions.transition_id = plan.transition_id
            WHERE plan.plan_schema_version < 1
               OR length(plan.plan_digest) != 64
               OR plan.action_count NOT BETWEEN 1 AND 512
               OR plan.transition_revision < 1
               OR plan.action_count != COALESCE(actions.actual_count, 0);
            """;
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0)
            throw new InvalidDataException("Machine v5 canonical mode action plan is inconsistent.");
    }

    private static async Task VerifyCanonicalInvariantsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        foreach (var table in new[]
                 {
                     "schema_metadata",
                     "operational_mode_state",
                     "machine_operation_idempotency",
                     "machine_schema_migration_history",
                     "machine_mutation_lease",
                     "mode_transition",
                     "mode_transition_policy_binding",
                     "mode_transition_policy_fallback",
                     "mode_transition_action_plan",
                     "mode_transition_action"
                 })
        {
            if (!await TableExistsAsync(connection, table, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidDataException($"Machine canonical table {table} is missing.");
            }
        }

        var command = connection.CreateCommand();
        command.CommandText = "SELECT schema_version FROM schema_metadata WHERE component_key = 'machine';";
        var metadataVersion = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (metadataVersion is null or DBNull || Convert.ToInt32(metadataVersion) != SchemaVersion)
        {
            throw new InvalidDataException("Machine schema metadata does not match physical schema v5.");
        }

        command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM operational_mode_state WHERE singleton_id = 1;";
        var singletonCount = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        if (singletonCount != 1)
        {
            throw new InvalidDataException("Machine canonical OperationalModeState singleton is missing.");
        }

        foreach (var column in new[]
                 {
                     "control_session_key", "activation_epoch_id", "policy_catalog_id", "policy_version",
                     "policy_release_id", "policy_catalog_digest", "policy_target", "resolved_policy_digest"
                 })
        {
            if (!await ColumnExistsAsync(connection, null, column, cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException($"Machine v6 OperationalModeState column {column} is missing.");
        }

        command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM operational_mode_state
            WHERE (committed_mode = 'NONE' AND (
                       control_session_key IS NOT NULL OR activation_epoch_id IS NOT NULL OR
                       policy_catalog_id IS NOT NULL OR policy_version IS NOT NULL OR policy_release_id IS NOT NULL OR
                       policy_catalog_digest IS NOT NULL OR policy_target IS NOT NULL OR resolved_policy_digest IS NOT NULL
                   ))
               OR (committed_mode IN ('WORK','GAME') AND NOT (
                       (control_session_key IS NULL AND activation_epoch_id IS NULL AND
                        policy_catalog_id IS NULL AND policy_version IS NULL AND policy_release_id IS NULL AND
                        policy_catalog_digest IS NULL AND policy_target IS NULL AND resolved_policy_digest IS NULL)
                       OR
                       (trim(control_session_key) != '' AND activation_epoch_id IS NOT NULL AND
                        trim(policy_catalog_id) != '' AND policy_version >= 1 AND trim(policy_release_id) != '' AND
                        length(policy_catalog_digest) = 64 AND policy_target = committed_mode AND
                        length(resolved_policy_digest) = 64)
                   ));
            """;
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0)
            throw new InvalidDataException("Machine canonical OperationalMode identity violates v6 invariants.");

        // Also parse typed identity values so malformed GUIDs cannot survive canonical validation.
        _ = await ReadOperationalModeAsync(connection, null, cancellationToken).ConfigureAwait(false);

        command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM machine_mutation_lease WHERE singleton_id = 1;";
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 1)
        {
            throw new InvalidDataException("Machine canonical major mutation lease singleton is missing.");
        }

        command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM machine_mutation_lease
            WHERE singleton_id != 1
               OR fence_token < 0
               OR revision < 1
               OR (
                    lease_id IS NULL AND (
                        mutation_type IS NOT NULL OR owner_operation_id IS NOT NULL OR owner_correlation_id IS NOT NULL OR
                        owner_control_session_key IS NOT NULL OR acquired_utc IS NOT NULL OR heartbeat_utc IS NOT NULL OR expires_utc IS NOT NULL
                    )
               )
               OR (
                    lease_id IS NOT NULL AND (
                        mutation_type NOT IN ('MODE','UPDATE','RECOVERY') OR owner_operation_id IS NULL OR owner_correlation_id IS NULL OR
                        trim(owner_control_session_key) = '' OR acquired_utc IS NULL OR heartbeat_utc IS NULL OR expires_utc IS NULL OR fence_token < 1
                    )
               );
            """;
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0)
        {
            throw new InvalidDataException("Machine canonical major mutation lease violates v6 invariants.");
        }

        command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM mode_transition_policy_binding binding
            JOIN mode_transition transition_row ON transition_row.transition_id = binding.transition_id
            WHERE trim(binding.policy_catalog_id) = ''
               OR binding.policy_version < 1
               OR trim(binding.policy_release_id) = ''
               OR length(binding.policy_catalog_digest) != 64
               OR length(binding.resolved_policy_digest) != 64
               OR binding.policy_target NOT IN ('BASE','WORK','GAME')
               OR (transition_row.target_mode = 'NONE' AND binding.policy_target != 'BASE')
               OR (transition_row.target_mode = 'WORK' AND binding.policy_target != 'WORK')
               OR (transition_row.target_mode = 'GAME' AND binding.policy_target != 'GAME');
            """;
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0)
        {
            throw new InvalidDataException("Machine canonical resolved mode policy binding violates v6 invariants.");
        }

        command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM mode_transition_action_plan plan
            LEFT JOIN (
                SELECT transition_id, COUNT(*) AS actual_count
                FROM mode_transition_action
                GROUP BY transition_id
            ) actions ON actions.transition_id = plan.transition_id
            WHERE plan.plan_schema_version < 1
               OR length(plan.plan_digest) != 64
               OR plan.action_count < 1
               OR plan.action_count > 512
               OR plan.transition_revision < 1
               OR plan.action_count != COALESCE(actions.actual_count, 0);
            """;
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0)
        {
            throw new InvalidDataException("Machine canonical mode action plan violates v6 invariants.");
        }

        command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM mode_transition_action
            WHERE sequence_no < 1
               OR trim(owning_module) = ''
               OR trim(action_type) = ''
               OR desired_schema_version < 1
               OR length(desired_state_digest) != 64
               OR mandatory NOT IN (0,1)
               OR trim(rollback_class) = ''
               OR trim(verification_class) = ''
               OR action_state NOT IN (
                    'PLANNED','APPLYING','APPLIED','VERIFYING','VERIFIED','FAILED',
                    'ROLLING_BACK','ROLLED_BACK','ROLLBACK_FAILED','SKIPPED')
               OR (pre_state_digest IS NOT NULL AND length(pre_state_digest) != 64)
               OR revision < 1;
            """;
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0)
        {
            throw new InvalidDataException("Machine canonical mode action journal violates v6 invariants.");
        }
    }

    private static async Task<bool> TableExistsAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 1;
    }

    private static async Task<bool> ColumnExistsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string columnName,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('operational_mode_state') WHERE name = $name;";
        command.Parameters.AddWithValue("$name", columnName);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 1;
    }

    private static async Task<OperationalModeRecord> ReadOperationalModeAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT committed_mode, committed_utc, committed_by_operation_id, correlation_id, revision, updated_utc,
                   control_session_key, activation_epoch_id, policy_catalog_id, policy_version,
                   policy_release_id, policy_catalog_digest, policy_target, resolved_policy_digest
            FROM operational_mode_state WHERE singleton_id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("OperationalModeState singleton is missing from machine canonical storage.");

        var hasAnyIdentity = Enumerable.Range(6, 8).Any(index => !reader.IsDBNull(index));
        var hasFullIdentity = Enumerable.Range(6, 8).All(index => !reader.IsDBNull(index));
        if (hasAnyIdentity && !hasFullIdentity)
            throw new InvalidDataException("OperationalModeState contains a partial managed identity tuple.");

        Guid? activationEpochId = null;
        PersistedModePolicyIdentity? policyIdentity = null;
        PersistedModePolicyTarget? policyTarget = null;
        string? resolvedPolicyDigest = null;
        string? controlSessionKey = null;
        if (hasFullIdentity)
        {
            controlSessionKey = reader.GetString(6);
            if (!Guid.TryParse(reader.GetString(7), out var parsedEpoch) || parsedEpoch == Guid.Empty)
                throw new InvalidDataException("OperationalModeState activation epoch is malformed.");
            activationEpochId = parsedEpoch;
            policyIdentity = new PersistedModePolicyIdentity(
                reader.GetString(8),
                reader.GetInt64(9),
                reader.GetString(10),
                reader.GetString(11));
            policyTarget = reader.GetString(12) switch
            {
                "BASE" => PersistedModePolicyTarget.Base,
                "WORK" => PersistedModePolicyTarget.Work,
                "GAME" => PersistedModePolicyTarget.Game,
                _ => throw new InvalidDataException("OperationalModeState policy target is malformed.")
            };
            resolvedPolicyDigest = reader.GetString(13);
        }

        return new OperationalModeRecord(
            reader.GetString(0),
            DateTimeOffset.Parse(reader.GetString(1)),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetInt32(4),
            DateTimeOffset.Parse(reader.GetString(5)),
            controlSessionKey,
            activationEpochId,
            policyIdentity,
            policyTarget,
            resolvedPolicyDigest);
    }

    private static async Task<IdempotencyEntry?> ReadIdempotencyEntryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string operationId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT request_kind, target_mode, expected_revision, correlation_id,
                   result_mode, result_revision, result_committed_utc, result_updated_utc
            FROM machine_operation_idempotency
            WHERE operation_id = $operation;
            """;
        command.Parameters.AddWithValue("$operation", operationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;

        return new IdempotencyEntry(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt32(5),
            DateTimeOffset.Parse(reader.GetString(6)),
            DateTimeOffset.Parse(reader.GetString(7)));
    }

    private static void ValidateTargetMode(string targetMode)
    {
        if (!string.Equals(targetMode, "NONE", StringComparison.Ordinal))
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetMode),
                targetMode,
                "Direct OperationalMode writes permit only NONE convergence; WORK/GAME require the atomic mode-transition commit boundary.");
        }
    }

    private static bool IsSqliteCorruption(SqliteException exception)
        => exception.SqliteErrorCode is 11 or 26;

    private sealed record IdempotencyEntry(
        string RequestKind,
        string TargetMode,
        int ExpectedRevision,
        string CorrelationId,
        string ResultMode,
        int ResultRevision,
        DateTimeOffset ResultCommittedUtc,
        DateTimeOffset ResultUpdatedUtc)
    {
        public OperationalModeRecord ToOperationalModeRecord(string operationId)
            => new(
                ResultMode,
                ResultCommittedUtc,
                operationId,
                CorrelationId,
                ResultRevision,
                ResultUpdatedUtc);
    }
}
