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

public sealed class MachineStateStore
{
    public const int SchemaVersion = 1;
    private readonly SqliteDatabase _database;
    private readonly string _markerPath;

    public MachineStateStore(string? databasePath = null, string? markerPath = null)
    {
        var path = databasePath ?? StoragePaths.MachineDatabase;
        _markerPath = markerPath ?? StoragePaths.MachineBootstrapMarker;
        _database = new SqliteDatabase(new SqliteDatabaseOptions(path, SplitOSDatabaseRole.Machine, SchemaVersion, "development"));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var dbExists = File.Exists(_database.Options.DatabasePath);
        if (!dbExists && File.Exists(_markerPath))
        {
            throw new InvalidDataException("machine.db is missing after prior machine-store initialization. Recovery is required; canonical NONE must not be fabricated.");
        }

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await _database.InitializeMetadataAsync(connection, "machine", cancellationToken).ConfigureAwait(false);
        await CreateSchemaAsync(connection, cancellationToken).ConfigureAwait(false);

        if (!dbExists)
        {
            await BootstrapInitialStateAsync(connection, cancellationToken).ConfigureAwait(false);
        }

        await _database.VerifyIntegrityAsync(connection, cancellationToken).ConfigureAwait(false);
        await EnsureBootstrapMarkerAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationalModeRecord> GetOperationalModeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
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

    private async Task EnsureBootstrapMarkerAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(_markerPath)) return;
        var markerDirectory = Path.GetDirectoryName(_markerPath);
        if (!string.IsNullOrWhiteSpace(markerDirectory)) Directory.CreateDirectory(markerDirectory);
        await File.WriteAllTextAsync(_markerPath, DateTimeOffset.UtcNow.ToString("O"), cancellationToken).ConfigureAwait(false);
    }

    private static async Task CreateSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
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
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task BootstrapInitialStateAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO operational_mode_state(
                singleton_id, committed_mode, committed_utc, committed_by_operation_id, correlation_id, revision, updated_utc)
            VALUES (1, 'NONE', $now, 'bootstrap', NULL, 1, $now);
            """;
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
