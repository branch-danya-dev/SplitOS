using SplitOS.Contracts.Protocol;
using SplitOS.Persistence.Machine;

namespace SplitOS.Broker.Service;

/// <summary>
/// BASE removes SplitOS service deltas and preserves the user's original Windows state.
/// For interrupted first activation, immutable pre-mutation evidence defines that baseline.
/// </summary>
public static class InitialBaseServicePolicy
{
    public static IReadOnlyDictionary<string, string> Resolve(ModeTransitionActionPlan plan)
        => ResolveCore(plan, true);

    public static IReadOnlyDictionary<string, string> ResolveCommittedActivation(ModeTransitionActionPlan plan)
        => ResolveCore(plan, false);

    private static IReadOnlyDictionary<string, string> ResolveCore(ModeTransitionActionPlan plan, bool compensated)
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var action in plan.Actions.OrderBy(action => action.SequenceNo))
        {
            if (action.State is PersistedModeActionState.Planned or PersistedModeActionState.Skipped)
                continue;
            if ((compensated ? action.State != PersistedModeActionState.RolledBack || action.RollbackResultCode != "ROLLED_BACK"
                    : action.State != PersistedModeActionState.Verified) ||
                action.OwningModule != ManagedServicePolicyActionContract.OwningModule ||
                action.ActionType != ManagedServicePolicyActionContract.ActionType ||
                action.TargetRef != ManagedServicePolicyActionContract.TargetRef ||
                action.DesiredSchemaVersion != ManagedServicePolicyActionContract.DesiredSchemaVersion ||
                action.PreStateJson is null || action.DesiredStateJson is null)
                throw new InvalidDataException("Initial BASE requires complete typed compensation evidence.");
            var captured = ManagedServicePolicyActionContract.DeserializePreState(action.PreStateJson);
            var desired = ManagedServicePolicyActionContract.DeserializeDesiredState(action.DesiredStateJson);
            if (!string.Equals(action.PreStateDigest, ManagedServicePolicyActionContract.ComputePreStateDigest(captured), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(action.DesiredStateDigest, ManagedServicePolicyActionContract.ComputeDesiredStateDigest(desired), StringComparison.OrdinalIgnoreCase) ||
                !captured.Select(entry => entry.ManagedServiceId).SequenceEqual(desired.Select(entry => entry.ManagedServiceId)))
                throw new InvalidDataException("Initial BASE snapshot does not match immutable action targets.");
            foreach (var entry in captured)
            {
                if (entry.ActualState is not ("RUNNING" or "STOPPED"))
                    throw new InvalidDataException("Initial BASE contains an unresolved service observation.");
                // A later action may capture a value modified by an earlier action.
                expected.TryAdd(entry.ManagedServiceId, entry.ActualState);
            }
        }
        if (expected.Count == 0) throw new InvalidDataException("Initial BASE has no captured service evidence to verify.");
        return expected;
    }
}
