using Microsoft.Data.Sqlite;
using SplitOS.Persistence;

namespace SplitOS.Persistence.Machine;

public sealed record ModeMutationActionBinding(
    string OwningModule,
    string ActionType,
    string? TargetRef,
    int DesiredSchemaVersion,
    string DesiredStateDigest);

public sealed record ModeMutationFenceContext(
    Guid TransitionId,
    Guid ActionId,
    Guid LeaseId,
    long FenceToken,
    Guid OperationId,
    Guid CorrelationId,
    string ControlSessionKey,
    int ExpectedActionRevision,
    ModeMutationActionBinding? ExpectedAction = null);

public enum ModeMutationFenceValidationDisposition
{
    Authorized,
    Missing,
    RevisionConflict,
    LeaseConflict,
    ReconciliationRequired,
    OwnershipConflict,
    InvalidLifecycle
}

public sealed record ModeMutationFenceValidationOutcome(
    ModeMutationFenceValidationDisposition Disposition,
    string ProductCode,
    int? ActualActionRevision = null,
    string? Detail = null)
{
    public bool IsAuthorized => Disposition == ModeMutationFenceValidationDisposition.Authorized;
}

/// <summary>
/// Canonical read-side validation used immediately at the privileged Broker mutation boundary.
/// All lease, transition and action evidence is read from one SQLite transaction/snapshot so a
/// stale Runtime Host cannot combine observations from different canonical revisions.
/// This repository authorizes no mutation by itself; the Broker remains the enforcement point.
/// </summary>
public sealed class ModeMutationFenceStore
{
    private readonly SqliteDatabase _database;
    private readonly string _databasePath;
    private readonly string _markerPath;
    private readonly string _quarantineMarkerPath;
    private readonly TimeProvider _timeProvider;

    public ModeMutationFenceStore(
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

    public Task<ModeMutationFenceValidationOutcome> ValidateAsync(
        ModeMutationFenceContext context,
        CancellationToken cancellationToken = default)
        => ValidateCoreAsync(context, false, cancellationToken);

    public Task<ModeMutationFenceValidationOutcome> ValidateRollbackAsync(
        ModeMutationFenceContext context,
        CancellationToken cancellationToken = default)
        => ValidateCoreAsync(context, true, cancellationToken);

    private async Task<ModeMutationFenceValidationOutcome> ValidateCoreAsync(
        ModeMutationFenceContext context, bool rollback, CancellationToken cancellationToken)
    {
        ValidateContext(context);
        EnsureCanonicalStoreAvailable();

        await using var connection = await OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        var lease = await ReadLeaseAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        if (lease is null || !lease.LeaseId.HasValue)
        {
            return Denied(
                ModeMutationFenceValidationDisposition.LeaseConflict,
                "MUTATION_LEASE_STALE_FENCE",
                "No active major mutation lease exists.");
        }

        if (_timeProvider.GetUtcNow() >= lease.ExpiresUtc)
        {
            return Denied(
                ModeMutationFenceValidationDisposition.ReconciliationRequired,
                "MUTATION_LEASE_RECONCILIATION_REQUIRED",
                "Major mutation lease expired; privileged MODE mutation requires reconciliation before any further apply.");
        }

        if (!string.Equals(lease.MutationType, "MODE", StringComparison.Ordinal) ||
            lease.LeaseId.Value != context.LeaseId ||
            lease.FenceToken != context.FenceToken ||
            lease.OperationId != context.OperationId ||
            lease.CorrelationId != context.CorrelationId ||
            !string.Equals(lease.ControlSessionKey, context.ControlSessionKey, StringComparison.Ordinal))
        {
            return Denied(
                ModeMutationFenceValidationDisposition.LeaseConflict,
                "MUTATION_LEASE_STALE_FENCE",
                "Current canonical MODE lease/fence ownership does not match the supplied Broker mutation context.");
        }

        var transition = await ReadTransitionAsync(
            connection,
            transaction,
            context.TransitionId,
            cancellationToken).ConfigureAwait(false);
        if (transition is null)
        {
            return Denied(
                ModeMutationFenceValidationDisposition.Missing,
                "MODE_MUTATION_TRANSITION_NOT_FOUND",
                "Owning mode transition does not exist.");
        }

        if (transition.OperationId != context.OperationId ||
            transition.CorrelationId != context.CorrelationId ||
            transition.LeaseId != context.LeaseId ||
            transition.FenceToken != context.FenceToken ||
            !string.Equals(transition.ControlSessionKey, context.ControlSessionKey, StringComparison.Ordinal))
        {
            return Denied(
                ModeMutationFenceValidationDisposition.OwnershipConflict,
                "MODE_MUTATION_TRANSITION_MISMATCH",
                "Owning transition identity no longer matches the current mutation context.");
        }

        if (!string.Equals(transition.State, rollback ? "ROLLING_BACK" : "APPLYING", StringComparison.Ordinal) ||
            !string.Equals(transition.Stage, rollback ? "ROLLBACK_STARTED" : "APPLY_STARTED", StringComparison.Ordinal) ||
            transition.CommitDurable)
        {
            return Denied(
                ModeMutationFenceValidationDisposition.InvalidLifecycle,
                "MODE_MUTATION_INVALID_LIFECYCLE",
                "Broker MODE mutation is permitted only while the owning transition is APPLYING/APPLY_STARTED before durable commit.");
        }

        var action = await ReadActionAsync(
            connection,
            transaction,
            context.ActionId,
            cancellationToken).ConfigureAwait(false);
        if (action is null)
        {
            return Denied(
                ModeMutationFenceValidationDisposition.Missing,
                "MODE_MUTATION_ACTION_NOT_FOUND",
                "Owning durable mode action does not exist.");
        }

        if (action.TransitionId != context.TransitionId)
        {
            return Denied(
                ModeMutationFenceValidationDisposition.OwnershipConflict,
                "MODE_MUTATION_ACTION_MISMATCH",
                "Durable action belongs to a different mode transition.",
                action.Revision);
        }

        if (action.Revision != context.ExpectedActionRevision)
        {
            return Denied(
                ModeMutationFenceValidationDisposition.RevisionConflict,
                "MODE_ACTION_REVISION_CONFLICT",
                $"Expected action revision {context.ExpectedActionRevision}, actual {action.Revision}.",
                action.Revision);
        }

        if (!action.PlanComplete)
        {
            return Denied(
                ModeMutationFenceValidationDisposition.InvalidLifecycle,
                "MODE_ACTION_PLAN_INCOMPLETE",
                "Durable action-plan header/count no longer matches the persisted action journal.",
                action.Revision);
        }

        if (context.ExpectedAction is not null && !MatchesActionBinding(action, context.ExpectedAction))
        {
            return Denied(
                ModeMutationFenceValidationDisposition.InvalidLifecycle,
                "MODE_MUTATION_ACTION_SEMANTICS_MISMATCH",
                "Privileged request does not match immutable durable action semantics.",
                action.Revision);
        }

        if (!string.Equals(action.State, rollback ? "ROLLING_BACK" : "APPLYING", StringComparison.Ordinal))
        {
            return Denied(
                ModeMutationFenceValidationDisposition.InvalidLifecycle,
                "MODE_ACTION_NOT_APPLYING",
                "Broker MODE mutation requires the owning durable action to be marked APPLYING before adapter invocation.",
                action.Revision);
        }

        if (rollback)
        {
            var order = connection.CreateCommand();
            order.Transaction = transaction;
            order.CommandText = """
                SELECT COUNT(*) FROM mode_transition_action
                WHERE transition_id = $transition AND (
                    action_state = 'APPLYING' OR action_state = 'ROLLBACK_FAILED' OR
                    (sequence_no > (SELECT sequence_no FROM mode_transition_action WHERE action_id = $action)
                     AND (action_state = 'ROLLING_BACK' OR
                          (apply_result_code IN ('APPLIED','UNKNOWN') AND action_state != 'ROLLED_BACK'))));
                """;
            order.Parameters.AddWithValue("$transition", context.TransitionId.ToString("D"));
            order.Parameters.AddWithValue("$action", context.ActionId.ToString("D"));
            if (Convert.ToInt32(await order.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0)
                return Denied(ModeMutationFenceValidationDisposition.InvalidLifecycle,
                    "MODE_ROLLBACK_ORDER_BLOCKED", "Unresolved apply or later compensation blocks rollback.", action.Revision);
        }

        transaction.Commit();
        return new ModeMutationFenceValidationOutcome(
            ModeMutationFenceValidationDisposition.Authorized,
            "MODE_MUTATION_FENCE_AUTHORIZED",
            action.Revision);
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
                    $"Mode mutation fence requires machine schema {MachineStateStore.SchemaVersion}, got {version}.");
            }

            foreach (var table in new[]
                     {
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
            throw new InvalidDataException($"Machine canonical store is quarantined. Marker: {_quarantineMarkerPath}");
        }

        if (!File.Exists(_markerPath) || !File.Exists(_databasePath))
        {
            throw new InvalidDataException(
                "Machine canonical store must be initialized before Broker mutation fencing is evaluated.");
        }
    }

    private static async Task<LeaseRow?> ReadLeaseAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
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
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        if (reader.IsDBNull(0)) return new LeaseRow(null, null, null, null, null, reader.GetInt64(5), DateTimeOffset.MinValue);

        if (!Guid.TryParse(reader.GetString(0), out var leaseId) ||
            !Guid.TryParse(reader.GetString(2), out var operationId) ||
            !Guid.TryParse(reader.GetString(3), out var correlationId) ||
            !DateTimeOffset.TryParse(reader.GetString(6), out var expiresUtc))
        {
            throw new InvalidDataException("Major mutation lease contains malformed canonical Broker-fence values.");
        }

        return new LeaseRow(
            leaseId,
            reader.GetString(1),
            operationId,
            correlationId,
            reader.GetString(4),
            reader.GetInt64(5),
            expiresUtc.ToUniversalTime());
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
            WHERE transition_id = $transition AND recovery_context_id IS NULL;
            """;
        command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;

        if (!Guid.TryParse(reader.GetString(0), out var operationId) ||
            !Guid.TryParse(reader.GetString(1), out var correlationId) ||
            !Guid.TryParse(reader.GetString(3), out var leaseId))
        {
            throw new InvalidDataException("Mode transition contains malformed canonical Broker-fence ownership values.");
        }

        return new TransitionRow(
            operationId,
            correlationId,
            reader.GetString(2),
            leaseId,
            reader.GetInt64(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetInt32(7) == 1);
    }

    private static async Task<ActionRow?> ReadActionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid actionId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT action_row.transition_id, action_row.action_state, action_row.revision,
                   action_row.owning_module, action_row.action_type, action_row.target_ref,
                   action_row.desired_schema_version, action_row.desired_state_digest,
                   CASE WHEN plan.transition_id IS NOT NULL
                         AND plan.action_count = (
                             SELECT COUNT(*)
                             FROM mode_transition_action counted
                             WHERE counted.transition_id = action_row.transition_id
                         )
                        THEN 1 ELSE 0 END AS plan_complete
            FROM mode_transition_action action_row
            LEFT JOIN mode_transition_action_plan plan ON plan.transition_id = action_row.transition_id
            WHERE action_row.action_id = $action;
            """;
        command.Parameters.AddWithValue("$action", actionId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;

        if (!Guid.TryParse(reader.GetString(0), out var transitionId))
        {
            throw new InvalidDataException("Mode action contains malformed transition ownership.");
        }

        return new ActionRow(
            transitionId,
            reader.GetString(1),
            reader.GetInt32(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetInt32(6),
            reader.GetString(7),
            reader.GetInt32(8) == 1);
    }

    private static bool MatchesActionBinding(ActionRow action, ModeMutationActionBinding expected)
        => string.Equals(action.OwningModule, expected.OwningModule, StringComparison.Ordinal) &&
           string.Equals(action.ActionType, expected.ActionType, StringComparison.Ordinal) &&
           string.Equals(action.TargetRef, expected.TargetRef, StringComparison.Ordinal) &&
           action.DesiredSchemaVersion == expected.DesiredSchemaVersion &&
           string.Equals(action.DesiredStateDigest, expected.DesiredStateDigest, StringComparison.OrdinalIgnoreCase);

    private static ModeMutationFenceValidationOutcome Denied(
        ModeMutationFenceValidationDisposition disposition,
        string productCode,
        string detail,
        int? actualActionRevision = null)
        => new(disposition, productCode, actualActionRevision, detail);

    private static void ValidateContext(ModeMutationFenceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.TransitionId == Guid.Empty) throw new ArgumentException("TransitionId must not be empty.", nameof(context));
        if (context.ActionId == Guid.Empty) throw new ArgumentException("ActionId must not be empty.", nameof(context));
        if (context.LeaseId == Guid.Empty) throw new ArgumentException("LeaseId must not be empty.", nameof(context));
        if (context.FenceToken < 1) throw new ArgumentOutOfRangeException(nameof(context), "FenceToken must be greater than zero.");
        if (context.OperationId == Guid.Empty) throw new ArgumentException("OperationId must not be empty.", nameof(context));
        if (context.CorrelationId == Guid.Empty) throw new ArgumentException("CorrelationId must not be empty.", nameof(context));
        if (string.IsNullOrWhiteSpace(context.ControlSessionKey) ||
            context.ControlSessionKey.Length > 256 ||
            context.ControlSessionKey.Any(static character => char.IsControl(character)))
        {
            throw new ArgumentException("ControlSessionKey must be a bounded non-empty semantic identifier.", nameof(context));
        }
        if (context.ExpectedActionRevision < 1)
            throw new ArgumentOutOfRangeException(nameof(context), "ExpectedActionRevision must be greater than zero.");

        if (context.ExpectedAction is not null)
        {
            ValidateActionBinding(context.ExpectedAction);
        }
    }

    private static void ValidateActionBinding(ModeMutationActionBinding binding)
    {
        if (!IsBoundedSemanticId(binding.OwningModule, 128) ||
            !IsBoundedSemanticId(binding.ActionType, 128))
        {
            throw new ArgumentException("Expected action module/type is outside supported semantic bounds.", nameof(binding));
        }

        if (binding.TargetRef is { } targetRef &&
            (string.IsNullOrWhiteSpace(targetRef) || targetRef.Length > 256 || targetRef.Any(static character => char.IsControl(character))))
        {
            throw new ArgumentException("Expected action target reference is outside supported bounds.", nameof(binding));
        }

        if (binding.DesiredSchemaVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(binding), "Desired schema version must be greater than zero.");
        if (!IsSha256Hex(binding.DesiredStateDigest))
            throw new ArgumentException("Expected desired-state digest must be SHA-256 hex.", nameof(binding));
    }

    private static bool IsBoundedSemanticId(string value, int maxLength)
        => !string.IsNullOrWhiteSpace(value) &&
           value.Length <= maxLength &&
           !value.Any(static character => char.IsControl(character));

    private static bool IsSha256Hex(string value)
    {
        if (value is null || value.Length != 64) return false;
        foreach (var character in value)
        {
            if (!Uri.IsHexDigit(character)) return false;
        }
        return true;
    }

    private sealed record LeaseRow(
        Guid? LeaseId,
        string? MutationType,
        Guid? OperationId,
        Guid? CorrelationId,
        string? ControlSessionKey,
        long FenceToken,
        DateTimeOffset ExpiresUtc);

    private sealed record TransitionRow(
        Guid OperationId,
        Guid CorrelationId,
        string ControlSessionKey,
        Guid LeaseId,
        long FenceToken,
        string State,
        string Stage,
        bool CommitDurable);

    private sealed record ActionRow(
        Guid TransitionId,
        string State,
        int Revision,
        string OwningModule,
        string ActionType,
        string? TargetRef,
        int DesiredSchemaVersion,
        string DesiredStateDigest,
        bool PlanComplete);
}
