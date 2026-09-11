using SplitOS.Contracts.Protocol;
using SplitOS.Persistence.Machine;

namespace SplitOS.Broker.Service;

/// <summary>Verifies the complete committed service plan defining the canonical source.</summary>
public sealed class BrokerManagedServiceSourceVerificationExecutor(
    ModeTransitionReconciliationStore reconciliation, ModeTransitionStore transitions,
    ModeTransitionActionPlanStore plans, ModeTransitionPolicyStore policies,
    IManagedServiceCatalog catalog, IManagedServiceAdapter adapter)
{
    public async Task<MachineServiceSourceVerifyResult> ExecuteAsync(Guid operationId, Guid correlationId,
        MachineServiceSourceVerifyRequest request, CancellationToken cancellationToken = default)
    {
        var before = await reconciliation.InspectAsync(request.ControlSessionKey, cancellationToken).ConfigureAwait(false);
        if (!Matches(before, operationId, correlationId, request)) return new("REJECTED", "MODE_SOURCE_VERIFY_OWNER_INVALID");
        var canonical = before.CanonicalMode;
        var interruptedPlan = await plans.GetAsync(request.TransitionId, cancellationToken).ConfigureAwait(false);
        if (interruptedPlan is null) return new("UNAVAILABLE", "MODE_SOURCE_PLAN_UNAVAILABLE");
        Dictionary<string, string> expected;
        var recoveredBase = await ReadRecoveredBaseAsync(canonical, cancellationToken).ConfigureAwait(false);
        if (recoveredBase is not null)
        {
            expected = recoveredBase;
            foreach (var action in interruptedPlan.Actions)
                if (!TryRead(action, out var entries) || entries.Any(entry => !expected.ContainsKey(entry.ManagedServiceId)))
                    return new("UNAVAILABLE", "MODE_SOURCE_PLAN_COVERAGE_MISSING");
        }
        else if (canonical.CommittedMode == "NONE" && canonical.Revision == 1 && canonical.CommittedByOperationId == "bootstrap")
        {
            expected = new Dictionary<string, string>(InitialBaseServicePolicy.Resolve(interruptedPlan), StringComparer.Ordinal);
        }
        else
        {
            if (!Guid.TryParse(canonical.CommittedByOperationId, out var sourceOperation) || sourceOperation == Guid.Empty)
                return new("UNAVAILABLE", "MODE_SOURCE_POLICY_UNAVAILABLE");
            var source = await transitions.GetByOperationIdAsync(sourceOperation, cancellationToken).ConfigureAwait(false);
            if (source is null || !source.CommitDurable || source.TransitionState != PersistedModeTransitionState.Completed ||
                source.TargetMode != canonical.CommittedMode || source.SourceModeRevision + 1 != canonical.Revision)
                return new("UNAVAILABLE", "MODE_SOURCE_COMMIT_EVIDENCE_INVALID");
            var binding = await policies.GetAsync(source.TransitionId, cancellationToken).ConfigureAwait(false);
            if (binding is null || (canonical.CommittedMode == "NONE"
                    ? binding.Target != PersistedModePolicyTarget.Base
                    : binding.Identity != canonical.PolicyIdentity || binding.Target != canonical.PolicyTarget || binding.ResolvedDigest != canonical.ResolvedPolicyDigest))
                return new("UNAVAILABLE", "MODE_SOURCE_POLICY_BINDING_INVALID");
            var plan = await plans.GetAsync(source.TransitionId, cancellationToken).ConfigureAwait(false);
            if (plan is null || plan.Actions.Count == 0 || plan.ActionCount != plan.Actions.Count || interruptedPlan is null)
                return new("UNAVAILABLE", "MODE_SOURCE_PLAN_UNAVAILABLE");
    
            expected = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var action in plan.Actions)
            {
                if (action.State != PersistedModeActionState.Verified || !TryRead(action, out var entries))
                    return new("UNAVAILABLE", "MODE_SOURCE_PLAN_UNSUPPORTED");
                foreach (var entry in entries)
                {
                    if (expected.TryGetValue(entry.ManagedServiceId, out var state) && state != entry.DesiredState)
                        return new("UNAVAILABLE", "MODE_SOURCE_PLAN_CONFLICT");
                    expected[entry.ManagedServiceId] = entry.DesiredState;
                }
            }
            foreach (var action in interruptedPlan.Actions)
            {
                if (!TryRead(action, out var entries) || entries.Any(entry => !expected.ContainsKey(entry.ManagedServiceId)))
                    return new("UNAVAILABLE", "MODE_SOURCE_PLAN_COVERAGE_MISSING");
            }
        }
        var targets = new List<(ManagedServiceCatalogEntry Entry, ManagedServiceObservedState Expected)>();
        foreach (var pair in expected)
        {
            var desired = pair.Value == "RUNNING" ? ManagedServiceDesiredState.Running : ManagedServiceDesiredState.Stopped;
            if (!catalog.TryResolve(pair.Key, out var entry) || !entry.AllowedStates.Contains(desired))
                return new("UNAVAILABLE", "MODE_SOURCE_TARGET_UNSUPPORTED");
            targets.Add((entry, desired == ManagedServiceDesiredState.Running ? ManagedServiceObservedState.Running : ManagedServiceObservedState.Stopped));
        }
        foreach (var target in targets)
        {
            var current = await reconciliation.InspectAsync(request.ControlSessionKey, cancellationToken).ConfigureAwait(false);
            if (!Matches(current, operationId, correlationId, request) || current.CanonicalMode != canonical)
                return new("REJECTED", "MODE_SOURCE_VERIFY_CONTEXT_CHANGED");
            var observed = await adapter.QueryAsync(target.Entry, cancellationToken).ConfigureAwait(false);
            if (observed.State != target.Expected) return new("MISMATCH", "MODE_SOURCE_POLICY_NOT_VERIFIED");
        }
        var after = await reconciliation.InspectAsync(request.ControlSessionKey, cancellationToken).ConfigureAwait(false);
        return Matches(after, operationId, correlationId, request) && after.CanonicalMode == canonical
            ? new("VERIFIED", "MODE_SOURCE_POLICY_VERIFIED") : new("REJECTED", "MODE_SOURCE_VERIFY_CONTEXT_CHANGED");
    }

    private async Task<Dictionary<string, string>?> ReadRecoveredBaseAsync(OperationalModeRecord canonical, CancellationToken cancellationToken)
    {
        if (canonical.CommittedMode != "NONE" || !Guid.TryParse(canonical.CommittedByOperationId, out var operation)) return null;
        var source = await transitions.GetByOperationIdAsync(operation, cancellationToken).ConfigureAwait(false);
        if (source?.RecoveryContextId is null) return null;
        var record = await reconciliation.GetBaseRecoveryAsync(source.TransitionId, cancellationToken).ConfigureAwait(false);
        if (record is not { Completed: true } || record.RecoveryId != source.RecoveryContextId ||
            record.SourceRevision + 1 != canonical.Revision || source.TransitionState != PersistedModeTransitionState.FailedWithSafeFallback)
            throw new InvalidDataException("Completed BASE recovery evidence does not match canonical mode.");
        var entries = ManagedServicePolicyActionContract.DeserializeDesiredState(record.DesiredJson);
        if (ManagedServicePolicyActionContract.ComputeDesiredStateDigest(entries) != record.DesiredDigest)
            throw new InvalidDataException("Completed BASE recovery digest is invalid.");
        return entries.ToDictionary(entry => entry.ManagedServiceId, entry => entry.DesiredState, StringComparer.Ordinal);
    }

    private static bool Matches(ModeCrashReconciliationSnapshot snapshot, Guid operationId, Guid correlationId, MachineServiceSourceVerifyRequest request)
        => snapshot.Action == ModeCrashReconciliationAction.VerifySourceAfterRollback &&
           snapshot.LeaseState == ModeCrashReconciliationLeaseState.Current &&
           snapshot.Transition is { CommitDurable: false, RecoveryContextId: null } transition && transition.TransitionId == request.TransitionId &&
           transition.OperationId == operationId && transition.CorrelationId == correlationId &&
           transition.LeaseId == request.LeaseId && transition.FenceToken == request.FenceToken &&
           transition.Revision == request.ExpectedTransitionRevision && transition.ControlSessionKey == request.ControlSessionKey;

    private static bool TryRead(PersistedModeActionRecord action, out IReadOnlyList<ManagedServicePolicyEntry> entries)
    {
        entries = Array.Empty<ManagedServicePolicyEntry>();
        if (action.OwningModule != ManagedServicePolicyActionContract.OwningModule || action.ActionType != ManagedServicePolicyActionContract.ActionType ||
            action.TargetRef != ManagedServicePolicyActionContract.TargetRef || action.DesiredSchemaVersion != ManagedServicePolicyActionContract.DesiredSchemaVersion ||
            action.DesiredStateJson is null) return false;
        entries = ManagedServicePolicyActionContract.DeserializeDesiredState(action.DesiredStateJson);
        return string.Equals(action.DesiredStateDigest, ManagedServicePolicyActionContract.ComputeDesiredStateDigest(entries), StringComparison.OrdinalIgnoreCase);
    }
}
