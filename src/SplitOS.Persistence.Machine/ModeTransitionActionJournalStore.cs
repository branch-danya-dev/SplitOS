using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SplitOS.Persistence;

namespace SplitOS.Persistence.Machine;

public enum PersistedModeApplyResult
{
    Applied,
    Failed,
    Unknown
}

public enum PersistedModeVerifyResult
{
    Verified,
    Mismatch,
    Unknown
}

public enum ModeActionAdvanceDisposition
{
    Advanced,
    Replayed,
    Missing,
    RevisionConflict,
    LeaseConflict,
    ReconciliationRequired,
    OwnershipConflict,
    InvalidLifecycle,
    EvidenceConflict
}

public sealed record ModeActionAdvanceOutcome(
    ModeActionAdvanceDisposition Disposition,
    PersistedModeActionRecord? Action,
    string ProductCode,
    int? ActualActionRevision = null,
    string? Detail = null);

/// <summary>
/// Durable SPEC-05 per-action execution journal. The full immutable plan is persisted by
/// <see cref="ModeTransitionActionPlanStore"/> before this repository may advance any action.
///
/// The repository does not invoke Windows or Broker capabilities. Runtime first records APPLYING,
/// then the Broker mutation boundary validates that exact action revision/fence before invoking a
/// privileged adapter. Immediate apply and verification outcomes are persisted here afterwards.
/// Rollback execution is intentionally left to the next Slice-03 increment.
/// </summary>
public sealed class ModeTransitionActionJournalStore
{
    public const int MaxPreStateBytes = 64 * 1024;

    private readonly SqliteDatabase _database;
    private readonly string _databasePath;
    private readonly string _markerPath;
    private readonly string _quarantineMarkerPath;
    private readonly TimeProvider _timeProvider;

    public ModeTransitionActionJournalStore(
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

    public async Task<PersistedModeActionRecord?> GetAsync(
        Guid actionId,
        CancellationToken cancellationToken = default)
    {
        if (actionId == Guid.Empty) throw new ArgumentException("Action id must not be empty.", nameof(actionId));
        EnsureCanonicalStoreAvailable();
        await using var connection = await OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        return await ReadActionAsync(connection, null, actionId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ModeActionAdvanceOutcome> BeginApplyAsync(
        Guid transitionId,
        Guid actionId,
        int expectedActionRevision,
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        string? preStateJson = null,
        string? preStateDigest = null,
        CancellationToken cancellationToken = default)
    {
        ValidateCommonRequest(
            transitionId,
            actionId,
            expectedActionRevision,
            leaseId,
            fenceToken,
            ownerOperationId);
        ValidatePreState(preStateJson, preStateDigest);
        preStateDigest = preStateDigest?.ToLowerInvariant();
        EnsureCanonicalStoreAvailable();

        await using var connection = await OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        var context = await ReadOwnedContextAsync(
            connection,
            transaction,
            transitionId,
            actionId,
            cancellationToken).ConfigureAwait(false);
        if (context.Outcome is not null) return context.Outcome;
        var transition = context.Transition!;
        var action = context.Action!;

        if (action.State == PersistedModeActionState.Applying &&
            ReplayRevisionMatches(action.Revision, expectedActionRevision) &&
            string.Equals(action.PreStateJson, preStateJson, StringComparison.Ordinal) &&
            string.Equals(action.PreStateDigest, preStateDigest, StringComparison.OrdinalIgnoreCase))
        {
            return Replay(action, "MODE_ACTION_APPLY_BEGIN_REPLAYED");
        }

        var validation = await ValidateMutableActionContextAsync(
            connection,
            transaction,
            transition,
            action,
            expectedActionRevision,
            leaseId,
            fenceToken,
            ownerOperationId,
            expectedTransitionState: "APPLYING",
            expectedTransitionStage: "APPLY_STARTED",
            cancellationToken).ConfigureAwait(false);
        if (validation is not null) return validation;

        if (action.State != PersistedModeActionState.Planned)
        {
            return InvalidLifecycle(
                action,
                "MODE_ACTION_APPLY_BEGIN_INVALID_LIFECYCLE",
                $"Action must be PLANNED before apply begins; actual state is {action.State}.");
        }

        if (!await PriorActionsPermitForwardApplyAsync(
                connection,
                transaction,
                transitionId,
                action.SequenceNo,
                cancellationToken).ConfigureAwait(false))
        {
            return InvalidLifecycle(
                action,
                "MODE_ACTION_APPLY_ORDER_BLOCKED",
                "All earlier mandatory actions must be APPLIED and earlier optional actions must be settled before this action may begin.");
        }

        var now = _timeProvider.GetUtcNow();
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE mode_transition_action
            SET action_state = 'APPLYING',
                pre_state_json = $pre_json,
                pre_state_digest = $pre_digest,
                started_utc = $started,
                updated_utc = $updated,
                revision = revision + 1
            WHERE action_id = $action
              AND transition_id = $transition
              AND revision = $revision
              AND action_state = 'PLANNED';
            """;
        command.Parameters.AddWithValue("$pre_json", (object?)preStateJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$pre_digest", (object?)preStateDigest ?? DBNull.Value);
        command.Parameters.AddWithValue("$started", now.ToString("O"));
        command.Parameters.AddWithValue("$updated", now.ToString("O"));
        command.Parameters.AddWithValue("$action", actionId.ToString("D"));
        command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
        command.Parameters.AddWithValue("$revision", expectedActionRevision);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            return await RevisionConflictAsync(
                connection,
                transaction,
                actionId,
                expectedActionRevision,
                cancellationToken).ConfigureAwait(false);
        }

        transaction.Commit();
        var advanced = action with
        {
            State = PersistedModeActionState.Applying,
            PreStateJson = preStateJson,
            PreStateDigest = preStateDigest,
            StartedUtc = now,
            UpdatedUtc = now,
            Revision = checked(action.Revision + 1)
        };
        return Advanced(advanced, "MODE_ACTION_APPLY_STARTED");
    }

    public async Task<ModeActionAdvanceOutcome> RecordApplyResultAsync(
        Guid transitionId,
        Guid actionId,
        int expectedActionRevision,
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        PersistedModeApplyResult result,
        CancellationToken cancellationToken = default)
    {
        ValidateCommonRequest(
            transitionId,
            actionId,
            expectedActionRevision,
            leaseId,
            fenceToken,
            ownerOperationId);
        EnsureCanonicalStoreAvailable();

        var resultCode = ToStorageCode(result);
        var nextState = result == PersistedModeApplyResult.Applied
            ? PersistedModeActionState.Applied
            : PersistedModeActionState.Failed;

        await using var connection = await OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        var context = await ReadOwnedContextAsync(
            connection,
            transaction,
            transitionId,
            actionId,
            cancellationToken).ConfigureAwait(false);
        if (context.Outcome is not null) return context.Outcome;
        var transition = context.Transition!;
        var action = context.Action!;

        if (action.State == nextState &&
            ReplayRevisionMatches(action.Revision, expectedActionRevision) &&
            string.Equals(action.ApplyResultCode, resultCode, StringComparison.Ordinal))
        {
            return Replay(action, "MODE_ACTION_APPLY_RESULT_REPLAYED");
        }

        var validation = await ValidateMutableActionContextAsync(
            connection,
            transaction,
            transition,
            action,
            expectedActionRevision,
            leaseId,
            fenceToken,
            ownerOperationId,
            expectedTransitionState: "APPLYING",
            expectedTransitionStage: "APPLY_STARTED",
            cancellationToken).ConfigureAwait(false);
        if (validation is not null) return validation;

        if (action.State != PersistedModeActionState.Applying)
        {
            return InvalidLifecycle(
                action,
                "MODE_ACTION_APPLY_RESULT_INVALID_LIFECYCLE",
                $"Apply result can be recorded only from APPLYING; actual state is {action.State}.");
        }

        var now = _timeProvider.GetUtcNow();
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE mode_transition_action
            SET action_state = $state,
                apply_result_code = $result,
                applied_utc = $applied,
                updated_utc = $updated,
                revision = revision + 1
            WHERE action_id = $action
              AND transition_id = $transition
              AND revision = $revision
              AND action_state = 'APPLYING';
            """;
        command.Parameters.AddWithValue("$state", nextState == PersistedModeActionState.Applied ? "APPLIED" : "FAILED");
        command.Parameters.AddWithValue("$result", resultCode);
        command.Parameters.AddWithValue("$applied", result == PersistedModeApplyResult.Applied ? now.ToString("O") : DBNull.Value);
        command.Parameters.AddWithValue("$updated", now.ToString("O"));
        command.Parameters.AddWithValue("$action", actionId.ToString("D"));
        command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
        command.Parameters.AddWithValue("$revision", expectedActionRevision);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            return await RevisionConflictAsync(
                connection,
                transaction,
                actionId,
                expectedActionRevision,
                cancellationToken).ConfigureAwait(false);
        }

        transaction.Commit();
        var advanced = action with
        {
            State = nextState,
            ApplyResultCode = resultCode,
            AppliedUtc = result == PersistedModeApplyResult.Applied ? now : null,
            UpdatedUtc = now,
            Revision = checked(action.Revision + 1)
        };
        return Advanced(
            advanced,
            result == PersistedModeApplyResult.Applied
                ? "MODE_ACTION_APPLIED"
                : "MODE_ACTION_APPLY_FAILED");
    }

    public async Task<ModeActionAdvanceOutcome> SkipOptionalAsync(
        Guid transitionId,
        Guid actionId,
        int expectedActionRevision,
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        CancellationToken cancellationToken = default)
    {
        ValidateCommonRequest(
            transitionId,
            actionId,
            expectedActionRevision,
            leaseId,
            fenceToken,
            ownerOperationId);
        EnsureCanonicalStoreAvailable();

        await using var connection = await OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        var context = await ReadOwnedContextAsync(
            connection,
            transaction,
            transitionId,
            actionId,
            cancellationToken).ConfigureAwait(false);
        if (context.Outcome is not null) return context.Outcome;
        var transition = context.Transition!;
        var action = context.Action!;

        if (action.State == PersistedModeActionState.Skipped &&
            ReplayRevisionMatches(action.Revision, expectedActionRevision) &&
            string.Equals(action.ApplyResultCode, "SKIPPED", StringComparison.Ordinal))
        {
            return Replay(action, "MODE_ACTION_SKIP_REPLAYED");
        }

        var validation = await ValidateMutableActionContextAsync(
            connection,
            transaction,
            transition,
            action,
            expectedActionRevision,
            leaseId,
            fenceToken,
            ownerOperationId,
            expectedTransitionState: "APPLYING",
            expectedTransitionStage: "APPLY_STARTED",
            cancellationToken).ConfigureAwait(false);
        if (validation is not null) return validation;

        if (action.Mandatory)
        {
            return InvalidLifecycle(
                action,
                "MODE_ACTION_MANDATORY_CANNOT_SKIP",
                "Mandatory action cannot be marked SKIPPED.");
        }

        if (action.State != PersistedModeActionState.Planned)
        {
            return InvalidLifecycle(
                action,
                "MODE_ACTION_SKIP_INVALID_LIFECYCLE",
                $"Optional action can be skipped only from PLANNED; actual state is {action.State}.");
        }

        if (!await PriorActionsPermitForwardApplyAsync(
                connection,
                transaction,
                transitionId,
                action.SequenceNo,
                cancellationToken).ConfigureAwait(false))
        {
            return InvalidLifecycle(
                action,
                "MODE_ACTION_APPLY_ORDER_BLOCKED",
                "Earlier actions are not settled, so this optional action cannot be skipped yet.");
        }

        var now = _timeProvider.GetUtcNow();
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE mode_transition_action
            SET action_state = 'SKIPPED',
                apply_result_code = 'SKIPPED',
                started_utc = $started,
                updated_utc = $updated,
                revision = revision + 1
            WHERE action_id = $action
              AND transition_id = $transition
              AND revision = $revision
              AND action_state = 'PLANNED'
              AND mandatory = 0;
            """;
        command.Parameters.AddWithValue("$started", now.ToString("O"));
        command.Parameters.AddWithValue("$updated", now.ToString("O"));
        command.Parameters.AddWithValue("$action", actionId.ToString("D"));
        command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
        command.Parameters.AddWithValue("$revision", expectedActionRevision);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            return await RevisionConflictAsync(
                connection,
                transaction,
                actionId,
                expectedActionRevision,
                cancellationToken).ConfigureAwait(false);
        }

        transaction.Commit();
        var advanced = action with
        {
            State = PersistedModeActionState.Skipped,
            ApplyResultCode = "SKIPPED",
            StartedUtc = now,
            UpdatedUtc = now,
            Revision = checked(action.Revision + 1)
        };
        return Advanced(advanced, "MODE_ACTION_SKIPPED");
    }

    public async Task<ModeActionAdvanceOutcome> BeginVerifyAsync(
        Guid transitionId,
        Guid actionId,
        int expectedActionRevision,
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        CancellationToken cancellationToken = default)
    {
        ValidateCommonRequest(
            transitionId,
            actionId,
            expectedActionRevision,
            leaseId,
            fenceToken,
            ownerOperationId);
        EnsureCanonicalStoreAvailable();

        await using var connection = await OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        var context = await ReadOwnedContextAsync(
            connection,
            transaction,
            transitionId,
            actionId,
            cancellationToken).ConfigureAwait(false);
        if (context.Outcome is not null) return context.Outcome;
        var transition = context.Transition!;
        var action = context.Action!;

        if (action.State == PersistedModeActionState.Verifying && ReplayRevisionMatches(action.Revision, expectedActionRevision))
        {
            return Replay(action, "MODE_ACTION_VERIFY_BEGIN_REPLAYED");
        }

        var validation = await ValidateMutableActionContextAsync(
            connection,
            transaction,
            transition,
            action,
            expectedActionRevision,
            leaseId,
            fenceToken,
            ownerOperationId,
            expectedTransitionState: "VERIFYING",
            expectedTransitionStage: "VERIFY_STARTED",
            cancellationToken).ConfigureAwait(false);
        if (validation is not null) return validation;

        if (action.State != PersistedModeActionState.Applied)
        {
            return InvalidLifecycle(
                action,
                "MODE_ACTION_VERIFY_BEGIN_INVALID_LIFECYCLE",
                $"Verification can begin only for an APPLIED action; actual state is {action.State}.");
        }

        if (!await PriorActionsPermitVerificationAsync(
                connection,
                transaction,
                transitionId,
                action.SequenceNo,
                cancellationToken).ConfigureAwait(false))
        {
            return InvalidLifecycle(
                action,
                "MODE_ACTION_VERIFY_ORDER_BLOCKED",
                "Earlier mandatory actions must be VERIFIED and earlier optional actions must be settled before this action is verified.");
        }

        var now = _timeProvider.GetUtcNow();
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE mode_transition_action
            SET action_state = 'VERIFYING',
                updated_utc = $updated,
                revision = revision + 1
            WHERE action_id = $action
              AND transition_id = $transition
              AND revision = $revision
              AND action_state = 'APPLIED';
            """;
        command.Parameters.AddWithValue("$updated", now.ToString("O"));
        command.Parameters.AddWithValue("$action", actionId.ToString("D"));
        command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
        command.Parameters.AddWithValue("$revision", expectedActionRevision);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            return await RevisionConflictAsync(
                connection,
                transaction,
                actionId,
                expectedActionRevision,
                cancellationToken).ConfigureAwait(false);
        }

        transaction.Commit();
        var advanced = action with
        {
            State = PersistedModeActionState.Verifying,
            UpdatedUtc = now,
            Revision = checked(action.Revision + 1)
        };
        return Advanced(advanced, "MODE_ACTION_VERIFY_STARTED");
    }

    public async Task<ModeActionAdvanceOutcome> RecordVerifyResultAsync(
        Guid transitionId,
        Guid actionId,
        int expectedActionRevision,
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        PersistedModeVerifyResult result,
        CancellationToken cancellationToken = default)
    {
        ValidateCommonRequest(
            transitionId,
            actionId,
            expectedActionRevision,
            leaseId,
            fenceToken,
            ownerOperationId);
        EnsureCanonicalStoreAvailable();

        var resultCode = ToStorageCode(result);
        var nextState = result == PersistedModeVerifyResult.Verified
            ? PersistedModeActionState.Verified
            : PersistedModeActionState.Failed;

        await using var connection = await OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        var context = await ReadOwnedContextAsync(
            connection,
            transaction,
            transitionId,
            actionId,
            cancellationToken).ConfigureAwait(false);
        if (context.Outcome is not null) return context.Outcome;
        var transition = context.Transition!;
        var action = context.Action!;

        if (action.State == nextState &&
            ReplayRevisionMatches(action.Revision, expectedActionRevision) &&
            string.Equals(action.VerifyResultCode, resultCode, StringComparison.Ordinal))
        {
            return Replay(action, "MODE_ACTION_VERIFY_RESULT_REPLAYED");
        }

        var validation = await ValidateMutableActionContextAsync(
            connection,
            transaction,
            transition,
            action,
            expectedActionRevision,
            leaseId,
            fenceToken,
            ownerOperationId,
            expectedTransitionState: "VERIFYING",
            expectedTransitionStage: "VERIFY_STARTED",
            cancellationToken).ConfigureAwait(false);
        if (validation is not null) return validation;

        if (action.State != PersistedModeActionState.Verifying)
        {
            return InvalidLifecycle(
                action,
                "MODE_ACTION_VERIFY_RESULT_INVALID_LIFECYCLE",
                $"Verification result can be recorded only from VERIFYING; actual state is {action.State}.");
        }

        var now = _timeProvider.GetUtcNow();
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE mode_transition_action
            SET action_state = $state,
                verify_result_code = $result,
                verified_utc = $verified,
                updated_utc = $updated,
                revision = revision + 1
            WHERE action_id = $action
              AND transition_id = $transition
              AND revision = $revision
              AND action_state = 'VERIFYING';
            """;
        command.Parameters.AddWithValue("$state", nextState == PersistedModeActionState.Verified ? "VERIFIED" : "FAILED");
        command.Parameters.AddWithValue("$result", resultCode);
        command.Parameters.AddWithValue("$verified", result == PersistedModeVerifyResult.Verified ? now.ToString("O") : DBNull.Value);
        command.Parameters.AddWithValue("$updated", now.ToString("O"));
        command.Parameters.AddWithValue("$action", actionId.ToString("D"));
        command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
        command.Parameters.AddWithValue("$revision", expectedActionRevision);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            return await RevisionConflictAsync(
                connection,
                transaction,
                actionId,
                expectedActionRevision,
                cancellationToken).ConfigureAwait(false);
        }

        transaction.Commit();
        var advanced = action with
        {
            State = nextState,
            VerifyResultCode = resultCode,
            VerifiedUtc = result == PersistedModeVerifyResult.Verified ? now : null,
            UpdatedUtc = now,
            Revision = checked(action.Revision + 1)
        };
        return Advanced(
            advanced,
            result == PersistedModeVerifyResult.Verified
                ? "MODE_ACTION_VERIFIED"
                : "MODE_ACTION_VERIFY_FAILED");
    }

    private async Task<ModeActionAdvanceOutcome?> ValidateMutableActionContextAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TransitionRow transition,
        PersistedModeActionRecord action,
        int expectedActionRevision,
        Guid leaseId,
        long fenceToken,
        Guid ownerOperationId,
        string expectedTransitionState,
        string expectedTransitionStage,
        CancellationToken cancellationToken)
    {
        if (action.Revision != expectedActionRevision)
        {
            return new ModeActionAdvanceOutcome(
                ModeActionAdvanceDisposition.RevisionConflict,
                action,
                "MODE_ACTION_REVISION_CONFLICT",
                action.Revision,
                $"Expected action revision {expectedActionRevision}, actual {action.Revision}.");
        }

        if (transition.OperationId != ownerOperationId ||
            transition.LeaseId != leaseId ||
            transition.FenceToken != fenceToken)
        {
            return new ModeActionAdvanceOutcome(
                ModeActionAdvanceDisposition.OwnershipConflict,
                action,
                "MODE_ACTION_OWNERSHIP_MISMATCH",
                action.Revision,
                "Transition ownership does not match the supplied action mutation context.");
        }

        if (transition.CommitDurable ||
            !string.Equals(transition.State, expectedTransitionState, StringComparison.Ordinal) ||
            !string.Equals(transition.Stage, expectedTransitionStage, StringComparison.Ordinal))
        {
            return InvalidLifecycle(
                action,
                "MODE_ACTION_TRANSITION_INVALID_LIFECYCLE",
                $"Action advancement requires transition {expectedTransitionState}/{expectedTransitionStage} before durable commit; actual {transition.State}/{transition.Stage}.");
        }

        var lease = await ValidateCurrentModeLeaseAsync(
            connection,
            transaction,
            transition,
            leaseId,
            fenceToken,
            cancellationToken).ConfigureAwait(false);
        if (!lease.IsCurrent)
        {
            return new ModeActionAdvanceOutcome(
                lease.ReconciliationRequired
                    ? ModeActionAdvanceDisposition.ReconciliationRequired
                    : ModeActionAdvanceDisposition.LeaseConflict,
                action,
                lease.ProductCode,
                action.Revision,
                lease.Detail);
        }

        if (!await HasCompletePlanAsync(connection, transaction, transition.TransitionId, cancellationToken).ConfigureAwait(false))
        {
            return InvalidLifecycle(
                action,
                "MODE_ACTION_PLAN_INCOMPLETE",
                "Durable action-plan header/count no longer matches the persisted action journal.");
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
        {
            return LeaseValidation.Stale("No active major mutation lease exists.");
        }

        if (!Guid.TryParse(reader.GetString(0), out var actualLeaseId) ||
            !Guid.TryParse(reader.GetString(2), out var actualOperationId) ||
            !Guid.TryParse(reader.GetString(3), out var actualCorrelationId) ||
            !DateTimeOffset.TryParse(reader.GetString(6), out var expiresUtc))
        {
            throw new InvalidDataException("Major mutation lease contains malformed canonical action-journal values.");
        }

        if (_timeProvider.GetUtcNow() >= expiresUtc.ToUniversalTime())
        {
            return LeaseValidation.Reconcile(
                "Major mutation lease expired; action execution requires reconciliation before further advancement.");
        }

        if (actualLeaseId != leaseId ||
            !string.Equals(reader.GetString(1), "MODE", StringComparison.Ordinal) ||
            actualOperationId != transition.OperationId ||
            actualCorrelationId != transition.CorrelationId ||
            !string.Equals(reader.GetString(4), transition.ControlSessionKey, StringComparison.Ordinal) ||
            reader.GetInt64(5) != fenceToken)
        {
            return LeaseValidation.Stale("Current canonical MODE lease identity/fence no longer matches the transition owner.");
        }

        return LeaseValidation.Current;
    }

    private static async Task<OwnedContext> ReadOwnedContextAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid transitionId,
        Guid actionId,
        CancellationToken cancellationToken)
    {
        var transition = await ReadTransitionAsync(
            connection,
            transaction,
            transitionId,
            cancellationToken).ConfigureAwait(false);
        if (transition is null)
        {
            return new OwnedContext(
                null,
                null,
                new ModeActionAdvanceOutcome(
                    ModeActionAdvanceDisposition.Missing,
                    null,
                    "MODE_TRANSITION_NOT_FOUND"));
        }

        var action = await ReadActionAsync(connection, transaction, actionId, cancellationToken).ConfigureAwait(false);
        if (action is null)
        {
            return new OwnedContext(
                transition,
                null,
                new ModeActionAdvanceOutcome(
                    ModeActionAdvanceDisposition.Missing,
                    null,
                    "MODE_ACTION_NOT_FOUND"));
        }

        if (action.TransitionId != transitionId)
        {
            return new OwnedContext(
                transition,
                action,
                new ModeActionAdvanceOutcome(
                    ModeActionAdvanceDisposition.OwnershipConflict,
                    action,
                    "MODE_ACTION_TRANSITION_MISMATCH",
                    action.Revision,
                    "Durable action belongs to a different transition."));
        }

        return new OwnedContext(transition, action, null);
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
                    $"Mode action journal requires machine schema {MachineStateStore.SchemaVersion}, got {version}.");
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
                "Machine canonical store must be initialized before mode action execution is journaled.");
        }
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
            throw new InvalidDataException("Mode transition contains malformed action-journal ownership values.");
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
            throw new InvalidDataException("Mode action contains malformed canonical action-journal values.");
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

    private static async Task<bool> HasCompletePlanAsync(
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

    private static async Task<bool> PriorActionsPermitForwardApplyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid transitionId,
        int sequenceNo,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM mode_transition_action
            WHERE transition_id = $transition
              AND sequence_no < $sequence
              AND (
                  (mandatory = 1 AND action_state != 'APPLIED') OR
                  (mandatory = 0 AND action_state NOT IN ('APPLIED','FAILED','SKIPPED'))
              );
            """;
        command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
        command.Parameters.AddWithValue("$sequence", sequenceNo);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 0;
    }

    private static async Task<bool> PriorActionsPermitVerificationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid transitionId,
        int sequenceNo,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM mode_transition_action
            WHERE transition_id = $transition
              AND sequence_no < $sequence
              AND (
                  (mandatory = 1 AND action_state != 'VERIFIED') OR
                  (mandatory = 0 AND action_state NOT IN ('VERIFIED','FAILED','SKIPPED'))
              );
            """;
        command.Parameters.AddWithValue("$transition", transitionId.ToString("D"));
        command.Parameters.AddWithValue("$sequence", sequenceNo);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 0;
    }

    private static async Task<ModeActionAdvanceOutcome> RevisionConflictAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid actionId,
        int expectedActionRevision,
        CancellationToken cancellationToken)
    {
        var actual = await ReadActionAsync(connection, transaction, actionId, cancellationToken).ConfigureAwait(false);
        return new ModeActionAdvanceOutcome(
            ModeActionAdvanceDisposition.RevisionConflict,
            actual,
            "MODE_ACTION_REVISION_CONFLICT",
            actual?.Revision,
            actual is null
                ? "Action disappeared while its revision was being advanced."
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

    private static void ValidatePreState(string? json, string? digest)
    {
        if (json is null)
        {
            if (digest is not null)
                throw new ArgumentException("Pre-state digest cannot be supplied without pre-state JSON.", nameof(digest));
            return;
        }

        if (!IsSha256Digest(digest))
            throw new ArgumentException("Pre-state digest must be a SHA-256 hexadecimal digest.", nameof(digest));

        var bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > MaxPreStateBytes)
            throw new ArgumentException($"Pre-state JSON exceeds {MaxPreStateBytes} UTF-8 bytes.", nameof(json));

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                throw new ArgumentException("Pre-state JSON must be an object or array.", nameof(json));
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("Pre-state JSON is malformed.", nameof(json), ex);
        }

        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(digest!.ToLowerInvariant()),
                Encoding.ASCII.GetBytes(actual)))
        {
            throw new ArgumentException("Pre-state digest does not match the JSON payload.", nameof(digest));
        }
    }

    private static DateTimeOffset? ParseNullableUtc(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return null;
        if (!DateTimeOffset.TryParse(reader.GetString(ordinal), out var parsed))
            throw new InvalidDataException("Mode action timestamp is malformed.");
        return parsed.ToUniversalTime();
    }

    private static bool ReplayRevisionMatches(int actualRevision, int expectedRevision)
        => actualRevision == expectedRevision || actualRevision == checked(expectedRevision + 1);

    private static ModeActionAdvanceOutcome Advanced(PersistedModeActionRecord action, string productCode)
        => new(ModeActionAdvanceDisposition.Advanced, action, productCode, action.Revision);

    private static ModeActionAdvanceOutcome Replay(PersistedModeActionRecord action, string productCode)
        => new(ModeActionAdvanceDisposition.Replayed, action, productCode, action.Revision);

    private static ModeActionAdvanceOutcome InvalidLifecycle(
        PersistedModeActionRecord action,
        string productCode,
        string detail)
        => new(
            ModeActionAdvanceDisposition.InvalidLifecycle,
            action,
            productCode,
            action.Revision,
            detail);

    private static bool IsSha256Digest(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           value.Length == 64 &&
           value.All(static character => Uri.IsHexDigit(character));

    private static string ToStorageCode(PersistedModeApplyResult result)
        => result switch
        {
            PersistedModeApplyResult.Applied => "APPLIED",
            PersistedModeApplyResult.Failed => "FAILED",
            PersistedModeApplyResult.Unknown => "UNKNOWN",
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };

    private static string ToStorageCode(PersistedModeVerifyResult result)
        => result switch
        {
            PersistedModeVerifyResult.Verified => "VERIFIED",
            PersistedModeVerifyResult.Mismatch => "MISMATCH",
            PersistedModeVerifyResult.Unknown => "UNKNOWN",
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };

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
        Guid TransitionId,
        Guid OperationId,
        Guid CorrelationId,
        string ControlSessionKey,
        Guid LeaseId,
        long FenceToken,
        string State,
        string Stage,
        bool CommitDurable);

    private sealed record OwnedContext(
        TransitionRow? Transition,
        PersistedModeActionRecord? Action,
        ModeActionAdvanceOutcome? Outcome);

    private sealed record LeaseValidation(bool IsCurrent, bool ReconciliationRequired, string ProductCode, string? Detail)
    {
        public static LeaseValidation Current { get; } = new(true, false, "MUTATION_LEASE_CURRENT", null);
        public static LeaseValidation Stale(string detail) => new(false, false, "MUTATION_LEASE_STALE_FENCE", detail);
        public static LeaseValidation Reconcile(string detail) => new(false, true, "MUTATION_LEASE_RECONCILIATION_REQUIRED", detail);
    }
}
