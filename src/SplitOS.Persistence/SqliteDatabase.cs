using Microsoft.Data.Sqlite;

namespace SplitOS.Persistence;

public enum SplitOSDatabaseRole
{
    Machine,
    User,
    Projection
}

public sealed record SqliteDatabaseOptions(
    string DatabasePath,
    SplitOSDatabaseRole Role,
    int SchemaVersion,
    string ReleaseId);

public sealed class SqliteDatabase(SqliteDatabaseOptions options)
{
    public SqliteDatabaseOptions Options { get; } = options;

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(Options.DatabasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = Options.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        };

        var connection = new SqliteConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureAsync(connection, cancellationToken).ConfigureAwait(false);
        return connection;
    }

    public async Task InitializeMetadataAsync(SqliteConnection connection, string componentKey, CancellationToken cancellationToken = default)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_metadata (
                component_key TEXT PRIMARY KEY,
                schema_version INTEGER NOT NULL CHECK(schema_version >= 1),
                created_utc TEXT NOT NULL,
                last_migrated_utc TEXT NOT NULL,
                release_id TEXT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var currentPhysicalVersion = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        if (currentPhysicalVersion != 0 && currentPhysicalVersion != Options.SchemaVersion)
        {
            throw new InvalidDataException($"SQLite schema version {currentPhysicalVersion} is incompatible with expected {Options.SchemaVersion} for {Options.Role}.");
        }

        command = connection.CreateCommand();
        command.CommandText = "SELECT schema_version FROM schema_metadata WHERE component_key = $component;";
        command.Parameters.AddWithValue("$component", componentKey);
        var metadataVersionRaw = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (metadataVersionRaw is not null and not DBNull && Convert.ToInt32(metadataVersionRaw) != Options.SchemaVersion)
        {
            throw new InvalidDataException($"SplitOS schema metadata is incompatible with expected version {Options.SchemaVersion} for {Options.Role}.");
        }

        var now = DateTimeOffset.UtcNow.ToString("O");
        command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO schema_metadata(component_key, schema_version, created_utc, last_migrated_utc, release_id)
            VALUES ($component, $schema, $now, $now, $release)
            ON CONFLICT(component_key) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$component", componentKey);
        command.Parameters.AddWithValue("$schema", Options.SchemaVersion);
        command.Parameters.AddWithValue("$now", now);
        command.Parameters.AddWithValue("$release", Options.ReleaseId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        if (currentPhysicalVersion == 0)
        {
            command = connection.CreateCommand();
            command.CommandText = $"PRAGMA user_version = {Options.SchemaVersion};";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task VerifyIntegrityAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        var quickCheck = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        if (!string.Equals(quickCheck, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"SQLite quick_check failed for {Options.Role}: {quickCheck}");
        }

        command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var userVersion = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        if (userVersion != Options.SchemaVersion)
        {
            throw new InvalidDataException($"SQLite schema version mismatch for {Options.Role}. Expected {Options.SchemaVersion}, got {userVersion}.");
        }
    }

    private async Task ConfigureAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var synchronous = Options.Role == SplitOSDatabaseRole.Projection ? "NORMAL" : "FULL";
        await ExecutePragmaAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken).ConfigureAwait(false);
        await ExecutePragmaAsync(connection, $"PRAGMA synchronous = {synchronous};", cancellationToken).ConfigureAwait(false);
        await ExecutePragmaAsync(connection, "PRAGMA foreign_keys = ON;", cancellationToken).ConfigureAwait(false);
        await ExecutePragmaAsync(connection, "PRAGMA busy_timeout = 5000;", cancellationToken).ConfigureAwait(false);

        var journalMode = Convert.ToString(await ScalarAsync(connection, "PRAGMA journal_mode;", cancellationToken).ConfigureAwait(false));
        var synchronousValue = Convert.ToInt32(await ScalarAsync(connection, "PRAGMA synchronous;", cancellationToken).ConfigureAwait(false));
        var foreignKeys = Convert.ToInt32(await ScalarAsync(connection, "PRAGMA foreign_keys;", cancellationToken).ConfigureAwait(false));

        if (!string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"SQLite WAL could not be established for {Options.Role}.");
        }

        var expectedSynchronous = Options.Role == SplitOSDatabaseRole.Projection ? 1 : 2;
        if (synchronousValue != expectedSynchronous)
        {
            throw new InvalidOperationException($"SQLite synchronous policy mismatch for {Options.Role}. Expected {expectedSynchronous}, got {synchronousValue}.");
        }

        if (foreignKeys != 1)
        {
            throw new InvalidOperationException($"SQLite foreign_keys could not be enabled for {Options.Role}.");
        }
    }

    private static async Task ExecutePragmaAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }
}
