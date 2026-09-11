using Microsoft.Data.Sqlite;

namespace SplitOS.Persistence.Machine;

public sealed record ModeBaseRecoveryRecord(string RecoveryId, string DesiredJson, string DesiredDigest,
    int SourceRevision, bool Completed);

public sealed partial class ModeTransitionReconciliationStore
{
    public async Task<ModeBaseRecoveryRecord?> GetBaseRecoveryAsync(Guid transitionId, CancellationToken cancellationToken = default)
    {
        EnsureCanonicalStoreAvailable();
        await using var connection = await OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        return await ReadBaseRecoveryAsync(connection, null, transitionId, cancellationToken).ConfigureAwait(false);
    }

    // Broker-only API: desired values are resolved from immutable activation evidence, never IPC input.
    public async Task<ModeTransitionRecord?> BeginBaseRecoveryAsync(ModeTransitionRecord expected,
        string desiredJson, string desiredDigest, CancellationToken cancellationToken = default)
    {
        EnsureCanonicalStoreAvailable();
        await using var connection = await OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        if (!await BaseOwnerMatchesAsync(connection, transaction, expected, cancellationToken).ConfigureAwait(false)) return null;
        var existing = await ReadBaseRecoveryAsync(connection, transaction, expected.TransitionId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
            return !existing.Completed && existing.RecoveryId == expected.RecoveryContextId &&
                existing.DesiredJson == desiredJson && existing.DesiredDigest == desiredDigest && existing.SourceRevision == expected.SourceModeRevision
                ? expected : null;
        if (expected.RecoveryContextId is not null) return null;
        var id = Guid.NewGuid().ToString("D");
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO mode_base_recovery
                (transition_id,recovery_id,desired_json,desired_digest,source_revision,started_utc)
            VALUES ($transition,$id,$json,$digest,$revision,$now);
            UPDATE mode_transition SET recovery_context_id=$id, revision=revision+1, updated_utc=$now
            WHERE transition_id=$transition;
            """;
        command.Parameters.AddWithValue("$transition", expected.TransitionId.ToString("D"));
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$json", desiredJson);
        command.Parameters.AddWithValue("$digest", desiredDigest);
        command.Parameters.AddWithValue("$revision", expected.SourceModeRevision);
        command.Parameters.AddWithValue("$now", _timeProvider.GetUtcNow().ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        var updated = await ReadTransitionByOperationIdAsync(connection, transaction, expected.OperationId, cancellationToken).ConfigureAwait(false);
        transaction.Commit();
        return updated;
    }

    public async Task<bool> ValidateBaseRecoveryOwnerAsync(ModeTransitionRecord expected, CancellationToken cancellationToken = default)
    {
        EnsureCanonicalStoreAvailable();
        await using var connection = await OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        return await BaseOwnerMatchesAsync(connection, transaction, expected, cancellationToken).ConfigureAwait(false);
    }

    // Called only by Broker immediately after fresh actual-state verification of the stored BASE plan.
    public async Task<bool> CompleteBaseRecoveryAsync(ModeTransitionRecord expected, string desiredDigest,
        CancellationToken cancellationToken = default)
    {
        EnsureCanonicalStoreAvailable();
        await using var connection = await OpenReadyAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        if (!await BaseOwnerMatchesAsync(connection, transaction, expected, cancellationToken).ConfigureAwait(false)) return false;
        var recovery = await ReadBaseRecoveryAsync(connection, transaction, expected.TransitionId, cancellationToken).ConfigureAwait(false);
        if (recovery is null || recovery.Completed || recovery.RecoveryId != expected.RecoveryContextId ||
            recovery.DesiredDigest != desiredDigest || recovery.SourceRevision != expected.SourceModeRevision) return false;
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE operational_mode_state SET committed_mode='NONE', committed_utc=$now,
                committed_by_operation_id=$operation, correlation_id=$correlation,
                control_session_key=NULL, activation_epoch_id=NULL, policy_catalog_id=NULL, policy_version=NULL,
                policy_release_id=NULL, policy_catalog_digest=NULL, policy_target=NULL, resolved_policy_digest=NULL,
                revision=revision+1, updated_utc=$now WHERE singleton_id=1;
            UPDATE mode_transition SET transition_state='FAILED_WITH_SAFE_FALLBACK', stage_code='TERMINAL',
                terminal_outcome='FAILED_WITH_SAFE_FALLBACK', updated_utc=$now, revision=revision+1
                WHERE transition_id=$transition;
            UPDATE mode_base_recovery SET completed_utc=$now WHERE transition_id=$transition;
            UPDATE machine_mutation_lease SET lease_id=NULL, mutation_type=NULL, owner_operation_id=NULL,
                owner_correlation_id=NULL, owner_control_session_key=NULL, acquired_utc=NULL,
                heartbeat_utc=NULL, expires_utc=NULL, revision=revision+1 WHERE singleton_id=1;
            """;
        command.Parameters.AddWithValue("$now", _timeProvider.GetUtcNow().ToString("O"));
        command.Parameters.AddWithValue("$operation", expected.OperationId.ToString("D"));
        command.Parameters.AddWithValue("$correlation", expected.CorrelationId.ToString("D"));
        command.Parameters.AddWithValue("$transition", expected.TransitionId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        transaction.Commit();
        return true;
    }

    private async Task<bool> BaseOwnerMatchesAsync(SqliteConnection connection, SqliteTransaction transaction,
        ModeTransitionRecord expected, CancellationToken cancellationToken)
    {
        var current = await ReadTransitionByOperationIdAsync(connection, transaction, expected.OperationId, cancellationToken).ConfigureAwait(false);
        var canonical = await ReadOperationalModeAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        var lease = await ReadLeaseAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        var incomplete = await ReadIncompleteTransitionsAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        return current == expected && !expected.CommitDurable && !IsTerminal(expected.TransitionState) &&
            incomplete.Count == 1 && incomplete[0].TransitionId == expected.TransitionId &&
            expected.SourceMode is "WORK" or "GAME" && canonical.CommittedMode == expected.SourceMode &&
            canonical.Revision == expected.SourceModeRevision && canonical.ControlSessionKey == expected.ControlSessionKey &&
            LeaseExactlyMatchesTransition(expected, lease) && lease.ExpiresUtc is { } expires && _timeProvider.GetUtcNow() < expires;
    }

    private static async Task<ModeBaseRecoveryRecord?> ReadBaseRecoveryAsync(SqliteConnection connection,
        SqliteTransaction? transaction, Guid transitionId, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT recovery_id,desired_json,desired_digest,source_revision,completed_utc FROM mode_base_recovery WHERE transition_id=$id;";
        command.Parameters.AddWithValue("$id", transitionId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), !reader.IsDBNull(4)) : null;
    }
}
