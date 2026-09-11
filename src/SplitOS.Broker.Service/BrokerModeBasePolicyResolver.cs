using SplitOS.Contracts.Protocol;
using SplitOS.Persistence.Machine;

namespace SplitOS.Broker.Service;

/// <summary>Restoration policy from the successful activation that began current managed ownership.</summary>
public sealed class BrokerModeBasePolicyResolver(ModeTransitionReconciliationStore reconciliation,
    ModeTransitionStore transitions, ModeTransitionActionPlanStore plans)
{
    public async Task<MachineModeBasePolicyResult> ExecuteAsync(Guid operationId, Guid correlationId,
        MachineModeBasePolicyRequest request, CancellationToken cancellationToken = default)
    {
        var snapshot = await reconciliation.InspectAsync(request.ControlSessionKey, cancellationToken).ConfigureAwait(false);
        if (!Matches(snapshot, operationId, correlationId, request)) return Denied("MODE_BASE_POLICY_OWNER_INVALID");
        var canonical = snapshot.CanonicalMode;
        var activation = await transitions.GetLatestCommittedActivationAsync(canonical.Revision, cancellationToken).ConfigureAwait(false);
        if (activation is null || activation.ControlSessionKey != request.ControlSessionKey)
            return Denied("MODE_BASE_POLICY_ACTIVATION_MISSING");
        var originalPlan = await plans.GetAsync(activation.TransitionId, cancellationToken).ConfigureAwait(false);
        if (originalPlan is null || !Guid.TryParse(canonical.CommittedByOperationId, out var currentOperation))
            return Denied("MODE_BASE_POLICY_EVIDENCE_MISSING");
        var currentTransition = await transitions.GetByOperationIdAsync(currentOperation, cancellationToken).ConfigureAwait(false);
        if (currentTransition is null || !currentTransition.CommitDurable || currentTransition.TransitionState != PersistedModeTransitionState.Completed ||
            currentTransition.SourceModeRevision + 1 != canonical.Revision || currentTransition.TargetMode != canonical.CommittedMode)
            return Denied("MODE_BASE_POLICY_SOURCE_INVALID");
        var currentPlan = await plans.GetAsync(currentTransition.TransitionId, cancellationToken).ConfigureAwait(false);
        if (currentPlan is null) return Denied("MODE_BASE_POLICY_SOURCE_INVALID");
        var baseline = InitialBaseServicePolicy.ResolveCommittedActivation(originalPlan);
        foreach (var action in currentPlan.Actions)
        {
            if (action.OwningModule != ManagedServicePolicyActionContract.OwningModule ||
                action.ActionType != ManagedServicePolicyActionContract.ActionType ||
                action.TargetRef != ManagedServicePolicyActionContract.TargetRef ||
                action.DesiredSchemaVersion != ManagedServicePolicyActionContract.DesiredSchemaVersion || action.DesiredStateJson is null)
                return Denied("MODE_BASE_POLICY_COVERAGE_MISSING");
            var entries = ManagedServicePolicyActionContract.DeserializeDesiredState(action.DesiredStateJson);
            if (!string.Equals(action.DesiredStateDigest, ManagedServicePolicyActionContract.ComputeDesiredStateDigest(entries), StringComparison.OrdinalIgnoreCase))
                return Denied("MODE_BASE_POLICY_SOURCE_INVALID");
            if (entries.Any(entry => !baseline.ContainsKey(entry.ManagedServiceId))) return Denied("MODE_BASE_POLICY_COVERAGE_MISSING");
        }
        var after = await reconciliation.InspectAsync(request.ControlSessionKey, cancellationToken).ConfigureAwait(false);
        if (!Matches(after, operationId, correlationId, request) || after.CanonicalMode != canonical || after.Transition != snapshot.Transition)
            return Denied("MODE_BASE_POLICY_CONTEXT_CHANGED");
        var result = ManagedServicePolicyActionContract.NormalizeEntries(baseline.Select(pair => new ManagedServicePolicyEntry(pair.Key, pair.Value)).ToArray());
        return new("RESOLVED", "MODE_BASE_POLICY_RESOLVED", result, ManagedServicePolicyActionContract.ComputeDesiredStateDigest(result));
    }

    private static bool Matches(ModeCrashReconciliationSnapshot snapshot, Guid operation, Guid correlation, MachineModeBasePolicyRequest request)
        => snapshot.LeaseState == ModeCrashReconciliationLeaseState.Current && snapshot.CanonicalMode.CommittedMode is "WORK" or "GAME" &&
           snapshot.CanonicalMode.Revision == request.SourceModeRevision && snapshot.CanonicalMode.ControlSessionKey == request.ControlSessionKey &&
           snapshot.Transition is { OperationKind: PersistedModeOperationKind.Deactivate, TransitionState: PersistedModeTransitionState.Resolving,
               Stage: PersistedModeTransitionStage.ResolutionStarted, CommitDurable: false } transition &&
           transition.OperationId == operation && transition.CorrelationId == correlation && transition.ControlSessionKey == request.ControlSessionKey;
    private static MachineModeBasePolicyResult Denied(string code) => new("UNAVAILABLE", code, Array.Empty<ManagedServicePolicyEntry>(), null);
}
