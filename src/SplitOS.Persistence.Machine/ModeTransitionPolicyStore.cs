using Microsoft.Data.Sqlite;
using SplitOS.Persistence;

namespace SplitOS.Persistence.Machine;

/// <summary>
/// Durable write-once binding between a mode transition and the immutable resolved policy snapshot
/// that will govern its action plan. This store never accepts Windows commands or mechanism payloads.
/// </summary>
public sealed class ModeTransitionPolicyStore : IModeTransitionPolicyStore
{
    private readonly SqliteDatabase _database;
    private readonly string _databasePath;
    private readonly string _markerPath;
    private readonly string _quarantineMarkerPath;
    private readonly TimeProvider _timeProvider;

    public ModeTransitionPolicyStore(
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
    }

    public async Task<ModeTransitionPolicyBinding?> GetAsync(
        Guid transitionId,
        CancellationToken cancellationToken = default)
    {
        if (transitionId == Guid.Empty) throw new ArgumentException("Transition id must not be empty.", nameof(transitionId));
        EnsureCanonicalStoreAvailable();
        await using var connection = await OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        return await ReadBindingAsync(connection, null, transitionId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ModeTransitionPolicyBindOutcome> BindResolvedPolicyAsync(
        Guid transitionId,
        int expectedTransitionRevision,
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        PersistedModePolicyIdentity identity,
        PersistedModePolicyTarget target,
        string resolvedDigest,
        IReadOnlyCollection<PersistedModePolicyFallbackSelection>? selectedFallbacks = null,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(
            transitionId,
            expectedTransitionRevision,
            leaseId,
            fenceToken,
            ownerOperationId,
            identity,
            resolvedDigest,
            selectedFallbacks);
        EnsureCanonicalStoreAvailable();

        var normalizedFallbacks = (selectedFallbacks ?? Array.Empty<PersistedModePolicyFallbackSelection>())
            .OrderBy(static fallback => fallback.RuleId, StringComparer.Ordinal)
            .ToArray();

        await using var connection = await OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        var transition = await ReadTransitionAsync(connection, transaction, transitionId, cancellationToken).ConfigureAwait(false);
        if (transition is null)
        {
            return new ModeTransitionPolicyBindOutcome(
                ModeTransitionPolicyBindDisposition.Missing,
                null,
                "MODE_TRANSITION_NOT_FOUND");
        }

        var existing = await ReadBindingAsync(connection, transaction, transitionId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (Matches(existing, identity, target, resolvedDigest, normalizedFallbacks))
            {
                return new ModeTransitionPolicyBindOutcome(
                    ModeTransitionPolicyBindDisposition.Replayed,
                    existing,
                    "MODE_POLICY_BINDING_REPLAYED",
                    transition.Revision);
            }

            return new ModeTransitionPolicyBindOutcome(
                ModeTransitionPolicyBindDisposition.BindingConflict,
                existing,
                "MODE_POLICY_BINDING_CONFLICT",
                transition.Revision,
                "Resolved policy is already durably bound to a different immutable snapshot.");
        }

        if (transition.Revision != expectedTransitionRevision)
        {
            return new ModeTransitionPolicyBindOutcome(
                ModeTransitionPolicyBindDisposition.RevisionConflict,
                null,
                "MODE_TRANSITION_REVISION_CONFLICT",
                transition.Revision,
                $"Expected transition revision {expectedTransitionRevision}, actual {transition.Revision}.");
        }

        if (transition.OperationId != ownerOperationId ||
            transition.LeaseId != leaseId ||
            transition.FenceToken != fenceToken)
        {
            return new ModeTransitionPolicyBindOutcome(
                ModeTransitionPolicyBindDisposition.LeaseConflict,
                null,
                "MUTATION_LEASE_STALE_FENCE",
                transition.Revision,
                "Transition ownership no longer matches the supplied lease/fence identity.");
        }

        if (!string.Equals(transition.State, "RESOLVING", StringComparison.Ordinal) ||
            !string.Equals(transition.Stage, "RESOLUTION_STARTED", StringComparison.Ordinal))
        {
            return new ModeTransitionPolicyBindOutcome(
                ModeTransitionPolicyBindDisposition.InvalidLifecycle,
                null,
                "MODE_POLICY_BINDING_INVALID_LIFECYCLE",
                transition.Revision,
                "Resolved policy can be bound only while RESOLVING/RESOLUTION_STARTED.");
        }

        var expectedTarget = transition.TargetMode switch
        {
            "NONE" => PersistedModePolicyTarget.Base,
            "WORK" => PersistedModePolicyTarget.Work,
            "GAME" => PersistedModePolicyTarget.Game,
            _ => throw new InvalidDataException("Mode transition target violates canonical policy mapping.")
        };
        if (target != expectedTarget)
        {
            return new ModeTransitionPolicyBindOutcome(
                ModeTransitionPolicyBindDisposition.TargetMismatch,
                null,
                "MODE_POLICY_TARGET_MISMATCH",
                transition.Revision,
                $"Transition target {transition.TargetMode} requires policy target {expectedTarget}.");
        }

        var leaseValidation = await ValidateCurrentModeLeaseAsync(
            connection,
            transaction,
            transition,
            leaseId,
            fenceToken,
            cancellationToken).ConfigureAwait(false);
        if (!leaseValidation.IsCurrent)
        {
            return new ModeTransitionPolicyBindOutcome(
                leaseValidation.ReconciliationRequired
                    ? ModeTransitionPolicyBindDisposition.ReconciliationRequired
                    : ModeTransitionPolicyBindDisposition.LeaseConflict,
                null,
                leaseValidation.ProductCode,
                transition.Revision,
                leaseValidation.Detail);
        }

        var now = _timeProvider.GetUtcNow();
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO mode_transition_policy_binding(
                transition_id, policy_catalog_id, policy_version, policy_release_id,
                policy_catalog_digest, policy_target, resolved_policy_digest, bound_utc)
            VALUES(
                $transition, $catalog, $version, $release,
                $catalog_digest, $target, $resolved_digest, $bound_utc);
            """;
        command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
        command.Parameters.AddWithValue("$catalog", identity.PolicyCatalogId);
        command.Parameters.AddWithValue("$version", identity.PolicyVersion);
        command.Parameters.AddWithValue("$release", identity.ReleaseId);
        command.Parameters.AddWithValue("$catalog_digest", identity.CatalogDigest.ToLowerInvariant());
        command.Parameters.AddWithValue("$target", ToStorageCode(target));
        command.Parameters.AddWithValue("$resolved_digest", resolvedDigest.ToLowerInvariant());
        command.Parameters.AddWithValue("$bound_utc", now.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        foreach (var fallback in normalizedFallbacks)
        {
            command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO mode_transition_policy_fallback(
                    transition_id, rule_id, fallback_class, target_id)
                VALUES($transition, $rule, $class, $target_id);
                """;
            command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
            command.Parameters.AddWithValue("$rule", fallback.RuleId);
            command.Parameters.AddWithValue("$class", ToStorageCode(fallback.FallbackClass));
            command.Parameters.AddWithValue("$target_id", (object?)fallback.TargetId ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE mode_transition
            SET updated_utc = $updated,
                revision = revision + 1
            WHERE transition_id = $transition
              AND revision = $revision
              AND operation_id = $operation
              AND lease_id = $lease
              AND fence_token = $fence
              AND transition_state = 'RESOLVING'
              AND stage_code = 'RESOLUTION_STARTED';
            """;
        command.Parameters.AddWithValue("$updated", now.ToString("O"));
        command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
        command.Parameters.AddWithValue("$revision", expectedTransitionRevision);
        command.Parameters.AddWithValue("$operation", ownerOperationId.ToString("D"));
        command.Parameters.AddWithValue("$lease", leaseId.ToString("D"));
        command.Parameters.AddWithValue("$fence", fenceToken);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            transaction.Rollback();
            return new ModeTransitionPolicyBindOutcome(
                ModeTransitionPolicyBindDisposition.RevisionConflict,
                null,
                "MODE_TRANSITION_REVISION_CONFLICT",
                null,
                "Transition changed concurrently while durable policy binding was being established.");
        }

        transaction.Commit();
        var binding = new ModeTransitionPolicyBinding(
            transitionId,
            identity with { CatalogDigest = identity.CatalogDigest.ToLowerInvariant() },
            target,
            resolvedDigest.ToLowerInvariant(),
            normalizedFallbacks,
            now,
            checked(expectedTransitionRevision + 1));
        return new ModeTransitionPolicyBindOutcome(
            ModeTransitionPolicyBindDisposition.Bound,
            binding,
            "MODE_POLICY_BOUND",
            binding.TransitionRevision);
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
                    $"Mode policy repository requires machine schema {MachineStateStore.SchemaVersion}, got {version}.");
            }

            foreach (var table in new[]
                     {
                         "mode_transition",
                         "machine_mutation_lease",
                         "mode_transition_policy_binding",
                         "mode_transition_policy_fallback"
                     })
            {
                var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
                command.Parameters.AddWithValue("$name", table);
                if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 1)
                {
                    throw new InvalidDataException($"Canonical machine table {table} is missing.");
                }
            }

            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private void EnsureCanonicalStoreAvailable()
    {
        if (File.Exists(_quarantineMarkerPath))
        {
            throw new InvalidDataException($"Machine canonical store is quarantined. Marker: {_quarantineMarkerPath}");
        }

        if (!File.Exists(_markerPath) || !File.Exists(_databasePath))
        {
            throw new InvalidDataException("Machine canonical store must be initialized before mode policy binding is accessed.");
        }
    }

    private async Task<TransitionRow?> ReadTransitionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid transitionId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT operation_id, correlation_id, target_mode, control_session_key,
                   lease_id, fence_token, transition_state, stage_code, revision
            FROM mode_transition
            WHERE transition_id = $transition;
            """;
        command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;

        if (!Guid.TryParse(reader.GetString(0), out var operationId) ||
            !Guid.TryParse(reader.GetString(1), out var correlationId) ||
            !Guid.TryParse(reader.GetString(4), out var leaseId))
        {
            throw new InvalidDataException("Mode transition contains malformed ownership identity.");
        }

        return new TransitionRow(
            operationId,
            correlationId,
            reader.GetString(2),
            reader.GetString(3),
            leaseId,
            reader.GetInt64(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetInt32(8));
    }

    private async Task<ModeTransitionPolicyBinding?> ReadBindingAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid transitionId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT policy_catalog_id, policy_version, policy_release_id,
                   policy_catalog_digest, policy_target, resolved_policy_digest, bound_utc
            FROM mode_transition_policy_binding
            WHERE transition_id = $transition;
            """;
        command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));

        string catalogId;
        long policyVersion;
        string releaseId;
        string catalogDigest;
        PersistedModePolicyTarget target;
        string resolvedDigest;
        DateTimeOffset boundUtc;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
            if (!TryParseTarget(reader.GetString(4), out target) ||
                !DateTimeOffset.TryParse(reader.GetString(6), out boundUtc))
            {
                throw new InvalidDataException("Mode policy binding contains malformed canonical values.");
            }

            catalogId = reader.GetString(0);
            policyVersion = reader.GetInt64(1);
            releaseId = reader.GetString(2);
            catalogDigest = reader.GetString(3);
            resolvedDigest = reader.GetString(5);
        }

        command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT rule_id, fallback_class, target_id
            FROM mode_transition_policy_fallback
            WHERE transition_id = $transition
            ORDER BY rule_id ASC;
            """;
        command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
        var fallbacks = new List<PersistedModePolicyFallbackSelection>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!TryParseFallbackClass(reader.GetString(1), out var fallbackClass))
                {
                    throw new InvalidDataException("Mode policy fallback contains an unknown fallback class.");
                }

                fallbacks.Add(new PersistedModePolicyFallbackSelection(
                    reader.GetString(0),
                    fallbackClass,
                    reader.IsDBNull(2) ? null : reader.GetString(2)));
            }
        }

        command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT revision FROM mode_transition WHERE transition_id = $transition;";
        command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
        var revision = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));

        return new ModeTransitionPolicyBinding(
            transitionId,
            new PersistedModePolicyIdentity(catalogId, policyVersion, releaseId, catalogDigest),
            target,
            resolvedDigest,
            fallbacks,
            boundUtc.ToUniversalTime(),
            revision);
    }

    private async Task<LeaseValidation> ValidateCurrentModeLeaseAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TransitionRow transition,
        Guid leaseId,
        long fenceToken,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT lease_id, mutation_type, owner_operation_id, owner_correlation_id,
                   owner_control_session_key, fence_token, expires_utc
            FROM machine_mutation_lease
            WHERE singleton_id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || reader.IsDBNull(0))
        {
            return LeaseValidation.Stale("No active major mutation lease exists.");
        }

        if (!Guid.TryParse(reader.GetString(0), out var actualLeaseId) ||
            !Guid.TryParse(reader.GetString(2), out var actualOperationId) ||
            !Guid.TryParse(reader.GetString(3), out var actualCorrelationId) ||
            !DateTimeOffset.TryParse(reader.GetString(6), out var expiresUtc))
        {
            throw new InvalidDataException("Major mutation lease contains malformed canonical values.");
        }

        if (_timeProvider.GetUtcNow() >= expiresUtc.ToUniversalTime())
        {
            return LeaseValidation.Reconcile("Major mutation lease expired; persisted transition state must be reconciled before policy binding.");
        }

        if (actualLeaseId != leaseId ||
            !string.Equals(reader.GetString(1), "MODE", StringComparison.Ordinal) ||
            actualOperationId != transition.OperationId ||
            actualCorrelationId != transition.CorrelationId ||
            !string.Equals(reader.GetString(4), transition.ControlSessionKey, StringComparison.Ordinal) ||
            reader.GetInt64(5) != fenceToken)
        {
            return LeaseValidation.Stale("Major mutation lease identity/fence no longer matches the transition owner.");
        }

        return LeaseValidation.Current;
    }

    private static bool Matches(
        ModeTransitionPolicyBinding existing,
        PersistedModePolicyIdentity identity,
        PersistedModePolicyTarget target,
        string resolvedDigest,
        IReadOnlyList<PersistedModePolicyFallbackSelection> fallbacks)
        => string.Equals(existing.Identity.PolicyCatalogId, identity.PolicyCatalogId, StringComparison.Ordinal) &&
           existing.Identity.PolicyVersion == identity.PolicyVersion &&
           string.Equals(existing.Identity.ReleaseId, identity.ReleaseId, StringComparison.Ordinal) &&
           string.Equals(existing.Identity.CatalogDigest, identity.CatalogDigest, StringComparison.OrdinalIgnoreCase) &&
           existing.Target == target &&
           string.Equals(existing.ResolvedDigest, resolvedDigest, StringComparison.OrdinalIgnoreCase) &&
           existing.SelectedFallbacks.SequenceEqual(fallbacks);

    private static void ValidateRequest(
        Guid transitionId,
        int expectedTransitionRevision,
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        PersistedModePolicyIdentity identity,
        string resolvedDigest,
        IReadOnlyCollection<PersistedModePolicyFallbackSelection>? fallbacks)
    {
        if (transitionId == Guid.Empty) throw new ArgumentException("Transition id must not be empty.", nameof(transitionId));
        if (expectedTransitionRevision < 1) throw new ArgumentOutOfRangeException(nameof(expectedTransitionRevision));
        if (leaseId == Guid.Empty) throw new ArgumentException("Lease id must not be empty.", nameof(leaseId));
        if (fenceToken < 1) throw new ArgumentOutOfRangeException(nameof(fenceToken));
        if (ownerOperationId == Guid.Empty) throw new ArgumentException("Owner operation id must not be empty.", nameof(ownerOperationId));
        ArgumentNullException.ThrowIfNull(identity);
        ValidateSemanticId(identity.PolicyCatalogId, nameof(identity.PolicyCatalogId));
        ValidateSemanticId(identity.ReleaseId, nameof(identity.ReleaseId));
        if (identity.PolicyVersion < 1) throw new ArgumentOutOfRangeException(nameof(identity.PolicyVersion));
        ValidateDigest(identity.CatalogDigest, nameof(identity.CatalogDigest));
        ValidateDigest(resolvedDigest, nameof(resolvedDigest));

        if (fallbacks is { Count: > 256 })
        {
            throw new ArgumentException("Fallback selection count exceeds the bounded policy rule maximum.", nameof(fallbacks));
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fallback in fallbacks ?? Array.Empty<PersistedModePolicyFallbackSelection>())
        {
            if (fallback is null) throw new ArgumentException("Fallback selection must not be null.", nameof(fallbacks));
            ValidateSemanticId(fallback.RuleId, nameof(fallback.RuleId));
            if (!seen.Add(fallback.RuleId)) throw new ArgumentException("Fallback rule IDs must be unique.", nameof(fallbacks));
            if (fallback.FallbackClass == PersistedModePolicyFallbackClass.ApprovedAlternate)
            {
                ValidateSemanticId(fallback.TargetId, nameof(fallback.TargetId));
            }
            else if (fallback.TargetId is not null)
            {
                throw new ArgumentException("Only APPROVED_ALTERNATE fallback may carry target ID.", nameof(fallbacks));
            }
        }
    }

    private static void ValidateSemanticId(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
        {
            throw new ArgumentException("Release semantic ID is missing or outside supported bounds.", paramName);
        }

        if (value.Any(static character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or ':' or '-')))
        {
            throw new ArgumentException(
                "Release semantic IDs may contain only ASCII letters, digits, '.', '_', ':' and '-'.",
                paramName);
        }
    }

    private static void ValidateDigest(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 64 || value.Any(static character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException($"{paramName} must be a SHA-256 hexadecimal digest.", paramName);
        }
    }

    private static string ToStorageCode(PersistedModePolicyTarget value)
        => value switch
        {
            PersistedModePolicyTarget.Base => "BASE",
            PersistedModePolicyTarget.Work => "WORK",
            PersistedModePolicyTarget.Game => "GAME",
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

    private static bool TryParseTarget(string value, out PersistedModePolicyTarget target)
    {
        target = value switch
        {
            "BASE" => PersistedModePolicyTarget.Base,
            "WORK" => PersistedModePolicyTarget.Work,
            "GAME" => PersistedModePolicyTarget.Game,
            _ => default
        };
        return value is "BASE" or "WORK" or "GAME";
    }

    private static string ToStorageCode(PersistedModePolicyFallbackClass value)
        => value switch
        {
            PersistedModePolicyFallbackClass.ReleaseDefault => "RELEASE_DEFAULT",
            PersistedModePolicyFallbackClass.ApprovedAlternate => "APPROVED_ALTERNATE",
            PersistedModePolicyFallbackClass.PreserveCurrent => "PRESERVE_CURRENT",
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

    private static bool TryParseFallbackClass(string value, out PersistedModePolicyFallbackClass fallbackClass)
    {
        fallbackClass = value switch
        {
            "RELEASE_DEFAULT" => PersistedModePolicyFallbackClass.ReleaseDefault,
            "APPROVED_ALTERNATE" => PersistedModePolicyFallbackClass.ApprovedAlternate,
            "PRESERVE_CURRENT" => PersistedModePolicyFallbackClass.PreserveCurrent,
            _ => default
        };
        return value is "RELEASE_DEFAULT" or "APPROVED_ALTERNATE" or "PRESERVE_CURRENT";
    }

    private sealed record TransitionRow(
        Guid OperationId,
        Guid CorrelationId,
        string TargetMode,
        string ControlSessionKey,
        Guid LeaseId,
        long FenceToken,
        string State,
        string Stage,
        int Revision);

    private sealed record LeaseValidation(bool IsCurrent, bool ReconciliationRequired, string ProductCode, string? Detail)
    {
        public static LeaseValidation Current { get; } = new(true, false, "MUTATION_LEASE_CURRENT", null);
        public static LeaseValidation Stale(string detail) => new(false, false, "MUTATION_LEASE_STALE_FENCE", detail);
        public static LeaseValidation Reconcile(string detail) => new(false, true, "MUTATION_LEASE_RECONCILIATION_REQUIRED", detail);
    }
}
