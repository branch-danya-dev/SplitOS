namespace SplitOS.RuntimeHost.WindowsContext;

public enum DisplayExtendCandidateDisposition
{
    Resolved,
    StaleTopology,
    TargetAmbiguous,
    TargetNotFound,
    TargetAlreadyActive,
    CandidateNotFound,
    CandidateAmbiguous
}

public sealed record DisplayExtendCandidateResolution(
    DisplayExtendCandidateDisposition Disposition,
    DisplayPathKey? TargetKey,
    DisplayConnectionCandidate? Candidate,
    string ProductCode,
    string? Detail = null)
{
    public bool IsResolved => Disposition == DisplayExtendCandidateDisposition.Resolved;
}

public sealed class DisplayExtendCandidateResolver(
    PersistentDisplaySelectorResolver selectorResolver)
{
    public DisplayExtendCandidateResolution Resolve(
        PersistentDisplaySelector selector,
        DisplaySnapshot activeSnapshot,
        DisplayConnectionCandidateSnapshot allPathsSnapshot)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(activeSnapshot);
        ArgumentNullException.ThrowIfNull(allPathsSnapshot);

        if (activeSnapshot.Generation != allPathsSnapshot.Generation)
        {
            return new DisplayExtendCandidateResolution(
                DisplayExtendCandidateDisposition.StaleTopology,
                null,
                null,
                "DISPLAY_EXTEND_TOPOLOGY_CHANGED",
                "Active topology and QDC_ALL_PATHS evidence were observed under different display generations.");
        }

        var physicalTargets = allPathsSnapshot.Candidates
            .Where(static candidate => candidate.Identity is not null)
            .Select(candidate => new DisplayTargetIdentityCandidate(candidate.TargetKey, candidate.Identity!));
        var targetResolution = selectorResolver.ResolveIdentity(selector, physicalTargets);

        if (!targetResolution.IsResolved || !targetResolution.TargetKey.HasValue)
        {
            return new DisplayExtendCandidateResolution(
                targetResolution.Disposition == DisplayTargetIdentityResolutionDisposition.Ambiguous
                    ? DisplayExtendCandidateDisposition.TargetAmbiguous
                    : DisplayExtendCandidateDisposition.TargetNotFound,
                null,
                null,
                targetResolution.ProductCode,
                targetResolution.Detail);
        }

        var targetKey = targetResolution.TargetKey.Value;
        if (activeSnapshot.Paths.Any(path => path.Active && path.TargetKey == targetKey) ||
            allPathsSnapshot.Candidates.Any(candidate => candidate.Active && candidate.TargetKey == targetKey))
        {
            return new DisplayExtendCandidateResolution(
                DisplayExtendCandidateDisposition.TargetAlreadyActive,
                targetKey,
                null,
                "DISPLAY_EXTEND_TARGET_ALREADY_ACTIVE",
                "The selected physical target is already part of the active topology; EXTEND must not activate a second path to it.");
        }

        var activeSources = activeSnapshot.Paths
            .Where(static path => path.Active)
            .Select(path => new DisplaySourceKey(path.SourceAdapterLuid, path.SourceId))
            .ToHashSet();

        var eligible = allPathsSnapshot.Candidates
            .Where(candidate => candidate.TargetKey == targetKey && !candidate.Active)
            .Where(candidate => !activeSources.Contains(new DisplaySourceKey(candidate.SourceAdapterLuid, candidate.SourceId)))
            .GroupBy(candidate => new DisplayConnectionKey(
                candidate.SourceAdapterLuid,
                candidate.SourceId,
                candidate.TargetKey))
            .Select(group => group.OrderBy(candidate => candidate.PriorityOrdinal).First())
            .OrderBy(candidate => candidate.PriorityOrdinal)
            .ToArray();

        if (eligible.Length == 0)
        {
            return new DisplayExtendCandidateResolution(
                DisplayExtendCandidateDisposition.CandidateNotFound,
                targetKey,
                null,
                "DISPLAY_EXTEND_CONNECTION_NOT_FOUND",
                "No inactive source-to-target path can be activated without reusing a source that is already active.");
        }

        if (eligible.Length > 1)
        {
            return new DisplayExtendCandidateResolution(
                DisplayExtendCandidateDisposition.CandidateAmbiguous,
                targetKey,
                null,
                "DISPLAY_EXTEND_CONNECTION_AMBIGUOUS",
                $"{eligible.Length} inactive source-to-target paths remain eligible; SplitOS refuses to treat Windows path priority as implicit user intent.");
        }

        return new DisplayExtendCandidateResolution(
            DisplayExtendCandidateDisposition.Resolved,
            targetKey,
            eligible[0],
            "DISPLAY_EXTEND_CONNECTION_RESOLVED");
    }

    private readonly record struct DisplaySourceKey(long AdapterLuid, uint SourceId);

    private readonly record struct DisplayConnectionKey(
        long SourceAdapterLuid,
        uint SourceId,
        DisplayPathKey TargetKey);
}
