using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SplitOS.Persistence;

namespace SplitOS.Persistence.Machine;

/// <summary>
/// Durable SPEC-05 ordered action-plan journal. This store persists the complete immutable plan
/// before any mutating action may begin. Action execution/state advancement is intentionally
/// outside this Slice-03 increment.
/// </summary>
public sealed class ModeTransitionActionPlanStore : IModeTransitionActionPlanStore
{
    public const int CurrentPlanSchemaVersion = 1;
    public const int MaxActionCount = 512;
    public const int MaxDesiredStateBytes = 64 * 1024;

    private readonly SqliteDatabase _database;
    private readonly string _databasePath;
    private readonly string _markerPath;
    private readonly string _quarantineMarkerPath;
    private readonly TimeProvider _timeProvider;

    public ModeTransitionActionPlanStore(
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

    public async Task<ModeTransitionActionPlan?> GetAsync(
        Guid transitionId,
        CancellationToken cancellationToken = default)
    {
        if (transitionId == Guid.Empty) throw new ArgumentException("Transition id must not be empty.", nameof(transitionId));
        EnsureCanonicalStoreAvailable();
        await using var connection = await OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        return await ReadPlanAsync(connection, null, transitionId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ModeTransitionActionPlanPersistOutcome> PersistActionPlanAsync(
        Guid transitionId,
        int expectedTransitionRevision,
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        IReadOnlyCollection<PersistedModeActionDefinition> actions,
        CancellationToken cancellationToken = default)
    {
        var normalized = ValidateAndNormalizeRequest(
            transitionId,
            expectedTransitionRevision,
            leaseId,
            fenceToken,
            ownerOperationId,
            actions);
        var planDigest = ComputePlanDigest(normalized);
        EnsureCanonicalStoreAvailable();

        await using var connection = await OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        var transition = await ReadTransitionAsync(connection, transaction, transitionId, cancellationToken).ConfigureAwait(false);
        if (transition is null)
        {
            return new ModeTransitionActionPlanPersistOutcome(
                ModeTransitionActionPlanPersistDisposition.Missing,
                null,
                "MODE_TRANSITION_NOT_FOUND");
        }

        var existing = await ReadPlanAsync(connection, transaction, transitionId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (Matches(existing, planDigest, normalized))
            {
                return new ModeTransitionActionPlanPersistOutcome(
                    ModeTransitionActionPlanPersistDisposition.Replayed,
                    existing,
                    "MODE_ACTION_PLAN_REPLAYED",
                    transition.Revision);
            }

            return new ModeTransitionActionPlanPersistOutcome(
                ModeTransitionActionPlanPersistDisposition.PlanConflict,
                existing,
                "MODE_ACTION_PLAN_CONFLICT",
                transition.Revision,
                "A different immutable action plan is already persisted for this transition.");
        }

        if (transition.Revision != expectedTransitionRevision)
        {
            return new ModeTransitionActionPlanPersistOutcome(
                ModeTransitionActionPlanPersistDisposition.RevisionConflict,
                null,
                "MODE_TRANSITION_REVISION_CONFLICT",
                transition.Revision,
                $"Expected transition revision {expectedTransitionRevision}, actual {transition.Revision}.");
        }

        if (transition.OperationId != ownerOperationId ||
            transition.LeaseId != leaseId ||
            transition.FenceToken != fenceToken)
        {
            return new ModeTransitionActionPlanPersistOutcome(
                ModeTransitionActionPlanPersistDisposition.LeaseConflict,
                null,
                "MUTATION_LEASE_STALE_FENCE",
                transition.Revision,
                "Transition ownership no longer matches the supplied lease/fence identity.");
        }

        if (!string.Equals(transition.State, "RESOLVING", StringComparison.Ordinal) ||
            !string.Equals(transition.Stage, "RESOLUTION_STARTED", StringComparison.Ordinal))
        {
            return new ModeTransitionActionPlanPersistOutcome(
                ModeTransitionActionPlanPersistDisposition.InvalidLifecycle,
                null,
                "MODE_ACTION_PLAN_INVALID_LIFECYCLE",
                transition.Revision,
                "Action plan can be persisted only while RESOLVING/RESOLUTION_STARTED.");
        }

        if (!await HasPolicyBindingAsync(connection, transaction, transitionId, cancellationToken).ConfigureAwait(false))
        {
            return new ModeTransitionActionPlanPersistOutcome(
                ModeTransitionActionPlanPersistDisposition.MissingPolicyBinding,
                null,
                "MODE_POLICY_BINDING_REQUIRED",
                transition.Revision,
                "Durable resolved policy binding is required before action-plan persistence.");
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
            return new ModeTransitionActionPlanPersistOutcome(
                leaseValidation.ReconciliationRequired
                    ? ModeTransitionActionPlanPersistDisposition.ReconciliationRequired
                    : ModeTransitionActionPlanPersistDisposition.LeaseConflict,
                null,
                leaseValidation.ProductCode,
                transition.Revision,
                leaseValidation.Detail);
        }

        var now = _timeProvider.GetUtcNow();
        var nextRevision = checked(transition.Revision + 1);
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO mode_transition_action_plan(
                transition_id, plan_schema_version, plan_digest, action_count,
                persisted_utc, transition_revision)
            VALUES(
                $transition, $schema, $digest, $count,
                $persisted, $transition_revision);
            """;
        command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
        command.Parameters.AddWithValue("$schema", CurrentPlanSchemaVersion);
        command.Parameters.AddWithValue("$digest", planDigest);
        command.Parameters.AddWithValue("$count", normalized.Count);
        command.Parameters.AddWithValue("$persisted", now.ToString("O"));
        command.Parameters.AddWithValue("$transition_revision", nextRevision);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        foreach (var action in normalized)
        {
            command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO mode_transition_action(
                    action_id, transition_id, sequence_no, owning_module, action_type, target_ref,
                    desired_schema_version, desired_state_json, desired_state_digest, mandatory,
                    rollback_class, verification_class, action_state,
                    pre_state_json, pre_state_digest, apply_result_code, verify_result_code, rollback_result_code,
                    started_utc, applied_utc, verified_utc, updated_utc, revision)
                VALUES(
                    $action, $transition, $sequence, $module, $type, $target,
                    $desired_schema, $desired_json, $desired_digest, $mandatory,
                    $rollback, $verification, 'PLANNED',
                    NULL, NULL, NULL, NULL, NULL,
                    NULL, NULL, NULL, $updated, 1);
                """;
            command.Parameters.AddWithValue("$action", action.ActionId.ToString("D"));
            command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
            command.Parameters.AddWithValue("$sequence", action.SequenceNo);
            command.Parameters.AddWithValue("$module", action.OwningModule);
            command.Parameters.AddWithValue("$type", action.ActionType);
            command.Parameters.AddWithValue("$target", (object?)action.TargetRef ?? DBNull.Value);
            command.Parameters.AddWithValue("$desired_schema", action.DesiredSchemaVersion);
            command.Parameters.AddWithValue("$desired_json", (object?)action.DesiredStateJson ?? DBNull.Value);
            command.Parameters.AddWithValue("$desired_digest", action.DesiredStateDigest.ToLowerInvariant());
            command.Parameters.AddWithValue("$mandatory", action.Mandatory ? 1 : 0);
            command.Parameters.AddWithValue("$rollback", action.RollbackClass);
            command.Parameters.AddWithValue("$verification", action.VerificationClass);
            command.Parameters.AddWithValue("$updated", now.ToString("O"));
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
            return new ModeTransitionActionPlanPersistOutcome(
                ModeTransitionActionPlanPersistDisposition.RevisionConflict,
                null,
                "MODE_TRANSITION_REVISION_CONFLICT",
                null,
                "Transition changed concurrently while persisting the action plan.");
        }

        transaction.Commit();
        return new ModeTransitionActionPlanPersistOutcome(
            ModeTransitionActionPlanPersistDisposition.Persisted,
            new ModeTransitionActionPlan(
                transitionId,
                CurrentPlanSchemaVersion,
                planDigest,
                normalized.Count,
                now,
                nextRevision,
                normalized.Select(action => ToPlannedRecord(transitionId, action, now)).ToArray()),
            "MODE_ACTION_PLAN_PERSISTED",
            nextRevision);
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
                    $"Mode action-plan repository requires machine schema {MachineStateStore.SchemaVersion}, got {version}.");
            }

            foreach (var table in new[]
                     {
                         "mode_transition",
                         "machine_mutation_lease",
                         "mode_transition_policy_binding",
                         "mode_transition_action_plan",
                         "mode_transition_action"
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
            throw new InvalidDataException(
                "Machine canonical store must be initialized before mode action-plan state is accessed.");
        }
    }

    private static async Task<bool> HasPolicyBindingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid transitionId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM mode_transition_policy_binding WHERE transition_id=$transition;";
        command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 1;
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
            return LeaseValidation.Reconcile(
                "Major mutation lease expired; action-plan persistence requires transition reconciliation.");
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

    private static async Task<TransitionRow?> ReadTransitionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid transitionId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT operation_id, correlation_id, control_session_key, lease_id, fence_token,
                   transition_state, stage_code, revision
            FROM mode_transition
            WHERE transition_id = $transition;
            """;
        command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;

        if (!Guid.TryParse(reader.GetString(0), out var operationId) ||
            !Guid.TryParse(reader.GetString(1), out var correlationId) ||
            !Guid.TryParse(reader.GetString(3), out var persistedLeaseId))
        {
            throw new InvalidDataException("Mode transition contains malformed action-plan ownership values.");
        }

        return new TransitionRow(
            operationId,
            correlationId,
            reader.GetString(2),
            persistedLeaseId,
            reader.GetInt64(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetInt32(7));
    }

    private static async Task<ModeTransitionActionPlan?> ReadPlanAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid transitionId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT plan_schema_version, plan_digest, action_count, persisted_utc, transition_revision
            FROM mode_transition_action_plan
            WHERE transition_id = $transition;
            """;
        command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;

        var schema = reader.GetInt32(0);
        var digest = reader.GetString(1);
        var count = reader.GetInt32(2);
        if (!DateTimeOffset.TryParse(reader.GetString(3), out var persistedUtc))
        {
            throw new InvalidDataException("Mode action-plan timestamp is malformed.");
        }
        var transitionRevision = reader.GetInt32(4);
        await reader.DisposeAsync().ConfigureAwait(false);

        var actions = await ReadActionsAsync(connection, transaction, transitionId, cancellationToken).ConfigureAwait(false);
        if (schema != CurrentPlanSchemaVersion || count != actions.Count || count < 1 || count > MaxActionCount ||
            !IsSha256Digest(digest))
        {
            throw new InvalidDataException("Mode action-plan header violates canonical invariants.");
        }

        var reconstructed = actions.Select(ToDefinition).ToArray();
        var actualDigest = ComputePlanDigest(reconstructed);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(digest.ToLowerInvariant()),
                Encoding.ASCII.GetBytes(actualDigest)))
        {
            throw new InvalidDataException("Mode action-plan digest does not match persisted action semantics.");
        }

        return new ModeTransitionActionPlan(
            transitionId,
            schema,
            digest.ToLowerInvariant(),
            count,
            persistedUtc.ToUniversalTime(),
            transitionRevision,
            actions);
    }

    private static async Task<IReadOnlyList<PersistedModeActionRecord>> ReadActionsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid transitionId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT action_id, sequence_no, owning_module, action_type, target_ref,
                   desired_schema_version, desired_state_json, desired_state_digest, mandatory,
                   rollback_class, verification_class, action_state,
                   pre_state_json, pre_state_digest, apply_result_code, verify_result_code, rollback_result_code,
                   started_utc, applied_utc, verified_utc, updated_utc, revision
            FROM mode_transition_action
            WHERE transition_id = $transition
            ORDER BY sequence_no ASC;
            """;
        command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));

        var result = new List<PersistedModeActionRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!Guid.TryParse(reader.GetString(0), out var actionId) ||
                !TryParseActionState(reader.GetString(11), out var state) ||
                !DateTimeOffset.TryParse(reader.GetString(20), out var updatedUtc))
            {
                throw new InvalidDataException("Mode action row contains malformed canonical values.");
            }

            result.Add(new PersistedModeActionRecord(
                actionId,
                transitionId,
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetString(7),
                reader.GetInt32(8) == 1,
                reader.GetString(9),
                reader.GetString(10),
                state,
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetString(15),
                reader.IsDBNull(16) ? null : reader.GetString(16),
                ParseNullableUtc(reader, 17),
                ParseNullableUtc(reader, 18),
                ParseNullableUtc(reader, 19),
                updatedUtc.ToUniversalTime(),
                reader.GetInt32(21)));
        }

        foreach (var action in result)
        {
            ValidatePersistedAction(action);
        }
        return result;
    }

    private static DateTimeOffset? ParseNullableUtc(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return null;
        if (!DateTimeOffset.TryParse(reader.GetString(ordinal), out var parsed))
            throw new InvalidDataException("Mode action timestamp is malformed.");
        return parsed.ToUniversalTime();
    }

    private static IReadOnlyList<PersistedModeActionDefinition> ValidateAndNormalizeRequest(
        Guid transitionId,
        int expectedTransitionRevision,
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        IReadOnlyCollection<PersistedModeActionDefinition> actions)
    {
        if (transitionId == Guid.Empty) throw new ArgumentException("Transition id must not be empty.", nameof(transitionId));
        if (expectedTransitionRevision < 1) throw new ArgumentOutOfRangeException(nameof(expectedTransitionRevision));
        if (leaseId == Guid.Empty) throw new ArgumentException("Lease id must not be empty.", nameof(leaseId));
        if (fenceToken < 1) throw new ArgumentOutOfRangeException(nameof(fenceToken));
        if (ownerOperationId == Guid.Empty) throw new ArgumentException("Owner operation id must not be empty.", nameof(ownerOperationId));
        ArgumentNullException.ThrowIfNull(actions);
        if (actions.Count is < 1 or > MaxActionCount)
            throw new ArgumentException($"Action plan must contain between 1 and {MaxActionCount} actions.", nameof(actions));

        var actionIds = new HashSet<Guid>();
        var sequences = new HashSet<int>();
        foreach (var action in actions)
        {
            if (action is null) throw new ArgumentException("Action plan contains a null action.", nameof(actions));
            if (action.ActionId == Guid.Empty || !actionIds.Add(action.ActionId))
                throw new ArgumentException("Action ids must be non-empty and unique within the plan.", nameof(actions));
            if (action.SequenceNo < 1 || !sequences.Add(action.SequenceNo))
                throw new ArgumentException("Action sequence numbers must be positive and unique.", nameof(actions));
            ValidateSemanticId(action.OwningModule, nameof(action.OwningModule), 128, allowSlash: false);
            ValidateSemanticId(action.ActionType, nameof(action.ActionType), 128, allowSlash: false);
            if (action.TargetRef is not null)
                ValidateSemanticId(action.TargetRef, nameof(action.TargetRef), 256, allowSlash: true);
            if (action.DesiredSchemaVersion < 1)
                throw new ArgumentOutOfRangeException(nameof(action.DesiredSchemaVersion));
            ValidateDesiredState(action.DesiredStateJson, action.DesiredStateDigest);
            ValidateSemanticId(action.RollbackClass, nameof(action.RollbackClass), 128, allowSlash: false);
            ValidateSemanticId(action.VerificationClass, nameof(action.VerificationClass), 128, allowSlash: false);
        }

        return actions.OrderBy(static action => action.SequenceNo).ToArray();
    }

    private static void ValidateDesiredState(string? json, string digest)
    {
        if (!IsSha256Digest(digest))
            throw new ArgumentException("Desired-state digest must be a SHA-256 hexadecimal digest.", nameof(digest));

        var bytes = Encoding.UTF8.GetBytes(json ?? string.Empty);
        if (bytes.Length > MaxDesiredStateBytes)
            throw new ArgumentException($"Desired-state JSON exceeds {MaxDesiredStateBytes} UTF-8 bytes.", nameof(json));

        if (json is not null)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                    throw new ArgumentException("Desired-state JSON must be an object or array.", nameof(json));
            }
            catch (JsonException ex)
            {
                throw new ArgumentException("Desired-state JSON is malformed.", nameof(json), ex);
            }
        }

        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(digest.ToLowerInvariant()),
                Encoding.ASCII.GetBytes(actual)))
        {
            throw new ArgumentException("Desired-state digest does not match the persisted JSON payload.", nameof(digest));
        }
    }

    private static void ValidatePersistedAction(PersistedModeActionRecord action)
    {
        if (action.SequenceNo < 1 || action.DesiredSchemaVersion < 1 || action.Revision < 1)
            throw new InvalidDataException("Mode action counters violate canonical bounds.");
        ValidateSemanticId(action.OwningModule, nameof(action.OwningModule), 128, allowSlash: false);
        ValidateSemanticId(action.ActionType, nameof(action.ActionType), 128, allowSlash: false);
        if (action.TargetRef is not null)
            ValidateSemanticId(action.TargetRef, nameof(action.TargetRef), 256, allowSlash: true);
        ValidateDesiredState(action.DesiredStateJson, action.DesiredStateDigest);
        ValidateSemanticId(action.RollbackClass, nameof(action.RollbackClass), 128, allowSlash: false);
        ValidateSemanticId(action.VerificationClass, nameof(action.VerificationClass), 128, allowSlash: false);

        if (action.State == PersistedModeActionState.Planned &&
            (action.PreStateJson is not null || action.PreStateDigest is not null ||
             action.ApplyResultCode is not null || action.VerifyResultCode is not null || action.RollbackResultCode is not null ||
             action.StartedUtc is not null || action.AppliedUtc is not null || action.VerifiedUtc is not null || action.Revision != 1))
        {
            throw new InvalidDataException("Freshly planned action contains execution evidence before apply begins.");
        }
    }

    private static string ComputePlanDigest(IReadOnlyCollection<PersistedModeActionDefinition> actions)
    {
        var builder = new StringBuilder();
        Append(builder, CurrentPlanSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var action in actions.OrderBy(static action => action.SequenceNo))
        {
            Append(builder, action.ActionId.ToString("D"));
            Append(builder, action.SequenceNo.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(builder, action.OwningModule);
            Append(builder, action.ActionType);
            Append(builder, action.TargetRef ?? string.Empty);
            Append(builder, action.DesiredSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(builder, action.DesiredStateDigest.ToLowerInvariant());
            Append(builder, action.Mandatory ? "1" : "0");
            Append(builder, action.RollbackClass);
            Append(builder, action.VerificationClass);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static void Append(StringBuilder builder, string value)
        => builder.Append(value.Length).Append(':').Append(value).Append('|');

    private static bool Matches(
        ModeTransitionActionPlan existing,
        string planDigest,
        IReadOnlyList<PersistedModeActionDefinition> requested)
    {
        if (!string.Equals(existing.PlanDigest, planDigest, StringComparison.OrdinalIgnoreCase) ||
            existing.ActionCount != requested.Count ||
            existing.Actions.Count != requested.Count)
            return false;

        for (var i = 0; i < requested.Count; i++)
        {
            var persisted = existing.Actions[i];
            var action = requested[i];
            if (persisted.ActionId != action.ActionId ||
                persisted.SequenceNo != action.SequenceNo ||
                !string.Equals(persisted.OwningModule, action.OwningModule, StringComparison.Ordinal) ||
                !string.Equals(persisted.ActionType, action.ActionType, StringComparison.Ordinal) ||
                !string.Equals(persisted.TargetRef, action.TargetRef, StringComparison.Ordinal) ||
                persisted.DesiredSchemaVersion != action.DesiredSchemaVersion ||
                !string.Equals(persisted.DesiredStateJson, action.DesiredStateJson, StringComparison.Ordinal) ||
                !string.Equals(persisted.DesiredStateDigest, action.DesiredStateDigest, StringComparison.OrdinalIgnoreCase) ||
                persisted.Mandatory != action.Mandatory ||
                !string.Equals(persisted.RollbackClass, action.RollbackClass, StringComparison.Ordinal) ||
                !string.Equals(persisted.VerificationClass, action.VerificationClass, StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    private static PersistedModeActionDefinition ToDefinition(PersistedModeActionRecord action)
        => new(
            action.ActionId,
            action.SequenceNo,
            action.OwningModule,
            action.ActionType,
            action.TargetRef,
            action.DesiredSchemaVersion,
            action.DesiredStateJson,
            action.DesiredStateDigest,
            action.Mandatory,
            action.RollbackClass,
            action.VerificationClass);

    private static PersistedModeActionRecord ToPlannedRecord(
        Guid transitionId,
        PersistedModeActionDefinition action,
        DateTimeOffset now)
        => new(
            action.ActionId,
            transitionId,
            action.SequenceNo,
            action.OwningModule,
            action.ActionType,
            action.TargetRef,
            action.DesiredSchemaVersion,
            action.DesiredStateJson,
            action.DesiredStateDigest.ToLowerInvariant(),
            action.Mandatory,
            action.RollbackClass,
            action.VerificationClass,
            PersistedModeActionState.Planned,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            now,
            1);

    private static void ValidateSemanticId(string? value, string paramName, int maxLength, bool allowSlash)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
            throw new ArgumentException("Semantic id is missing or outside supported bounds.", paramName);

        foreach (var character in value)
        {
            var allowed = char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or ':' or '-' ||
                          (allowSlash && character == '/');
            if (!allowed)
                throw new ArgumentException("Semantic id contains unsupported characters.", paramName);
        }
    }

    private static bool IsSha256Digest(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           value.Length == 64 &&
           value.All(static character => Uri.IsHexDigit(character));

    private static bool TryParseActionState(string value, out PersistedModeActionState state)
    {
        state = value switch
        {
            "PLANNED" => PersistedModeActionState.Planned,
            "APPLYING" => PersistedModeActionState.Applying,
            "APPLIED" => PersistedModeActionState.Applied,
            "VERIFYING" => PersistedModeActionState.Verifying,
            "VERIFIED" => PersistedModeActionState.Verified,
            "FAILED" => PersistedModeActionState.Failed,
            "ROLLING_BACK" => PersistedModeActionState.RollingBack,
            "ROLLED_BACK" => PersistedModeActionState.RolledBack,
            "ROLLBACK_FAILED" => PersistedModeActionState.RollbackFailed,
            "SKIPPED" => PersistedModeActionState.Skipped,
            _ => default
        };
        return value is "PLANNED" or "APPLYING" or "APPLIED" or "VERIFYING" or "VERIFIED" or "FAILED" or
            "ROLLING_BACK" or "ROLLED_BACK" or "ROLLBACK_FAILED" or "SKIPPED";
    }

    private sealed record TransitionRow(
        Guid OperationId,
        Guid CorrelationId,
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
