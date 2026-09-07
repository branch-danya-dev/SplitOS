using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace SplitOS.Persistence;

public sealed record CanonicalStoreBackupResult(
    string BackupPath,
    int SourceSchemaVersion,
    DateTimeOffset CreatedAtUtc);

public sealed record CanonicalStoreQuarantineResult(
    string QuarantineDirectory,
    string MarkerPath,
    DateTimeOffset CreatedAtUtc);

public static class CanonicalStoreRecovery
{
    public static async Task<CanonicalStoreBackupResult> CreateVerifiedBackupAsync(
        SqliteConnection sourceConnection,
        string sourceDatabasePath,
        string backupDirectory,
        int expectedSourceSchemaVersion,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(backupDirectory);
        var createdAt = DateTimeOffset.UtcNow;
        var stem = Path.GetFileNameWithoutExtension(sourceDatabasePath);
        var backupPath = Path.Combine(
            backupDirectory,
            $"{stem}.schema-v{expectedSourceSchemaVersion}.{createdAt:yyyyMMddTHHmmssfffZ}.{Guid.NewGuid():N}.db");

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = backupPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        };

        try
        {
            await using var destination = new SqliteConnection(builder.ConnectionString);
            await destination.OpenAsync(cancellationToken).ConfigureAwait(false);
            sourceConnection.BackupDatabase(destination);

            var command = destination.CreateCommand();
            command.CommandText = "PRAGMA quick_check;";
            var quickCheck = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
            if (!string.Equals(quickCheck, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Backup quick_check failed: {quickCheck}");
            }

            command = destination.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            var schemaVersion = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
            if (schemaVersion != expectedSourceSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Backup schema version mismatch. Expected {expectedSourceSchemaVersion}, got {schemaVersion}.");
            }
        }
        catch
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(backupPath)) File.Delete(backupPath);
            throw;
        }

        return new CanonicalStoreBackupResult(backupPath, expectedSourceSchemaVersion, createdAt);
    }

    public static async Task<CanonicalStoreQuarantineResult> PreserveForensicCopyAsync(
        string databasePath,
        string quarantineDirectory,
        string markerPath,
        string reason,
        CancellationToken cancellationToken = default)
    {
        SqliteConnection.ClearAllPools();
        var createdAt = DateTimeOffset.UtcNow;
        var incidentDirectory = Path.Combine(
            quarantineDirectory,
            $"{createdAt:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(incidentDirectory);

        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var source = databasePath + suffix;
            if (!File.Exists(source)) continue;
            var destination = Path.Combine(incidentDirectory, Path.GetFileName(source));
            File.Copy(source, destination, overwrite: false);
        }

        var manifest = JsonSerializer.Serialize(
            new
            {
                schemaVersion = 1,
                databasePath,
                reason,
                createdAtUtc = createdAt
            },
            new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(
            Path.Combine(incidentDirectory, "quarantine-manifest.json"),
            manifest,
            cancellationToken).ConfigureAwait(false);

        var markerDirectory = Path.GetDirectoryName(markerPath);
        if (!string.IsNullOrWhiteSpace(markerDirectory)) Directory.CreateDirectory(markerDirectory);
        await File.WriteAllTextAsync(markerPath, manifest, cancellationToken).ConfigureAwait(false);

        return new CanonicalStoreQuarantineResult(incidentDirectory, markerPath, createdAt);
    }
}
