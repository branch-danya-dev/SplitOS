using Microsoft.Data.Sqlite;
using SplitOS.Persistence;

namespace SplitOS.Persistence.Machine;

public enum ModeTransitionCommitDisposition
{
    Committed,
    Replayed,
    Missing,
    TransitionRevisionConflict,
    ModeRevisionConflict,
    LeaseConflict,
    ReconciliationRequired,
    InvalidTransition,
    AuthorityDenied,
    ConcurrencyConflict
}

public sealed record ModeTransitionCommitOutcome(
    ModeTransitionCommitDisposition Disposition,
    string ProductCode,
    OperationalModeRecord? OperationalMode,
    ModeTransitionRecord? Transition,
    int? ActualModeRevision = null,
    int? ActualTransitionRevision = null,
    string? Detail = null);

/// <summary>
/// SPEC-05 atomic semantic commit boundary. The canonical OperationalMode row and durable
/// ModeTransition commit marker change in one SQLite transaction or neither changes.
///
/// This repository is intentionally not exposed over IPC and does not itself mutate Windows.
/// Runtime orchestration must supply current premium-target authority evidence immediately before
/// invoking this operation; DEACTIVATE to NONE remains available even after premium authority loss.
/// </summary>
public sealed class ModeTransitionCommitStore
{
    private readonly SqliteDatabase _database;
    private readonly string _databasePath;
    private readonly string _markerPath;
    private readonly string _quarantineMarkerPath;
    private readonly TimeProvider _timeProvider;

    public ModeTransitionCommitStore(
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

    public async Task<ModeTransitionCommitOutcome> CommitTransitionAndModeAsync(
        Guid transitionId,
        int expectedTransitionRevision,
        int expectedModeRevision,
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        bool runtimeAccessPermitsTarget,
        bool policyIdentityCompatible,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(
            transitionId,
            expectedTransitionRevision,
            expectedModeRevision,
            leaseId,
            fenceToken,
            ownerOperationId);
        EnsureCanonicalStoreAvailable();

        await using var connection = await OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        var transition = await ReadTransitionAsync(
            connection,
            transaction,
            transitionId,
            cancellationToken).ConfigureAwait(false);
        if (transition is null)
        {
            return new ModeTransitionCommitOutcome(
                ModeTransitionCommitDisposition.Missing,
                "MODE_TRANSITION_NOT_FOUND",
                null,
                null);
        }

        var canonicalMode = await ReadOperationalModeAsync(
            connection,
            transaction,
            cancellationToken).ConfigureAwait(false);

        if (transition.CommitDurable)
        {
            return IsDurableReplay(transition, canonicalMode, ownerOperationId)
                ? new ModeTransitionCommitOutcome(
                    ModeTransitionCommitDisposition.Replayed,
                    "MODE_COMMIT_REPLAYED",
                    canonicalMode,
                    transition,
                    canonicalMode.Revision,
                    transition.Revision)
                : new ModeTransitionCommitOutcome(
                    ModeTransitionCommitDisposition.ConcurrencyConflict,
                    "MODE_COMMIT_DURABLE_STATE_MISMATCH",
                    canonicalMode,
                    transition,
                    canonicalMode.Revision,
                    transition.Revision,
                    "Transition claims durable commit but canonical OperationalMode does not match the committed target evidence.");
        }

        if (transition.Revision != expectedTransitionRevision)
        {
            return new ModeTransitionCommitOutcome(
                ModeTransitionCommitDisposition.TransitionRevisionConflict,
                "MODE_TRANSITION_REVISION_CONFLICT",
                canonicalMode,
                transition,
                canonicalMode.Revision,
                transition.Revision,
                $"Expected transition revision {expectedTransitionRevision}, actual {transition.Revision}.");
        }

        if (canonicalMode.Revision != expectedModeRevision ||
            transition.SourceModeRevision != expectedModeRevision ||
            !string.Equals(canonicalMode.CommittedMode, transition.SourceMode, StringComparison.Ordinal))
        {
            return new ModeTransitionCommitOutcome(
                ModeTransitionCommitDisposition.ModeRevisionConflict,
                "MODE_SOURCE_REVISION_CONFLICT",
                canonicalMode,
                transition,
                canonicalMode.Revision,
                transition.Revision,
                $"Expected canonical source {transition.SourceMode}@{expectedModeRevision}, actual {canonicalMode.CommittedMode}@{canonicalMode.Revision}.");
        }

        if (transition.OperationId != ownerOperationId ||
            transition.LeaseId != leaseId ||
            transition.FenceToken != fenceToken)
        {
            return new ModeTransitionCommitOutcome(
                ModeTransitionCommitDisposition.LeaseConflict,
                "MUTATION_LEASE_STALE_FENCE",
                canonicalMode,
                transition,
                canonicalMode.Revision,
                transition.Revision,
                "Transition ownership no longer matches the supplied mutation lease identity.");
        }

        var transitionError = ValidateCommitReadyTransition(transition);
        if (transitionError is not null)
        {
            return new ModeTransitionCommitOutcome(
                ModeTransitionCommitDisposition.InvalidTransition,
                "MODE_TRANSITION_NOT_COMMIT_READY",
                canonicalMode,
                transition,
                canonicalMode.Revision,
                transition.Revision,
                transitionError);
        }

        if (!await HasDurableVerificationEvidenceAsync(connection, transaction, transition.TransitionId, cancellationToken).ConfigureAwait(false))
        {
            return new ModeTransitionCommitOutcome(
                ModeTransitionCommitDisposition.InvalidTransition,
                "MODE_TRANSITION_VERIFICATION_EVIDENCE_INCOMPLETE",
                canonicalMode,
                transition,
                canonicalMode.Revision,
                transition.Revision,
                "Atomic target commit requires every mandatory action to remain durably VERIFIED.");
        }

        if (transition.TargetMode is "WORK" or "GAME" &&
            (!runtimeAccessPermitsTarget || !policyIdentityCompatible))
        {
            return new ModeTransitionCommitOutcome(
                ModeTransitionCommitDisposition.AuthorityDenied,
                "MODE_TARGET_AUTHORITY_DENIED",
                canonicalMode,
                transition,
                canonicalMode.Revision,
                transition.Revision,
                "Premium target commit requires current RuntimeAccess and compatible policy authority.");
        }

        var lease = await ReadLeaseAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow();
        if (!lease.IsHeld ||
            lease.LeaseId != leaseId ||
            lease.MutationType != MachineMutationType.Mode ||
            lease.OwnerOperationId != transition.OperationId ||
            lease.OwnerCorrelationId != transition.CorrelationId ||
            !string.Equals(lease.OwnerControlSessionKey, transition.ControlSessionKey, StringComparison.Ordinal) ||
            lease.FenceToken != fenceToken)
        {
            return new ModeTransitionCommitOutcome(
                ModeTransitionCommitDisposition.LeaseConflict,
                "MUTATION_LEASE_STALE_FENCE",
                canonicalMode,
                transition,
                canonicalMode.Revision,
                transition.Revision,
                "Current major mutation lease does not match the durable transition owner/fence.");
        }

        if (lease.ExpiresUtc is null || now >= lease.ExpiresUtc.Value)
        {
            return new ModeTransitionCommitOutcome(
                ModeTransitionCommitDisposition.ReconciliationRequired,
                "MUTATION_LEASE_RECONCILIATION_REQUIRED",
                canonicalMode,
                transition,
                canonicalMode.Revision,
                transition.Revision,
                "Major mutation lease expired before target commit; persisted state must be reconciled.");
        }

        var operationKey = transition.OperationId.ToString("D");
        var correlationKey = transition.CorrelationId.ToString("D");
        var transitionKey = transition.TransitionId.ToString("D");
        var leaseKey = leaseId.ToString("D");
        var nowText = now.ToString("O");

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE operational_mode_state
            SET committed_mode = $target,
                committed_utc = $now,
                committed_by_operation_id = $operation,
                correlation_id = $correlation,
                revision = revision + 1,
                updated_utc = $now
            WHERE singleton_id = 1
              AND committed_mode = $source
              AND revision = $mode_revision
              AND EXISTS (
                  SELECT 1
                  FROM machine_mutation_lease lease
                  WHERE lease.singleton_id = 1
                    AND lease.lease_id = $lease
                    AND lease.mutation_type = 'MODE'
                    AND lease.owner_operation_id = $operation
                    AND lease.owner_correlation_id = $correlation
                    AND lease.owner_control_session_key = $session
                    AND lease.fence_token = $fence
                    AND lease.expires_utc IS NOT NULL
                    AND lease.expires_utc > $now
              );
            """;
        command.Parameters.AddWithValue("$target", transition.TargetMode);
        command.Parameters.AddWithValue("$source", transition.SourceMode);
        command.Parameters.AddWithValue("$now", nowText);
        command.Parameters.AddWithValue("$operation", operationKey);
        command.Parameters.AddWithValue("$correlation", correlationKey);
        command.Parameters.AddWithValue("$session", transition.ControlSessionKey);
        command.Parameters.AddWithValue("$lease", leaseKey);
        command.Parameters.AddWithValue("$fence", fenceToken);
        command.Parameters.AddWithValue("$mode_revision", expectedModeRevision);
        var modeAffected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (modeAffected != 1)
        {
            return new ModeTransitionCommitOutcome(
                ModeTransitionCommitDisposition.ConcurrencyConflict,
                "MODE_COMMIT_CONCURRENCY_CONFLICT",
                canonicalMode,
                transition,
                canonicalMode.Revision,
                transition.Revision,
                "Canonical mode or mutation lease changed while the atomic commit was being established.");
        }

        command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE mode_transition
            SET stage_code = 'COMMIT_DURABLE',
                commit_durable = 1,
                updated_utc = $now,
                revision = revision + 1
            WHERE transition_id = $transition
              AND operation_id = $operation
              AND correlation_id = $correlation
              AND transition_state = 'COMMITTING'
              AND stage_code = 'COMMIT_STARTED'
              AND mandatory_verified = 1
              AND commit_durable = 0
              AND source_mode = $source
              AND target_mode = $target
              AND source_mode_revision = $mode_revision
              AND control_session_key = $session
              AND lease_id = $lease
              AND fence_token = $fence
              AND revision = $transition_revision;
            """;
        command.Parameters.AddWithValue("$transition", transitionKey);
        command.Parameters.AddWithValue("$operation", operationKey);
        command.Parameters.AddWithValue("$correlation", correlationKey);
        command.Parameters.AddWithValue("$source", transition.SourceMode);
        command.Parameters.AddWithValue("$target", transition.TargetMode);
        command.Parameters.AddWithValue("$mode_revision", expectedModeRevision);
        command.Parameters.AddWithValue("$session", transition.ControlSessionKey);
        command.Parameters.AddWithValue("$lease", leaseKey);
        command.Parameters.AddWithValue("$fence", fenceToken);
        command.Parameters.AddWithValue("$transition_revision", expectedTransitionRevision);
        command.Parameters.AddWithValue("$now", nowText);
        var transitionAffected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (transitionAffected != 1)
        {
            return new ModeTransitionCommitOutcome(
                ModeTransitionCommitDisposition.ConcurrencyConflict,
                "MODE_COMMIT_CONCURRENCY_CONFLICT",
                canonicalMode,
                transition,
                canonicalMode.Revision,
                transition.Revision,
                "Durable transition marker changed while the atomic commit was being established.");
        }

        transaction.Commit();

        var committedMode = canonicalMode with
        {
            CommittedMode = transition.TargetMode,
            CommittedUtc = now,
            CommittedByOperationId = operationKey,
            CorrelationId = correlationKey,
            Revision = checked(expectedModeRevision + 1),
            UpdatedUtc = now
        };
        var committedTransition = transition with
        {
            Stage = PersistedModeTransitionStage.CommitDurable,
            CommitDurable = true,
            UpdatedUtc = now,
            Revision = checked(expectedTransitionRevision + 1)
        };

        return new ModeTransitionCommitOutcome(
            ModeTransitionCommitDisposition.Committed,
            "MODE_COMMIT_DURABLE",
            committedMode,
            committedTransition,
            committedMode.Revision,
            committedTransition.Revision);
    }

    private static async Task<bool> HasDurableVerificationEvidenceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid transitionId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM mode_transition_action
            WHERE transition_id = $transition
              AND mandatory = 1
              AND NOT (action_state = 'VERIFIED' AND apply_result_code = 'APPLIED' AND verify_result_code = 'VERIFIED');
            """;
        command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 0;
    }

    private static void ValidateRequest(
        Guid transitionId,
        int expectedTransitionRevision,
        int expectedModeRevision,
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId)
    {
        if (transitionId == Guid.Empty) throw new ArgumentException("Transition id must not be empty.", nameof(transitionId));
        if (expectedTransitionRevision < 1) throw new ArgumentOutOfRangeException(nameof(expectedTransitionRevision));
        if (expectedModeRevision < 1) throw new ArgumentOutOfRangeException(nameof(expectedModeRevision));
        if (leaseId == Guid.Empty) throw new ArgumentException("Lease id must not be empty.", nameof(leaseId));
        if (fenceToken < 1) throw new ArgumentOutOfRangeException(nameof(fenceToken));
        if (ownerOperationId == Guid.Empty) throw new ArgumentException("Owner operation id must not be empty.", nameof(ownerOperationId));
    }

    private static string? ValidateCommitReadyTransition(ModeTransitionRecord transition)
    {
        if (transition.TransitionState != PersistedModeTransitionState.Committing ||
            transition.Stage != PersistedModeTransitionStage.CommitStarted)
        {
            return "Transition must be COMMITTING / COMMIT_STARTED.";
        }
        if (!transition.MandatoryVerified)
        {
            return "Mandatory target verification is not durably recorded.";
        }
        if (transition.CommitDurable)
        {
            return "Transition is already durably committed.";
        }
        if (transition.TerminalOutcome is not null)
        {
            return "Non-terminal commit-ready transition cannot have a terminal outcome.";
        }
        return null;
    }

    private static bool IsDurableReplay(
        ModeTransitionRecord transition,
        OperationalModeRecord mode,
        Guid ownerOperationId)
        => transition.OperationId == ownerOperationId &&
           transition.Stage == PersistedModeTransitionStage.CommitDurable &&
           transition.MandatoryVerified &&
           string.Equals(mode.CommittedMode, transition.TargetMode, StringComparison.Ordinal) &&
           string.Equals(mode.CommittedByOperationId, transition.OperationId.ToString("D"), StringComparison.Ordinal) &&
           string.Equals(mode.CorrelationId, transition.CorrelationId.ToString("D"), StringComparison.Ordinal) &&
           mode.Revision == checked(transition.SourceModeRevision + 1);

    private async Task<SqliteConnection> OpenReadyAsync(CancellationToken cancellationToken)
    {
        var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var version = await _database.ReadSchemaVersionAsync(connection, cancellationToken).ConfigureAwait(false);
            if (version != MachineStateStore.SchemaVersion)
            {
                throw new InvalidDataException(
                    $"Atomic mode commit requires machine schema {MachineStateStore.SchemaVersion}, got {version}.");
            }

            foreach (var table in new[]
                     {
                         "operational_mode_state",
                         "machine_mutation_lease",
                         "mode_transition",
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
            throw new InvalidDataException(
                $"Machine canonical store is quarantined. Marker: {_quarantineMarkerPath}");
        }
        if (!File.Exists(_markerPath) || !File.Exists(_databasePath))
        {
            throw new InvalidDataException(
                "Machine canonical store must be initialized before atomic mode commit is attempted.");
        }
    }

    private static async Task<OperationalModeRecord> ReadOperationalModeAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT committed_mode, committed_utc, committed_by_operation_id, correlation_id, revision, updated_utc
            FROM operational_mode_state WHERE singleton_id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("OperationalModeState singleton is missing.");

        return new OperationalModeRecord(
            reader.GetString(0),
            DateTimeOffset.Parse(reader.GetString(1)),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetInt32(4),
            DateTimeOffset.Parse(reader.GetString(5)));
    }

    private static async Task<MachineMutationLeaseRecord> ReadLeaseAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT lease_id, mutation_type, owner_operation_id, owner_correlation_id,
                   owner_control_session_key, fence_token, acquired_utc, heartbeat_utc, expires_utc, revision
            FROM machine_mutation_lease WHERE singleton_id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("Major mutation lease singleton is missing.");

        if (reader.IsDBNull(0))
        {
            return new MachineMutationLeaseRecord(
                null, null, null, null, null,
                reader.GetInt64(5), null, null, null, reader.GetInt32(9));
        }

        if (!Guid.TryParse(reader.GetString(0), out var leaseId) ||
            !TryParseMutationType(reader.GetString(1), out var mutationType) ||
            !Guid.TryParse(reader.GetString(2), out var operationId) ||
            !Guid.TryParse(reader.GetString(3), out var correlationId) ||
            !DateTimeOffset.TryParse(reader.GetString(6), out var acquiredUtc) ||
            !DateTimeOffset.TryParse(reader.GetString(7), out var heartbeatUtc) ||
            !DateTimeOffset.TryParse(reader.GetString(8), out var expiresUtc))
        {
            throw new InvalidDataException("Major mutation lease contains malformed canonical values.");
        }

        return new MachineMutationLeaseRecord(
            leaseId,
            mutationType,
            operationId,
            correlationId,
            reader.GetString(4),
            reader.GetInt64(5),
            acquiredUtc.ToUniversalTime(),
            heartbeatUtc.ToUniversalTime(),
            expiresUtc.ToUniversalTime(),
            reader.GetInt32(9));
    }

    private static async Task<ModeTransitionRecord?> ReadTransitionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid transitionId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT transition_id, operation_id, correlation_id, operation_kind,
                   source_mode, target_mode, source_mode_revision, control_session_key,
                   lease_id, fence_token, transition_state, stage_code,
                   started_utc, updated_utc, mandatory_verified, commit_durable,
                   terminal_outcome, recovery_context_id, revision
            FROM mode_transition
            WHERE transition_id = $transition;
            """;
        command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;

        if (!Guid.TryParse(reader.GetString(0), out var parsedTransitionId) ||
            !Guid.TryParse(reader.GetString(1), out var operationId) ||
            !Guid.TryParse(reader.GetString(2), out var correlationId) ||
            !TryParseOperationKind(reader.GetString(3), out var operationKind) ||
            !Guid.TryParse(reader.GetString(8), out var leaseId) ||
            !TryParseState(reader.GetString(10), out var state) ||
            !TryParseStage(reader.GetString(11), out var stage) ||
            !DateTimeOffset.TryParse(reader.GetString(12), out var startedUtc) ||
            !DateTimeOffset.TryParse(reader.GetString(13), out var updatedUtc))
        {
            throw new InvalidDataException("Mode transition contains malformed canonical values.");
        }

        return new ModeTransitionRecord(
            parsedTransitionId,
            operationId,
            correlationId,
            operationKind,
            reader.GetString(4),
            reader.GetString(5),
            reader.GetInt32(6),
            reader.GetString(7),
            leaseId,
            reader.GetInt64(9),
            state,
            stage,
            startedUtc.ToUniversalTime(),
            updatedUtc.ToUniversalTime(),
            reader.GetInt32(14) == 1,
            reader.GetInt32(15) == 1,
            reader.IsDBNull(16) ? null : reader.GetString(16),
            reader.IsDBNull(17) ? null : reader.GetString(17),
            reader.GetInt32(18));
    }

    private static bool TryParseMutationType(string value, out MachineMutationType type)
    {
        type = value switch
        {
            "MODE" => MachineMutationType.Mode,
            "UPDATE" => MachineMutationType.Update,
            "RECOVERY" => MachineMutationType.Recovery,
            _ => default
        };
        return value is "MODE" or "UPDATE" or "RECOVERY";
    }

    private static bool TryParseOperationKind(string value, out PersistedModeOperationKind result)
    {
        result = value switch
        {
            "ACTIVATE" => PersistedModeOperationKind.Activate,
            "SWITCH" => PersistedModeOperationKind.Switch,
            "DEACTIVATE" => PersistedModeOperationKind.Deactivate,
            _ => default
        };
        return value is "ACTIVATE" or "SWITCH" or "DEACTIVATE";
    }

    private static bool TryParseState(string value, out PersistedModeTransitionState result)
    {
        result = value switch
        {
            "REQUESTED" => PersistedModeTransitionState.Requested,
            "INSPECTING" => PersistedModeTransitionState.Inspecting,
            "BLOCKED" => PersistedModeTransitionState.Blocked,
            "AWAITING_USER" => PersistedModeTransitionState.AwaitingUser,
            "RESOLVING" => PersistedModeTransitionState.Resolving,
            "APPLYING" => PersistedModeTransitionState.Applying,
            "VERIFYING" => PersistedModeTransitionState.Verifying,
            "COMMITTING" => PersistedModeTransitionState.Committing,
            "ROLLING_BACK" => PersistedModeTransitionState.RollingBack,
            "COMPLETED" => PersistedModeTransitionState.Completed,
            "CANCELLED" => PersistedModeTransitionState.Cancelled,
            "FAILED_WITH_SAFE_FALLBACK" => PersistedModeTransitionState.FailedWithSafeFallback,
            _ => default
        };
        return value is "REQUESTED" or "INSPECTING" or "BLOCKED" or "AWAITING_USER" or "RESOLVING" or
            "APPLYING" or "VERIFYING" or "COMMITTING" or "ROLLING_BACK" or "COMPLETED" or "CANCELLED" or
            "FAILED_WITH_SAFE_FALLBACK";
    }

    private static bool TryParseStage(string value, out PersistedModeTransitionStage result)
    {
        result = value switch
        {
            "ACCEPTED" => PersistedModeTransitionStage.Accepted,
            "INSPECTION_STARTED" => PersistedModeTransitionStage.InspectionStarted,
            "INSPECTION_COMPLETE" => PersistedModeTransitionStage.InspectionComplete,
            "WAITING_FOR_USER" => PersistedModeTransitionStage.WaitingForUser,
            "RESOLUTION_STARTED" => PersistedModeTransitionStage.ResolutionStarted,
            "ACTION_PLAN_READY" => PersistedModeTransitionStage.ActionPlanReady,
            "APPLY_STARTED" => PersistedModeTransitionStage.ApplyStarted,
            "APPLY_COMPLETE" => PersistedModeTransitionStage.ApplyComplete,
            "VERIFY_STARTED" => PersistedModeTransitionStage.VerifyStarted,
            "VERIFY_COMPLETE" => PersistedModeTransitionStage.VerifyComplete,
            "COMMIT_STARTED" => PersistedModeTransitionStage.CommitStarted,
            "COMMIT_DURABLE" => PersistedModeTransitionStage.CommitDurable,
            "FINALIZATION_STARTED" => PersistedModeTransitionStage.FinalizationStarted,
            "ROLLBACK_STARTED" => PersistedModeTransitionStage.RollbackStarted,
            "ROLLBACK_VERIFY" => PersistedModeTransitionStage.RollbackVerify,
            "TERMINAL" => PersistedModeTransitionStage.Terminal,
            _ => default
        };
        return value is "ACCEPTED" or "INSPECTION_STARTED" or "INSPECTION_COMPLETE" or "WAITING_FOR_USER" or
            "RESOLUTION_STARTED" or "ACTION_PLAN_READY" or "APPLY_STARTED" or "APPLY_COMPLETE" or "VERIFY_STARTED" or
            "VERIFY_COMPLETE" or "COMMIT_STARTED" or "COMMIT_DURABLE" or "FINALIZATION_STARTED" or "ROLLBACK_STARTED" or
            "ROLLBACK_VERIFY" or "TERMINAL";
    }
}
