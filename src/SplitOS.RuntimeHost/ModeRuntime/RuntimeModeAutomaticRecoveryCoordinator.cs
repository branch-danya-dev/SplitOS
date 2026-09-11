using SplitOS.Persistence.Machine;
using SplitOS.Contracts.Protocol;

namespace SplitOS.RuntimeHost.ModeRuntime;

public sealed record RuntimeModeAutomaticRecoveryOutcome(bool MayCheckAccess, string ProductCode);

public interface IModeBaseRecoveryClient
{
    Task<MachineModeBaseRecoveryResult> RecoverBaseAsync(Guid operationId, Guid correlationId,
        MachineModeBaseRecoveryRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Bounded recovery of durable compensation evidence, before idle access reconciliation.</summary>
public sealed class RuntimeModeAutomaticRecoveryCoordinator(
    RuntimeModeRecoveryCoordinator preparation,
    IModeTransitionReconciliationStore reconciliation,
    IModeTransitionStore transitions,
    IMachineMutationLeaseStore leases,
    IControlSessionIdentity session,
    IModeSourceAuthority authority,
    ManagedServiceActionRollbackCoordinator rollback,
    RuntimeModeRollbackCompletionCoordinator completion,
    IModeBaseRecoveryClient? baseRecovery = null) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly TimeSpan LeaseLifetime = TimeSpan.FromMinutes(2);

    public void Dispose() => _gate.Dispose();

    public async Task<RuntimeModeAutomaticRecoveryOutcome> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var key = session.GetCurrentKey();
            var prepared = await preparation.PrepareAsync(key, LeaseLifetime, cancellationToken).ConfigureAwait(false);
            if (prepared.Disposition is RuntimeModeRecoveryDisposition.WaitingForOwner or RuntimeModeRecoveryDisposition.RecoveryRequired)
                return new(false, prepared.ProductCode);
            // An idle committed mode is handled by access reconciliation, not compensation.
            if ((await transitions.GetIncompleteAsync(cancellationToken).ConfigureAwait(false)).Count == 0)
                return new(true, prepared.ProductCode);
            var owner = prepared.Snapshot.Transition;
            if (owner is null || prepared.Snapshot.LeaseState != ModeCrashReconciliationLeaseState.Current)
                return new(false, "MODE_AUTOMATIC_RECOVERY_OWNER_REQUIRED");

            // Stop on the first unresolved step. A later pass must acquire a fresh expired lease.
            for (var step = 0; step < 64; step++)
            {
                if (session.GetCurrentKey() != key)
                    return new(false, "MODE_SOURCE_AUTHORITY_SESSION_CHANGED");
                var snapshot = await reconciliation.InspectAsync(key, cancellationToken).ConfigureAwait(false);
                var current = snapshot.Transition;
                if (snapshot.LeaseState != ModeCrashReconciliationLeaseState.Current || current is null ||
                    current.TransitionId != owner.TransitionId || current.LeaseId != owner.LeaseId ||
                    current.FenceToken != owner.FenceToken || current.CommitDurable)
                    return new(false, "MODE_AUTOMATIC_RECOVERY_OWNER_CHANGED");
                var permission = await authority.EvaluateAsync(current, cancellationToken).ConfigureAwait(false);
                if (permission.ProductCode == "MODE_SOURCE_AUTHORITY_BASE_CONVERGENCE_REQUIRED" && baseRecovery is not null)
                {
                    var lease = await leases.RenewAsync(current.LeaseId, current.FenceToken, current.OperationId,
                        LeaseLifetime, cancellationToken).ConfigureAwait(false);
                    if (lease.Disposition != MachineMutationLeaseRenewDisposition.Renewed) return new(false, lease.ProductCode);
                    if (session.GetCurrentKey() != key) return new(false, "MODE_SOURCE_AUTHORITY_SESSION_CHANGED");
                    var baseResult = await baseRecovery.RecoverBaseAsync(current.OperationId, current.CorrelationId,
                        new(current.TransitionId, current.LeaseId, current.FenceToken, key, current.Revision), cancellationToken).ConfigureAwait(false);
                    return new(baseResult.Disposition == "COMPLETED", baseResult.ProductCode);
                }
                if (snapshot.Action is not (ModeCrashReconciliationAction.RollbackSource or
                    ModeCrashReconciliationAction.ReconcileRollbackOutcome or ModeCrashReconciliationAction.VerifySourceAfterRollback))
                    return new(false, snapshot.ProductCode);
                if (!permission.MayRestoreSource) return new(false, permission.ProductCode);
                var renewed = await leases.RenewAsync(current.LeaseId, current.FenceToken, current.OperationId,
                    LeaseLifetime, cancellationToken).ConfigureAwait(false);
                if (renewed.Disposition != MachineMutationLeaseRenewDisposition.Renewed)
                    return new(false, renewed.ProductCode);
                if (snapshot.Action == ModeCrashReconciliationAction.VerifySourceAfterRollback)
                {
                    var completed = await completion.CompleteAsync(current.TransitionId, cancellationToken).ConfigureAwait(false);
                    return new(completed.Disposition == "FAILED_WITH_SAFE_FALLBACK", completed.ProductCode);
                }
                if (current.TransitionState != PersistedModeTransitionState.RollingBack)
                {
                    var started = await transitions.AdvanceAsync(current.TransitionId, current.Revision, current.LeaseId,
                        current.FenceToken, current.OperationId, PersistedModeTransitionState.RollingBack,
                        PersistedModeTransitionStage.RollbackStarted, current.MandatoryVerified, null, cancellationToken).ConfigureAwait(false);
                    if (started.Disposition != ModeTransitionAdvanceDisposition.Advanced)
                        return new(false, started.ProductCode);
                }
                var result = await rollback.ExecuteNextAsync(current.TransitionId, cancellationToken).ConfigureAwait(false);
                if (result.Disposition is not ("ROLLED_BACK" or "SOURCE_VERIFICATION_REQUIRED"))
                    return new(false, result.ProductCode);
            }
            return new(false, "MODE_AUTOMATIC_RECOVERY_STEP_LIMIT");
        }
        finally { _gate.Release(); }
    }
}
