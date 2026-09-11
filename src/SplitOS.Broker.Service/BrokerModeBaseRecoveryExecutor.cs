using SplitOS.Contracts.Protocol;
using SplitOS.Persistence.Machine;

namespace SplitOS.Broker.Service;

/// <summary>Restores activation baseline without restoring an unauthorized managed source.</summary>
public sealed class BrokerModeBaseRecoveryExecutor(ModeTransitionReconciliationStore recovery,
    ModeTransitionStore transitions, ModeTransitionActionPlanStore plans,
    IManagedServiceCatalog catalog, IManagedServiceAdapter adapter)
{
    public async Task<MachineModeBaseRecoveryResult> ExecuteAsync(Guid operationId, Guid correlationId,
        MachineModeBaseRecoveryRequest request, CancellationToken cancellationToken = default)
    {
        if (request.TransitionId == Guid.Empty || request.LeaseId == Guid.Empty || request.FenceToken < 1 ||
            request.ExpectedTransitionRevision < 1 || string.IsNullOrWhiteSpace(request.ControlSessionKey))
            throw new ArgumentException("Invalid BASE recovery identity.");
        await ManagedServiceMutationGate.Instance.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var transition = await transitions.GetAsync(request.TransitionId, cancellationToken).ConfigureAwait(false);
            if (transition is null || transition.OperationId != operationId || transition.CorrelationId != correlationId ||
                transition.ControlSessionKey != request.ControlSessionKey)
                return Denied("MODE_BASE_RECOVERY_OWNER_INVALID");
            var snapshot = await recovery.InspectAsync(request.ControlSessionKey, cancellationToken).ConfigureAwait(false);
            var existing = await recovery.GetBaseRecoveryAsync(request.TransitionId, cancellationToken).ConfigureAwait(false);
            if (existing is { Completed: true } && existing.RecoveryId == transition.RecoveryContextId &&
                transition.TransitionState == PersistedModeTransitionState.FailedWithSafeFallback &&
                snapshot.CanonicalMode.CommittedMode == "NONE" && snapshot.CanonicalMode.Revision == existing.SourceRevision + 1 &&
                snapshot.CanonicalMode.CommittedByOperationId == operationId.ToString("D"))
                return new("COMPLETED", "MODE_BASE_RECOVERY_COMPLETED");
            if (transition.LeaseId != request.LeaseId || transition.FenceToken != request.FenceToken ||
                transition.Revision != request.ExpectedTransitionRevision ||
                !await recovery.ValidateBaseRecoveryOwnerAsync(transition, cancellationToken).ConfigureAwait(false))
                return Denied("MODE_BASE_RECOVERY_OWNER_INVALID");

            var activation = await transitions.GetLatestCommittedActivationAsync(transition.SourceModeRevision, cancellationToken).ConfigureAwait(false);
            if (activation is null || activation.ControlSessionKey != request.ControlSessionKey)
                return Denied("MODE_BASE_RECOVERY_ACTIVATION_MISSING");
            var original = await plans.GetAsync(activation.TransitionId, cancellationToken).ConfigureAwait(false);
            if (original is null) return Denied("MODE_BASE_RECOVERY_PLAN_MISSING");
            var baseline = InitialBaseServicePolicy.ResolveCommittedActivation(original);
            if (!Guid.TryParse(snapshot.CanonicalMode.CommittedByOperationId, out var sourceId))
                return Denied("MODE_BASE_RECOVERY_SOURCE_INVALID");
            var source = await transitions.GetByOperationIdAsync(sourceId, cancellationToken).ConfigureAwait(false);
            if (source is null || !source.CommitDurable || source.TransitionState != PersistedModeTransitionState.Completed ||
                source.SourceModeRevision + 1 != transition.SourceModeRevision || source.TargetMode != transition.SourceMode)
                return Denied("MODE_BASE_RECOVERY_SOURCE_INVALID");
            foreach (var id in new[] { source.TransitionId, transition.TransitionId })
            {
                var plan = await plans.GetAsync(id, cancellationToken).ConfigureAwait(false);
                if (plan is null || plan.ActionCount != plan.Actions.Count || plan.Actions.Count == 0)
                    return Denied("MODE_BASE_RECOVERY_COVERAGE_MISSING");
                foreach (var action in plan.Actions)
                {
                    if (action.OwningModule != ManagedServicePolicyActionContract.OwningModule ||
                        action.ActionType != ManagedServicePolicyActionContract.ActionType ||
                        action.TargetRef != ManagedServicePolicyActionContract.TargetRef ||
                        action.DesiredSchemaVersion != ManagedServicePolicyActionContract.DesiredSchemaVersion || action.DesiredStateJson is null)
                        return Denied("MODE_BASE_RECOVERY_COVERAGE_MISSING");
                    var entries = ManagedServicePolicyActionContract.DeserializeDesiredState(action.DesiredStateJson);
                    if (!string.Equals(action.DesiredStateDigest, ManagedServicePolicyActionContract.ComputeDesiredStateDigest(entries), StringComparison.OrdinalIgnoreCase) ||
                        entries.Any(entry => !baseline.ContainsKey(entry.ManagedServiceId)))
                        return Denied("MODE_BASE_RECOVERY_COVERAGE_MISSING");
                }
            }
            var desired = baseline.Select(pair => new ManagedServicePolicyEntry(pair.Key, pair.Value)).ToArray();
            var digest = ManagedServicePolicyActionContract.ComputeDesiredStateDigest(desired);
            var targets = new List<(ManagedServiceCatalogEntry Entry, ManagedServiceDesiredState Desired, ManagedServiceObservedState Observed)>();
            foreach (var entry in desired)
            {
                var state = entry.DesiredState == "RUNNING" ? ManagedServiceDesiredState.Running : ManagedServiceDesiredState.Stopped;
                if (!catalog.TryResolve(entry.ManagedServiceId, out var target) || !target.AllowedStates.Contains(state))
                    return Denied("MODE_BASE_RECOVERY_TARGET_INVALID");
                targets.Add((target, state, state == ManagedServiceDesiredState.Running ? ManagedServiceObservedState.Running : ManagedServiceObservedState.Stopped));
            }
            transition = await recovery.BeginBaseRecoveryAsync(transition,
                ManagedServicePolicyActionContract.SerializeDesiredState(desired), digest, cancellationToken).ConfigureAwait(false);
            if (transition is null) return Denied("MODE_BASE_RECOVERY_CONTEXT_CHANGED");
            foreach (var target in targets)
            {
                if (!await recovery.ValidateBaseRecoveryOwnerAsync(transition, cancellationToken).ConfigureAwait(false))
                    return Denied("MODE_BASE_RECOVERY_OWNER_CHANGED");
                var actual = await adapter.QueryAsync(target.Entry, cancellationToken).ConfigureAwait(false);
                if (actual.State == target.Observed) continue;
                if (actual.State is not (ManagedServiceObservedState.Running or ManagedServiceObservedState.Stopped))
                    return Denied("MODE_BASE_RECOVERY_STATE_UNCERTAIN");
                if (!await recovery.ValidateBaseRecoveryOwnerAsync(transition, cancellationToken).ConfigureAwait(false))
                    return Denied("MODE_BASE_RECOVERY_OWNER_CHANGED");
                _ = await adapter.ApplyAsync(target.Entry, target.Desired, cancellationToken).ConfigureAwait(false);
            }
            foreach (var target in targets)
            {
                if (!await recovery.ValidateBaseRecoveryOwnerAsync(transition, cancellationToken).ConfigureAwait(false))
                    return Denied("MODE_BASE_RECOVERY_OWNER_CHANGED");
                if ((await adapter.QueryAsync(target.Entry, cancellationToken).ConfigureAwait(false)).State != target.Observed)
                    return Denied("MODE_BASE_RECOVERY_NOT_VERIFIED");
            }
            return await recovery.CompleteBaseRecoveryAsync(transition, digest, cancellationToken).ConfigureAwait(false)
                ? new("COMPLETED", "MODE_BASE_RECOVERY_COMPLETED") : Denied("MODE_BASE_RECOVERY_COMMIT_REJECTED");
        }
        finally { ManagedServiceMutationGate.Instance.Release(); }
    }

    private static MachineModeBaseRecoveryResult Denied(string code) => new("RECOVERY_PENDING", code);
}
