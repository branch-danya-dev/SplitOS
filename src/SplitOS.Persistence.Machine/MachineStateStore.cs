using Microsoft.Data.Sqlite;
using SplitOS.Persistence;

namespace SplitOS.Persistence.Machine;

public sealed record OperationalModeRecord(
    string CommittedMode,
    DateTimeOffset CommittedUtc,
    string CommittedByOperationId,
    string? CorrelationId,
    int Revision,
    DateTimeOffset UpdatedUtc);

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
    public const int SchemaVersion = 2;
    private const int LegacySchemaVersion = 1;
    private const string ReleaseId = "development";
    private const string OperationRequestKind = "OPERATIONAL_MODE_WRITE";

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
                await CreateSchemaV2Async(connection, null, cancellationToken).ConfigureAwait(false);
                await EnsureInitialStateAsync(connection, cancellationToken).ConfigureAwait(false);
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
                    }
                }
                else if (currentVersion != SchemaVersion)
                {
                    throw new InvalidDataException(
                        $"Machine canonical schema version {currentVersion} is not supported by runtime schema {SchemaVersion}.");
                }

                if (corruptionReason is null)
                {
                    try
                    {
                        await _database.InitializeMetadataAsync(connection, "machine", cancellationToken).ConfigureAwait(false);
                    }
                    catch (InvalidDataException ex)
                    {
                        corruptionReason = ex;
                    }

                    if (corruptionReason is null)
                    {
                        await CreateSchemaV2Async(connection, null, cancellationToken).ConfigureAwait(false);
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
            VALUES ('machine-v1-v2', 1, 2, $backup, $now, $release);
            """;
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

    private static async Task VerifyCanonicalInvariantsAsync(
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
                throw new InvalidDataException($"Machine canonical table {table} is missing.");
            }
        }

        var command = connection.CreateCommand();
        command.CommandText = "SELECT schema_version FROM schema_metadata WHERE component_key = 'machine';";
        var metadataVersion = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (metadataVersion is null or DBNull || Convert.ToInt32(metadataVersion) != SchemaVersion)
        {
            throw new InvalidDataException("Machine schema metadata does not match physical schema v2.");
        }

        command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM operational_mode_state WHERE singleton_id = 1;";
        var singletonCount = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        if (singletonCount != 1)
        {
            throw new InvalidDataException("Machine canonical OperationalModeState singleton is missing.");
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

    private static async Task<OperationalModeRecord> ReadOperationalModeAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT committed_mode, committed_utc, committed_by_operation_id, correlation_id, revision, updated_utc
            FROM operational_mode_state WHERE singleton_id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException("OperationalModeState singleton is missing from machine canonical storage.");
        }

        return new OperationalModeRecord(
            reader.GetString(0),
            DateTimeOffset.Parse(reader.GetString(1)),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetInt32(4),
            DateTimeOffset.Parse(reader.GetString(5)));
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
        if (targetMode is not ("NONE" or "WORK" or "GAME"))
        {
            throw new ArgumentOutOfRangeException(nameof(targetMode), targetMode, "Operational mode must be NONE, WORK, or GAME.");
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
