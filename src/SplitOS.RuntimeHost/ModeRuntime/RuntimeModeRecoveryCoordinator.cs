using SplitOS.Persistence.Machine;

namespace SplitOS.RuntimeHost.ModeRuntime;

public enum RuntimeModeRecoveryDisposition
{
    Ready,
    WaitingForOwner,
    ActualStateReconciliationRequired,
    RecoveryRequired
}

public sealed record RuntimeModeRecoveryOutcome(
    RuntimeModeRecoveryDisposition Disposition,
    ModeCrashReconciliationSnapshot Snapshot,
    string ProductCode);

/// <summary>
/// Prepares crash recovery using Broker-owned durable evidence. Only operations proven
/// to have made no machine mutation may be cancelled here. Windows observation and
/// compensation remain explicit work in the returned snapshot.
/// </summary>
public sealed class RuntimeModeRecoveryCoordinator(
    IModeTransitionReconciliationStore reconciliation,
    IModeTransitionStore transitions)
{
    public async Task<RuntimeModeRecoveryOutcome> PrepareAsync(
        string controlSessionKey,
        TimeSpan leaseLifetime,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(controlSessionKey) || controlSessionKey.Length > 256 || controlSessionKey.Any(char.IsControl))
            throw new ArgumentException("A valid control session key is required.", nameof(controlSessionKey));
        if (leaseLifetime <= TimeSpan.Zero || leaseLifetime > SplitOS.Contracts.ModePersistence.ModePersistenceProtocol.MaximumLeaseLifetime)
            throw new ArgumentOutOfRangeException(nameof(leaseLifetime));

        var snapshot = await reconciliation.InspectAsync(controlSessionKey, cancellationToken).ConfigureAwait(false);
        if (snapshot.Action == ModeCrashReconciliationAction.ReleaseTerminalLease)
        {
            var cleanup = await reconciliation.ReleaseTerminalLeaseAsync(cancellationToken).ConfigureAwait(false);
            if (cleanup.Disposition is not (ModeTerminalLeaseCleanupDisposition.Released or ModeTerminalLeaseCleanupDisposition.AlreadyReleased))
                return new(RuntimeModeRecoveryDisposition.RecoveryRequired, snapshot, cleanup.ProductCode);
            snapshot = await reconciliation.InspectAsync(controlSessionKey, cancellationToken).ConfigureAwait(false);
        }

        if (snapshot.Action == ModeCrashReconciliationAction.None)
            return new(RuntimeModeRecoveryDisposition.Ready, snapshot, snapshot.ProductCode);
        if (snapshot.Action is ModeCrashReconciliationAction.EscalateRecovery or ModeCrashReconciliationAction.ConvergeBaseForFreshSession)
            return new(RuntimeModeRecoveryDisposition.RecoveryRequired, snapshot, snapshot.ProductCode);
        if (snapshot.Action == ModeCrashReconciliationAction.WaitForMutationOwner ||
            snapshot.LeaseState is ModeCrashReconciliationLeaseState.Busy or ModeCrashReconciliationLeaseState.Current)
        {
            // An unexpired lease is not proof that its Runtime process has died.
            return new(RuntimeModeRecoveryDisposition.WaitingForOwner, snapshot, "MODE_RECOVERY_WAIT_FOR_OWNER");
        }
        if (snapshot.LeaseState == ModeCrashReconciliationLeaseState.TakeoverRequired && snapshot.Transition is { } interrupted)
        {
            var takeover = await reconciliation.TakeOverAsync(interrupted.TransitionId, interrupted.Revision,
                controlSessionKey, leaseLifetime, cancellationToken).ConfigureAwait(false);
            if (takeover.Disposition != ModeReconciliationTakeoverDisposition.Acquired)
                return new(takeover.Disposition is ModeReconciliationTakeoverDisposition.RecoveryRequired or ModeReconciliationTakeoverDisposition.SessionConflict
                    ? RuntimeModeRecoveryDisposition.RecoveryRequired
                    : RuntimeModeRecoveryDisposition.WaitingForOwner, snapshot, takeover.ProductCode);
            snapshot = await reconciliation.InspectAsync(controlSessionKey, cancellationToken).ConfigureAwait(false);
            if (snapshot.Transition?.LeaseId != takeover.Lease.LeaseId || snapshot.Transition?.FenceToken != takeover.Lease.FenceToken)
                return new(RuntimeModeRecoveryDisposition.WaitingForOwner, snapshot, "MODE_RECOVERY_OWNER_CHANGED");
        }

        if (snapshot.Action == ModeCrashReconciliationAction.CancelBeforeMutation &&
            snapshot.LeaseState == ModeCrashReconciliationLeaseState.Current &&
            !snapshot.HasMutationEvidence && snapshot.Transition is { CommitDurable: false } transition)
        {
            var cancelled = await transitions.AdvanceAsync(transition.TransitionId, transition.Revision,
                transition.LeaseId, transition.FenceToken, transition.OperationId,
                PersistedModeTransitionState.Cancelled, PersistedModeTransitionStage.Terminal,
                transition.MandatoryVerified, "CANCELLED", cancellationToken).ConfigureAwait(false);
            if (cancelled.Disposition is not (ModeTransitionAdvanceDisposition.Advanced or ModeTransitionAdvanceDisposition.Unchanged))
                return new(RuntimeModeRecoveryDisposition.RecoveryRequired, snapshot, cancelled.ProductCode);
            var cleanup = await reconciliation.ReleaseTerminalLeaseAsync(cancellationToken).ConfigureAwait(false);
            snapshot = await reconciliation.InspectAsync(controlSessionKey, cancellationToken).ConfigureAwait(false);
            if (cleanup.Disposition is not (ModeTerminalLeaseCleanupDisposition.Released or ModeTerminalLeaseCleanupDisposition.AlreadyReleased))
                return new(RuntimeModeRecoveryDisposition.RecoveryRequired, snapshot, cleanup.ProductCode);
            return new(snapshot.Action == ModeCrashReconciliationAction.None
                ? RuntimeModeRecoveryDisposition.Ready
                : RuntimeModeRecoveryDisposition.ActualStateReconciliationRequired, snapshot, snapshot.ProductCode);
        }

        return new(RuntimeModeRecoveryDisposition.ActualStateReconciliationRequired, snapshot, snapshot.ProductCode);
    }
}
