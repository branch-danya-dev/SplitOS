using Microsoft.Data.Sqlite;
using SplitOS.Persistence;

namespace SplitOS.Persistence.Machine;

public enum MachineMutationType
{
    Mode,
    Update,
    Recovery
}

public sealed record MachineMutationLeaseRecord(
    Guid? LeaseId,
    MachineMutationType? MutationType,
    Guid? OwnerOperationId,
    Guid? OwnerCorrelationId,
    string? OwnerControlSessionKey,
    long FenceToken,
    DateTimeOffset? AcquiredUtc,
    DateTimeOffset? HeartbeatUtc,
    DateTimeOffset? ExpiresUtc,
    int Revision)
{
    public bool IsHeld => LeaseId.HasValue;
}

public enum MachineMutationLeaseAcquireDisposition
{
    Acquired,
    AlreadyOwned,
    Busy,
    ReconciliationRequired
}

public sealed record MachineMutationLeaseAcquireOutcome(
    MachineMutationLeaseAcquireDisposition Disposition,
    MachineMutationLeaseRecord Lease,
    string ProductCode);

public enum MachineMutationLeaseRenewDisposition
{
    Renewed,
    StaleOwner,
    ReconciliationRequired
}

public sealed record MachineMutationLeaseRenewOutcome(
    MachineMutationLeaseRenewDisposition Disposition,
    MachineMutationLeaseRecord Lease,
    string ProductCode);

public enum MachineMutationLeaseReleaseDisposition
{
    Released,
    AlreadyReleased,
    StaleOwner,
    ReconciliationRequired
}

public sealed record MachineMutationLeaseReleaseOutcome(
    MachineMutationLeaseReleaseDisposition Disposition,
    MachineMutationLeaseRecord Lease,
    string ProductCode);

/// <summary>
/// Typed SPEC-05 machine-wide exclusion primitive shared by MODE/UPDATE/RECOVERY.
/// Expiry is deliberately not an ownership bypass: an expired lease is preserved until an explicit
/// reconciliation path proves takeover safety. The repository never creates or migrates its schema;
/// MachineStateStore v3 is the only canonical schema owner.
/// </summary>
public sealed class MachineMutationLeaseStore
{
    public static readonly TimeSpan MaximumLeaseLifetime = TimeSpan.FromMinutes(5);

    private readonly SqliteDatabase _database;
    private readonly string _databasePath;
    private readonly string _markerPath;
    private readonly string _quarantineMarkerPath;
    private readonly TimeProvider _timeProvider;

    public MachineMutationLeaseStore(
        string? databasePath = null,
        string? markerPath = null,
        string? quarantineMarkerPath = null,
        TimeProvider? timeProvider = null)
    {
        _databasePath = databasePath ?? StoragePaths.MachineDatabase;
        var customRoot = databasePath is null ? null : Path.GetDirectoryName(Path.GetFullPath(_databasePath));
        _markerPath = markerPath ?? (customRoot is null
            ? StoragePaths.MachineBootstrapMarker
            : Path.Combine(customRoot, "machine-store.initialized"));
        _quarantineMarkerPath = quarantineMarkerPath ?? (customRoot is null
            ? StoragePaths.MachineQuarantineMarker
            : Path.Combine(customRoot, "machine-store.quarantined.json"));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _database = new SqliteDatabase(new SqliteDatabaseOptions(
            _databasePath,
            SplitOSDatabaseRole.Machine,
            MachineStateStore.SchemaVersion,
            "development"));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        EnsureCanonicalStoreAvailable();
        await using var connection = await OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        var lease = await ReadAsync(connection, cancellationToken).ConfigureAwait(false);
        ValidateRecord(lease);
    }

    public async Task<MachineMutationLeaseRecord> GetAsync(CancellationToken cancellationToken = default)
    {
        EnsureCanonicalStoreAvailable();
        await using var connection = await OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        return await ReadAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MachineMutationLeaseAcquireOutcome> TryAcquireAsync(
        MachineMutationType mutationType,
        Guid ownerOperationId,
        Guid ownerCorrelationId,
        string ownerControlSessionKey,
        TimeSpan leaseLifetime,
        CancellationToken cancellationToken = default)
    {
        ValidateOwner(ownerOperationId, ownerCorrelationId, ownerControlSessionKey);
        ValidateLeaseLifetime(leaseLifetime);
        EnsureCanonicalStoreAvailable();

        await using var connection = await OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow();
        var leaseId = Guid.NewGuid();
        var mutationCode = ToStorageCode(mutationType);
        var expires = now.Add(leaseLifetime);

        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE machine_mutation_lease
            SET lease_id = $lease,
                mutation_type = $type,
                owner_operation_id = $operation,
                owner_correlation_id = $correlation,
                owner_control_session_key = $session,
                fence_token = fence_token + 1,
                acquired_utc = $now,
                heartbeat_utc = $now,
                expires_utc = $expires,
                revision = revision + 1
            WHERE singleton_id = 1 AND lease_id IS NULL
            RETURNING fence_token, revision;
            """;
        command.Parameters.AddWithValue("$lease", leaseId.ToString("D"));
        command.Parameters.AddWithValue("$type", mutationCode);
        command.Parameters.AddWithValue("$operation", ownerOperationId.ToString("D"));
        command.Parameters.AddWithValue("$correlation", ownerCorrelationId.ToString("D"));
        command.Parameters.AddWithValue("$session", ownerControlSessionKey);
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.Parameters.AddWithValue("$expires", expires.ToString("O"));

        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var fence = reader.GetInt64(0);
                var revision = reader.GetInt32(1);
                var acquired = new MachineMutationLeaseRecord(
                    leaseId,
                    mutationType,
                    ownerOperationId,
                    ownerCorrelationId,
                    ownerControlSessionKey,
                    fence,
                    now,
                    now,
                    expires,
                    revision);
                ValidateRecord(acquired);
                return new MachineMutationLeaseAcquireOutcome(
                    MachineMutationLeaseAcquireDisposition.Acquired,
                    acquired,
                    "MUTATION_LEASE_ACQUIRED");
            }
        }

        var current = await ReadAsync(connection, cancellationToken).ConfigureAwait(false);
        if (!current.IsHeld)
        {
            return new MachineMutationLeaseAcquireOutcome(
                MachineMutationLeaseAcquireDisposition.Busy,
                current,
                "MUTATION_LEASE_RETRY_REQUIRED");
        }

        if (IsSameOwner(current, mutationType, ownerOperationId, ownerCorrelationId, ownerControlSessionKey))
        {
            if (IsExpired(current, now))
            {
                return new MachineMutationLeaseAcquireOutcome(
                    MachineMutationLeaseAcquireDisposition.ReconciliationRequired,
                    current,
                    "MUTATION_LEASE_RECONCILIATION_REQUIRED");
            }

            return new MachineMutationLeaseAcquireOutcome(
                MachineMutationLeaseAcquireDisposition.AlreadyOwned,
                current,
                "MUTATION_LEASE_ALREADY_OWNED");
        }

        return IsExpired(current, now)
            ? new MachineMutationLeaseAcquireOutcome(
                MachineMutationLeaseAcquireDisposition.ReconciliationRequired,
                current,
                "MUTATION_LEASE_RECONCILIATION_REQUIRED")
            : new MachineMutationLeaseAcquireOutcome(
                MachineMutationLeaseAcquireDisposition.Busy,
                current,
                BusyCode(current.MutationType));
    }

    public async Task<MachineMutationLeaseRenewOutcome> RenewAsync(
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        TimeSpan leaseLifetime,
        CancellationToken cancellationToken = default)
    {
        ValidateLeaseIdentity(leaseId, fenceToken, ownerOperationId);
        ValidateLeaseLifetime(leaseLifetime);
        EnsureCanonicalStoreAvailable();

        await using var connection = await OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        var current = await ReadAsync(connection, cancellationToken).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow();

        if (!Owns(current, leaseId, fenceToken, ownerOperationId))
        {
            return new MachineMutationLeaseRenewOutcome(
                MachineMutationLeaseRenewDisposition.StaleOwner,
                current,
                "MUTATION_LEASE_STALE_FENCE");
        }

        if (IsExpired(current, now))
        {
            return new MachineMutationLeaseRenewOutcome(
                MachineMutationLeaseRenewDisposition.ReconciliationRequired,
                current,
                "MUTATION_LEASE_RECONCILIATION_REQUIRED");
        }

        var expires = now.Add(leaseLifetime);
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE machine_mutation_lease
            SET heartbeat_utc = $now,
                expires_utc = $expires,
                revision = revision + 1
            WHERE singleton_id = 1
              AND lease_id = $lease
              AND fence_token = $fence
              AND owner_operation_id = $operation
              AND revision = $revision;
            """;
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.Parameters.AddWithValue("$expires", expires.ToString("O"));
        command.Parameters.AddWithValue("$lease", leaseId.ToString("D"));
        command.Parameters.AddWithValue("$fence", fenceToken);
        command.Parameters.AddWithValue("$operation", ownerOperationId.ToString("D"));
        command.Parameters.AddWithValue("$revision", current.Revision);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            var actual = await ReadAsync(connection, cancellationToken).ConfigureAwait(false);
            return new MachineMutationLeaseRenewOutcome(
                MachineMutationLeaseRenewDisposition.StaleOwner,
                actual,
                "MUTATION_LEASE_STALE_FENCE");
        }

        var renewed = current with
        {
            HeartbeatUtc = now,
            ExpiresUtc = expires,
            Revision = checked(current.Revision + 1)
        };
        return new MachineMutationLeaseRenewOutcome(
            MachineMutationLeaseRenewDisposition.Renewed,
            renewed,
            "MUTATION_LEASE_RENEWED");
    }

    public async Task<MachineMutationLeaseReleaseOutcome> ReleaseAsync(
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        CancellationToken cancellationToken = default)
    {
        ValidateLeaseIdentity(leaseId, fenceToken, ownerOperationId);
        EnsureCanonicalStoreAvailable();

        await using var connection = await OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        var current = await ReadAsync(connection, cancellationToken).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow();

        if (!current.IsHeld)
        {
            return new MachineMutationLeaseReleaseOutcome(
                MachineMutationLeaseReleaseDisposition.AlreadyReleased,
                current,
                "MUTATION_LEASE_ALREADY_RELEASED");
        }

        if (!Owns(current, leaseId, fenceToken, ownerOperationId))
        {
            return new MachineMutationLeaseReleaseOutcome(
                MachineMutationLeaseReleaseDisposition.StaleOwner,
                current,
                "MUTATION_LEASE_STALE_FENCE");
        }

        if (IsExpired(current, now))
        {
            return new MachineMutationLeaseReleaseOutcome(
                MachineMutationLeaseReleaseDisposition.ReconciliationRequired,
                current,
                "MUTATION_LEASE_RECONCILIATION_REQUIRED");
        }

        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE machine_mutation_lease
            SET lease_id = NULL,
                mutation_type = NULL,
                owner_operation_id = NULL,
                owner_correlation_id = NULL,
                owner_control_session_key = NULL,
                acquired_utc = NULL,
                heartbeat_utc = NULL,
                expires_utc = NULL,
                revision = revision + 1
            WHERE singleton_id = 1
              AND lease_id = $lease
              AND fence_token = $fence
              AND owner_operation_id = $operation
              AND revision = $revision;
            """;
        command.Parameters.AddWithValue("$lease", leaseId.ToString("D"));
        command.Parameters.AddWithValue("$fence", fenceToken);
        command.Parameters.AddWithValue("$operation", ownerOperationId.ToString("D"));
        command.Parameters.AddWithValue("$revision", current.Revision);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            var actual = await ReadAsync(connection, cancellationToken).ConfigureAwait(false);
            return new MachineMutationLeaseReleaseOutcome(
                MachineMutationLeaseReleaseDisposition.StaleOwner,
                actual,
                "MUTATION_LEASE_STALE_FENCE");
        }

        var released = new MachineMutationLeaseRecord(
            null,
            null,
            null,
            null,
            null,
            current.FenceToken,
            null,
            null,
            null,
            checked(current.Revision + 1));
        return new MachineMutationLeaseReleaseOutcome(
            MachineMutationLeaseReleaseDisposition.Released,
            released,
            "MUTATION_LEASE_RELEASED");
    }

    private async Task<SqliteConnection> OpenReadyAsync(CancellationToken cancellationToken)
    {
        var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var version = await _database.ReadSchemaVersionAsync(connection, cancellationToken).ConfigureAwait(false);
            if (version != MachineStateStore.SchemaVersion)
            {
                throw new InvalidDataException(
                    $"Major mutation lease requires machine schema {MachineStateStore.SchemaVersion}, got {version}.");
            }

            var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='machine_mutation_lease';";
            if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 1)
            {
                throw new InvalidDataException("Major mutation lease schema is not initialized by MachineStateStore.");
            }

            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<MachineMutationLeaseRecord> ReadAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT lease_id, mutation_type, owner_operation_id, owner_correlation_id,
                   owner_control_session_key, fence_token, acquired_utc, heartbeat_utc, expires_utc, revision
            FROM machine_mutation_lease
            WHERE singleton_id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException("Major mutation lease singleton is missing.");
        }

        MachineMutationLeaseRecord record;
        if (reader.IsDBNull(0))
        {
            if (!reader.IsDBNull(1) || !reader.IsDBNull(2) || !reader.IsDBNull(3) || !reader.IsDBNull(4) ||
                !reader.IsDBNull(6) || !reader.IsDBNull(7) || !reader.IsDBNull(8))
            {
                throw new InvalidDataException("Released major mutation lease retains canonical owner fields.");
            }

            record = new MachineMutationLeaseRecord(
                null,
                null,
                null,
                null,
                null,
                reader.GetInt64(5),
                null,
                null,
                null,
                reader.GetInt32(9));
        }
        else
        {
            if (reader.IsDBNull(1) || reader.IsDBNull(2) || reader.IsDBNull(3) || reader.IsDBNull(4) ||
                reader.IsDBNull(6) || reader.IsDBNull(7) || reader.IsDBNull(8) ||
                !Guid.TryParse(reader.GetString(0), out var leaseId) ||
                !TryParseMutationType(reader.GetString(1), out var mutationType) ||
                !Guid.TryParse(reader.GetString(2), out var operationId) ||
                !Guid.TryParse(reader.GetString(3), out var correlationId) ||
                !DateTimeOffset.TryParse(reader.GetString(6), out var acquired) ||
                !DateTimeOffset.TryParse(reader.GetString(7), out var heartbeat) ||
                !DateTimeOffset.TryParse(reader.GetString(8), out var expires))
            {
                throw new InvalidDataException("Major mutation lease contains malformed canonical values.");
            }

            record = new MachineMutationLeaseRecord(
                leaseId,
                mutationType,
                operationId,
                correlationId,
                reader.GetString(4),
                reader.GetInt64(5),
                acquired.ToUniversalTime(),
                heartbeat.ToUniversalTime(),
                expires.ToUniversalTime(),
                reader.GetInt32(9));
        }

        ValidateRecord(record);
        return record;
    }

    private void EnsureCanonicalStoreAvailable()
    {
        if (File.Exists(_quarantineMarkerPath))
        {
            throw new InvalidDataException(
                $"Machine canonical store is quarantined. Marker: {_quarantineMarkerPath}");
        }

        if (!File.Exists(_markerPath) || !File.Exists(_databasePath))
        {
            throw new InvalidDataException(
                "Machine canonical store must be initialized before major mutation lease state is accessed.");
        }
    }

    private static void ValidateOwner(Guid operationId, Guid correlationId, string controlSessionKey)
    {
        if (operationId == Guid.Empty) throw new ArgumentException("Owner operation id must not be empty.", nameof(operationId));
        if (correlationId == Guid.Empty) throw new ArgumentException("Owner correlation id must not be empty.", nameof(correlationId));
        if (string.IsNullOrWhiteSpace(controlSessionKey) ||
            controlSessionKey.Length > 256 ||
            controlSessionKey.Any(static character => char.IsControl(character)))
        {
            throw new ArgumentException("Owner control-session key is missing or outside supported bounds.", nameof(controlSessionKey));
        }
    }

    private static void ValidateLeaseIdentity(Guid leaseId, long fenceToken, Guid ownerOperationId)
    {
        if (leaseId == Guid.Empty) throw new ArgumentException("Lease id must not be empty.", nameof(leaseId));
        if (fenceToken < 1) throw new ArgumentOutOfRangeException(nameof(fenceToken));
        if (ownerOperationId == Guid.Empty) throw new ArgumentException("Owner operation id must not be empty.", nameof(ownerOperationId));
    }

    private static void ValidateLeaseLifetime(TimeSpan leaseLifetime)
    {
        if (leaseLifetime <= TimeSpan.Zero || leaseLifetime > MaximumLeaseLifetime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseLifetime),
                $"Major mutation lease lifetime must be > 0 and <= {MaximumLeaseLifetime}.");
        }
    }

    private static void ValidateRecord(MachineMutationLeaseRecord record)
    {
        if (record.FenceToken < 0 || record.Revision < 1)
        {
            throw new InvalidDataException("Major mutation lease monotonic counters are invalid.");
        }

        if (!record.IsHeld)
        {
            if (record.MutationType is not null ||
                record.OwnerOperationId is not null ||
                record.OwnerCorrelationId is not null ||
                record.OwnerControlSessionKey is not null ||
                record.AcquiredUtc is not null ||
                record.HeartbeatUtc is not null ||
                record.ExpiresUtc is not null)
            {
                throw new InvalidDataException("Released major mutation lease retains owner fields.");
            }
            return;
        }

        if (record.MutationType is null ||
            record.OwnerOperationId is null ||
            record.OwnerCorrelationId is null ||
            string.IsNullOrWhiteSpace(record.OwnerControlSessionKey) ||
            record.OwnerControlSessionKey.Length > 256 ||
            record.OwnerControlSessionKey.Any(static character => char.IsControl(character)) ||
            record.AcquiredUtc is null ||
            record.HeartbeatUtc is null ||
            record.ExpiresUtc is null ||
            record.FenceToken < 1 ||
            record.HeartbeatUtc < record.AcquiredUtc ||
            record.ExpiresUtc <= record.HeartbeatUtc)
        {
            throw new InvalidDataException("Held major mutation lease violates canonical owner/timing invariants.");
        }
    }

    private static bool Owns(
        MachineMutationLeaseRecord current,
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId)
        => current.LeaseId == leaseId &&
           current.FenceToken == fenceToken &&
           current.OwnerOperationId == ownerOperationId;

    private static bool IsSameOwner(
        MachineMutationLeaseRecord current,
        MachineMutationType mutationType,
        Guid ownerOperationId,
        Guid ownerCorrelationId,
        string ownerControlSessionKey)
        => current.MutationType == mutationType &&
           current.OwnerOperationId == ownerOperationId &&
           current.OwnerCorrelationId == ownerCorrelationId &&
           string.Equals(current.OwnerControlSessionKey, ownerControlSessionKey, StringComparison.Ordinal);

    private static bool IsExpired(MachineMutationLeaseRecord lease, DateTimeOffset now)
        => lease.ExpiresUtc is not null && now >= lease.ExpiresUtc.Value;

    private static string ToStorageCode(MachineMutationType type)
        => type switch
        {
            MachineMutationType.Mode => "MODE",
            MachineMutationType.Update => "UPDATE",
            MachineMutationType.Recovery => "RECOVERY",
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

    private static bool TryParseMutationType(string value, out MachineMutationType mutationType)
    {
        mutationType = value switch
        {
            "MODE" => MachineMutationType.Mode,
            "UPDATE" => MachineMutationType.Update,
            "RECOVERY" => MachineMutationType.Recovery,
            _ => default
        };
        return value is "MODE" or "UPDATE" or "RECOVERY";
    }

    private static string BusyCode(MachineMutationType? type)
        => type switch
        {
            MachineMutationType.Update => "BUSY_UPDATE",
            MachineMutationType.Recovery => "BUSY_RECOVERY",
            _ => "MUTATION_LEASE_BUSY"
        };
}
