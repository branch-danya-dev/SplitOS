using Microsoft.Data.Sqlite;
using SplitOS.Persistence;

namespace SplitOS.Persistence.User;

public enum UserAssociationRemovalDisposition
{
    Removed,
    Missing,
    RevisionConflict,
    InvalidState
}

public sealed record UserAssociationRemovalOutcome(
    UserAssociationRemovalDisposition Disposition,
    UserAccountAssociationRecord? Record,
    int? ActualRevision,
    string? Detail);

public interface IUserAccountAssociationSignOutStore
{
    Task<UserAssociationRemovalOutcome> RemoveReauthAssociationAsync(
        string associationId,
        string windowsUserSid,
        string accountId,
        int expectedRevision,
        Guid operationId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Removes only the exact canonical association that has already converged to REAUTH_REQUIRED.
/// It never creates/replaces user state and it refuses to operate on missing/quarantined canonical storage.
/// </summary>
public sealed class UserAccountAssociationSignOutStore : IUserAccountAssociationSignOutStore
{
    private readonly string _databasePath;
    private readonly string _bootstrapMarkerPath;
    private readonly string _quarantineMarkerPath;

    public UserAccountAssociationSignOutStore(
        string? databasePath = null,
        string? bootstrapMarkerPath = null,
        string? quarantineMarkerPath = null)
    {
        _databasePath = databasePath ?? StoragePaths.UserDatabase;
        var customRoot = databasePath is null ? null : Path.GetDirectoryName(Path.GetFullPath(_databasePath));
        _bootstrapMarkerPath = bootstrapMarkerPath ?? (customRoot is null
            ? StoragePaths.UserBootstrapMarker
            : Path.Combine(customRoot, "user-store.initialized"));
        _quarantineMarkerPath = quarantineMarkerPath ?? (customRoot is null
            ? StoragePaths.UserQuarantineMarker
            : Path.Combine(customRoot, "user-store.quarantined.json"));
    }

    public async Task<UserAssociationRemovalOutcome> RemoveReauthAssociationAsync(
        string associationId,
        string windowsUserSid,
        string accountId,
        int expectedRevision,
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(associationId) || !Guid.TryParse(associationId, out _))
            throw new ArgumentException("Association id must be a non-empty GUID value.", nameof(associationId));
        if (string.IsNullOrWhiteSpace(windowsUserSid))
            throw new ArgumentException("Windows user SID is required.", nameof(windowsUserSid));
        if (string.IsNullOrWhiteSpace(accountId))
            throw new ArgumentException("Account id is required.", nameof(accountId));
        if (expectedRevision < 1)
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        if (operationId == Guid.Empty)
            throw new ArgumentException("Operation id must not be empty.", nameof(operationId));

        if (File.Exists(_quarantineMarkerPath))
            throw new InvalidDataException($"User canonical store is quarantined. Marker: {_quarantineMarkerPath}");

        if (!File.Exists(_bootstrapMarkerPath) || !File.Exists(_databasePath))
            throw new InvalidDataException(
                "User canonical store is not initialized or is missing. Sign-out cannot fabricate replacement user state.");

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        };

        await using var connection = new SqliteConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000; PRAGMA synchronous = FULL;";
        await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version;";
        var schemaVersion = Convert.ToInt32(await versionCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        if (schemaVersion != UserStateStore.SchemaVersion)
            throw new InvalidDataException(
                $"User association sign-out requires schema {UserStateStore.SchemaVersion}, got {schemaVersion}.");

        using var transaction = connection.BeginTransaction();
        var current = await ReadAssociationAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        if (current is null)
            return new UserAssociationRemovalOutcome(UserAssociationRemovalDisposition.Missing, null, null, "No account association exists.");

        if (current.Revision != expectedRevision ||
            !string.Equals(current.AssociationId, associationId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(current.WindowsUserSid, windowsUserSid, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(current.AccountId, accountId, StringComparison.Ordinal))
        {
            return new UserAssociationRemovalOutcome(
                UserAssociationRemovalDisposition.RevisionConflict,
                current,
                current.Revision,
                "The canonical account association changed before sign-out removal could commit.");
        }

        if (!string.Equals(current.AssociationState, "REAUTH_REQUIRED", StringComparison.Ordinal))
        {
            return new UserAssociationRemovalOutcome(
                UserAssociationRemovalDisposition.InvalidState,
                current,
                current.Revision,
                "Association must be REAUTH_REQUIRED before durable sign-out removal.");
        }

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM account_association
            WHERE singleton_id = 1
              AND revision = $revision
              AND association_state = 'REAUTH_REQUIRED'
              AND association_id = $association
              AND windows_user_sid = $sid
              AND splitos_account_id = $account;
            """;
        command.Parameters.AddWithValue("$revision", expectedRevision);
        command.Parameters.AddWithValue("$association", associationId);
        command.Parameters.AddWithValue("$sid", windowsUserSid);
        command.Parameters.AddWithValue("$account", accountId);

        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            var actual = await ReadAssociationAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            return new UserAssociationRemovalOutcome(
                actual is null ? UserAssociationRemovalDisposition.Missing : UserAssociationRemovalDisposition.RevisionConflict,
                actual,
                actual?.Revision,
                "The canonical account association changed concurrently during sign-out.");
        }

        transaction.Commit();
        return new UserAssociationRemovalOutcome(
            UserAssociationRemovalDisposition.Removed,
            null,
            expectedRevision,
            operationId.ToString("D"));
    }

    private static async Task<UserAccountAssociationRecord?> ReadAssociationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
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
}
