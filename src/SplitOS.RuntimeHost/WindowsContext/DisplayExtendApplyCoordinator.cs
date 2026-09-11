namespace SplitOS.RuntimeHost.WindowsContext;

public sealed record DisplayExtendRequest(
    PersistentDisplaySelector Selector,
    long SnapshotGeneration);

public sealed record ResolvedDisplayExtendConnection(
    long SnapshotGeneration,
    long SourceAdapterLuid,
    uint SourceId,
    DisplayPathKey TargetKey,
    int PriorityOrdinal,
    int OutputTechnology);

public interface IDisplayNativeExtendApplier
{
    DisplayNativeMutationOutcome ValidateAndApply(ResolvedDisplayExtendConnection connection);
}

public enum DisplayExtendApplyDisposition
{
    AppliedVerified,
    StaleSnapshot,
    TargetAmbiguous,
    TargetNotFound,
    TargetAlreadyActive,
    ConnectionNotFound,
    ConnectionAmbiguous,
    OperationRejected,
    UnsupportedCapability,
    TechnicalFailure,
    VerificationFailed
}

public sealed record DisplayExtendApplyOutcome(
    DisplayExtendApplyDisposition Disposition,
    string ProductCode,
    DisplaySnapshot Before,
    DisplayConnectionCandidateSnapshot CandidateSnapshot,
    DisplaySnapshot? After = null,
    DisplayPathEvidence? ObservedTarget = null,
    int? NativeErrorCode = null,
    string? Detail = null)
{
    public bool IsVerified => Disposition == DisplayExtendApplyDisposition.AppliedVerified;
}

public sealed class DisplayExtendApplyCoordinator(
    IDisplaySnapshotReader snapshotReader,
    IDisplayConnectionCandidateReader candidateReader,
    IDisplayGenerationTracker generationTracker,
    DisplayExtendCandidateResolver candidateResolver,
    IDisplayNativeExtendApplier nativeExtendApplier)
{
    public DisplayExtendApplyOutcome Apply(DisplayExtendRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Selector);
        if (request.SnapshotGeneration < 1)
            throw new ArgumentOutOfRangeException(nameof(request), "Display generation must be positive.");

        var before = snapshotReader.Read();
        var candidates = candidateReader.Read();
        if (before.Generation != request.SnapshotGeneration ||
            candidates.Generation != request.SnapshotGeneration ||
            generationTracker.CurrentGeneration != request.SnapshotGeneration)
        {
            return Stale(before, candidates,
                "Display generation changed before EXTEND connection resolution completed.");
        }

        var resolved = candidateResolver.Resolve(request.Selector, before, candidates);
        if (!resolved.IsResolved || resolved.Candidate is null || !resolved.TargetKey.HasValue)
            return MapResolutionFailure(resolved, before, candidates);

        if (resolved.Candidate.Identity is null)
        {
            return new DisplayExtendApplyOutcome(
                DisplayExtendApplyDisposition.TechnicalFailure,
                "DISPLAY_EXTEND_IDENTITY_EVIDENCE_LOST",
                before,
                candidates,
                Detail: "The resolved source-to-target connection lost the target identity evidence used to select the physical monitor.");
        }

        if (generationTracker.CurrentGeneration != request.SnapshotGeneration)
        {
            return Stale(before, candidates,
                "Display generation changed after EXTEND connection resolution and before native validation.");
        }

        var connection = new ResolvedDisplayExtendConnection(
            request.SnapshotGeneration,
            resolved.Candidate.SourceAdapterLuid,
            resolved.Candidate.SourceId,
            resolved.TargetKey.Value,
            resolved.Candidate.PriorityOrdinal,
            resolved.Candidate.Identity.OutputTechnology);

        var native = nativeExtendApplier.ValidateAndApply(connection);
        if (!native.IsApplied)
            return MapNativeFailure(native, before, candidates);

        var racedDuringMutation = generationTracker.CurrentGeneration != request.SnapshotGeneration;
        var expectedReadBackGeneration = generationTracker.Invalidate("SetDisplayConfig EXTEND applied");
        var after = snapshotReader.Read();
        var observed = after.Paths.Where(path => path.TargetKey == connection.TargetKey).Take(2).ToArray();

        if (racedDuringMutation || after.Generation != expectedReadBackGeneration)
        {
            return new DisplayExtendApplyOutcome(
                DisplayExtendApplyDisposition.StaleSnapshot,
                "DISPLAY_STALE_SNAPSHOT",
                before,
                candidates,
                after,
                observed.Length == 1 ? observed[0] : null,
                Detail: "Display generation changed concurrently with EXTEND apply/read-back.");
        }

        if (observed.Length != 1 ||
            !observed[0].Active ||
            !observed[0].TargetAvailable ||
            observed[0].SourceAdapterLuid != connection.SourceAdapterLuid ||
            observed[0].SourceId != connection.SourceId ||
            !TopologyIsExactExtension(before, after, connection.TargetKey))
        {
            return new DisplayExtendApplyOutcome(
                DisplayExtendApplyDisposition.VerificationFailed,
                "DISPLAY_EXTEND_VERIFICATION_FAILED",
                before,
                candidates,
                after,
                observed.Length == 1 ? observed[0] : null,
                Detail: "Fresh CCD read-back did not contain exactly the resolved EXTEND connection while preserving every previously active target.");
        }

        return new DisplayExtendApplyOutcome(
            DisplayExtendApplyDisposition.AppliedVerified,
            "DISPLAY_EXTEND_APPLIED_VERIFIED",
            before,
            candidates,
            after,
            observed[0]);
    }

    private static bool TopologyIsExactExtension(
        DisplaySnapshot before,
        DisplaySnapshot after,
        DisplayPathKey extendedTarget)
    {
        var expected = before.Paths
            .Where(static path => path.Active)
            .Select(static path => path.TargetKey)
            .Append(extendedTarget)
            .Distinct()
            .OrderBy(static key => key.AdapterLuid)
            .ThenBy(static key => key.TargetId)
            .ToArray();
        var actual = after.Paths
            .Where(static path => path.Active)
            .Select(static path => path.TargetKey)
            .OrderBy(static key => key.AdapterLuid)
            .ThenBy(static key => key.TargetId)
            .ToArray();

        return expected.SequenceEqual(actual);
    }

    private static DisplayExtendApplyOutcome MapResolutionFailure(
        DisplayExtendCandidateResolution resolution,
        DisplaySnapshot before,
        DisplayConnectionCandidateSnapshot candidates)
    {
        var disposition = resolution.Disposition switch
        {
            DisplayExtendCandidateDisposition.StaleTopology => DisplayExtendApplyDisposition.StaleSnapshot,
            DisplayExtendCandidateDisposition.TargetAmbiguous => DisplayExtendApplyDisposition.TargetAmbiguous,
            DisplayExtendCandidateDisposition.TargetNotFound => DisplayExtendApplyDisposition.TargetNotFound,
            DisplayExtendCandidateDisposition.TargetAlreadyActive => DisplayExtendApplyDisposition.TargetAlreadyActive,
            DisplayExtendCandidateDisposition.CandidateNotFound => DisplayExtendApplyDisposition.ConnectionNotFound,
            DisplayExtendCandidateDisposition.CandidateAmbiguous => DisplayExtendApplyDisposition.ConnectionAmbiguous,
            _ => throw new InvalidOperationException($"Unexpected EXTEND resolution disposition {resolution.Disposition}.")
        };

        return new DisplayExtendApplyOutcome(
            disposition,
            resolution.ProductCode,
            before,
            candidates,
            Detail: resolution.Detail);
    }

    private static DisplayExtendApplyOutcome MapNativeFailure(
        DisplayNativeMutationOutcome native,
        DisplaySnapshot before,
        DisplayConnectionCandidateSnapshot candidates)
    {
        var disposition = native.Disposition switch
        {
            DisplayNativeMutationDisposition.ValidationRejected => DisplayExtendApplyDisposition.OperationRejected,
            DisplayNativeMutationDisposition.UnsupportedCapability => DisplayExtendApplyDisposition.UnsupportedCapability,
            DisplayNativeMutationDisposition.TargetNotFound => DisplayExtendApplyDisposition.ConnectionNotFound,
            DisplayNativeMutationDisposition.TargetUnavailable => DisplayExtendApplyDisposition.ConnectionNotFound,
            DisplayNativeMutationDisposition.ApplyFailed => DisplayExtendApplyDisposition.TechnicalFailure,
            _ => throw new InvalidOperationException($"Unexpected native EXTEND disposition {native.Disposition}.")
        };

        return new DisplayExtendApplyOutcome(
            disposition,
            native.ProductCode,
            before,
            candidates,
            NativeErrorCode: native.NativeErrorCode,
            Detail: native.Detail);
    }

    private static DisplayExtendApplyOutcome Stale(
        DisplaySnapshot before,
        DisplayConnectionCandidateSnapshot candidates,
        string detail) => new(
        DisplayExtendApplyDisposition.StaleSnapshot,
        "DISPLAY_STALE_SNAPSHOT",
        before,
        candidates,
        Detail: detail);
}
