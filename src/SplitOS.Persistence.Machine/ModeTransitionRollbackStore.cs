using Microsoft.Data.Sqlite;
using SplitOS.Persistence;

namespace SplitOS.Persistence.Machine;

public enum PersistedModeRollbackResult
{
    RolledBack,
    Failed,
    Unknown
}

public enum ModeRollbackAdvanceDisposition
{
    Advanced,
    Replayed,
    Missing,
    RevisionConflict,
    LeaseConflict,
    ReconciliationRequired,
    OwnershipConflict,
    InvalidLifecycle
}

public sealed record ModeRollbackAdvanceOutcome(
    ModeRollbackAdvanceDisposition Disposition,
    PersistedModeActionRecord? Action,
    string ProductCode,
    int? ActualActionRevision = null,
    string? Detail = null);

/// <summary>
/// Durable SPEC-05 rollback journal for pre-commit mode transitions.
/// The repository never invokes an owning adapter. It selects and fences rollback work,
/// enforces reverse action order, and records the durable compensation result so Runtime
/// can reconcile or escalate without assuming a Windows mutation succeeded.
/// </summary>
public sealed class ModeTransitionRollbackStore
{
    private readonly SqliteDatabase _database;
    private readonly string _databasePath;
    private readonly string _markerPath;
    private readonly string _quarantineMarkerPath;
    private readonly TimeProvider _timeProvider;

    public ModeTransitionRollbackStore(
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

    public async Task<PersistedModeActionRecord?> GetNextRollbackCandidateAsync(
        Guid transitionId,
        CancellationToken cancellationToken = default)
    {
        if (transitionId == Guid.Empty) throw new ArgumentException("Transition id must not be empty.", nameof(transitionId));
        EnsureCanonicalStoreAvailable();
        await using var connection = await OpenReadyAsync(cancellationToken).ConfigureAwait(false);

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT action_id
            FROM mode_transition_action
            WHERE transition_id = $transition
              AND (
                  action_state = 'ROLLING_BACK' OR
                  (
                      action_state IN ('APPLIED','VERIFYING','VERIFIED','FAILED')
                      AND apply_result_code IN ('APPLIED','UNKNOWN')
                  )
              )
            ORDER BY sequence_no DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
        var scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (scalar is not string actionIdText || !Guid.TryParse(actionIdText, out var actionId)) return null;
        return await ReadActionAsync(connection, null, actionId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ModeRollbackAdvanceOutcome> BeginRollbackAsync(
        Guid transitionId,
        Guid actionId,
        int expectedActionRevision,
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        CancellationToken cancellationToken = default)
    {
        ValidateCommonRequest(transitionId, actionId, expectedActionRevision, leaseId, fenceToken, ownerOperationId);
        EnsureCanonicalStoreAvailable();

        await using var connection = await OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        var context = await ReadOwnedContextAsync(connection, transaction, transitionId, actionId, cancellationToken).ConfigureAwait(false);
        if (context.Outcome is not null) return context.Outcome;
        var transition = context.Transition!;
        var action = context.Action!;

        if (action.State == PersistedModeActionState.RollingBack && ReplayRevisionMatches(action.Revision, expectedActionRevision))
        {
            return Replay(action, "MODE_ACTION_ROLLBACK_BEGIN_REPLAYED");
        }

        var validation = await ValidateMutableContextAsync(
            connection,
            transaction,
            transition,
            action,
            expectedActionRevision,
            leaseId,
            fenceToken,
            ownerOperationId,
            cancellationToken).ConfigureAwait(false);
        if (validation is not null) return validation;

        if (action.State == PersistedModeActionState.Applying)
        {
            return new ModeRollbackAdvanceOutcome(
                ModeRollbackAdvanceDisposition.ReconciliationRequired,
                action,
                "MODE_ACTION_APPLY_OUTCOME_RECONCILIATION_REQUIRED",
                action.Revision,
                "Action is still APPLYING; actual-state reconciliation is required before compensation can be chosen.");
        }

        if (!RequiresRollback(action))
        {
            return InvalidLifecycle(
                action,
                "MODE_ACTION_ROLLBACK_NOT_REQUIRED",
                $"Action {action.ActionId:D} has no persisted applied/unknown mutation evidence requiring rollback.");
        }

        var reverseOrder = await ValidateReverseOrderAsync(
            connection,
            transaction,
            transitionId,
            action.SequenceNo,
            cancellationToken).ConfigureAwait(false);
        if (reverseOrder is not null) return reverseOrder with { Action = action, ActualActionRevision = action.Revision };

        var now = _timeProvider.GetUtcNow();
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE mode_transition_action
            SET action_state = 'ROLLING_BACK',
                rollback_result_code = NULL,
                updated_utc = $updated,
                revision = revision + 1
            WHERE action_id = $action
              AND transition_id = $transition
              AND revision = $revision
              AND action_state IN ('APPLIED','VERIFYING','VERIFIED','FAILED')
              AND apply_result_code IN ('APPLIED','UNKNOWN');
            """;
        command.Parameters.AddWithValue("$updated", now.ToString("O"));
        command.Parameters.AddWithValue("$action", actionId.ToString("D"));
        command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
        command.Parameters.AddWithValue("$revision", expectedActionRevision);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            return await RevisionConflictAsync(connection, transaction, actionId, expectedActionRevision, cancellationToken).ConfigureAwait(false);
        }

        transaction.Commit();
        var advanced = action with
        {
            State = PersistedModeActionState.RollingBack,
            RollbackResultCode = null,
            UpdatedUtc = now,
            Revision = checked(action.Revision + 1)
        };
        return Advanced(advanced, "MODE_ACTION_ROLLBACK_STARTED");
    }

    public async Task<ModeRollbackAdvanceOutcome> RecordRollbackResultAsync(
        Guid transitionId,
        Guid actionId,
        int expectedActionRevision,
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        PersistedModeRollbackResult result,
        CancellationToken cancellationToken = default)
    {
        ValidateCommonRequest(transitionId, actionId, expectedActionRevision, leaseId, fenceToken, ownerOperationId);
        EnsureCanonicalStoreAvailable();

        var resultCode = result switch
        {
            PersistedModeRollbackResult.RolledBack => "ROLLED_BACK",
            PersistedModeRollbackResult.Failed => "FAILED",
            PersistedModeRollbackResult.Unknown => "UNKNOWN",
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };
        var nextState = result == PersistedModeRollbackResult.RolledBack
            ? PersistedModeActionState.RolledBack
            : PersistedModeActionState.RollbackFailed;

        await using var connection = await OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        var context = await ReadOwnedContextAsync(connection, transaction, transitionId, actionId, cancellationToken).ConfigureAwait(false);
        if (context.Outcome is not null) return context.Outcome;
        var transition = context.Transition!;
        var action = context.Action!;

        if (action.State == nextState &&
            ReplayRevisionMatches(action.Revision, expectedActionRevision) &&
            string.Equals(action.RollbackResultCode, resultCode, StringComparison.Ordinal))
        {
            return Replay(action, "MODE_ACTION_ROLLBACK_RESULT_REPLAYED");
        }

        var validation = await ValidateMutableContextAsync(
            connection,
            transaction,
            transition,
            action,
            expectedActionRevision,
            leaseId,
            fenceToken,
            ownerOperationId,
            cancellationToken).ConfigureAwait(false);
        if (validation is not null) return validation;

        if (action.State != PersistedModeActionState.RollingBack)
        {
            return InvalidLifecycle(
                action,
                "MODE_ACTION_ROLLBACK_RESULT_INVALID_LIFECYCLE",
                $"Rollback result can be recorded only from ROLLING_BACK; actual state is {action.State}.");
        }

        var now = _timeProvider.GetUtcNow();
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE mode_transition_action
            SET action_state = $state,
                rollback_result_code = $result,
                updated_utc = $updated,
                revision = revision + 1
            WHERE action_id = $action
              AND transition_id = $transition
              AND revision = $revision
              AND action_state = 'ROLLING_BACK';
            """;
        command.Parameters.AddWithValue("$state", nextState == PersistedModeActionState.RolledBack ? "ROLLED_BACK" : "ROLLBACK_FAILED");
        command.Parameters.AddWithValue("$result", resultCode);
        command.Parameters.AddWithValue("$updated", now.ToString("O"));
        command.Parameters.AddWithValue("$action", actionId.ToString("D"));
        command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
        command.Parameters.AddWithValue("$revision", expectedActionRevision);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            return await RevisionConflictAsync(connection, transaction, actionId, expectedActionRevision, cancellationToken).ConfigureAwait(false);
        }

        transaction.Commit();
        var advanced = action with
        {
            State = nextState,
            RollbackResultCode = resultCode,
            UpdatedUtc = now,
            Revision = checked(action.Revision + 1)
        };
        return Advanced(
            advanced,
            result == PersistedModeRollbackResult.RolledBack
                ? "MODE_ACTION_ROLLED_BACK"
                : "MODE_ACTION_ROLLBACK_FAILED");
    }

    private async Task<ModeRollbackAdvanceOutcome?> ValidateMutableContextAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TransitionRow transition,
        PersistedModeActionRecord action,
        int expectedActionRevision,
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        CancellationToken cancellationToken)
    {
        if (action.Revision != expectedActionRevision)
        {
            return new ModeRollbackAdvanceOutcome(
                ModeRollbackAdvanceDisposition.RevisionConflict,
                action,
                "MODE_ACTION_REVISION_CONFLICT",
                action.Revision,
                $"Expected action revision {expectedActionRevision}, actual {action.Revision}.");
        }

        if (transition.OperationId != ownerOperationId || transition.LeaseId != leaseId || transition.FenceToken != fenceToken)
        {
            return new ModeRollbackAdvanceOutcome(
                ModeRollbackAdvanceDisposition.OwnershipConflict,
                action,
                "MODE_ACTION_OWNERSHIP_MISMATCH",
                action.Revision,
                "Transition ownership does not match the supplied rollback mutation context.");
        }

        if (transition.CommitDurable ||
            !string.Equals(transition.State, "ROLLING_BACK", StringComparison.Ordinal) ||
            !string.Equals(transition.Stage, "ROLLBACK_STARTED", StringComparison.Ordinal))
        {
            return InvalidLifecycle(
                action,
                "MODE_ACTION_ROLLBACK_TRANSITION_INVALID_LIFECYCLE",
                $"Rollback action advancement requires ROLLING_BACK/ROLLBACK_STARTED before durable commit; actual {transition.State}/{transition.Stage}.");
        }

        var lease = await ValidateCurrentModeLeaseAsync(connection, transaction, transition, leaseId, fenceToken, cancellationToken).ConfigureAwait(false);
        if (!lease.IsCurrent)
        {
            return new ModeRollbackAdvanceOutcome(
                lease.ReconciliationRequired
                    ? ModeRollbackAdvanceDisposition.ReconciliationRequired
                    : ModeRollbackAdvanceDisposition.LeaseConflict,
                action,
                lease.ProductCode,
                action.Revision,
                lease.Detail);
        }

        return null;
    }

    private static async Task<ModeRollbackAdvanceOutcome?> ValidateReverseOrderAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid transitionId,
        int sequenceNo,
        CancellationToken cancellationToken)
    {
        var failed = connection.CreateCommand();
        failed.Transaction = transaction;
        failed.CommandText = """
            SELECT COUNT(*)
            FROM mode_transition_action
            WHERE transition_id = $transition
              AND sequence_no > $sequence
              AND action_state = 'ROLLBACK_FAILED';
            """;
        failed.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
        failed.Parameters.AddWithValue("$sequence", sequenceNo);
        if (Convert.ToInt32(await failed.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0)
        {
            return new ModeRollbackAdvanceOutcome(
                ModeRollbackAdvanceDisposition.ReconciliationRequired,
                null,
                "MODE_ROLLBACK_HIGHER_ACTION_FAILED",
                null,
                "A later action failed rollback; normal reverse compensation is blocked and Recovery/reconciliation is required.");
        }

        var pending = connection.CreateCommand();
        pending.Transaction = transaction;
        pending.CommandText = """
            SELECT COUNT(*)
            FROM mode_transition_action
            WHERE transition_id = $transition
              AND sequence_no > $sequence
              AND (
                  action_state = 'ROLLING_BACK' OR
                  (
                      action_state IN ('APPLIED','VERIFYING','VERIFIED','FAILED')
                      AND apply_result_code IN ('APPLIED','UNKNOWN')
                  )
              );
            """;
        pending.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
        pending.Parameters.AddWithValue("$sequence", sequenceNo);
        if (Convert.ToInt32(await pending.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0)
        {
            return new ModeRollbackAdvanceOutcome(
                ModeRollbackAdvanceDisposition.InvalidLifecycle,
                null,
                "MODE_ACTION_ROLLBACK_ORDER_BLOCKED",
                null,
                "All later mutated/unknown actions must be durably rolled back before an earlier action may begin compensation.");
        }

        return null;
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
            return LeaseValidation.Stale("No active major mutation lease exists.");

        if (!Guid.TryParse(reader.GetString(0), out var actualLeaseId) ||
            !Guid.TryParse(reader.GetString(2), out var actualOperationId) ||
            !Guid.TryParse(reader.GetString(3), out var actualCorrelationId) ||
            !DateTimeOffset.TryParse(reader.GetString(6), out var expiresUtc))
        {
            throw new InvalidDataException("Major mutation lease contains malformed canonical rollback values.");
        }

        if (_timeProvider.GetUtcNow() >= expiresUtc.ToUniversalTime())
            return LeaseValidation.Reconcile("Major mutation lease expired; rollback requires reconciliation before further mutation.");

        if (actualLeaseId != leaseId ||
            !string.Equals(reader.GetString(1), "MODE", StringComparison.Ordinal) ||
            actualOperationId != transition.OperationId ||
            actualCorrelationId != transition.CorrelationId ||
            !string.Equals(reader.GetString(4), transition.ControlSessionKey, StringComparison.Ordinal) ||
            reader.GetInt64(5) != fenceToken)
        {
            return LeaseValidation.Stale("Current canonical MODE lease identity/fence no longer matches the rollback transition owner.");
        }

        return LeaseValidation.Current;
    }

    private static bool RequiresRollback(PersistedModeActionRecord action)
        => action.State is PersistedModeActionState.Applied
            or PersistedModeActionState.Verifying
            or PersistedModeActionState.Verified
            or PersistedModeActionState.Failed &&
           action.ApplyResultCode is "APPLIED" or "UNKNOWN";

    private static async Task<OwnedContext> ReadOwnedContextAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid transitionId,
        Guid actionId,
        CancellationToken cancellationToken)
    {
        var transition = await ReadTransitionAsync(connection, transaction, transitionId, cancellationToken).ConfigureAwait(false);
        if (transition is null)
        {
            return new OwnedContext(null, null, new ModeRollbackAdvanceOutcome(
                ModeRollbackAdvanceDisposition.Missing, null, "MODE_TRANSITION_NOT_FOUND"));
        }

        var action = await ReadActionAsync(connection, transaction, actionId, cancellationToken).ConfigureAwait(false);
        if (action is null)
        {
            return new OwnedContext(transition, null, new ModeRollbackAdvanceOutcome(
                ModeRollbackAdvanceDisposition.Missing, null, "MODE_ACTION_NOT_FOUND"));
        }

        if (action.TransitionId != transitionId)
        {
            return new OwnedContext(transition, action, new ModeRollbackAdvanceOutcome(
                ModeRollbackAdvanceDisposition.OwnershipConflict,
                action,
                "MODE_ACTION_TRANSITION_MISMATCH",
                action.Revision,
                "Durable action belongs to a different transition."));
        }

        return new OwnedContext(transition, action, null);
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
                   transition_state, stage_code, commit_durable
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
            throw new InvalidDataException("Mode transition contains malformed rollback ownership values.");
        }

        return new TransitionRow(
            transitionId,
            operationId,
            correlationId,
            reader.GetString(2),
            persistedLeaseId,
            reader.GetInt64(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetInt32(7) == 1);
    }

    private static async Task<PersistedModeActionRecord?> ReadActionAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid actionId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT action_id, transition_id, sequence_no, owning_module, action_type, target_ref,
                   desired_schema_version, desired_state_json, desired_state_digest, mandatory,
                   rollback_class, verification_class, action_state,
                   pre_state_json, pre_state_digest, apply_result_code, verify_result_code, rollback_result_code,
                   started_utc, applied_utc, verified_utc, updated_utc, revision
            FROM mode_transition_action
            WHERE action_id = $action;
            """;
        command.Parameters.AddWithValue("$action", actionId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;

        if (!Guid.TryParse(reader.GetString(0), out var persistedActionId) ||
            !Guid.TryParse(reader.GetString(1), out var transitionId) ||
            !TryParseActionState(reader.GetString(12), out var state) ||
            !DateTimeOffset.TryParse(reader.GetString(21), out var updatedUtc))
        {
            throw new InvalidDataException("Mode action contains malformed canonical rollback values.");
        }

        return new PersistedModeActionRecord(
            persistedActionId,
            transitionId,
            reader.GetInt32(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetInt32(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetString(8),
            reader.GetInt32(9) == 1,
            reader.GetString(10),
            reader.GetString(11),
            state,
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.IsDBNull(14) ? null : reader.GetString(14),
            reader.IsDBNull(15) ? null : reader.GetString(15),
            reader.IsDBNull(16) ? null : reader.GetString(16),
            reader.IsDBNull(17) ? null : reader.GetString(17),
            ParseNullableUtc(reader, 18),
            ParseNullableUtc(reader, 19),
            ParseNullableUtc(reader, 20),
            updatedUtc.ToUniversalTime(),
            reader.GetInt32(22));
    }

    private async Task<SqliteConnection> OpenReadyAsync(CancellationToken cancellationToken)
    {
        var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var version = await _database.ReadSchemaVersionAsync(connection, cancellationToken).ConfigureAwait(false);
            if (version != MachineStateStore.SchemaVersion)
                throw new InvalidDataException($"Mode rollback journal requires machine schema {MachineStateStore.SchemaVersion}, got {version}.");

            foreach (var table in new[] { "machine_mutation_lease", "mode_transition", "mode_transition_action_plan", "mode_transition_action" })
            {
                var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
                command.Parameters.AddWithValue("$name", table);
                if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 1)
                    throw new InvalidDataException($"Canonical machine table {table} is missing.");
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
            throw new InvalidDataException($"Machine canonical store is quarantined. Marker: {_quarantineMarkerPath}");
        if (!File.Exists(_markerPath) || !File.Exists(_databasePath))
            throw new InvalidDataException("Machine canonical store must be initialized before mode rollback is journaled.");
    }

    private static async Task<ModeRollbackAdvanceOutcome> RevisionConflictAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid actionId,
        int expectedActionRevision,
        CancellationToken cancellationToken)
    {
        var actual = await ReadActionAsync(connection, transaction, actionId, cancellationToken).ConfigureAwait(false);
        return new ModeRollbackAdvanceOutcome(
            ModeRollbackAdvanceDisposition.RevisionConflict,
            actual,
            "MODE_ACTION_REVISION_CONFLICT",
            actual?.Revision,
            actual is null
                ? "Action disappeared while its rollback revision was being advanced."
                : $"Expected action revision {expectedActionRevision}, actual {actual.Revision}.");
    }

    private static void ValidateCommonRequest(
        Guid transitionId,
        Guid actionId,
        int expectedActionRevision,
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId)
    {
        if (transitionId == Guid.Empty) throw new ArgumentException("Transition id must not be empty.", nameof(transitionId));
        if (actionId == Guid.Empty) throw new ArgumentException("Action id must not be empty.", nameof(actionId));
        if (expectedActionRevision < 1) throw new ArgumentOutOfRangeException(nameof(expectedActionRevision));
        if (leaseId == Guid.Empty) throw new ArgumentException("Lease id must not be empty.", nameof(leaseId));
        if (fenceToken < 1) throw new ArgumentOutOfRangeException(nameof(fenceToken));
        if (ownerOperationId == Guid.Empty) throw new ArgumentException("Owner operation id must not be empty.", nameof(ownerOperationId));
    }

    private static bool ReplayRevisionMatches(int actualRevision, int expectedRevision)
        => actualRevision == expectedRevision + 1;

    private static DateTimeOffset? ParseNullableUtc(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return null;
        if (!DateTimeOffset.TryParse(reader.GetString(ordinal), out var parsed))
            throw new InvalidDataException("Mode action timestamp is malformed.");
        return parsed.ToUniversalTime();
    }

    private static bool TryParseActionState(string value, out PersistedModeActionState result)
    {
        result = value switch
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

    private static ModeRollbackAdvanceOutcome Advanced(PersistedModeActionRecord action, string code)
        => new(ModeRollbackAdvanceDisposition.Advanced, action, code, action.Revision);

    private static ModeRollbackAdvanceOutcome Replay(PersistedModeActionRecord action, string code)
        => new(ModeRollbackAdvanceDisposition.Replayed, action, code, action.Revision);

    private static ModeRollbackAdvanceOutcome InvalidLifecycle(PersistedModeActionRecord action, string code, string detail)
        => new(ModeRollbackAdvanceDisposition.InvalidLifecycle, action, code, action.Revision, detail);

    private sealed record OwnedContext(TransitionRow? Transition, PersistedModeActionRecord? Action, ModeRollbackAdvanceOutcome? Outcome);

    private sealed record TransitionRow(
        Guid TransitionId,
        Guid OperationId,
        Guid CorrelationId,
        string ControlSessionKey,
        Guid LeaseId,
        long FenceToken,
        string State,
        string Stage,
        bool CommitDurable);

    private sealed record LeaseValidation(bool IsCurrent, bool ReconciliationRequired, string ProductCode, string? Detail)
    {
        public static LeaseValidation Current { get; } = new(true, false, "MUTATION_LEASE_CURRENT", null);
        public static LeaseValidation Stale(string detail) => new(false, false, "MUTATION_LEASE_STALE_FENCE", detail);
        public static LeaseValidation Reconcile(string detail) => new(false, true, "MUTATION_LEASE_RECONCILIATION_REQUIRED", detail);
    }
}
