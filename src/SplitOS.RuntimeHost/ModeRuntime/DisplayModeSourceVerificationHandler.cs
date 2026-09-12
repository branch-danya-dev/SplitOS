using System.Text.Json;
using SplitOS.Contracts.Protocol;
using SplitOS.Persistence.Machine;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.ModeRuntime;

/// <summary>
/// Independently proves the restored display source after reverse-order compensation. The verifier
/// reconstructs source expectations from durable per-action pre-state rather than trusting rollback
/// return codes alone, then compares them with one fresh Windows display snapshot.
/// </summary>
public sealed class DisplayModeSourceVerificationHandler(
    IModeActionPlanReader plans,
    IModeActionRecordReader actions,
    IDisplaySnapshotReader snapshots,
    IDisplayGenerationTracker generationTracker,
    PersistentDisplaySelectorResolver selectorResolver,
    IControlSessionIdentity controlSessionIdentity) : IModeSourceVerificationHandler
{
    public bool CanHandle(PersistedModeActionRecord action) => DisplayActionSemantics.CanHandle(action);

    public async Task<RuntimeModeRollbackStepOutcome> VerifySourceAsync(
        ModeSourceVerificationCommand command,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!AuthorityMatches(command.ControlSessionKey))
            return Reconciliation("MODE_SOURCE_DISPLAY_CONTROL_CONTEXT_STALE");

        var plan = await plans.GetAsync(command.TransitionId, cancellationToken).ConfigureAwait(false);
        if (plan is null || plan.TransitionId != command.TransitionId || plan.ActionCount != plan.Actions.Count)
            return new("UNAVAILABLE", "MODE_SOURCE_DISPLAY_PLAN_UNAVAILABLE");

        var displayPlanActions = plan.Actions
            .Where(DisplayActionSemantics.CanHandle)
            .OrderBy(static action => action.SequenceNo)
            .ToArray();
        if (displayPlanActions.Length == 0)
            return new("UNAVAILABLE", "MODE_SOURCE_DISPLAY_PLAN_UNAVAILABLE");

        await actions.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var records = new List<PersistedModeActionRecord>(displayPlanActions.Length);
        foreach (var planned in displayPlanActions)
        {
            var record = await actions.GetAsync(planned.ActionId, cancellationToken).ConfigureAwait(false);
            if (record is null || record.TransitionId != command.TransitionId ||
                record.ActionId != planned.ActionId || record.SequenceNo != planned.SequenceNo ||
                !DisplayActionSemantics.CanHandle(record) ||
                !string.Equals(record.ActionType, planned.ActionType, StringComparison.Ordinal))
            {
                return Reconciliation("MODE_SOURCE_DISPLAY_JOURNAL_MISMATCH");
            }

            if (record.State is PersistedModeActionState.Applying
                or PersistedModeActionState.RollingBack
                or PersistedModeActionState.RollbackFailed)
            {
                return Reconciliation("MODE_SOURCE_DISPLAY_ACTION_UNSETTLED");
            }

            var mutationEvidence = record.ApplyResultCode is "APPLIED" or "UNKNOWN";
            if (mutationEvidence &&
                (record.State != PersistedModeActionState.RolledBack ||
                 !string.Equals(record.RollbackResultCode, "ROLLED_BACK", StringComparison.Ordinal)))
            {
                return Reconciliation("MODE_SOURCE_DISPLAY_ROLLBACK_EVIDENCE_REQUIRED");
            }
            if (mutationEvidence &&
                (string.IsNullOrWhiteSpace(record.PreStateJson) || string.IsNullOrWhiteSpace(record.PreStateDigest)))
            {
                return Reconciliation("MODE_SOURCE_DISPLAY_PRESTATE_REQUIRED");
            }

            records.Add(record);
        }

        IReadOnlyList<PersistentDisplaySelector>? sourceTargets = null;
        var expectedModes = new Dictionary<string, DisplayTopologyTargetModePreState>(StringComparer.Ordinal);
        var observedPreState = false;
        foreach (var record in records)
        {
            if (string.IsNullOrWhiteSpace(record.PreStateJson) && string.IsNullOrWhiteSpace(record.PreStateDigest))
                continue;
            if (string.IsNullOrWhiteSpace(record.PreStateJson) || string.IsNullOrWhiteSpace(record.PreStateDigest))
                return Reconciliation("MODE_SOURCE_DISPLAY_PRESTATE_INVALID");

            try
            {
                if (record.ActionType == DisplayModeActionContract.TopologyExtendActionType)
                {
                    var preState = DisplayActionSemantics.ReadTopologyPreState(record);
                    sourceTargets ??= preState.ActiveTargets;
                    if (!ContainsSourceTargets(preState.ActiveTargets, sourceTargets))
                        return Reconciliation("MODE_SOURCE_DISPLAY_PRESTATE_CHAIN_INVALID");

                    foreach (var mode in preState.ActiveTargetModes)
                    {
                        var key = SelectorKey(mode.Selector);
                        if (SourceContains(sourceTargets, key) && !expectedModes.ContainsKey(key))
                            expectedModes.Add(key, mode);
                    }
                }
                else if (record.ActionType == DisplayModeActionContract.TargetModeActionType)
                {
                    var preState = DisplayActionSemantics.ReadTargetModePreState(record);
                    sourceTargets ??= preState.ActiveTargets;
                    if (!ContainsSourceTargets(preState.ActiveTargets, sourceTargets))
                        return Reconciliation("MODE_SOURCE_DISPLAY_PRESTATE_CHAIN_INVALID");

                    var key = SelectorKey(preState.Selector);
                    if (SourceContains(sourceTargets, key) && !expectedModes.ContainsKey(key))
                    {
                        expectedModes.Add(key, new DisplayTopologyTargetModePreState(
                            preState.Selector,
                            preState.Width,
                            preState.Height,
                            preState.RefreshNumerator,
                            preState.RefreshDenominator,
                            preState.Rotation));
                    }
                }
                else
                {
                    return Reconciliation("MODE_SOURCE_DISPLAY_ACTION_SEMANTICS_INVALID");
                }
            }
            catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidDataException)
            {
                return Reconciliation("MODE_SOURCE_DISPLAY_PRESTATE_INVALID");
            }

            observedPreState = true;
        }

        if (!observedPreState || sourceTargets is null)
        {
            // No display action crossed BeginApply, therefore this domain had no Windows display
            // mutation boundary to restore. Any action claiming mutation evidence was rejected above.
            return AuthorityMatches(command.ControlSessionKey)
                ? new RuntimeModeRollbackStepOutcome("VERIFIED", "MODE_SOURCE_DISPLAY_NO_MUTATION")
                : Reconciliation("MODE_SOURCE_DISPLAY_CONTROL_CONTEXT_STALE");
        }

        DisplaySnapshot current;
        try
        {
            current = snapshots.Read();
        }
        catch
        {
            return Reconciliation("MODE_SOURCE_DISPLAY_EVIDENCE_UNAVAILABLE");
        }

        if (current.Generation != generationTracker.CurrentGeneration)
            return Reconciliation("MODE_SOURCE_DISPLAY_STALE_SNAPSHOT");
        if (!AuthorityMatches(command.ControlSessionKey))
            return Reconciliation("MODE_SOURCE_DISPLAY_CONTROL_CONTEXT_STALE");

        IReadOnlyList<PersistentDisplaySelector> currentTargets;
        try
        {
            currentTargets = DisplayModeActionPreStateContract.CaptureActiveSelectors(current);
        }
        catch
        {
            return Reconciliation("MODE_SOURCE_DISPLAY_EVIDENCE_UNAVAILABLE");
        }

        if (!DisplayModeActionPreStateContract.TargetSetsEqual(sourceTargets, currentTargets))
            return new("MISMATCH", "MODE_SOURCE_DISPLAY_TOPOLOGY_MISMATCH");

        foreach (var expected in expectedModes.Values)
        {
            var resolved = selectorResolver.Resolve(expected.Selector, current);
            if (!resolved.IsResolved || resolved.Path is null || !resolved.Path.Active || !resolved.Path.TargetAvailable)
                return new("MISMATCH", "MODE_SOURCE_DISPLAY_TARGET_MISMATCH");
            if (!ModeMatches(resolved.Path, expected))
                return new("MISMATCH", "MODE_SOURCE_DISPLAY_MODE_MISMATCH");
        }

        if (current.Generation != generationTracker.CurrentGeneration)
            return Reconciliation("MODE_SOURCE_DISPLAY_STALE_SNAPSHOT");
        if (!AuthorityMatches(command.ControlSessionKey))
            return Reconciliation("MODE_SOURCE_DISPLAY_CONTROL_CONTEXT_STALE");

        return new("VERIFIED", "MODE_SOURCE_DISPLAY_VERIFIED");
    }

    private static bool ContainsSourceTargets(
        IReadOnlyList<PersistentDisplaySelector> candidate,
        IReadOnlyList<PersistentDisplaySelector> source)
    {
        var candidateKeys = candidate.Select(SelectorKey).ToHashSet(StringComparer.Ordinal);
        return source.Select(SelectorKey).All(candidateKeys.Contains);
    }

    private static bool SourceContains(IReadOnlyList<PersistentDisplaySelector> source, string key)
        => source.Select(SelectorKey).Contains(key, StringComparer.Ordinal);

    private static string SelectorKey(PersistentDisplaySelector selector)
        => JsonSerializer.Serialize(DisplayModeActionContract.NormalizeSelector(selector), ProtocolJson.Options);

    private static bool ModeMatches(DisplayPathEvidence path, DisplayTopologyTargetModePreState expected)
        => path.SourceResolution == new DisplayPixelSize(expected.Width, expected.Height) &&
           path.Rotation == expected.Rotation &&
           RationalEquals(path.RefreshRate, new DisplayRational(expected.RefreshNumerator, expected.RefreshDenominator));

    private static bool RationalEquals(DisplayRational? actual, DisplayRational expected)
        => actual.HasValue &&
           (ulong)actual.Value.Numerator * expected.Denominator ==
           (ulong)expected.Numerator * actual.Value.Denominator;

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

    private static RuntimeModeRollbackStepOutcome Reconciliation(string productCode)
        => new("RECONCILIATION_REQUIRED", productCode);
}
