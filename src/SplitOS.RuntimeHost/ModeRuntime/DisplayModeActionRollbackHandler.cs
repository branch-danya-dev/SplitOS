using System.Text.Json;
using SplitOS.Persistence.Machine;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.ModeRuntime;

/// <summary>
/// Restart-safe compensation for durable display actions. The handler re-resolves physical displays
/// from persisted selector evidence and refuses to compensate across topology drift or control-session
/// changes. Operation-local CCD keys are derived only from the fresh snapshot used for this attempt.
/// </summary>
public sealed class DisplayModeActionRollbackHandler(
    IDisplaySnapshotReader snapshots,
    IDisplayGenerationTracker generationTracker,
    PersistentDisplaySelectorResolver selectorResolver,
    IDisplayNativeTopologyRollbackApplier topologyRollback,
    DisplayTargetApplyCoordinator targetApply,
    IControlSessionIdentity controlSessionIdentity) : IModeActionRollbackHandler
{
    public bool CanHandle(PersistedModeActionRecord action) => DisplayActionSemantics.CanHandle(action);

    public Task<RuntimeModeRollbackStepOutcome> RollbackAsync(
        ModeActionRollbackCommand command,
        PersistedModeActionRecord action,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = Validate(action);
        if (validation is not null)
            return Task.FromResult(new RuntimeModeRollbackStepOutcome("RECONCILIATION_REQUIRED", validation.Value.ProductCode));
        if (!AuthorityMatches(command.ControlSessionKey))
            return Task.FromResult(new RuntimeModeRollbackStepOutcome("RECONCILIATION_REQUIRED", "MODE_DISPLAY_CONTROL_CONTEXT_STALE"));

        try
        {
            return Task.FromResult(action.ActionType switch
            {
                DisplayModeActionContract.TopologyExtendActionType => RollbackTopology(command, action),
                DisplayModeActionContract.TargetModeActionType => RollbackTargetMode(command, action),
                _ => new RuntimeModeRollbackStepOutcome("RECONCILIATION_REQUIRED", "MODE_DISPLAY_ACTION_SEMANTICS_INVALID")
            });
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidDataException or InvalidOperationException)
        {
            return Task.FromResult(new RuntimeModeRollbackStepOutcome(
                "RECONCILIATION_REQUIRED",
                "MODE_DISPLAY_ROLLBACK_EVIDENCE_UNAVAILABLE"));
        }
    }

    private RuntimeModeRollbackStepOutcome RollbackTopology(
        ModeActionRollbackCommand command,
        PersistedModeActionRecord action)
    {
        var desired = DisplayActionSemantics.ReadTopologyDesired(action);
        var baseline = DisplayActionSemantics.ReadTopologyPreState(action);
        var before = snapshots.Read();
        if (before.Generation != generationTracker.CurrentGeneration)
            return Reconciliation("DISPLAY_STALE_SNAPSHOT");

        var resolved = selectorResolver.Resolve(desired.Selector, before);
        if (!resolved.IsResolved || resolved.Path is null || !resolved.Path.Active ||
            !resolved.Path.TargetAvailable || resolved.Path.Identity is null)
            return Reconciliation(resolved.ProductCode);

        var addedSelector = DisplayModeActionPreStateContract.SelectorFromIdentity(resolved.Path.Identity);
        IReadOnlyList<PersistentDisplaySelector> expected;
        try
        {
            expected = DisplayModeActionPreStateContract.Normalize(
                new DisplayTopologyPreState(baseline.ActiveTargets.Append(addedSelector).ToArray())).ActiveTargets;
        }
        catch (InvalidDataException)
        {
            return Reconciliation("MODE_DISPLAY_ROLLBACK_TOPOLOGY_AMBIGUOUS");
        }

        var currentTargets = DisplayModeActionPreStateContract.CaptureActiveSelectors(before);
        if (!DisplayModeActionPreStateContract.TargetSetsEqual(expected, currentTargets))
            return Reconciliation("MODE_DISPLAY_ROLLBACK_TOPOLOGY_DRIFT");

        var baselinePaths = before.Paths
            .Where(path => path.Active && path.TargetKey != resolved.Path.TargetKey)
            .ToArray();
        if (baselinePaths.Length != baseline.ActiveTargets.Count || baselinePaths.Length == 0)
            return Reconciliation("MODE_DISPLAY_ROLLBACK_BASELINE_UNAVAILABLE");

        if (!AuthorityMatches(command.ControlSessionKey) || before.Generation != generationTracker.CurrentGeneration)
            return Reconciliation("MODE_DISPLAY_CONTROL_CONTEXT_STALE");

        var native = topologyRollback.ValidateAndApply(new ResolvedDisplayTopologyRollback(
            before.Generation,
            baselinePaths,
            resolved.Path));
        if (!native.IsApplied)
            return Reconciliation(native.ProductCode);

        var racedDuringMutation = generationTracker.CurrentGeneration != before.Generation;
        var expectedReadBackGeneration = generationTracker.Invalidate("durable display topology rollback applied");
        var after = snapshots.Read();
        if (racedDuringMutation || after.Generation != expectedReadBackGeneration)
            return Reconciliation("DISPLAY_STALE_SNAPSHOT");
        if (!AuthorityMatches(command.ControlSessionKey))
            return Reconciliation("MODE_DISPLAY_CONTROL_CONTEXT_STALE");

        var restoredTargets = DisplayModeActionPreStateContract.CaptureActiveSelectors(after);
        if (!DisplayModeActionPreStateContract.TargetSetsEqual(baseline.ActiveTargets, restoredTargets))
            return Reconciliation("MODE_DISPLAY_ROLLBACK_VERIFICATION_FAILED");

        return new RuntimeModeRollbackStepOutcome("VERIFIED", "MODE_DISPLAY_TOPOLOGY_ROLLBACK_VERIFIED");
    }

    private RuntimeModeRollbackStepOutcome RollbackTargetMode(
        ModeActionRollbackCommand command,
        PersistedModeActionRecord action)
    {
        _ = DisplayActionSemantics.ReadTargetModeDesired(action);
        var preState = DisplayActionSemantics.ReadTargetModePreState(action);
        var before = snapshots.Read();
        if (before.Generation != generationTracker.CurrentGeneration)
            return Reconciliation("DISPLAY_STALE_SNAPSHOT");

        var currentTargets = DisplayModeActionPreStateContract.CaptureActiveSelectors(before);
        if (!DisplayModeActionPreStateContract.TargetSetsEqual(preState.ActiveTargets, currentTargets))
            return Reconciliation("MODE_DISPLAY_ROLLBACK_TOPOLOGY_DRIFT");

        var resolved = selectorResolver.Resolve(preState.Selector, before);
        if (!resolved.IsResolved || resolved.Path is null || !resolved.Path.Active || !resolved.Path.TargetAvailable)
            return Reconciliation(resolved.ProductCode);
        if (!AuthorityMatches(command.ControlSessionKey) || before.Generation != generationTracker.CurrentGeneration)
            return Reconciliation("MODE_DISPLAY_CONTROL_CONTEXT_STALE");

        var outcome = targetApply.Apply(new ResolvedDisplayTarget(
            resolved.Path.TargetKey,
            before.Generation,
            new DisplayPixelSize(preState.Width, preState.Height),
            new DisplayRational(preState.RefreshNumerator, preState.RefreshDenominator),
            preState.Rotation,
            DisplayTopologyIntent.PreserveActiveTopology));
        if (!outcome.IsVerified)
            return Reconciliation(outcome.ProductCode);
        if (!AuthorityMatches(command.ControlSessionKey))
            return Reconciliation("MODE_DISPLAY_CONTROL_CONTEXT_STALE");

        var after = snapshots.Read();
        if (after.Generation != generationTracker.CurrentGeneration)
            return Reconciliation("DISPLAY_STALE_SNAPSHOT");
        var restoredTargets = DisplayModeActionPreStateContract.CaptureActiveSelectors(after);
        if (!DisplayModeActionPreStateContract.TargetSetsEqual(preState.ActiveTargets, restoredTargets))
            return Reconciliation("MODE_DISPLAY_ROLLBACK_TOPOLOGY_DRIFT");
        var restored = selectorResolver.Resolve(preState.Selector, after);
        if (!restored.IsResolved || restored.Path is null ||
            restored.Path.SourceResolution != new DisplayPixelSize(preState.Width, preState.Height) ||
            restored.Path.Rotation != preState.Rotation ||
            !RationalEquals(restored.Path.RefreshRate, new DisplayRational(preState.RefreshNumerator, preState.RefreshDenominator)))
            return Reconciliation("MODE_DISPLAY_ROLLBACK_VERIFICATION_FAILED");

        return new RuntimeModeRollbackStepOutcome("VERIFIED", "MODE_DISPLAY_MODE_ROLLBACK_VERIFIED");
    }

    private static (string ProductCode, string Detail)? Validate(PersistedModeActionRecord action)
    {
        if (!DisplayActionSemantics.CanHandle(action) || action.State != PersistedModeActionState.RollingBack)
            return ("MODE_DISPLAY_ROLLBACK_ACTION_INVALID", "Durable display rollback requires a supported ROLLING_BACK action.");
        if (string.IsNullOrWhiteSpace(action.PreStateJson) || string.IsNullOrWhiteSpace(action.PreStateDigest))
            return ("MODE_DISPLAY_PRESTATE_MISSING", "Durable display rollback has no captured pre-state evidence.");
        try
        {
            if (action.ActionType == DisplayModeActionContract.TopologyExtendActionType)
            {
                _ = DisplayActionSemantics.ReadTopologyDesired(action);
                _ = DisplayActionSemantics.ReadTopologyPreState(action);
            }
            else
            {
                _ = DisplayActionSemantics.ReadTargetModeDesired(action);
                _ = DisplayActionSemantics.ReadTargetModePreState(action);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidDataException)
        {
            return ("MODE_DISPLAY_ROLLBACK_EVIDENCE_INVALID", ex.Message);
        }
        return null;
    }

    private bool AuthorityMatches(string expected)
    {
        try
        {
            return string.Equals(controlSessionIdentity.GetCurrentKey(), expected, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static bool RationalEquals(DisplayRational? actual, DisplayRational expected)
        => actual.HasValue &&
           (ulong)actual.Value.Numerator * expected.Denominator ==
           (ulong)expected.Numerator * actual.Value.Denominator;

    private static RuntimeModeRollbackStepOutcome Reconciliation(string productCode)
        => new("RECONCILIATION_REQUIRED", productCode);
}
