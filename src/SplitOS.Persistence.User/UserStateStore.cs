using Microsoft.Data.Sqlite;
using SplitOS.Persistence;

namespace SplitOS.Persistence.User;

public sealed class UserStateStore
{
    public const int SchemaVersion = 1;
    private readonly SqliteDatabase _database;

    public UserStateStore(string? databasePath = null)
    {
        _database = new SqliteDatabase(new SqliteDatabaseOptions(
            databasePath ?? StoragePaths.UserDatabase,
            SplitOSDatabaseRole.User,
            SchemaVersion,
            "development"));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await _database.InitializeMetadataAsync(connection, "user", cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS account_association (
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
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await _database.VerifyIntegrityAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GetAssociationStateAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT association_state FROM account_association WHERE singleton_id = 1;";
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null or DBNull ? "UNASSOCIATED" : Convert.ToString(value) ?? "UNASSOCIATED";
    }
}
