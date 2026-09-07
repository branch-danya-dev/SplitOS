using Microsoft.Data.Sqlite;
using SplitOS.Persistence;

namespace SplitOS.Persistence.Projection;

public sealed class ProjectionStore
{
    public const int SchemaVersion = 1;
    private readonly string _databasePath;

    public ProjectionStore(string? databasePath = null)
    {
        _databasePath = databasePath ?? StoragePaths.ProjectionDatabase;
    }

    public async Task InitializeOrRebuildAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && File.Exists(_databasePath))
        {
            SqliteConnection.ClearAllPools();
            DeleteProjectionFiles();
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var database = new SqliteDatabase(new SqliteDatabaseOptions(
            _databasePath,
            SplitOSDatabaseRole.Projection,
            SchemaVersion,
            "development"));
        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await database.InitializeMetadataAsync(connection, "projection", cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS game_client_projection (
                client_id TEXT PRIMARY KEY,
                client_type TEXT NOT NULL,
                availability_state TEXT NOT NULL,
                capability_status_json TEXT NOT NULL,
                observed_at_utc TEXT NOT NULL,
                expires_at_utc TEXT NULL,
                projection_schema_version INTEGER NOT NULL CHECK(projection_schema_version >= 1)
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await database.VerifyIntegrityAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    private void DeleteProjectionFiles()
    {
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var path = _databasePath + suffix;
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
