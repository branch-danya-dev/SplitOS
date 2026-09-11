using SplitOS.Contracts.Protocol;
using SplitOS.Persistence.Machine;

namespace SplitOS.RuntimeHost.ModeRuntime;

public interface IManagedServiceRollbackClient
{
    Task<MachineServicePolicyRollbackResult> RollbackAsync(Guid operationId, Guid correlationId,
        MachineServicePolicyRollbackRequest request, CancellationToken cancellationToken = default);
}

public sealed record RuntimeModeRollbackStepOutcome(string Disposition, string ProductCode);

/// <summary>
/// Executes one reverse-order compensation under the caller's already-owned transition lease.
/// Transport loss leaves ROLLING_BACK intact so the next attempt must observe actual state.
/// </summary>
public sealed class ManagedServiceActionRollbackCoordinator(
    IModeTransitionStore transitions,
    IModeTransitionRollbackStore rollback,
    IManagedServiceRollbackClient broker,
    IModeTransitionReconciliationStore reconciliation,
    IModeSourceAuthority? sourceAuthority = null)
{
    public async Task<RuntimeModeRollbackStepOutcome> ExecuteNextAsync(Guid transitionId, CancellationToken cancellationToken = default)
    {
        var transition = await transitions.GetAsync(transitionId, cancellationToken).ConfigureAwait(false);
        if (transition is null || transition.CommitDurable || transition.TransitionState != PersistedModeTransitionState.RollingBack ||
            transition.Stage != PersistedModeTransitionStage.RollbackStarted)
            return new("REJECTED", "MODE_ROLLBACK_TRANSITION_INVALID");
        // Production supplies fresh source authority. Legacy compositions without it
        // retain the failed-activation-only boundary and cannot restore managed modes.
        if (sourceAuthority is not null)
        {
            var authority = await sourceAuthority.EvaluateAsync(transition, cancellationToken).ConfigureAwait(false);
            if (!authority.MayRestoreSource) return new("RECONCILIATION_REQUIRED", authority.ProductCode);
        }
        else if (transition.SourceMode != "NONE")
            return new("RECONCILIATION_REQUIRED", "MODE_ROLLBACK_SOURCE_AUTHORITY_REQUIRED");
        var candidate = await rollback.GetNextRollbackCandidateAsync(transitionId, cancellationToken).ConfigureAwait(false);
        if (candidate is null)
        {
            var snapshot = await reconciliation.InspectAsync(transition.ControlSessionKey, cancellationToken).ConfigureAwait(false);
            return snapshot.Action == ModeCrashReconciliationAction.VerifySourceAfterRollback
                ? new("SOURCE_VERIFICATION_REQUIRED", snapshot.ProductCode)
                : new("RECONCILIATION_REQUIRED", snapshot.ProductCode);
        }
        var action = candidate;
        if (candidate.State != PersistedModeActionState.RollingBack)
        {
            var started = await rollback.BeginRollbackAsync(transitionId, candidate.ActionId, candidate.Revision,
                transition.LeaseId, transition.FenceToken, transition.OperationId, cancellationToken).ConfigureAwait(false);
            if (started.Disposition is not (ModeRollbackAdvanceDisposition.Advanced or ModeRollbackAdvanceDisposition.Replayed) || started.Action is null)
                return new("REJECTED", started.ProductCode);
            action = started.Action;
        }
        var result = await broker.RollbackAsync(transition.OperationId, transition.CorrelationId,
            new(transitionId, candidate.ActionId, transition.LeaseId, transition.FenceToken, transition.ControlSessionKey,
                action.Revision), cancellationToken).ConfigureAwait(false);
        if (result.Disposition != "VERIFIED") return new(result.Disposition, result.ProductCode);
        if (sourceAuthority is not null)
        {
            var authority = await sourceAuthority.EvaluateAsync(transition, cancellationToken).ConfigureAwait(false);
            if (!authority.MayRestoreSource) return new("RECONCILIATION_REQUIRED", authority.ProductCode);
        }
        var recorded = await rollback.RecordRollbackResultAsync(transitionId, candidate.ActionId, action.Revision,
            transition.LeaseId, transition.FenceToken, transition.OperationId, PersistedModeRollbackResult.RolledBack,
            cancellationToken).ConfigureAwait(false);
        return recorded.Disposition is ModeRollbackAdvanceDisposition.Advanced or ModeRollbackAdvanceDisposition.Replayed
            ? new("ROLLED_BACK", recorded.ProductCode) : new("REJECTED", recorded.ProductCode);
    }
}
