using Microsoft.Data.Sqlite;
using SplitOS.Persistence;

namespace SplitOS.Persistence.Machine;

public enum ModePreMutationEvidenceDisposition
{
    Authorized,
    Missing,
    RevisionConflict,
    LeaseConflict,
    ReconciliationRequired,
    OwnershipConflict,
    InvalidLifecycle
}

public sealed record ModePreMutationEvidenceOutcome(
    ModePreMutationEvidenceDisposition Disposition,
    string ProductCode,
    int? ActualActionRevision = null,
    string? Detail = null)
{
    public bool IsAuthorized => Disposition == ModePreMutationEvidenceDisposition.Authorized;
}

/// <summary>
/// Read-side authorization for collecting bounded actual-state evidence immediately before
/// BeginApply. Unlike <see cref="ModeMutationFenceStore"/>, this boundary requires the durable
/// action to remain PLANNED. It never authorizes a Windows mutation.
/// </summary>
public sealed class ModePreMutationEvidenceStore
{
    private readonly SqliteDatabase _database;
    private readonly string _databasePath;
    private readonly string _markerPath;
    private readonly string _quarantineMarkerPath;
    private readonly TimeProvider _timeProvider;

    public ModePreMutationEvidenceStore(
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

    public async Task<ModePreMutationEvidenceOutcome> ValidateAsync(
        ModeMutationFenceContext context,
        CancellationToken cancellationToken = default)
    {
        ValidateContext(context);
        if (context.ExpectedAction is null)
            throw new ArgumentException("Pre-mutation evidence requires immutable action semantics.", nameof(context));
        EnsureCanonicalStoreAvailable();

        await using var connection = await OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        var lease = await ReadLeaseAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        if (lease is null || !lease.LeaseId.HasValue)
            return Denied(ModePreMutationEvidenceDisposition.LeaseConflict, "MUTATION_LEASE_STALE_FENCE", "No active major mutation lease exists.");

        if (_timeProvider.GetUtcNow() >= lease.ExpiresUtc)
            return Denied(ModePreMutationEvidenceDisposition.ReconciliationRequired, "MUTATION_LEASE_RECONCILIATION_REQUIRED", "Major mutation lease expired before pre-state observation.");

        if (!string.Equals(lease.MutationType, "MODE", StringComparison.Ordinal) ||
            lease.LeaseId.Value != context.LeaseId ||
            lease.FenceToken != context.FenceToken ||
            lease.OperationId != context.OperationId ||
            lease.CorrelationId != context.CorrelationId ||
            !string.Equals(lease.ControlSessionKey, context.ControlSessionKey, StringComparison.Ordinal))
        {
            return Denied(ModePreMutationEvidenceDisposition.LeaseConflict, "MUTATION_LEASE_STALE_FENCE", "Current MODE lease/fence does not match the pre-state request.");
        }

        var transition = await ReadTransitionAsync(connection, transaction, context.TransitionId, cancellationToken).ConfigureAwait(false);
        if (transition is null)
            return Denied(ModePreMutationEvidenceDisposition.Missing, "MODE_PRESTATE_TRANSITION_NOT_FOUND", "Owning mode transition does not exist.");

        if (transition.OperationId != context.OperationId ||
            transition.CorrelationId != context.CorrelationId ||
            transition.LeaseId != context.LeaseId ||
            transition.FenceToken != context.FenceToken ||
            !string.Equals(transition.ControlSessionKey, context.ControlSessionKey, StringComparison.Ordinal))
        {
            return Denied(ModePreMutationEvidenceDisposition.OwnershipConflict, "MODE_PRESTATE_TRANSITION_MISMATCH", "Transition ownership no longer matches the pre-state request.");
        }

        if (!string.Equals(transition.State, "APPLYING", StringComparison.Ordinal) ||
            !string.Equals(transition.Stage, "APPLY_STARTED", StringComparison.Ordinal) ||
            transition.CommitDurable)
        {
            return Denied(ModePreMutationEvidenceDisposition.InvalidLifecycle, "MODE_PRESTATE_INVALID_LIFECYCLE", "Pre-state may be captured only during APPLYING/APPLY_STARTED before durable commit.");
        }

        var action = await ReadActionAsync(connection, transaction, context.ActionId, cancellationToken).ConfigureAwait(false);
        if (action is null)
            return Denied(ModePreMutationEvidenceDisposition.Missing, "MODE_PRESTATE_ACTION_NOT_FOUND", "Owning durable action does not exist.");

        if (action.TransitionId != context.TransitionId)
            return Denied(ModePreMutationEvidenceDisposition.OwnershipConflict, "MODE_PRESTATE_ACTION_MISMATCH", "Durable action belongs to a different transition.", action.Revision);

        if (action.Revision != context.ExpectedActionRevision)
            return Denied(ModePreMutationEvidenceDisposition.RevisionConflict, "MODE_ACTION_REVISION_CONFLICT", $"Expected action revision {context.ExpectedActionRevision}, actual {action.Revision}.", action.Revision);

        if (!action.PlanComplete)
            return Denied(ModePreMutationEvidenceDisposition.InvalidLifecycle, "MODE_ACTION_PLAN_INCOMPLETE", "Durable action-plan header/count no longer matches the action journal.", action.Revision);

        if (!MatchesActionBinding(action, context.ExpectedAction))
            return Denied(ModePreMutationEvidenceDisposition.InvalidLifecycle, "MODE_PRESTATE_ACTION_SEMANTICS_MISMATCH", "Pre-state request does not match immutable durable action semantics.", action.Revision);

        if (!string.Equals(action.State, "PLANNED", StringComparison.Ordinal))
            return Denied(ModePreMutationEvidenceDisposition.InvalidLifecycle, "MODE_PRESTATE_ACTION_NOT_PLANNED", "Pre-state capture is valid only before BeginApply while the action remains PLANNED.", action.Revision);

        transaction.Commit();
        return new ModePreMutationEvidenceOutcome(
            ModePreMutationEvidenceDisposition.Authorized,
            "MODE_PRESTATE_AUTHORIZED",
            action.Revision);
    }

    private async Task<SqliteConnection> OpenReadyAsync(CancellationToken cancellationToken)
    {
        var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var version = await _database.ReadSchemaVersionAsync(connection, cancellationToken).ConfigureAwait(false);
            if (version != MachineStateStore.SchemaVersion)
                throw new InvalidDataException($"Pre-mutation evidence requires machine schema {MachineStateStore.SchemaVersion}, got {version}.");

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
            throw new InvalidDataException("Machine canonical store must be initialized before pre-mutation evidence is evaluated.");
    }

    private static async Task<LeaseRow?> ReadLeaseAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT lease_id, mutation_type, owner_operation_id, owner_correlation_id,
                   owner_control_session_key, fence_token, expires_utc
            FROM machine_mutation_lease WHERE singleton_id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        if (reader.IsDBNull(0)) return new LeaseRow(null, null, null, null, null, reader.GetInt64(5), DateTimeOffset.MinValue);
        if (!Guid.TryParse(reader.GetString(0), out var leaseId) ||
            !Guid.TryParse(reader.GetString(2), out var operationId) ||
            !Guid.TryParse(reader.GetString(3), out var correlationId) ||
            !DateTimeOffset.TryParse(reader.GetString(6), out var expiresUtc))
            throw new InvalidDataException("Major mutation lease contains malformed pre-state values.");
        return new LeaseRow(leaseId, reader.GetString(1), operationId, correlationId, reader.GetString(4), reader.GetInt64(5), expiresUtc.ToUniversalTime());
    }

    private static async Task<TransitionRow?> ReadTransitionAsync(SqliteConnection connection, SqliteTransaction transaction, Guid transitionId, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT operation_id, correlation_id, control_session_key, lease_id, fence_token,
                   transition_state, stage_code, commit_durable
            FROM mode_transition WHERE transition_id = $transition;
            """;
        command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        if (!Guid.TryParse(reader.GetString(0), out var operationId) ||
            !Guid.TryParse(reader.GetString(1), out var correlationId) ||
            !Guid.TryParse(reader.GetString(3), out var leaseId))
            throw new InvalidDataException("Mode transition contains malformed pre-state ownership values.");
        return new TransitionRow(operationId, correlationId, reader.GetString(2), leaseId, reader.GetInt64(4), reader.GetString(5), reader.GetString(6), reader.GetInt32(7) == 1);
    }

    private static async Task<ActionRow?> ReadActionAsync(SqliteConnection connection, SqliteTransaction transaction, Guid actionId, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT action_row.transition_id, action_row.action_state, action_row.revision,
                   action_row.owning_module, action_row.action_type, action_row.target_ref,
                   action_row.desired_schema_version, action_row.desired_state_digest,
                   CASE WHEN plan.transition_id IS NOT NULL
                         AND plan.action_count = (SELECT COUNT(*) FROM mode_transition_action counted WHERE counted.transition_id = action_row.transition_id)
                        THEN 1 ELSE 0 END AS plan_complete
            FROM mode_transition_action action_row
            LEFT JOIN mode_transition_action_plan plan ON plan.transition_id = action_row.transition_id
            WHERE action_row.action_id = $action;
            """;
        command.Parameters.AddWithValue("$action", actionId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        if (!Guid.TryParse(reader.GetString(0), out var transitionId))
            throw new InvalidDataException("Mode action contains malformed pre-state transition ownership.");
        return new ActionRow(
            transitionId, reader.GetString(1), reader.GetInt32(2), reader.GetString(3), reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetInt32(6), reader.GetString(7), reader.GetInt32(8) == 1);
    }

    private static bool MatchesActionBinding(ActionRow action, ModeMutationActionBinding expected)
        => string.Equals(action.OwningModule, expected.OwningModule, StringComparison.Ordinal) &&
           string.Equals(action.ActionType, expected.ActionType, StringComparison.Ordinal) &&
           string.Equals(action.TargetRef, expected.TargetRef, StringComparison.Ordinal) &&
           action.DesiredSchemaVersion == expected.DesiredSchemaVersion &&
           string.Equals(action.DesiredStateDigest, expected.DesiredStateDigest, StringComparison.OrdinalIgnoreCase);

    private static void ValidateContext(ModeMutationFenceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.TransitionId == Guid.Empty || context.ActionId == Guid.Empty || context.LeaseId == Guid.Empty ||
            context.OperationId == Guid.Empty || context.CorrelationId == Guid.Empty)
            throw new ArgumentException("Pre-state context identifiers must not be empty.", nameof(context));
        if (context.FenceToken < 1 || context.ExpectedActionRevision < 1)
            throw new ArgumentOutOfRangeException(nameof(context), "Fence and action revision must be positive.");
        if (string.IsNullOrWhiteSpace(context.ControlSessionKey) || context.ControlSessionKey.Length > 256 || context.ControlSessionKey.Any(static c => char.IsControl(c)))
            throw new ArgumentException("ControlSessionKey is outside supported bounds.", nameof(context));
    }

    private static ModePreMutationEvidenceOutcome Denied(ModePreMutationEvidenceDisposition disposition, string code, string detail, int? revision = null)
        => new(disposition, code, revision, detail);

    private sealed record LeaseRow(Guid? LeaseId, string? MutationType, Guid? OperationId, Guid? CorrelationId, string? ControlSessionKey, long FenceToken, DateTimeOffset ExpiresUtc);
    private sealed record TransitionRow(Guid OperationId, Guid CorrelationId, string ControlSessionKey, Guid LeaseId, long FenceToken, string State, string Stage, bool CommitDurable);
    private sealed record ActionRow(Guid TransitionId, string State, int Revision, string OwningModule, string ActionType, string? TargetRef, int DesiredSchemaVersion, string DesiredStateDigest, bool PlanComplete);
}
