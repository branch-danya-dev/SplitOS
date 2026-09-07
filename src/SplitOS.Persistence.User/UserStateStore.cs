using Microsoft.Data.Sqlite;
using SplitOS.Persistence;

namespace SplitOS.Persistence.User;

public sealed record UserAccountAssociationRecord(
    string AssociationId,
    string WindowsUserSid,
    string AccountId,
    string AssociationState,
    DateTimeOffset AssociatedUtc,
    DateTimeOffset? LastAuthenticatedUtc,
    string? LastEntitlementVersion,
    DateTimeOffset? LastEntitlementObservedUtc,
    DateTimeOffset? LastServerUtc,
    string? SecretReference,
    int Revision,
    DateTimeOffset UpdatedUtc,
    string UpdatedByOperationId);

public enum UserAssociationWriteDisposition
{
    Applied,
    Unchanged,
    RevisionConflict,
    AlreadyExists,
    Missing
}

public sealed record UserAssociationWriteOutcome(
    UserAssociationWriteDisposition Disposition,
    UserAccountAssociationRecord? Record,
    int? ActualRevision,
    string? Detail);

public interface IUserAccountAssociationStore
{
    Task<UserAccountAssociationRecord?> GetAccountAssociationAsync(CancellationToken cancellationToken = default);

    Task<UserAssociationWriteOutcome> CreateActiveAssociationAsync(
        Guid associationId,
        string windowsUserSid,
        string accountId,
        DateTimeOffset authenticatedUtc,
        string secretReference,
        string? entitlementVersion,
        DateTimeOffset? entitlementObservedUtc,
        DateTimeOffset? lastServerUtc,
        Guid operationId,
        CancellationToken cancellationToken = default);

    Task<UserAssociationWriteOutcome> MarkReauthRequiredAsync(
        int expectedRevision,
        Guid operationId,
        CancellationToken cancellationToken = default);
}

public sealed class UserStateStore : IUserAccountAssociationStore
{
    public const int SchemaVersion = 2;
    private const int LegacySchemaVersion = 1;
    private const string ReleaseId = "development";
    private const string MigrationId = "user-v1-v2";

    private readonly SqliteDatabase _database;
    private readonly string _databasePath;
    private readonly string _backupDirectory;

    public UserStateStore(string? databasePath = null, string? backupDirectory = null)
    {
        _databasePath = databasePath ?? StoragePaths.UserDatabase;
        var customRoot = databasePath is null ? null : Path.GetDirectoryName(Path.GetFullPath(_databasePath));
        _backupDirectory = backupDirectory ?? (customRoot is null
            ? StoragePaths.UserBackupRoot
            : Path.Combine(customRoot, "backups"));
        _database = new SqliteDatabase(new SqliteDatabaseOptions(
            _databasePath,
            SplitOSDatabaseRole.User,
            SchemaVersion,
            ReleaseId));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        var currentVersion = await _database.ReadSchemaVersionAsync(connection, cancellationToken).ConfigureAwait(false);

        if (currentVersion == 0)
        {
            await _database.InitializeMetadataAsync(connection, "user", cancellationToken).ConfigureAwait(false);
            await CreateSchemaV2Async(connection, null, cancellationToken).ConfigureAwait(false);
        }
        else if (currentVersion == LegacySchemaVersion)
        {
            await _database.VerifyQuickCheckAsync(connection, cancellationToken).ConfigureAwait(false);
            await VerifyLegacyV1CanonicalAsync(connection, cancellationToken).ConfigureAwait(false);
            var backup = await CanonicalStoreRecovery.CreateVerifiedBackupAsync(
                connection,
                _databasePath,
                _backupDirectory,
                LegacySchemaVersion,
                cancellationToken).ConfigureAwait(false);
            await MigrateV1ToV2Async(connection, backup.BackupPath, cancellationToken).ConfigureAwait(false);
        }
        else if (currentVersion == SchemaVersion)
        {
            await _database.InitializeMetadataAsync(connection, "user", cancellationToken).ConfigureAwait(false);
            await CreateSchemaV2Async(connection, null, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            throw new InvalidDataException(
                $"User canonical schema version {currentVersion} is not supported by runtime schema {SchemaVersion}.");
        }

        await _database.VerifyIntegrityAsync(connection, cancellationToken).ConfigureAwait(false);
        await VerifyCanonicalInvariantsAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GetAssociationStateAsync(CancellationToken cancellationToken = default)
    {
        var association = await GetAccountAssociationAsync(cancellationToken).ConfigureAwait(false);
        return association?.AssociationState ?? "UNASSOCIATED";
    }

    public async Task<UserAccountAssociationRecord?> GetAccountAssociationAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenCurrentSchemaAsync(cancellationToken).ConfigureAwait(false);
        return await ReadAssociationAsync(connection, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<UserAssociationWriteOutcome> CreateActiveAssociationAsync(
        Guid associationId,
        string windowsUserSid,
        string accountId,
        DateTimeOffset authenticatedUtc,
        string secretReference,
        string? entitlementVersion,
        DateTimeOffset? entitlementObservedUtc,
        DateTimeOffset? lastServerUtc,
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        if (associationId == Guid.Empty) throw new ArgumentException("Association id must not be empty.", nameof(associationId));
        if (operationId == Guid.Empty) throw new ArgumentException("Operation id must not be empty.", nameof(operationId));
        if (string.IsNullOrWhiteSpace(windowsUserSid)) throw new ArgumentException("Windows user SID is required.", nameof(windowsUserSid));
        if (string.IsNullOrWhiteSpace(accountId)) throw new ArgumentException("Account id is required.", nameof(accountId));
        if (string.IsNullOrWhiteSpace(secretReference)) throw new ArgumentException("Secret reference is required.", nameof(secretReference));
        if (authenticatedUtc == default) throw new ArgumentException("Authentication timestamp is required.", nameof(authenticatedUtc));

        await using var connection = await OpenCurrentSchemaAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        var existing = await ReadAssociationAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return new UserAssociationWriteOutcome(
                UserAssociationWriteDisposition.AlreadyExists,
                existing,
                existing.Revision,
                "An account association already exists for this Windows user store.");
        }

        var now = DateTimeOffset.UtcNow;
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO account_association(
                singleton_id, association_id, windows_user_sid, splitos_account_id, association_state,
                associated_utc, last_authenticated_utc, last_entitlement_version,
                last_entitlement_observed_utc, last_server_utc, secret_reference,
                revision, updated_utc, updated_by_operation_id)
            VALUES(
                1, $association, $sid, $account, 'ACTIVE',
                $associated, $authenticated, $entitlement_version,
                $entitlement_observed, $last_server, $secret_reference,
                1, $updated, $operation);
            """;
        command.Parameters.AddWithValue("$association", associationId.ToString("D"));
        command.Parameters.AddWithValue("$sid", windowsUserSid);
        command.Parameters.AddWithValue("$account", accountId);
        command.Parameters.AddWithValue("$associated", now.ToString("O"));
        command.Parameters.AddWithValue("$authenticated", authenticatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$entitlement_version", (object?)entitlementVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("$entitlement_observed", entitlementObservedUtc is null ? DBNull.Value : entitlementObservedUtc.Value.ToString("O"));
        command.Parameters.AddWithValue("$last_server", lastServerUtc is null ? DBNull.Value : lastServerUtc.Value.ToString("O"));
        command.Parameters.AddWithValue("$secret_reference", secretReference);
        command.Parameters.AddWithValue("$updated", now.ToString("O"));
        command.Parameters.AddWithValue("$operation", operationId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        transaction.Commit();
        return new UserAssociationWriteOutcome(
            UserAssociationWriteDisposition.Applied,
            new UserAccountAssociationRecord(
                associationId.ToString("D"),
                windowsUserSid,
                accountId,
                "ACTIVE",
                now,
                authenticatedUtc,
                entitlementVersion,
                entitlementObservedUtc,
                lastServerUtc,
                secretReference,
                1,
                now,
                operationId.ToString("D")),
            1,
            null);
    }

    public async Task<UserAssociationWriteOutcome> MarkReauthRequiredAsync(
        int expectedRevision,
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        if (expectedRevision < 1) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        if (operationId == Guid.Empty) throw new ArgumentException("Operation id must not be empty.", nameof(operationId));

        await using var connection = await OpenCurrentSchemaAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        var current = await ReadAssociationAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            return new UserAssociationWriteOutcome(
                UserAssociationWriteDisposition.Missing,
                null,
                null,
                "No account association exists.");
        }

        if (current.Revision != expectedRevision)
        {
            return new UserAssociationWriteOutcome(
                UserAssociationWriteDisposition.RevisionConflict,
                current,
                current.Revision,
                $"Expected revision {expectedRevision}, actual revision {current.Revision}.");
        }

        if (string.Equals(current.AssociationState, "REAUTH_REQUIRED", StringComparison.Ordinal))
        {
            return new UserAssociationWriteOutcome(
                UserAssociationWriteDisposition.Unchanged,
                current,
                current.Revision,
                null);
        }

        var now = DateTimeOffset.UtcNow;
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE account_association
            SET association_state = 'REAUTH_REQUIRED',
                revision = revision + 1,
                updated_utc = $updated,
                updated_by_operation_id = $operation
            WHERE singleton_id = 1 AND revision = $expected_revision;
            """;
        command.Parameters.AddWithValue("$updated", now.ToString("O"));
        command.Parameters.AddWithValue("$operation", operationId.ToString("D"));
        command.Parameters.AddWithValue("$expected_revision", expectedRevision);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            var actual = await ReadAssociationAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            return new UserAssociationWriteOutcome(
                UserAssociationWriteDisposition.RevisionConflict,
                actual,
                actual?.Revision,
                "Account association changed concurrently.");
        }

        transaction.Commit();
        return new UserAssociationWriteOutcome(
            UserAssociationWriteDisposition.Applied,
            current with
            {
                AssociationState = "REAUTH_REQUIRED",
                Revision = checked(current.Revision + 1),
                UpdatedUtc = now,
                UpdatedByOperationId = operationId.ToString("D")
            },
            checked(current.Revision + 1),
            null);
    }

    private async Task<SqliteConnection> OpenCurrentSchemaAsync(CancellationToken cancellationToken)
    {
        var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var version = await _database.ReadSchemaVersionAsync(connection, cancellationToken).ConfigureAwait(false);
            if (version != SchemaVersion)
            {
                throw new InvalidDataException($"User store access requires schema {SchemaVersion}, got {version}.");
            }
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task CreateSchemaV2Async(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS account_association (
                singleton_id INTEGER PRIMARY KEY CHECK(singleton_id = 1),
                association_id TEXT NOT NULL,
                windows_user_sid TEXT NOT NULL,
                splitos_account_id TEXT NOT NULL,
                association_state TEXT NOT NULL CHECK(association_state IN ('ACTIVE','REAUTH_REQUIRED')),
                associated_utc TEXT NOT NULL,
                last_authenticated_utc TEXT NULL,
                last_entitlement_version TEXT NULL,
                last_entitlement_observed_utc TEXT NULL,
                last_server_utc TEXT NULL,
                secret_reference TEXT NULL,
                revision INTEGER NOT NULL CHECK(revision >= 1),
                updated_utc TEXT NOT NULL,
                updated_by_operation_id TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS user_schema_migration_history (
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

    private static async Task MigrateV1ToV2Async(
        SqliteConnection connection,
        string backupPath,
        CancellationToken cancellationToken)
    {
        var legacy = await ReadLegacyAssociationAsync(connection, cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        var now = DateTimeOffset.UtcNow;

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "ALTER TABLE account_association RENAME TO account_association_v1;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await CreateSchemaV2Async(connection, transaction, cancellationToken).ConfigureAwait(false);

        if (legacy is not null && !string.IsNullOrWhiteSpace(legacy.AccountId) && !string.IsNullOrWhiteSpace(legacy.WindowsUserSid))
        {
            var associatedUtc = ParseNullableUtc(legacy.AssociatedUtc) ?? now;
            var authenticatedUtc = ParseNullableUtc(legacy.LastValidatedUtc);
            var newRevision = checked(Math.Max(legacy.Revision, 1) + 1);
            command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO account_association(
                    singleton_id, association_id, windows_user_sid, splitos_account_id, association_state,
                    associated_utc, last_authenticated_utc, last_entitlement_version,
                    last_entitlement_observed_utc, last_server_utc, secret_reference,
                    revision, updated_utc, updated_by_operation_id)
                VALUES(
                    1, $association, $sid, $account, 'REAUTH_REQUIRED',
                    $associated, $authenticated, NULL,
                    NULL, NULL, $secret_reference,
                    $revision, $updated, $operation);
                """;
            command.Parameters.AddWithValue("$association", Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue("$sid", legacy.WindowsUserSid);
            command.Parameters.AddWithValue("$account", legacy.AccountId);
            command.Parameters.AddWithValue("$associated", associatedUtc.ToString("O"));
            command.Parameters.AddWithValue("$authenticated", authenticatedUtc is null ? DBNull.Value : authenticatedUtc.Value.ToString("O"));
            command.Parameters.AddWithValue("$secret_reference", (object?)legacy.SecretReference ?? DBNull.Value);
            command.Parameters.AddWithValue("$revision", newRevision);
            command.Parameters.AddWithValue("$updated", now.ToString("O"));
            command.Parameters.AddWithValue("$operation", "migration:user-v1-v2");
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DROP TABLE account_association_v1;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE schema_metadata
            SET schema_version = 2,
                last_migrated_utc = $now,
                release_id = $release
            WHERE component_key = 'user' AND schema_version = 1;
            """;
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.Parameters.AddWithValue("$release", ReleaseId);
        var metadataUpdated = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (metadataUpdated != 1)
        {
            throw new InvalidDataException("Legacy user schema metadata could not be advanced from v1 to v2.");
        }

        command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version = 2;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO user_schema_migration_history(
                migration_id, from_schema_version, to_schema_version, backup_path, migrated_utc, release_id)
            VALUES($migration, 1, 2, $backup, $now, $release);
            """;
        command.Parameters.AddWithValue("$migration", MigrationId);
        command.Parameters.AddWithValue("$backup", backupPath);
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.Parameters.AddWithValue("$release", ReleaseId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        transaction.Commit();
    }

    private static async Task VerifyLegacyV1CanonicalAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, "schema_metadata", cancellationToken).ConfigureAwait(false) ||
            !await TableExistsAsync(connection, "account_association", cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException("Legacy user schema is missing required canonical tables.");
        }

        var command = connection.CreateCommand();
        command.CommandText = "SELECT schema_version FROM schema_metadata WHERE component_key = 'user';";
        var metadata = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (metadata is null or DBNull || Convert.ToInt32(metadata) != LegacySchemaVersion)
        {
            throw new InvalidDataException("Legacy user schema metadata is missing or inconsistent.");
        }

        command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM account_association;";
        var rowCount = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        if (rowCount is < 0 or > 1)
        {
            throw new InvalidDataException($"Legacy user association cardinality is invalid: {rowCount} rows.");
        }
    }

    private static async Task VerifyCanonicalInvariantsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, "schema_metadata", cancellationToken).ConfigureAwait(false) ||
            !await TableExistsAsync(connection, "account_association", cancellationToken).ConfigureAwait(false) ||
            !await TableExistsAsync(connection, "user_schema_migration_history", cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException("User canonical schema is missing required tables.");
        }

        var command = connection.CreateCommand();
        command.CommandText = "SELECT schema_version FROM schema_metadata WHERE component_key = 'user';";
        var metadata = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (metadata is null or DBNull || Convert.ToInt32(metadata) != SchemaVersion)
        {
            throw new InvalidDataException("User schema metadata is missing or inconsistent.");
        }

        command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM account_association
            WHERE singleton_id != 1
               OR trim(association_id) = ''
               OR trim(windows_user_sid) = ''
               OR trim(splitos_account_id) = ''
               OR association_state NOT IN ('ACTIVE','REAUTH_REQUIRED')
               OR revision < 1
               OR trim(updated_by_operation_id) = '';
            """;
        var invalidRows = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        if (invalidRows != 0)
        {
            throw new InvalidDataException("User account association violates canonical v2 invariants.");
        }
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string tableName, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 1;
    }

    private static async Task<UserAccountAssociationRecord?> ReadAssociationAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT association_id, windows_user_sid, splitos_account_id, association_state,
                   associated_utc, last_authenticated_utc, last_entitlement_version,
                   last_entitlement_observed_utc, last_server_utc, secret_reference,
                   revision, updated_utc, updated_by_operation_id
            FROM account_association
            WHERE singleton_id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;

        return new UserAccountAssociationRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            DateTimeOffset.Parse(reader.GetString(4)),
            reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5)),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7)),
            reader.IsDBNull(8) ? null : DateTimeOffset.Parse(reader.GetString(8)),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.GetInt32(10),
            DateTimeOffset.Parse(reader.GetString(11)),
            reader.GetString(12));
    }

    private static async Task<LegacyAssociation?> ReadLegacyAssociationAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT windows_user_sid, splitos_account_id, associated_utc, last_validated_utc,
                   secret_reference, revision, updated_utc
            FROM account_association
            WHERE singleton_id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;

        return new LegacyAssociation(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetInt32(5),
            reader.GetString(6));
    }

    private static DateTimeOffset? ParseNullableUtc(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : DateTimeOffset.Parse(value);

    private sealed record LegacyAssociation(
        string WindowsUserSid,
        string? AccountId,
        string? AssociatedUtc,
        string? LastValidatedUtc,
        string? SecretReference,
        int Revision,
        string UpdatedUtc);
}
