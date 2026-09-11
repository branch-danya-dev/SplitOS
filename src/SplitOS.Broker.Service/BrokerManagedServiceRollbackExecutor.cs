using SplitOS.Contracts.Protocol;
using SplitOS.Persistence.Machine;

namespace SplitOS.Broker.Service;

/// <summary>Restores a fenced ROLLING_BACK action from immutable captured pre-state.</summary>
public sealed class BrokerManagedServiceRollbackExecutor(
    ModeMutationFenceStore fence,
    ModeTransitionActionJournalStore journal,
    IManagedServiceCatalog catalog,
    IManagedServiceAdapter adapter)
{
    private static readonly SemaphoreSlim _gate = ManagedServiceMutationGate.Instance;

    public async Task<MachineServicePolicyRollbackResult> ExecuteAsync(
        Guid operationId, Guid correlationId, MachineServicePolicyRollbackRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var context = new ModeMutationFenceContext(request.TransitionId, request.ActionId, request.LeaseId,
                request.FenceToken, operationId, correlationId, request.ControlSessionKey, request.ExpectedActionRevision);
            var authorization = await fence.ValidateRollbackAsync(context, cancellationToken).ConfigureAwait(false);
            if (!authorization.IsAuthorized) return new("REJECTED", authorization.ProductCode);

            var action = await journal.GetAsync(request.ActionId, cancellationToken).ConfigureAwait(false);
            if (action is null || action.OwningModule != ManagedServicePolicyActionContract.OwningModule ||
                action.ActionType != ManagedServicePolicyActionContract.ActionType ||
                action.TargetRef != ManagedServicePolicyActionContract.TargetRef ||
                action.DesiredSchemaVersion != ManagedServicePolicyActionContract.DesiredSchemaVersion ||
                action.RollbackClass != "restore_pre_state" || action.PreStateJson is null || action.DesiredStateJson is null)
                return new("REJECTED", "SERVICE_ROLLBACK_EVIDENCE_INVALID");

            var desired = ManagedServicePolicyActionContract.DeserializeDesiredState(action.DesiredStateJson);
            var captured = ManagedServicePolicyActionContract.DeserializePreState(action.PreStateJson);
            if (!string.Equals(action.PreStateDigest, ManagedServicePolicyActionContract.ComputePreStateDigest(captured), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(action.DesiredStateDigest, ManagedServicePolicyActionContract.ComputeDesiredStateDigest(desired), StringComparison.OrdinalIgnoreCase) ||
                !desired.Select(x => x.ManagedServiceId).SequenceEqual(captured.Select(x => x.ManagedServiceId)))
                return new("REJECTED", "SERVICE_ROLLBACK_EVIDENCE_INVALID");

            var targets = new List<(ManagedServiceCatalogEntry Entry, ManagedServiceDesiredState Desired, ManagedServiceObservedState Observed)>();
            foreach (var entry in captured.Reverse())
            {
                if (entry.ActualState is not ("RUNNING" or "STOPPED") || !catalog.TryResolve(entry.ManagedServiceId, out var target))
                    return new("REJECTED", "SERVICE_ROLLBACK_TARGET_INVALID");
                var state = entry.ActualState == "RUNNING" ? ManagedServiceDesiredState.Running : ManagedServiceDesiredState.Stopped;
                if (!target.AllowedStates.Contains(state)) return new("REJECTED", "SERVICE_ROLLBACK_TARGET_INVALID");
                targets.Add((target, state, state == ManagedServiceDesiredState.Running ? ManagedServiceObservedState.Running : ManagedServiceObservedState.Stopped));
            }
            context = context with { ExpectedAction = new(action.OwningModule, action.ActionType, action.TargetRef,
                action.DesiredSchemaVersion, action.DesiredStateDigest) };
            foreach (var target in targets)
            {
                authorization = await fence.ValidateRollbackAsync(context, cancellationToken).ConfigureAwait(false);
                if (!authorization.IsAuthorized) return new("REJECTED", authorization.ProductCode);
                var actual = await adapter.QueryAsync(target.Entry, cancellationToken).ConfigureAwait(false);
                if (actual.State == target.Observed) continue;
                if (actual.State is not (ManagedServiceObservedState.Running or ManagedServiceObservedState.Stopped))
                    return new("UNKNOWN", "SERVICE_ROLLBACK_STATE_UNCERTAIN");
                // Query can take time: revalidate immediately before each privileged call.
                authorization = await fence.ValidateRollbackAsync(context, cancellationToken).ConfigureAwait(false);
                if (!authorization.IsAuthorized) return new("REJECTED", authorization.ProductCode);
                _ = await adapter.ApplyAsync(target.Entry, target.Desired, cancellationToken).ConfigureAwait(false);
                // The adapter's immediate return is not sufficient evidence of restoration.
                actual = await adapter.QueryAsync(target.Entry, cancellationToken).ConfigureAwait(false);
                if (actual.State != target.Observed) return new("UNKNOWN", "SERVICE_ROLLBACK_NOT_VERIFIED");
            }
            // Read every restored target again, including ones already satisfied on replay.
            foreach (var target in targets)
                if ((await adapter.QueryAsync(target.Entry, cancellationToken).ConfigureAwait(false)).State != target.Observed)
                    return new("UNKNOWN", "SERVICE_ROLLBACK_NOT_VERIFIED");
            authorization = await fence.ValidateRollbackAsync(context, cancellationToken).ConfigureAwait(false);
            return authorization.IsAuthorized ? new("VERIFIED", "SERVICE_ROLLBACK_VERIFIED") : new("REJECTED", authorization.ProductCode);
        }
        finally { _gate.Release(); }
    }
}
