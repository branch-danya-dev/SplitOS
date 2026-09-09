using Microsoft.Data.Sqlite;
using SplitOS.Persistence;

namespace SplitOS.Persistence.Machine;

public enum PersistedModeOperationKind
{
    Activate,
    Switch,
    Deactivate
}

public enum PersistedModeTransitionState
{
    Requested,
    Inspecting,
    Blocked,
    AwaitingUser,
    Resolving,
    Applying,
    Verifying,
    Committing,
    RollingBack,
    Completed,
    Cancelled,
    FailedWithSafeFallback
}

public enum PersistedModeTransitionStage
{
    Accepted,
    InspectionStarted,
    InspectionComplete,
    WaitingForUser,
    ResolutionStarted,
    ActionPlanReady,
    ApplyStarted,
    ApplyComplete,
    VerifyStarted,
    VerifyComplete,
    CommitStarted,
    CommitDurable,
    FinalizationStarted,
    RollbackStarted,
    RollbackVerify,
    Terminal
}

public sealed record ModeTransitionRecord(
    Guid TransitionId,
    Guid OperationId,
    Guid CorrelationId,
    PersistedModeOperationKind OperationKind,
    string SourceMode,
    string TargetMode,
    int SourceModeRevision,
    string ControlSessionKey,
    Guid LeaseId,
    long FenceToken,
    PersistedModeTransitionState TransitionState,
    PersistedModeTransitionStage Stage,
    DateTimeOffset StartedUtc,
    DateTimeOffset UpdatedUtc,
    bool MandatoryVerified,
    bool CommitDurable,
    string? TerminalOutcome,
    string? RecoveryContextId,
    int Revision);

public enum ModeTransitionCreateDisposition
{
    Created,
    Replayed,
    IdempotencyConflict,
    SourceConflict,
    LeaseConflict,
    ReconciliationRequired
}

public sealed record ModeTransitionCreateOutcome(
    ModeTransitionCreateDisposition Disposition,
    ModeTransitionRecord? Transition,
    string ProductCode,
    string? Detail = null);

public enum ModeTransitionAdvanceDisposition
{
    Advanced,
    Unchanged,
    Missing,
    RevisionConflict,
    LeaseConflict,
    InvalidLifecycle
}

public sealed record ModeTransitionAdvanceOutcome(
    ModeTransitionAdvanceDisposition Disposition,
    ModeTransitionRecord? Transition,
    string ProductCode,
    int? ActualRevision = null,
    string? Detail = null);

/// <summary>
/// Typed durable SPEC-05 transition journal. This repository owns only transition persistence semantics;
/// Windows mutation and canonical mode commit remain separate, fenced operations.
/// </summary>
public sealed class ModeTransitionStore
{
    private static readonly HashSet<PersistedModeTransitionState> TerminalStates =
        new()
        {
            PersistedModeTransitionState.Completed,
            PersistedModeTransitionState.Cancelled,
            PersistedModeTransitionState.FailedWithSafeFallback
        };

    private static readonly IReadOnlyDictionary<PersistedModeTransitionState, HashSet<PersistedModeTransitionState>> StateGraph =
        new Dictionary<PersistedModeTransitionState, HashSet<PersistedModeTransitionState>>
        {
            [PersistedModeTransitionState.Requested] = new() { PersistedModeTransitionState.Inspecting },
            [PersistedModeTransitionState.Inspecting] = new()
            {
                PersistedModeTransitionState.Blocked,
                PersistedModeTransitionState.AwaitingUser,
                PersistedModeTransitionState.Resolving
            },
            [PersistedModeTransitionState.Blocked] = new()
            {
                PersistedModeTransitionState.Inspecting,
                PersistedModeTransitionState.Cancelled
            },
            [PersistedModeTransitionState.AwaitingUser] = new()
            {
                PersistedModeTransitionState.Resolving,
                PersistedModeTransitionState.Cancelled
            },
            [PersistedModeTransitionState.Resolving] = new()
            {
                PersistedModeTransitionState.Applying,
                PersistedModeTransitionState.Cancelled
            },
            [PersistedModeTransitionState.Applying] = new()
            {
                PersistedModeTransitionState.Verifying,
                PersistedModeTransitionState.RollingBack
            },
            [PersistedModeTransitionState.Verifying] = new()
            {
                PersistedModeTransitionState.Committing,
                PersistedModeTransitionState.RollingBack
            },
            [PersistedModeTransitionState.Committing] = new()
            {
                PersistedModeTransitionState.Completed,
                PersistedModeTransitionState.RollingBack
            },
            [PersistedModeTransitionState.RollingBack] = new()
            {
                PersistedModeTransitionState.Cancelled,
                PersistedModeTransitionState.FailedWithSafeFallback
            },
            [PersistedModeTransitionState.Completed] = new(),
            [PersistedModeTransitionState.Cancelled] = new(),
            [PersistedModeTransitionState.FailedWithSafeFallback] = new()
        };

    private readonly SqliteDatabase _database;
    private readonly string _databasePath;
    private readonly string _markerPath;
    private readonly string _quarantineMarkerPath;
    private readonly TimeProvider _timeProvider;

    public ModeTransitionStore(
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
        _ = await ReadIncompleteAsync(connection, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ModeTransitionRecord?> GetAsync(
        Guid transitionId,
        CancellationToken cancellationToken = default)
    {
        if (transitionId == Guid.Empty) throw new ArgumentException("Transition id must not be empty.", nameof(transitionId));
        EnsureCanonicalStoreAvailable();
        await using var connection = await OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        return await ReadByTransitionIdAsync(connection, null, transitionId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ModeTransitionRecord>> GetIncompleteAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureCanonicalStoreAvailable();
        await using var connection = await OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        return await ReadIncompleteAsync(connection, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ModeTransitionCreateOutcome> CreateAsync(
        Guid transitionId,
        Guid operationId,
        Guid correlationId,
        PersistedModeOperationKind operationKind,
        string sourceMode,
        string targetMode,
        int sourceModeRevision,
        string controlSessionKey,
        Guid leaseId,
        long fenceToken,
        CancellationToken cancellationToken = default)
    {
        ValidateCreateRequest(
            transitionId,
            operationId,
            correlationId,
            operationKind,
            sourceMode,
            targetMode,
            sourceModeRevision,
            controlSessionKey,
            leaseId,
            fenceToken);
        EnsureCanonicalStoreAvailable();

        await using var connection = await OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        var existing = await ReadByOperationIdAsync(connection, transaction, operationId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (MatchesCreateRequest(
                    existing,
                    transitionId,
                    operationId,
                    correlationId,
                    operationKind,
                    sourceMode,
                    targetMode,
                    sourceModeRevision,
                    controlSessionKey,
                    leaseId,
                    fenceToken))
            {
                return new ModeTransitionCreateOutcome(
                    ModeTransitionCreateDisposition.Replayed,
                    existing,
                    "MODE_TRANSITION_REPLAYED");
            }

            return new ModeTransitionCreateOutcome(
                ModeTransitionCreateDisposition.IdempotencyConflict,
                existing,
                "MODE_TRANSITION_IDEMPOTENCY_CONFLICT",
                "OperationId is already bound to a different durable mode-transition request.");
        }

        var leaseCheck = await ValidateCurrentModeLeaseAsync(
            connection,
            transaction,
            leaseId,
            fenceToken,
            operationId,
            correlationId,
            controlSessionKey,
            cancellationToken).ConfigureAwait(false);
        if (!leaseCheck.IsCurrent)
        {
            return new ModeTransitionCreateOutcome(
                leaseCheck.ReconciliationRequired
                    ? ModeTransitionCreateDisposition.ReconciliationRequired
                    : ModeTransitionCreateDisposition.LeaseConflict,
                null,
                leaseCheck.ProductCode,
                leaseCheck.Detail);
        }

        var source = await ReadCanonicalModeAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(source.Mode, sourceMode, StringComparison.Ordinal) || source.Revision != sourceModeRevision)
        {
            return new ModeTransitionCreateOutcome(
                ModeTransitionCreateDisposition.SourceConflict,
                null,
                "MODE_SOURCE_REVISION_CONFLICT",
                $"Expected {sourceMode}@{sourceModeRevision}, actual {source.Mode}@{source.Revision}.");
        }

        var incomplete = await ReadIncompleteAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        if (incomplete.Count != 0)
        {
            return new ModeTransitionCreateOutcome(
                ModeTransitionCreateDisposition.ReconciliationRequired,
                incomplete[0],
                "MODE_RECONCILIATION_REQUIRED",
                "An incomplete durable mode transition already exists and must be reconciled before a new operation is created.");
        }

        var now = _timeProvider.GetUtcNow();
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO mode_transition(
                transition_id, operation_id, correlation_id, operation_kind,
                source_mode, target_mode, source_mode_revision, control_session_key,
                lease_id, fence_token, transition_state, stage_code,
                started_utc, updated_utc, mandatory_verified, commit_durable,
                terminal_outcome, recovery_context_id, revision)
            VALUES(
                $transition, $operation, $correlation, $kind,
                $source, $target, $source_revision, $session,
                $lease, $fence, 'REQUESTED', 'ACCEPTED',
                $now, $now, 0, 0,
                NULL, NULL, 1);
            """;
        command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
        command.Parameters.AddWithValue("$operation", operationId.ToString("D"));
        command.Parameters.AddWithValue("$correlation", correlationId.ToString("D"));
        command.Parameters.AddWithValue("$kind", ToStorageCode(operationKind));
        command.Parameters.AddWithValue("$source", sourceMode);
        command.Parameters.AddWithValue("$target", targetMode);
        command.Parameters.AddWithValue("$source_revision", sourceModeRevision);
        command.Parameters.AddWithValue("$session", controlSessionKey);
        command.Parameters.AddWithValue("$lease", leaseId.ToString("D"));
        command.Parameters.AddWithValue("$fence", fenceToken);
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        transaction.Commit();

        var created = new ModeTransitionRecord(
            transitionId,
            operationId,
            correlationId,
            operationKind,
            sourceMode,
            targetMode,
            sourceModeRevision,
            controlSessionKey,
            leaseId,
            fenceToken,
            PersistedModeTransitionState.Requested,
            PersistedModeTransitionStage.Accepted,
            now,
            now,
            false,
            false,
            null,
            null,
            1);
        return new ModeTransitionCreateOutcome(
            ModeTransitionCreateDisposition.Created,
            created,
            "MODE_TRANSITION_CREATED");
    }

    public async Task<ModeTransitionAdvanceOutcome> AdvanceAsync(
        Guid transitionId,
        int expectedRevision,
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        PersistedModeTransitionState nextState,
        PersistedModeTransitionStage nextStage,
        bool mandatoryVerified,
        string? terminalOutcome = null,
        CancellationToken cancellationToken = default)
    {
        if (transitionId == Guid.Empty) throw new ArgumentException("Transition id must not be empty.", nameof(transitionId));
        if (expectedRevision < 1) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        if (leaseId == Guid.Empty) throw new ArgumentException("Lease id must not be empty.", nameof(leaseId));
        if (fenceToken < 1) throw new ArgumentOutOfRangeException(nameof(fenceToken));
        if (ownerOperationId == Guid.Empty) throw new ArgumentException("Owner operation id must not be empty.", nameof(ownerOperationId));
        EnsureCanonicalStoreAvailable();

        await using var connection = await OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        var current = await ReadByTransitionIdAsync(connection, transaction, transitionId, cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            return new ModeTransitionAdvanceOutcome(
                ModeTransitionAdvanceDisposition.Missing,
                null,
                "MODE_TRANSITION_NOT_FOUND");
        }

        if (current.Revision != expectedRevision)
        {
            return new ModeTransitionAdvanceOutcome(
                ModeTransitionAdvanceDisposition.RevisionConflict,
                current,
                "MODE_TRANSITION_REVISION_CONFLICT",
                current.Revision,
                $"Expected revision {expectedRevision}, actual {current.Revision}.");
        }

        if (current.OperationId != ownerOperationId)
        {
            return new ModeTransitionAdvanceOutcome(
                ModeTransitionAdvanceDisposition.LeaseConflict,
                current,
                "MUTATION_LEASE_STALE_FENCE",
                current.Revision,
                "Transition owner operation does not match the caller.");
        }

        var leaseCheck = await ValidateCurrentModeLeaseAsync(
            connection,
            transaction,
            leaseId,
            fenceToken,
            current.OperationId,
            current.CorrelationId,
            current.ControlSessionKey,
            cancellationToken).ConfigureAwait(false);
        if (!leaseCheck.IsCurrent)
        {
            return new ModeTransitionAdvanceOutcome(
                ModeTransitionAdvanceDisposition.LeaseConflict,
                current,
                leaseCheck.ProductCode,
                current.Revision,
                leaseCheck.Detail);
        }

        // VERIFY_COMPLETE is a derived fact: if durable action evidence proves every mandatory
        // invariant, the transition repository records mandatory_verified regardless of caller input.
        if (nextStage == PersistedModeTransitionStage.VerifyComplete)
        {
            mandatoryVerified = true;
        }

        if (current.TransitionState == nextState &&
            current.Stage == nextStage &&
            current.MandatoryVerified == mandatoryVerified &&
            string.Equals(current.TerminalOutcome, terminalOutcome, StringComparison.Ordinal))
        {
            return new ModeTransitionAdvanceOutcome(
                ModeTransitionAdvanceDisposition.Unchanged,
                current,
                "MODE_TRANSITION_UNCHANGED",
                current.Revision);
        }

        var lifecycleError = ValidateAdvance(current, nextState, nextStage, mandatoryVerified, terminalOutcome);
        if (lifecycleError is not null)
        {
            return new ModeTransitionAdvanceOutcome(
                ModeTransitionAdvanceDisposition.InvalidLifecycle,
                current,
                "MODE_TRANSITION_INVALID_LIFECYCLE",
                current.Revision,
                lifecycleError);
        }

        if (nextStage == PersistedModeTransitionStage.ActionPlanReady &&
            !await HasDurablePolicyBindingAsync(
                connection,
                transaction,
                transitionId,
                cancellationToken).ConfigureAwait(false))
        {
            return new ModeTransitionAdvanceOutcome(
                ModeTransitionAdvanceDisposition.InvalidLifecycle,
                current,
                "MODE_TRANSITION_INVALID_LIFECYCLE",
                current.Revision,
                "Durable resolved policy binding is required before ACTION_PLAN_READY.");
        }

        if (nextStage == PersistedModeTransitionStage.ActionPlanReady &&
            !await HasCompleteDurableActionPlanAsync(
                connection,
                transaction,
                transitionId,
                cancellationToken).ConfigureAwait(false))
        {
            return new ModeTransitionAdvanceOutcome(
                ModeTransitionAdvanceDisposition.InvalidLifecycle,
                current,
                "MODE_TRANSITION_INVALID_LIFECYCLE",
                current.Revision,
                "Complete durable action plan is required before ACTION_PLAN_READY.");
        }

        if (nextStage == PersistedModeTransitionStage.ApplyComplete &&
            !await HasCompleteApplyEvidenceAsync(connection, transaction, transitionId, cancellationToken).ConfigureAwait(false))
        {
            return EvidenceDenied(current, "APPLY_COMPLETE requires every mandatory action to be durably APPLIED and every optional action to be settled.");
        }

        if (nextState == PersistedModeTransitionState.Verifying && nextStage == PersistedModeTransitionStage.VerifyStarted)
        {
            if (current.Stage != PersistedModeTransitionStage.ApplyComplete)
                return EvidenceDenied(current, "VERIFY_STARTED requires the durable APPLY_COMPLETE stage.");
            if (!await HasCompleteApplyEvidenceAsync(connection, transaction, transitionId, cancellationToken).ConfigureAwait(false))
                return EvidenceDenied(current, "VERIFY_STARTED requires complete durable apply evidence.");
        }

        if (nextStage == PersistedModeTransitionStage.VerifyComplete &&
            !await HasCompleteVerificationEvidenceAsync(connection, transaction, transitionId, cancellationToken).ConfigureAwait(false))
        {
            return EvidenceDenied(current, "VERIFY_COMPLETE requires all mandatory actions VERIFIED and all optional actions settled.");
        }

        if (nextState == PersistedModeTransitionState.Committing && nextStage == PersistedModeTransitionStage.CommitStarted)
        {
            if (current.Stage != PersistedModeTransitionStage.VerifyComplete)
                return EvidenceDenied(current, "COMMIT_STARTED requires the durable VERIFY_COMPLETE stage.");
            if (!await HasCompleteVerificationEvidenceAsync(connection, transaction, transitionId, cancellationToken).ConfigureAwait(false))
                return EvidenceDenied(current, "COMMIT_STARTED requires durable mandatory verification evidence.");
        }

        if (nextState == PersistedModeTransitionState.RollingBack && current.CommitDurable)
        {
            return EvidenceDenied(current, "A durably committed target cannot enter source rollback; reconciliation must converge around target truth.");
        }

        if (nextStage == PersistedModeTransitionStage.RollbackVerify &&
            !await HasCompleteRollbackEvidenceAsync(connection, transaction, transitionId, cancellationToken).ConfigureAwait(false))
        {
            return EvidenceDenied(current, "ROLLBACK_VERIFY requires every applied/unknown mutation to be durably ROLLED_BACK with no rollback failure in flight.");
        }

        if (current.TransitionState == PersistedModeTransitionState.RollingBack &&
            nextState is PersistedModeTransitionState.Cancelled or PersistedModeTransitionState.FailedWithSafeFallback)
        {
            if (current.Stage != PersistedModeTransitionStage.RollbackVerify)
                return EvidenceDenied(current, "Rollback terminalization requires the durable ROLLBACK_VERIFY stage.");
            if (!await HasCompleteRollbackEvidenceAsync(connection, transaction, transitionId, cancellationToken).ConfigureAwait(false))
                return EvidenceDenied(current, "Rollback terminalization requires complete durable rollback evidence.");
        }

        var now = _timeProvider.GetUtcNow();
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE mode_transition
            SET transition_state = $state,
                stage_code = $stage,
                mandatory_verified = $verified,
                terminal_outcome = $terminal,
                updated_utc = $updated,
                revision = revision + 1
            WHERE transition_id = $transition
              AND revision = $revision
              AND lease_id = $lease
              AND fence_token = $fence;
            """;
        command.Parameters.AddWithValue("$state", ToStorageCode(nextState));
        command.Parameters.AddWithValue("$stage", ToStorageCode(nextStage));
        command.Parameters.AddWithValue("$verified", mandatoryVerified ? 1 : 0);
        command.Parameters.AddWithValue("$terminal", (object?)terminalOutcome ?? DBNull.Value);
        command.Parameters.AddWithValue("$updated", now.ToString("O"));
        command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
        command.Parameters.AddWithValue("$revision", expectedRevision);
        command.Parameters.AddWithValue("$lease", leaseId.ToString("D"));
        command.Parameters.AddWithValue("$fence", fenceToken);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            var actual = await ReadByTransitionIdAsync(connection, transaction, transitionId, cancellationToken).ConfigureAwait(false);
            return new ModeTransitionAdvanceOutcome(
                ModeTransitionAdvanceDisposition.RevisionConflict,
                actual,
                "MODE_TRANSITION_REVISION_CONFLICT",
                actual?.Revision,
                "Transition changed concurrently.");
        }

        transaction.Commit();
        var advanced = current with
        {
            TransitionState = nextState,
            Stage = nextStage,
            MandatoryVerified = mandatoryVerified,
            TerminalOutcome = terminalOutcome,
            UpdatedUtc = now,
            Revision = checked(current.Revision + 1)
        };
        return new ModeTransitionAdvanceOutcome(
            ModeTransitionAdvanceDisposition.Advanced,
            advanced,
            "MODE_TRANSITION_ADVANCED",
            advanced.Revision);
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
                    $"Mode transition repository requires machine schema {MachineStateStore.SchemaVersion}, got {version}.");
            }

            foreach (var table in new[]
                     {
                         "operational_mode_state",
                         "machine_mutation_lease",
                         "mode_transition",
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
            throw new InvalidDataException(
                $"Machine canonical store is quarantined. Marker: {_quarantineMarkerPath}");
        }

        if (!File.Exists(_markerPath) || !File.Exists(_databasePath))
        {
            throw new InvalidDataException(
                "Machine canonical store must be initialized before mode-transition state is accessed.");
        }
    }

    private async Task<LeaseValidation> ValidateCurrentModeLeaseAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid leaseId,
        long fenceToken,
        Guid operationId,
        Guid correlationId,
        string controlSessionKey,
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

        var now = _timeProvider.GetUtcNow();
        if (now >= expiresUtc.ToUniversalTime())
        {
            return LeaseValidation.Reconcile("Major mutation lease expired; persisted transition state must be reconciled before further mutation.");
        }

        if (actualLeaseId != leaseId ||
            !string.Equals(reader.GetString(1), "MODE", StringComparison.Ordinal) ||
            actualOperationId != operationId ||
            actualCorrelationId != correlationId ||
            !string.Equals(reader.GetString(4), controlSessionKey, StringComparison.Ordinal) ||
            reader.GetInt64(5) != fenceToken)
        {
            return LeaseValidation.Stale("Major mutation lease identity/fence no longer matches the transition owner.");
        }

        return LeaseValidation.Current;
    }

    private static async Task<bool> HasCompleteDurableActionPlanAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid transitionId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM mode_transition_action_plan plan
            WHERE plan.transition_id = $transition
              AND plan.action_count BETWEEN 1 AND 512
              AND plan.action_count = (
                  SELECT COUNT(*)
                  FROM mode_transition_action action_row
                  WHERE action_row.transition_id = plan.transition_id
              );
            """;
        command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 1;
    }

    private static async Task<bool> HasCompleteApplyEvidenceAsync(
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
              AND NOT (
                  (mandatory = 1 AND action_state = 'APPLIED' AND apply_result_code = 'APPLIED') OR
                  (mandatory = 0 AND (
                      (action_state = 'APPLIED' AND apply_result_code = 'APPLIED') OR
                      (action_state = 'FAILED' AND apply_result_code IN ('FAILED','UNKNOWN')) OR
                      (action_state = 'SKIPPED' AND apply_result_code = 'SKIPPED')
                  ))
              );
            """;
        command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 0;
    }

    private static async Task<bool> HasCompleteVerificationEvidenceAsync(
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
              AND NOT (
                  (mandatory = 1 AND action_state = 'VERIFIED' AND apply_result_code = 'APPLIED' AND verify_result_code = 'VERIFIED') OR
                  (mandatory = 0 AND (
                      (action_state = 'VERIFIED' AND apply_result_code = 'APPLIED' AND verify_result_code = 'VERIFIED') OR
                      (action_state = 'FAILED' AND apply_result_code IN ('FAILED','UNKNOWN')) OR
                      (action_state = 'FAILED' AND apply_result_code = 'APPLIED' AND verify_result_code IN ('MISMATCH','UNKNOWN')) OR
                      (action_state = 'SKIPPED' AND apply_result_code = 'SKIPPED')
                  ))
              );
            """;
        command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 0;
    }

    private static async Task<bool> HasCompleteRollbackEvidenceAsync(
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
              AND (
                  action_state IN ('APPLYING','ROLLING_BACK','ROLLBACK_FAILED') OR
                  (apply_result_code IN ('APPLIED','UNKNOWN') AND action_state != 'ROLLED_BACK')
              );
            """;
        command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 0;
    }

    private static ModeTransitionAdvanceOutcome EvidenceDenied(ModeTransitionRecord current, string detail)
        => new(
            ModeTransitionAdvanceDisposition.InvalidLifecycle,
            current,
            "MODE_TRANSITION_EVIDENCE_INCOMPLETE",
            current.Revision,
            detail);

    private static async Task<bool> HasDurablePolicyBindingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid transitionId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM mode_transition_policy_binding
            WHERE transition_id = $transition;
            """;
        command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 1;
    }

    private static async Task<(string Mode, int Revision)> ReadCanonicalModeAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT committed_mode, revision FROM operational_mode_state WHERE singleton_id = 1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException("OperationalModeState singleton is missing.");
        }
        return (reader.GetString(0), reader.GetInt32(1));
    }

    private static async Task<ModeTransitionRecord?> ReadByTransitionIdAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid transitionId,
        CancellationToken cancellationToken)
        => await ReadSingleAsync(
            connection,
            transaction,
            "transition_id = $id",
            transitionId.ToString("D"),
            cancellationToken).ConfigureAwait(false);

    private static async Task<ModeTransitionRecord?> ReadByOperationIdAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid operationId,
        CancellationToken cancellationToken)
        => await ReadSingleAsync(
            connection,
            transaction,
            "operation_id = $id",
            operationId.ToString("D"),
            cancellationToken).ConfigureAwait(false);

    private static async Task<ModeTransitionRecord?> ReadSingleAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string predicate,
        string id,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT transition_id, operation_id, correlation_id, operation_kind,
                   source_mode, target_mode, source_mode_revision, control_session_key,
                   lease_id, fence_token, transition_state, stage_code,
                   started_utc, updated_utc, mandatory_verified, commit_durable,
                   terminal_outcome, recovery_context_id, revision
            FROM mode_transition
            WHERE {predicate};
            """;
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return ParseRecord(reader);
    }

    private static async Task<IReadOnlyList<ModeTransitionRecord>> ReadIncompleteAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
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
            WHERE transition_state NOT IN ('COMPLETED','CANCELLED','FAILED_WITH_SAFE_FALLBACK')
            ORDER BY started_utc ASC;
            """;
        var result = new List<ModeTransitionRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(ParseRecord(reader));
        }
        return result;
    }

    private static ModeTransitionRecord ParseRecord(SqliteDataReader reader)
    {
        if (!Guid.TryParse(reader.GetString(0), out var transitionId) ||
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

        var record = new ModeTransitionRecord(
            transitionId,
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
        ValidateCanonicalRecord(record);
        return record;
    }

    private static void ValidateCreateRequest(
        Guid transitionId,
        Guid operationId,
        Guid correlationId,
        PersistedModeOperationKind operationKind,
        string sourceMode,
        string targetMode,
        int sourceModeRevision,
        string controlSessionKey,
        Guid leaseId,
        long fenceToken)
    {
        if (transitionId == Guid.Empty) throw new ArgumentException("Transition id must not be empty.", nameof(transitionId));
        if (operationId == Guid.Empty) throw new ArgumentException("Operation id must not be empty.", nameof(operationId));
        if (correlationId == Guid.Empty) throw new ArgumentException("Correlation id must not be empty.", nameof(correlationId));
        if (sourceModeRevision < 1) throw new ArgumentOutOfRangeException(nameof(sourceModeRevision));
        if (leaseId == Guid.Empty) throw new ArgumentException("Lease id must not be empty.", nameof(leaseId));
        if (fenceToken < 1) throw new ArgumentOutOfRangeException(nameof(fenceToken));
        ValidateControlSessionKey(controlSessionKey);
        ValidateTuple(operationKind, sourceMode, targetMode);
    }

    private static void ValidateTuple(PersistedModeOperationKind operationKind, string sourceMode, string targetMode)
    {
        if (sourceMode is not ("NONE" or "WORK" or "GAME"))
            throw new ArgumentOutOfRangeException(nameof(sourceMode));
        if (targetMode is not ("NONE" or "WORK" or "GAME"))
            throw new ArgumentOutOfRangeException(nameof(targetMode));

        var valid = operationKind switch
        {
            PersistedModeOperationKind.Activate => sourceMode == "NONE" && targetMode is "WORK" or "GAME",
            PersistedModeOperationKind.Switch =>
                (sourceMode == "WORK" && targetMode == "GAME") ||
                (sourceMode == "GAME" && targetMode == "WORK"),
            PersistedModeOperationKind.Deactivate => sourceMode is "WORK" or "GAME" && targetMode == "NONE",
            _ => false
        };
        if (!valid)
        {
            throw new ArgumentException($"Invalid durable mode tuple {operationKind}: {sourceMode} -> {targetMode}.");
        }
    }

    private static void ValidateControlSessionKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(static c => char.IsControl(c)))
        {
            throw new ArgumentException("Control-session key is missing or outside supported bounds.", nameof(value));
        }
    }

    private static bool MatchesCreateRequest(
        ModeTransitionRecord record,
        Guid transitionId,
        Guid operationId,
        Guid correlationId,
        PersistedModeOperationKind operationKind,
        string sourceMode,
        string targetMode,
        int sourceModeRevision,
        string controlSessionKey,
        Guid leaseId,
        long fenceToken)
        => record.TransitionId == transitionId &&
           record.OperationId == operationId &&
           record.CorrelationId == correlationId &&
           record.OperationKind == operationKind &&
           string.Equals(record.SourceMode, sourceMode, StringComparison.Ordinal) &&
           string.Equals(record.TargetMode, targetMode, StringComparison.Ordinal) &&
           record.SourceModeRevision == sourceModeRevision &&
           string.Equals(record.ControlSessionKey, controlSessionKey, StringComparison.Ordinal) &&
           record.LeaseId == leaseId &&
           record.FenceToken == fenceToken;

    private static string? ValidateAdvance(
        ModeTransitionRecord current,
        PersistedModeTransitionState nextState,
        PersistedModeTransitionStage nextStage,
        bool mandatoryVerified,
        string? terminalOutcome)
    {
        if (current.CommitDurable && !mandatoryVerified)
            return "Mandatory verification cannot be cleared after durable commit.";
        if (current.MandatoryVerified && !mandatoryVerified)
            return "Mandatory verification is monotonic once recorded.";
        if (TerminalStates.Contains(current.TransitionState))
            return "Terminal mode transition cannot advance.";

        if (nextState != current.TransitionState)
        {
            if (!StateGraph.TryGetValue(current.TransitionState, out var allowed) || !allowed.Contains(nextState))
                return $"State {current.TransitionState} cannot advance to {nextState}.";
        }
        else if (StageRank(nextStage) < StageRank(current.Stage))
        {
            return "Stage cannot move backward within the same transition state.";
        }

        if (!IsValidStateStage(nextState, nextStage))
            return $"Stage {nextStage} is invalid for state {nextState}.";

        if (nextState == PersistedModeTransitionState.Committing && !mandatoryVerified)
            return "COMMITTING requires persisted mandatory verification.";
        if (nextStage == PersistedModeTransitionStage.CommitDurable && !current.CommitDurable)
            return "COMMIT_DURABLE can only be recorded by the atomic target-commit operation.";
        if (nextState == PersistedModeTransitionState.Completed && !current.CommitDurable)
            return "COMPLETED requires a durable target commit.";

        var expectedTerminal = nextState switch
        {
            PersistedModeTransitionState.Completed => "COMPLETED",
            PersistedModeTransitionState.Cancelled => "CANCELLED",
            PersistedModeTransitionState.FailedWithSafeFallback => "FAILED_WITH_SAFE_FALLBACK",
            _ => null
        };
        if (expectedTerminal is null && terminalOutcome is not null)
            return "Non-terminal transition cannot set terminal outcome.";
        if (expectedTerminal is not null && !string.Equals(expectedTerminal, terminalOutcome, StringComparison.Ordinal))
            return $"Terminal state {nextState} requires terminal outcome {expectedTerminal}.";

        if (nextState is PersistedModeTransitionState.Cancelled or PersistedModeTransitionState.FailedWithSafeFallback && current.CommitDurable)
            return "Rollback/cancellation terminal outcome cannot follow durable target commit.";

        return null;
    }

    private static bool IsValidStateStage(PersistedModeTransitionState state, PersistedModeTransitionStage stage)
        => state switch
        {
            PersistedModeTransitionState.Requested => stage == PersistedModeTransitionStage.Accepted,
            PersistedModeTransitionState.Inspecting => stage is PersistedModeTransitionStage.InspectionStarted or PersistedModeTransitionStage.InspectionComplete,
            PersistedModeTransitionState.Blocked => stage == PersistedModeTransitionStage.InspectionComplete,
            PersistedModeTransitionState.AwaitingUser => stage == PersistedModeTransitionStage.WaitingForUser,
            PersistedModeTransitionState.Resolving => stage is PersistedModeTransitionStage.ResolutionStarted or PersistedModeTransitionStage.ActionPlanReady,
            PersistedModeTransitionState.Applying => stage is PersistedModeTransitionStage.ApplyStarted or PersistedModeTransitionStage.ApplyComplete,
            PersistedModeTransitionState.Verifying => stage is PersistedModeTransitionStage.VerifyStarted or PersistedModeTransitionStage.VerifyComplete,
            PersistedModeTransitionState.Committing => stage is PersistedModeTransitionStage.CommitStarted or PersistedModeTransitionStage.CommitDurable or PersistedModeTransitionStage.FinalizationStarted,
            PersistedModeTransitionState.RollingBack => stage is PersistedModeTransitionStage.RollbackStarted or PersistedModeTransitionStage.RollbackVerify,
            PersistedModeTransitionState.Completed or PersistedModeTransitionState.Cancelled or PersistedModeTransitionState.FailedWithSafeFallback =>
                stage == PersistedModeTransitionStage.Terminal,
            _ => false
        };

    private static int StageRank(PersistedModeTransitionStage stage)
        => stage switch
        {
            PersistedModeTransitionStage.Accepted => 10,
            PersistedModeTransitionStage.InspectionStarted => 20,
            PersistedModeTransitionStage.InspectionComplete => 30,
            PersistedModeTransitionStage.WaitingForUser => 40,
            PersistedModeTransitionStage.ResolutionStarted => 50,
            PersistedModeTransitionStage.ActionPlanReady => 60,
            PersistedModeTransitionStage.ApplyStarted => 70,
            PersistedModeTransitionStage.ApplyComplete => 80,
            PersistedModeTransitionStage.VerifyStarted => 90,
            PersistedModeTransitionStage.VerifyComplete => 100,
            PersistedModeTransitionStage.CommitStarted => 110,
            PersistedModeTransitionStage.CommitDurable => 120,
            PersistedModeTransitionStage.FinalizationStarted => 130,
            PersistedModeTransitionStage.RollbackStarted => 140,
            PersistedModeTransitionStage.RollbackVerify => 150,
            PersistedModeTransitionStage.Terminal => 160,
            _ => throw new ArgumentOutOfRangeException(nameof(stage))
        };

    private static void ValidateCanonicalRecord(ModeTransitionRecord record)
    {
        ValidateTuple(record.OperationKind, record.SourceMode, record.TargetMode);
        ValidateControlSessionKey(record.ControlSessionKey);
        if (record.SourceModeRevision < 1 || record.FenceToken < 1 || record.Revision < 1)
            throw new InvalidDataException("Mode transition counters violate canonical bounds.");
        if (!IsValidStateStage(record.TransitionState, record.Stage))
            throw new InvalidDataException("Mode transition state/stage pair violates canonical semantics.");
        if (record.Stage == PersistedModeTransitionStage.CommitDurable && !record.CommitDurable)
            throw new InvalidDataException("COMMIT_DURABLE stage is missing durable commit marker.");
        if (record.TransitionState == PersistedModeTransitionState.Completed && !record.CommitDurable)
            throw new InvalidDataException("COMPLETED transition is missing durable commit marker.");
        if (record.TransitionState is PersistedModeTransitionState.Cancelled or PersistedModeTransitionState.FailedWithSafeFallback && record.CommitDurable)
            throw new InvalidDataException("Rollback/cancellation outcome cannot claim durable target commit.");
    }

    private static string ToStorageCode(PersistedModeOperationKind value)
        => value switch
        {
            PersistedModeOperationKind.Activate => "ACTIVATE",
            PersistedModeOperationKind.Switch => "SWITCH",
            PersistedModeOperationKind.Deactivate => "DEACTIVATE",
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

    private static string ToStorageCode(PersistedModeTransitionState value)
        => value switch
        {
            PersistedModeTransitionState.Requested => "REQUESTED",
            PersistedModeTransitionState.Inspecting => "INSPECTING",
            PersistedModeTransitionState.Blocked => "BLOCKED",
            PersistedModeTransitionState.AwaitingUser => "AWAITING_USER",
            PersistedModeTransitionState.Resolving => "RESOLVING",
            PersistedModeTransitionState.Applying => "APPLYING",
            PersistedModeTransitionState.Verifying => "VERIFYING",
            PersistedModeTransitionState.Committing => "COMMITTING",
            PersistedModeTransitionState.RollingBack => "ROLLING_BACK",
            PersistedModeTransitionState.Completed => "COMPLETED",
            PersistedModeTransitionState.Cancelled => "CANCELLED",
            PersistedModeTransitionState.FailedWithSafeFallback => "FAILED_WITH_SAFE_FALLBACK",
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

    private static string ToStorageCode(PersistedModeTransitionStage value)
        => value switch
        {
            PersistedModeTransitionStage.Accepted => "ACCEPTED",
            PersistedModeTransitionStage.InspectionStarted => "INSPECTION_STARTED",
            PersistedModeTransitionStage.InspectionComplete => "INSPECTION_COMPLETE",
            PersistedModeTransitionStage.WaitingForUser => "WAITING_FOR_USER",
            PersistedModeTransitionStage.ResolutionStarted => "RESOLUTION_STARTED",
            PersistedModeTransitionStage.ActionPlanReady => "ACTION_PLAN_READY",
            PersistedModeTransitionStage.ApplyStarted => "APPLY_STARTED",
            PersistedModeTransitionStage.ApplyComplete => "APPLY_COMPLETE",
            PersistedModeTransitionStage.VerifyStarted => "VERIFY_STARTED",
            PersistedModeTransitionStage.VerifyComplete => "VERIFY_COMPLETE",
            PersistedModeTransitionStage.CommitStarted => "COMMIT_STARTED",
            PersistedModeTransitionStage.CommitDurable => "COMMIT_DURABLE",
            PersistedModeTransitionStage.FinalizationStarted => "FINALIZATION_STARTED",
            PersistedModeTransitionStage.RollbackStarted => "ROLLBACK_STARTED",
            PersistedModeTransitionStage.RollbackVerify => "ROLLBACK_VERIFY",
            PersistedModeTransitionStage.Terminal => "TERMINAL",
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

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

    private sealed record LeaseValidation(bool IsCurrent, bool ReconciliationRequired, string ProductCode, string? Detail)
    {
        public static LeaseValidation Current { get; } = new(true, false, "MUTATION_LEASE_CURRENT", null);
        public static LeaseValidation Stale(string detail) => new(false, false, "MUTATION_LEASE_STALE_FENCE", detail);
        public static LeaseValidation Reconcile(string detail) => new(false, true, "MUTATION_LEASE_RECONCILIATION_REQUIRED", detail);
    }
}
