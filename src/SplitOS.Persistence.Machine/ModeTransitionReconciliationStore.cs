using Microsoft.Data.Sqlite;
using SplitOS.Persistence;

namespace SplitOS.Persistence.Machine;

/// <summary>
/// SPEC-05 startup/crash reconciliation boundary.
///
/// This repository does not read or mutate Windows state. It reconstructs the durable semantic
/// situation after Runtime restart, distinguishes pre-commit source truth from post-commit target
/// truth, and can atomically re-fence an incomplete MODE transition after the old lease expired or
/// was released. Re-fencing itself is not permission to assume an in-flight Windows call outcome:
/// APPLYING and ROLLING_BACK actions remain explicit actual-state reconciliation work before any
/// further machine mutation is allowed.
/// </summary>
public sealed partial class ModeTransitionReconciliationStore : IModeTransitionReconciliationStore
{
    private readonly SqliteDatabase _database;
    private readonly string _databasePath;
    private readonly string _markerPath;
    private readonly string _quarantineMarkerPath;
    private readonly TimeProvider _timeProvider;

    public ModeTransitionReconciliationStore(
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
        using var transaction = connection.BeginTransaction();
        _ = await ReadOperationalModeAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        _ = await ReadLeaseAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        _ = await ReadIncompleteTransitionsAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        transaction.Commit();
    }

    public async Task<ModeCrashReconciliationSnapshot> InspectAsync(
        string currentControlSessionKey,
        CancellationToken cancellationToken = default)
    {
        ValidateControlSessionKey(currentControlSessionKey);
        EnsureCanonicalStoreAvailable();

        await using var connection = await OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        var canonicalMode = await ReadOperationalModeAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        var lease = await ReadLeaseAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        var incomplete = await ReadIncompleteTransitionsAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

        if (incomplete.Count > 1)
        {
            transaction.Commit();
            return Snapshot(
                ModeCrashReconciliationAction.EscalateRecovery,
                ModeCrashReconciliationLeaseState.RecoveryRequired,
                canonicalMode,
                null,
                lease,
                null,
                false,
                "MODE_RECONCILIATION_MULTIPLE_INCOMPLETE",
                "More than one incomplete durable mode transition exists; semantic ownership cannot be guessed.");
        }

        if (incomplete.Count == 0)
        {
            var result = await InspectWithoutIncompleteTransitionAsync(
                connection,
                transaction,
                canonicalMode,
                lease,
                currentControlSessionKey,
                cancellationToken).ConfigureAwait(false);
            transaction.Commit();
            return result;
        }

        var transition = incomplete[0];
        var actions = await ReadActionsAsync(connection, transaction, transition.TransitionId, cancellationToken).ConfigureAwait(false);
        var hasMutationEvidence = actions.Any(HasPotentialMutationEvidence);

        if (!CanonicalRelationshipIsCoherent(transition, canonicalMode))
        {
            transaction.Commit();
            return Snapshot(
                ModeCrashReconciliationAction.EscalateRecovery,
                ModeCrashReconciliationLeaseState.RecoveryRequired,
                canonicalMode,
                transition,
                lease,
                null,
                hasMutationEvidence,
                "MODE_RECONCILIATION_CANONICAL_MISMATCH",
                "Durable transition commit marker and canonical OperationalMode do not describe the same semantic truth.");
        }

        var rollbackFailure = actions
            .Where(static action => action.State == PersistedModeActionState.RollbackFailed)
            .OrderByDescending(static action => action.SequenceNo)
            .FirstOrDefault();
        if (rollbackFailure is not null)
        {
            transaction.Commit();
            return Snapshot(
                ModeCrashReconciliationAction.EscalateRecovery,
                ModeCrashReconciliationLeaseState.RecoveryRequired,
                canonicalMode,
                transition,
                lease,
                rollbackFailure,
                hasMutationEvidence,
                "MODE_RECONCILIATION_ROLLBACK_FAILED",
                "A durable rollback failure exists; normal MODE continuation must not guess a coherent machine state.");
        }

        var leaseState = ClassifyLease(transition, lease, currentControlSessionKey);
        if (leaseState == ModeCrashReconciliationLeaseState.Busy)
        {
            transaction.Commit();
            return Snapshot(
                ModeCrashReconciliationAction.WaitForMutationOwner,
                leaseState,
                canonicalMode,
                transition,
                lease,
                null,
                hasMutationEvidence,
                BusyCode(lease.MutationType),
                "Another current major mutation owner must complete before MODE startup reconciliation proceeds.");
        }
        if (leaseState == ModeCrashReconciliationLeaseState.RecoveryRequired)
        {
            transaction.Commit();
            return Snapshot(
                ModeCrashReconciliationAction.EscalateRecovery,
                leaseState,
                canonicalMode,
                transition,
                lease,
                null,
                hasMutationEvidence,
                "MODE_RECONCILIATION_LEASE_MISMATCH",
                "Persisted transition ownership and the machine mutation lease cannot be reconciled safely.");
        }
        if (leaseState == ModeCrashReconciliationLeaseState.SessionMismatch)
        {
            var focus = FindUnsafeInFlightAction(actions);
            transaction.Commit();
            return Snapshot(
                ModeCrashReconciliationAction.ConvergeBaseForFreshSession,
                leaseState,
                canonicalMode,
                transition,
                lease,
                focus,
                hasMutationEvidence,
                "MODE_FRESH_SESSION_BASE_RECONCILIATION_REQUIRED",
                "The durable transition belongs to a prior control session; it must not be adopted for automatic managed-mode restoration.");
        }

        if (transition.CommitDurable)
        {
            var impossible = actions.FirstOrDefault(static action =>
                action.State is PersistedModeActionState.Applying or
                    PersistedModeActionState.RollingBack or
                    PersistedModeActionState.RolledBack);
            if (impossible is not null)
            {
                transaction.Commit();
                return Snapshot(
                    ModeCrashReconciliationAction.EscalateRecovery,
                    ModeCrashReconciliationLeaseState.RecoveryRequired,
                    canonicalMode,
                    transition,
                    lease,
                    impossible,
                    hasMutationEvidence,
                    "MODE_RECONCILIATION_POST_COMMIT_ACTION_MISMATCH",
                    "A durable target commit coexists with an incompatible in-flight/source-rollback action state.");
            }

            transaction.Commit();
            return Snapshot(
                ModeCrashReconciliationAction.VerifyCommittedTarget,
                leaseState,
                canonicalMode,
                transition,
                lease,
                null,
                hasMutationEvidence,
                "MODE_RECONCILE_COMMITTED_TARGET",
                "Target commit is durable; restart reconciliation must verify/finalize around target canonical truth.");
        }

        var rollingBack = actions
            .Where(static action => action.State == PersistedModeActionState.RollingBack)
            .OrderByDescending(static action => action.SequenceNo)
            .FirstOrDefault();
        if (rollingBack is not null)
        {
            transaction.Commit();
            return Snapshot(
                ModeCrashReconciliationAction.ReconcileRollbackOutcome,
                leaseState,
                canonicalMode,
                transition,
                lease,
                rollingBack,
                true,
                "MODE_RECONCILE_ROLLBACK_OUTCOME",
                "Rollback was in flight when Runtime stopped; compensation outcome requires actual-state reconciliation.");
        }

        var applying = actions
            .Where(static action => action.State == PersistedModeActionState.Applying)
            .OrderByDescending(static action => action.SequenceNo)
            .FirstOrDefault();
        if (applying is not null)
        {
            transaction.Commit();
            return Snapshot(
                ModeCrashReconciliationAction.ReconcileApplyingOutcome,
                leaseState,
                canonicalMode,
                transition,
                lease,
                applying,
                true,
                "MODE_RECONCILE_APPLY_OUTCOME",
                "Apply was in flight when Runtime stopped; missing response is not proof that the Windows mutation did not execute.");
        }

        if (transition.TransitionState == PersistedModeTransitionState.RollingBack)
        {
            var rollbackCandidate = FindRollbackCandidate(actions);
            transaction.Commit();
            return rollbackCandidate is null
                ? Snapshot(
                    ModeCrashReconciliationAction.VerifySourceAfterRollback,
                    leaseState,
                    canonicalMode,
                    transition,
                    lease,
                    null,
                    hasMutationEvidence,
                    "MODE_RECONCILE_VERIFY_SOURCE",
                    "Durable compensation journal is settled; source/BASE actual state must be verified as a whole before terminalization.")
                : Snapshot(
                    ModeCrashReconciliationAction.RollbackSource,
                    leaseState,
                    canonicalMode,
                    transition,
                    lease,
                    rollbackCandidate,
                    true,
                    "MODE_RECONCILE_ROLLBACK_SOURCE",
                    "Source remains canonical and durable mutation evidence still requires reverse-order compensation.");
        }

        var candidate = FindRollbackCandidate(actions);
        if (candidate is not null || hasMutationEvidence ||
            transition.TransitionState is PersistedModeTransitionState.Verifying or PersistedModeTransitionState.Committing)
        {
            transaction.Commit();
            return Snapshot(
                ModeCrashReconciliationAction.RollbackSource,
                leaseState,
                canonicalMode,
                transition,
                lease,
                candidate,
                hasMutationEvidence,
                "MODE_RECONCILE_ROLLBACK_SOURCE",
                "Target commit is not durable; source canonical truth must be restored/verified before the operation can terminate.");
        }

        transaction.Commit();
        return Snapshot(
            ModeCrashReconciliationAction.CancelBeforeMutation,
            leaseState,
            canonicalMode,
            transition,
            lease,
            null,
            false,
            "MODE_RECONCILE_CANCEL_PRE_MUTATION",
            "No durable mutation evidence exists; source is still canonical and the interrupted operation can be cancelled without compensation.");
    }

    public async Task<ModeReconciliationTakeoverOutcome> TakeOverAsync(
        Guid transitionId,
        int expectedTransitionRevision,
        string currentControlSessionKey,
        TimeSpan leaseLifetime,
        CancellationToken cancellationToken = default)
    {
        if (transitionId == Guid.Empty) throw new ArgumentException("Transition id must not be empty.", nameof(transitionId));
        if (expectedTransitionRevision < 1) throw new ArgumentOutOfRangeException(nameof(expectedTransitionRevision));
        ValidateControlSessionKey(currentControlSessionKey);
        ValidateLeaseLifetime(leaseLifetime);
        EnsureCanonicalStoreAvailable();

        await using var connection = await OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        var transition = await ReadTransitionByIdAsync(connection, transaction, transitionId, cancellationToken).ConfigureAwait(false);
        var lease = await ReadLeaseAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        if (transition is null || IsTerminal(transition.TransitionState))
        {
            return new ModeReconciliationTakeoverOutcome(
                ModeReconciliationTakeoverDisposition.Missing,
                transition,
                lease,
                "MODE_RECONCILIATION_TRANSITION_NOT_INCOMPLETE",
                transition?.Revision);
        }

        if (transition.Revision != expectedTransitionRevision)
        {
            return new ModeReconciliationTakeoverOutcome(
                ModeReconciliationTakeoverDisposition.RevisionConflict,
                transition,
                lease,
                "MODE_TRANSITION_REVISION_CONFLICT",
                transition.Revision,
                $"Expected transition revision {expectedTransitionRevision}, actual {transition.Revision}.");
        }

        if (!string.Equals(transition.ControlSessionKey, currentControlSessionKey, StringComparison.Ordinal))
        {
            return new ModeReconciliationTakeoverOutcome(
                ModeReconciliationTakeoverDisposition.SessionConflict,
                transition,
                lease,
                "MODE_RECONCILIATION_SESSION_CONFLICT",
                transition.Revision,
                "A fresh/different control session must not adopt the prior session's incomplete MODE transition.");
        }

        var canonicalMode = await ReadOperationalModeAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        if (!CanonicalRelationshipIsCoherent(transition, canonicalMode))
        {
            return new ModeReconciliationTakeoverOutcome(
                ModeReconciliationTakeoverDisposition.RecoveryRequired,
                transition,
                lease,
                "MODE_RECONCILIATION_CANONICAL_MISMATCH",
                transition.Revision,
                "Canonical mode and transition durable commit evidence disagree.");
        }

        var now = _timeProvider.GetUtcNow();
        if (LeaseExactlyMatchesTransition(transition, lease) && !IsExpired(lease, now))
        {
            return new ModeReconciliationTakeoverOutcome(
                ModeReconciliationTakeoverDisposition.AlreadyCurrent,
                transition,
                lease,
                "MODE_RECONCILIATION_LEASE_ALREADY_CURRENT",
                transition.Revision);
        }

        if (lease.IsHeld)
        {
            var sameDurableOwner = lease.MutationType == MachineMutationType.Mode &&
                                   lease.OwnerOperationId == transition.OperationId &&
                                   lease.OwnerCorrelationId == transition.CorrelationId &&
                                   string.Equals(lease.OwnerControlSessionKey, transition.ControlSessionKey, StringComparison.Ordinal) &&
                                   lease.LeaseId == transition.LeaseId &&
                                   lease.FenceToken == transition.FenceToken;
            if (!sameDurableOwner)
            {
                return new ModeReconciliationTakeoverOutcome(
                    IsExpired(lease, now)
                        ? ModeReconciliationTakeoverDisposition.RecoveryRequired
                        : ModeReconciliationTakeoverDisposition.Busy,
                    transition,
                    lease,
                    IsExpired(lease, now) ? "MODE_RECONCILIATION_FOREIGN_EXPIRED_LEASE" : BusyCode(lease.MutationType),
                    transition.Revision,
                    "Only the expired durable lease owned by this exact transition may be re-fenced by MODE reconciliation.");
            }
        }

        var newLeaseId = Guid.NewGuid();
        var newFence = checked(lease.FenceToken + 1);
        var expires = now.Add(leaseLifetime);
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE machine_mutation_lease
            SET lease_id = $lease,
                mutation_type = 'MODE',
                owner_operation_id = $operation,
                owner_correlation_id = $correlation,
                owner_control_session_key = $session,
                fence_token = fence_token + 1,
                acquired_utc = $now,
                heartbeat_utc = $now,
                expires_utc = $expires,
                revision = revision + 1
            WHERE singleton_id = 1
              AND revision = $lease_revision;
            """;
        command.Parameters.AddWithValue("$lease", newLeaseId.ToString("D"));
        command.Parameters.AddWithValue("$operation", transition.OperationId.ToString("D"));
        command.Parameters.AddWithValue("$correlation", transition.CorrelationId.ToString("D"));
        command.Parameters.AddWithValue("$session", transition.ControlSessionKey);
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.Parameters.AddWithValue("$expires", expires.ToString("O"));
        command.Parameters.AddWithValue("$lease_revision", lease.Revision);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            var actualLease = await ReadLeaseAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            return new ModeReconciliationTakeoverOutcome(
                ModeReconciliationTakeoverDisposition.RecoveryRequired,
                transition,
                actualLease,
                "MODE_RECONCILIATION_LEASE_CONCURRENCY_CONFLICT",
                transition.Revision,
                "Mutation lease changed while reconciliation was re-fencing the transition.");
        }

        command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE mode_transition
            SET lease_id = $lease,
                fence_token = $fence,
                updated_utc = $updated,
                revision = revision + 1
            WHERE transition_id = $transition
              AND revision = $revision
              AND operation_id = $operation
              AND correlation_id = $correlation
              AND control_session_key = $session
              AND transition_state NOT IN ('COMPLETED','CANCELLED','FAILED_WITH_SAFE_FALLBACK');
            """;
        command.Parameters.AddWithValue("$lease", newLeaseId.ToString("D"));
        command.Parameters.AddWithValue("$fence", newFence);
        command.Parameters.AddWithValue("$updated", now.ToString("O"));
        command.Parameters.AddWithValue("$transition", transition.TransitionId.ToString("D"));
        command.Parameters.AddWithValue("$revision", expectedTransitionRevision);
        command.Parameters.AddWithValue("$operation", transition.OperationId.ToString("D"));
        command.Parameters.AddWithValue("$correlation", transition.CorrelationId.ToString("D"));
        command.Parameters.AddWithValue("$session", transition.ControlSessionKey);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            var actual = await ReadTransitionByIdAsync(connection, transaction, transitionId, cancellationToken).ConfigureAwait(false);
            return new ModeReconciliationTakeoverOutcome(
                ModeReconciliationTakeoverDisposition.RevisionConflict,
                actual,
                lease,
                "MODE_TRANSITION_REVISION_CONFLICT",
                actual?.Revision,
                "Transition changed while the new reconciliation fence was being bound.");
        }

        transaction.Commit();
        var updatedTransition = transition with
        {
            LeaseId = newLeaseId,
            FenceToken = newFence,
            UpdatedUtc = now,
            Revision = checked(transition.Revision + 1)
        };
        var updatedLease = new MachineMutationLeaseRecord(
            newLeaseId,
            MachineMutationType.Mode,
            transition.OperationId,
            transition.CorrelationId,
            transition.ControlSessionKey,
            newFence,
            now,
            now,
            expires,
            checked(lease.Revision + 1));
        return new ModeReconciliationTakeoverOutcome(
            ModeReconciliationTakeoverDisposition.Acquired,
            updatedTransition,
            updatedLease,
            "MODE_RECONCILIATION_LEASE_ACQUIRED",
            updatedTransition.Revision);
    }

    public async Task<ModeTerminalLeaseCleanupOutcome> ReleaseTerminalLeaseAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureCanonicalStoreAvailable();
        await using var connection = await OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        var lease = await ReadLeaseAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        if (!lease.IsHeld)
        {
            return new ModeTerminalLeaseCleanupOutcome(
                ModeTerminalLeaseCleanupDisposition.AlreadyReleased,
                lease,
                "MODE_TERMINAL_LEASE_ALREADY_RELEASED");
        }
        if (lease.MutationType != MachineMutationType.Mode)
        {
            return new ModeTerminalLeaseCleanupOutcome(
                ModeTerminalLeaseCleanupDisposition.Busy,
                lease,
                BusyCode(lease.MutationType),
                "Only a MODE lease proven to belong to a terminal mode transition may be cleaned by this repository.");
        }

        var transition = lease.OwnerOperationId is null
            ? null
            : await ReadTransitionByOperationIdAsync(connection, transaction, lease.OwnerOperationId.Value, cancellationToken).ConfigureAwait(false);
        var canonicalMode = await ReadOperationalModeAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        if (transition is null ||
            !IsTerminal(transition.TransitionState) ||
            transition.Stage != PersistedModeTransitionStage.Terminal ||
            transition.LeaseId != lease.LeaseId ||
            transition.FenceToken != lease.FenceToken ||
            transition.CorrelationId != lease.OwnerCorrelationId ||
            !string.Equals(transition.ControlSessionKey, lease.OwnerControlSessionKey, StringComparison.Ordinal) ||
            !CanonicalRelationshipIsCoherent(transition, canonicalMode))
        {
            return new ModeTerminalLeaseCleanupOutcome(
                ModeTerminalLeaseCleanupDisposition.RecoveryRequired,
                lease,
                "MODE_TERMINAL_LEASE_CLEANUP_DENIED",
                "Held MODE lease is not exact durable residue of a coherent terminal mode transition.");
        }

        var command = connection.CreateCommand();
        command.Transaction = transaction;
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
              AND revision = $revision
              AND lease_id = $lease
              AND fence_token = $fence
              AND owner_operation_id = $operation;
            """;
        command.Parameters.AddWithValue("$revision", lease.Revision);
        command.Parameters.AddWithValue("$lease", lease.LeaseId!.Value.ToString("D"));
        command.Parameters.AddWithValue("$fence", lease.FenceToken);
        command.Parameters.AddWithValue("$operation", lease.OwnerOperationId!.Value.ToString("D"));
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            var actual = await ReadLeaseAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            return new ModeTerminalLeaseCleanupOutcome(
                ModeTerminalLeaseCleanupDisposition.RecoveryRequired,
                actual,
                "MODE_TERMINAL_LEASE_CONCURRENCY_CONFLICT",
                "Terminal lease changed concurrently during cleanup.");
        }

        transaction.Commit();
        var released = new MachineMutationLeaseRecord(
            null,
            null,
            null,
            null,
            null,
            lease.FenceToken,
            null,
            null,
            null,
            checked(lease.Revision + 1));
        return new ModeTerminalLeaseCleanupOutcome(
            ModeTerminalLeaseCleanupDisposition.Released,
            released,
            "MODE_TERMINAL_LEASE_RELEASED");
    }

    private async Task<ModeCrashReconciliationSnapshot> InspectWithoutIncompleteTransitionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        OperationalModeRecord canonicalMode,
        MachineMutationLeaseRecord lease,
        string currentControlSessionKey,
        CancellationToken cancellationToken)
    {
        // UPDATE/RECOVERY ownership always wins startup ordering. A fresh-session MODE
        // convergence decision must not bypass another major machine mutation owner.
        if (lease.IsHeld && lease.MutationType != MachineMutationType.Mode)
        {
            return Snapshot(
                ModeCrashReconciliationAction.WaitForMutationOwner,
                ModeCrashReconciliationLeaseState.Busy,
                canonicalMode,
                null,
                lease,
                null,
                false,
                BusyCode(lease.MutationType),
                "UPDATE/RECOVERY mutation ownership is resolved before MODE startup work.");
        }

        if (lease.IsHeld)
        {
            var terminal = lease.OwnerOperationId is null
                ? null
                : await ReadTransitionByOperationIdAsync(connection, transaction, lease.OwnerOperationId.Value, cancellationToken).ConfigureAwait(false);
            if (terminal is not null &&
                IsTerminal(terminal.TransitionState) &&
                terminal.Stage == PersistedModeTransitionStage.Terminal &&
                terminal.LeaseId == lease.LeaseId &&
                terminal.FenceToken == lease.FenceToken &&
                terminal.CorrelationId == lease.OwnerCorrelationId &&
                string.Equals(terminal.ControlSessionKey, lease.OwnerControlSessionKey, StringComparison.Ordinal) &&
                CanonicalRelationshipIsCoherent(terminal, canonicalMode))
            {
                return Snapshot(
                    ModeCrashReconciliationAction.ReleaseTerminalLease,
                    ModeCrashReconciliationLeaseState.TakeoverRequired,
                    canonicalMode,
                    terminal,
                    lease,
                    null,
                    false,
                    "MODE_TERMINAL_LEASE_CLEANUP_REQUIRED",
                    "Transition is terminal but its MODE lease residue was not released before Runtime stopped.");
            }

            return Snapshot(
                ModeCrashReconciliationAction.EscalateRecovery,
                ModeCrashReconciliationLeaseState.RecoveryRequired,
                canonicalMode,
                terminal,
                lease,
                null,
                false,
                "MODE_RECONCILIATION_ORPHAN_MODE_LEASE",
                "A held MODE lease exists without a matching coherent incomplete or terminal transition.");
        }

        if (canonicalMode.CommittedMode is "WORK" or "GAME")
        {
            if (!Guid.TryParse(canonicalMode.CommittedByOperationId, out var committedByOperationId))
            {
                return Snapshot(
                    ModeCrashReconciliationAction.EscalateRecovery,
                    ModeCrashReconciliationLeaseState.RecoveryRequired,
                    canonicalMode,
                    null,
                    lease,
                    null,
                    false,
                    "MODE_RECONCILIATION_MANAGED_MODE_OWNER_MISSING",
                    "Managed canonical mode has no parseable durable transition owner; control-session authority cannot be reconstructed.");
            }

            var committedTransition = await ReadTransitionByOperationIdAsync(
                connection,
                transaction,
                committedByOperationId,
                cancellationToken).ConfigureAwait(false);
            if (committedTransition is null ||
                committedTransition.TransitionState != PersistedModeTransitionState.Completed ||
                committedTransition.Stage != PersistedModeTransitionStage.Terminal ||
                !committedTransition.CommitDurable ||
                !CanonicalRelationshipIsCoherent(committedTransition, canonicalMode))
            {
                return Snapshot(
                    ModeCrashReconciliationAction.EscalateRecovery,
                    ModeCrashReconciliationLeaseState.RecoveryRequired,
                    canonicalMode,
                    committedTransition,
                    lease,
                    null,
                    false,
                    "MODE_RECONCILIATION_MANAGED_MODE_EVIDENCE_MISSING",
                    "Managed canonical mode is not backed by one coherent completed durable mode transition.");
            }

            if (!string.Equals(committedTransition.ControlSessionKey, currentControlSessionKey, StringComparison.Ordinal))
            {
                return Snapshot(
                    ModeCrashReconciliationAction.ConvergeBaseForFreshSession,
                    ModeCrashReconciliationLeaseState.SessionMismatch,
                    canonicalMode,
                    committedTransition,
                    lease,
                    null,
                    true,
                    "MODE_FRESH_SESSION_BASE_RECONCILIATION_REQUIRED",
                    "Canonical managed mode belongs to a prior control session and must not be auto-reactivated.");
            }

            return Snapshot(
                ModeCrashReconciliationAction.VerifyCommittedTarget,
                ModeCrashReconciliationLeaseState.None,
                canonicalMode,
                committedTransition,
                lease,
                null,
                true,
                "MODE_RECONCILE_COMMITTED_TARGET",
                "No mode operation is incomplete, but the same-session managed target remains canonical and must be verified/reconciled before normal continuation.");
        }

        return Snapshot(
            ModeCrashReconciliationAction.None,
            ModeCrashReconciliationLeaseState.None,
            canonicalMode,
            null,
            lease,
            null,
            false,
            "MODE_RECONCILIATION_NOT_REQUIRED");
    }

    private ModeCrashReconciliationLeaseState ClassifyLease(
        ModeTransitionRecord transition,
        MachineMutationLeaseRecord lease,
        string currentControlSessionKey)
    {
        if (!string.Equals(transition.ControlSessionKey, currentControlSessionKey, StringComparison.Ordinal))
            return ModeCrashReconciliationLeaseState.SessionMismatch;
        if (!lease.IsHeld)
            return ModeCrashReconciliationLeaseState.TakeoverRequired;

        var now = _timeProvider.GetUtcNow();
        if (lease.MutationType != MachineMutationType.Mode ||
            lease.OwnerOperationId != transition.OperationId ||
            lease.OwnerCorrelationId != transition.CorrelationId ||
            !string.Equals(lease.OwnerControlSessionKey, transition.ControlSessionKey, StringComparison.Ordinal))
        {
            return IsExpired(lease, now)
                ? ModeCrashReconciliationLeaseState.RecoveryRequired
                : ModeCrashReconciliationLeaseState.Busy;
        }

        if (!LeaseExactlyMatchesTransition(transition, lease))
            return ModeCrashReconciliationLeaseState.RecoveryRequired;
        return IsExpired(lease, now)
            ? ModeCrashReconciliationLeaseState.TakeoverRequired
            : ModeCrashReconciliationLeaseState.Current;
    }

    private static PersistedModeActionRecord? FindUnsafeInFlightAction(IReadOnlyList<PersistedModeActionRecord> actions)
        => actions
            .Where(static action => action.State is PersistedModeActionState.Applying or PersistedModeActionState.RollingBack)
            .OrderByDescending(static action => action.SequenceNo)
            .FirstOrDefault();

    private static PersistedModeActionRecord? FindRollbackCandidate(IReadOnlyList<PersistedModeActionRecord> actions)
        => actions
            .Where(static action =>
                action.State != PersistedModeActionState.RolledBack &&
                action.ApplyResultCode is "APPLIED" or "UNKNOWN")
            .OrderByDescending(static action => action.SequenceNo)
            .FirstOrDefault();

    private static bool HasPotentialMutationEvidence(PersistedModeActionRecord action)
        => action.State is PersistedModeActionState.Applying or
                PersistedModeActionState.Applied or
                PersistedModeActionState.Verifying or
                PersistedModeActionState.Verified or
                PersistedModeActionState.RollingBack or
                PersistedModeActionState.RolledBack or
                PersistedModeActionState.RollbackFailed ||
           action.ApplyResultCode is "APPLIED" or "UNKNOWN";

    private static bool CanonicalRelationshipIsCoherent(
        ModeTransitionRecord transition,
        OperationalModeRecord canonicalMode)
    {
        if (!transition.CommitDurable)
        {
            return string.Equals(canonicalMode.CommittedMode, transition.SourceMode, StringComparison.Ordinal) &&
                   canonicalMode.Revision == transition.SourceModeRevision;
        }

        return string.Equals(canonicalMode.CommittedMode, transition.TargetMode, StringComparison.Ordinal) &&
               canonicalMode.Revision == checked(transition.SourceModeRevision + 1) &&
               string.Equals(canonicalMode.CommittedByOperationId, transition.OperationId.ToString("D"), StringComparison.Ordinal) &&
               string.Equals(canonicalMode.CorrelationId, transition.CorrelationId.ToString("D"), StringComparison.Ordinal);
    }

    private static bool LeaseExactlyMatchesTransition(
        ModeTransitionRecord transition,
        MachineMutationLeaseRecord lease)
        => lease.IsHeld &&
           lease.MutationType == MachineMutationType.Mode &&
           lease.LeaseId == transition.LeaseId &&
           lease.OwnerOperationId == transition.OperationId &&
           lease.OwnerCorrelationId == transition.CorrelationId &&
           string.Equals(lease.OwnerControlSessionKey, transition.ControlSessionKey, StringComparison.Ordinal) &&
           lease.FenceToken == transition.FenceToken;

    private static bool IsTerminal(PersistedModeTransitionState state)
        => state is PersistedModeTransitionState.Completed or
            PersistedModeTransitionState.Cancelled or
            PersistedModeTransitionState.FailedWithSafeFallback;

    private static bool IsExpired(MachineMutationLeaseRecord lease, DateTimeOffset now)
        => lease.ExpiresUtc is not null && now >= lease.ExpiresUtc.Value;

    private static string BusyCode(MachineMutationType? type)
        => type switch
        {
            MachineMutationType.Update => "BUSY_UPDATE",
            MachineMutationType.Recovery => "BUSY_RECOVERY",
            _ => "MUTATION_LEASE_BUSY"
        };

    private static ModeCrashReconciliationSnapshot Snapshot(
        ModeCrashReconciliationAction action,
        ModeCrashReconciliationLeaseState leaseState,
        OperationalModeRecord canonicalMode,
        ModeTransitionRecord? transition,
        MachineMutationLeaseRecord lease,
        PersistedModeActionRecord? focusAction,
        bool hasMutationEvidence,
        string productCode,
        string? detail = null)
        => new(action, leaseState, canonicalMode, transition, lease, focusAction, hasMutationEvidence, productCode, detail);

    private async Task<SqliteConnection> OpenReadyAsync(CancellationToken cancellationToken)
    {
        var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var version = await _database.ReadSchemaVersionAsync(connection, cancellationToken).ConfigureAwait(false);
            if (version != MachineStateStore.SchemaVersion)
            {
                throw new InvalidDataException(
                    $"Mode reconciliation requires machine schema {MachineStateStore.SchemaVersion}, got {version}.");
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
            throw new InvalidDataException("Machine canonical store must be initialized before mode reconciliation is accessed.");
    }

    private static void ValidateControlSessionKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(static character => char.IsControl(character)))
            throw new ArgumentException("Control-session key is missing or outside supported bounds.", nameof(value));
    }

    private static void ValidateLeaseLifetime(TimeSpan leaseLifetime)
    {
        if (leaseLifetime <= TimeSpan.Zero || leaseLifetime > MachineMutationLeaseStore.MaximumLeaseLifetime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseLifetime),
                $"Reconciliation lease lifetime must be > 0 and <= {MachineMutationLeaseStore.MaximumLeaseLifetime}.");
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
            SELECT committed_mode, committed_utc, committed_by_operation_id, correlation_id, revision, updated_utc,
                   control_session_key, activation_epoch_id, policy_catalog_id, policy_version,
                   policy_release_id, policy_catalog_digest, policy_target, resolved_policy_digest
            FROM operational_mode_state WHERE singleton_id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("OperationalModeState singleton is missing.");

        var hasAnyIdentity = Enumerable.Range(6, 8).Any(index => !reader.IsDBNull(index));
        var hasFullIdentity = Enumerable.Range(6, 8).All(index => !reader.IsDBNull(index));
        if (hasAnyIdentity && !hasFullIdentity)
            throw new InvalidDataException("OperationalModeState contains a partial managed identity tuple.");

        string? controlSessionKey = null;
        Guid? activationEpochId = null;
        PersistedModePolicyIdentity? identity = null;
        PersistedModePolicyTarget? target = null;
        string? resolvedDigest = null;
        if (hasFullIdentity)
        {
            controlSessionKey = reader.GetString(6);
            if (!Guid.TryParse(reader.GetString(7), out var parsedEpoch) || parsedEpoch == Guid.Empty)
                throw new InvalidDataException("OperationalModeState activation epoch is malformed.");
            activationEpochId = parsedEpoch;
            identity = new PersistedModePolicyIdentity(
                reader.GetString(8), reader.GetInt64(9), reader.GetString(10), reader.GetString(11));
            target = reader.GetString(12) switch
            {
                "BASE" => PersistedModePolicyTarget.Base,
                "WORK" => PersistedModePolicyTarget.Work,
                "GAME" => PersistedModePolicyTarget.Game,
                _ => throw new InvalidDataException("Canonical policy target is invalid.")
            };
            resolvedDigest = reader.GetString(13);
        }

        return new OperationalModeRecord(
            reader.GetString(0), DateTimeOffset.Parse(reader.GetString(1)), reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetInt32(4), DateTimeOffset.Parse(reader.GetString(5)),
            controlSessionKey, activationEpochId, identity, target, resolvedDigest);
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
            FROM machine_mutation_lease
            WHERE singleton_id = 1;
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

    private static async Task<IReadOnlyList<ModeTransitionRecord>> ReadIncompleteTransitionsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
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
            result.Add(ParseTransition(reader));
        return result;
    }

    private static async Task<ModeTransitionRecord?> ReadTransitionByIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid transitionId,
        CancellationToken cancellationToken)
        => await ReadTransitionAsync(
            connection,
            transaction,
            "transition_id = $id",
            transitionId.ToString("D"),
            cancellationToken).ConfigureAwait(false);

    private static async Task<ModeTransitionRecord?> ReadTransitionByOperationIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        CancellationToken cancellationToken)
        => await ReadTransitionAsync(
            connection,
            transaction,
            "operation_id = $id",
            operationId.ToString("D"),
            cancellationToken).ConfigureAwait(false);

    private static async Task<ModeTransitionRecord?> ReadTransitionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
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
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ParseTransition(reader) : null;
    }

    private static ModeTransitionRecord ParseTransition(SqliteDataReader reader)
    {
        if (!Guid.TryParse(reader.GetString(0), out var transitionId) ||
            !Guid.TryParse(reader.GetString(1), out var operationId) ||
            !Guid.TryParse(reader.GetString(2), out var correlationId) ||
            !TryParseOperationKind(reader.GetString(3), out var operationKind) ||
            !Guid.TryParse(reader.GetString(8), out var leaseId) ||
            !TryParseTransitionState(reader.GetString(10), out var state) ||
            !TryParseTransitionStage(reader.GetString(11), out var stage) ||
            !DateTimeOffset.TryParse(reader.GetString(12), out var startedUtc) ||
            !DateTimeOffset.TryParse(reader.GetString(13), out var updatedUtc))
        {
            throw new InvalidDataException("Mode transition contains malformed canonical values.");
        }

        return new ModeTransitionRecord(
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
    }

    private static async Task<IReadOnlyList<PersistedModeActionRecord>> ReadActionsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid transitionId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT action_id, transition_id, sequence_no, owning_module, action_type, target_ref,
                   desired_schema_version, desired_state_json, desired_state_digest, mandatory,
                   rollback_class, verification_class, action_state, pre_state_json, pre_state_digest,
                   apply_result_code, verify_result_code, rollback_result_code,
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
                !Guid.TryParse(reader.GetString(1), out var parsedTransitionId) ||
                !TryParseActionState(reader.GetString(12), out var state) ||
                !DateTimeOffset.TryParse(reader.GetString(21), out var updatedUtc))
            {
                throw new InvalidDataException("Mode action journal contains malformed canonical values.");
            }

            result.Add(new PersistedModeActionRecord(
                actionId,
                parsedTransitionId,
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
                ParseOptionalDate(reader, 18),
                ParseOptionalDate(reader, 19),
                ParseOptionalDate(reader, 20),
                updatedUtc.ToUniversalTime(),
                reader.GetInt32(22)));
        }
        return result;
    }

    private static DateTimeOffset? ParseOptionalDate(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return null;
        if (!DateTimeOffset.TryParse(reader.GetString(ordinal), out var value))
            throw new InvalidDataException("Mode action journal contains malformed timestamp evidence.");
        return value.ToUniversalTime();
    }

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

    private static bool TryParseOperationKind(string value, out PersistedModeOperationKind kind)
    {
        kind = value switch
        {
            "ACTIVATE" => PersistedModeOperationKind.Activate,
            "SWITCH" => PersistedModeOperationKind.Switch,
            "DEACTIVATE" => PersistedModeOperationKind.Deactivate,
            _ => default
        };
        return value is "ACTIVATE" or "SWITCH" or "DEACTIVATE";
    }

    private static bool TryParseTransitionState(string value, out PersistedModeTransitionState state)
    {
        state = value switch
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

    private static bool TryParseTransitionStage(string value, out PersistedModeTransitionStage stage)
    {
        stage = value switch
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
}
