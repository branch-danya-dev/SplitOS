using SplitOS.Persistence.Machine;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.ModeRuntime;

/// <summary>
/// Topology semantics already covered by durable display apply/verify/rollback/source-verification.
/// This is an effective target, not persistent GameProfile intent and not current CCD state.
/// </summary>
public enum ResolvedDisplayTopologyIntent
{
    Extend,
    PreserveActiveTopology
}

/// <summary>
/// Immutable display target after SPEC-08 desired intent, current SPEC-06 capability evidence and
/// approved fallback policy have already been resolved. No operation-local adapterLuid/targetId or
/// process-local display generation belongs in this contract.
/// </summary>
public sealed record ResolvedDisplayActionTarget(
    PersistentDisplaySelector Selector,
    ResolvedDisplayTopologyIntent TopologyIntent,
    uint Width,
    uint Height,
    uint RefreshNumerator,
    uint RefreshDenominator,
    uint Rotation,
    bool Mandatory = true);

public sealed record ResolvedDisplayActionPlan(
    IReadOnlyList<PersistedModeActionDefinition> Actions)
{
    public PersistedModeActionDefinition ModeAction => Actions[^1];
    public PersistedModeActionDefinition? TopologyAction => Actions.Count == 2 ? Actions[0] : null;
}

/// <summary>
/// Converts one already-resolved effective display target into immutable durable action semantics.
/// It deliberately does not read GameProfile persistence, choose a monitor, resolve a refresh
/// fallback or inspect Windows. Those decisions must be complete before this boundary.
/// </summary>
public static class ResolvedDisplayActionPlanner
{
    public static ResolvedDisplayActionPlan Create(
        ResolvedDisplayActionTarget target,
        int firstSequenceNo,
        Guid? topologyActionId = null,
        Guid? modeActionId = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (firstSequenceNo < 1)
            throw new ArgumentOutOfRangeException(nameof(firstSequenceNo));

        var selector = DisplayModeActionContract.NormalizeSelector(target.Selector);
        var desiredMode = DisplayModeActionContract.Normalize(new DisplayTargetModeDesiredState(
            selector,
            target.Width,
            target.Height,
            target.RefreshNumerator,
            target.RefreshDenominator,
            target.Rotation));

        var resolvedModeActionId = modeActionId ?? Guid.NewGuid();
        if (resolvedModeActionId == Guid.Empty)
            throw new ArgumentException("Mode action id must not be empty.", nameof(modeActionId));

        switch (target.TopologyIntent)
        {
            case ResolvedDisplayTopologyIntent.PreserveActiveTopology:
                return new ResolvedDisplayActionPlan(new[]
                {
                    DisplayModeActionContract.CreateTargetModeDefinition(
                        resolvedModeActionId,
                        firstSequenceNo,
                        desiredMode,
                        target.Mandatory)
                });

            case ResolvedDisplayTopologyIntent.Extend:
                var resolvedTopologyActionId = topologyActionId ?? Guid.NewGuid();
                if (resolvedTopologyActionId == Guid.Empty)
                    throw new ArgumentException("Topology action id must not be empty.", nameof(topologyActionId));
                if (resolvedTopologyActionId == resolvedModeActionId)
                    throw new ArgumentException("Display topology and mode actions must have distinct action ids.");
                if (firstSequenceNo == int.MaxValue)
                    throw new ArgumentOutOfRangeException(nameof(firstSequenceNo), "EXTEND requires room for the following target-mode action sequence.");

                return new ResolvedDisplayActionPlan(new[]
                {
                    DisplayModeActionContract.CreateTopologyExtendDefinition(
                        resolvedTopologyActionId,
                        firstSequenceNo,
                        selector,
                        target.Mandatory),
                    DisplayModeActionContract.CreateTargetModeDefinition(
                        resolvedModeActionId,
                        checked(firstSequenceNo + 1),
                        desiredMode,
                        target.Mandatory)
                });

            default:
                throw new ArgumentOutOfRangeException(nameof(target), "Resolved display topology intent is not supported by the durable v1 display action set.");
        }
    }
}
