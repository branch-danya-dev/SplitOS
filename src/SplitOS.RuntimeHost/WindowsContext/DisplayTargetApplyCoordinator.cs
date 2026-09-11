namespace SplitOS.RuntimeHost.WindowsContext;

public enum DisplayTopologyIntent
{
    PreserveActiveTopology,
    SelectedOnly,
    Extend
}

public sealed record ResolvedDisplayTarget(
    DisplayPathKey TargetKey,
    long SnapshotGeneration,
    DisplayPixelSize Resolution,
    DisplayRational RefreshRate,
    uint Rotation,
    DisplayTopologyIntent TopologyIntent = DisplayTopologyIntent.PreserveActiveTopology);

public enum DisplayNativeMutationDisposition
{
    Applied,
    TargetNotFound,
    TargetUnavailable,
    UnsupportedCapability,
    ValidationRejected,
    ApplyFailed
}

public sealed record DisplayNativeMutationOutcome(
    DisplayNativeMutationDisposition Disposition,
    string ProductCode,
    int? NativeErrorCode = null,
    string? Detail = null)
{
    public bool IsApplied => Disposition == DisplayNativeMutationDisposition.Applied;
}

public interface IDisplayNativeTargetApplier
{
    DisplayNativeMutationOutcome ValidateAndApply(ResolvedDisplayTarget target);
}

public enum DisplayTargetApplyDisposition
{
    AppliedVerified,
    StaleSnapshot,
    TargetNotFound,
    TargetUnavailable,
    UnsupportedCapability,
    OperationRejected,
    TechnicalFailure,
    VerificationFailed
}

public sealed record DisplayTargetApplyOutcome(
    DisplayTargetApplyDisposition Disposition,
    string ProductCode,
    DisplaySnapshot Before,
    DisplaySnapshot? After = null,
    DisplayPathEvidence? ObservedTarget = null,
    int? NativeErrorCode = null,
    string? Detail = null)
{
    public bool IsVerified => Disposition == DisplayTargetApplyDisposition.AppliedVerified;
}

public sealed class DisplayTargetApplyCoordinator(
    IDisplaySnapshotReader snapshotReader,
    IDisplayGenerationTracker generationTracker,
    IDisplayNativeTargetApplier nativeTargetApplier)
{
    public DisplayTargetApplyOutcome Apply(ResolvedDisplayTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        ValidateTarget(target);

        var before = snapshotReader.Read();
        if (before.Generation != target.SnapshotGeneration ||
            generationTracker.CurrentGeneration != target.SnapshotGeneration)
        {
            return new DisplayTargetApplyOutcome(
                DisplayTargetApplyDisposition.StaleSnapshot,
                "DISPLAY_STALE_SNAPSHOT",
                before,
                Detail: "The resolved display target generation no longer matches the current display generation.");
        }

        var beforeMatch = FindUniqueTarget(before, target.TargetKey);
        if (beforeMatch.Error is not null)
            return beforeMatch.Error;
        var source = beforeMatch.Path!;

        if (!source.TargetAvailable || !source.Active)
        {
            return new DisplayTargetApplyOutcome(
                DisplayTargetApplyDisposition.TargetUnavailable,
                "DISPLAY_TARGET_UNAVAILABLE",
                before,
                ObservedTarget: source,
                Detail: "The resolved target is not both active and available in the fresh pre-apply snapshot.");
        }

        if (target.TopologyIntent == DisplayTopologyIntent.Extend)
        {
            return new DisplayTargetApplyOutcome(
                DisplayTargetApplyDisposition.UnsupportedCapability,
                "DISPLAY_EXTEND_REQUIRES_PERSISTENT_SELECTOR",
                before,
                ObservedTarget: source,
                Detail: "EXTEND is deferred until inactive-path discovery can be bound to a persistent display selector without guessing a connection path.");
        }

        if (source.BoostRefreshRate)
        {
            return new DisplayTargetApplyOutcome(
                DisplayTargetApplyDisposition.UnsupportedCapability,
                "DISPLAY_DYNAMIC_REFRESH_MUTATION_UNSUPPORTED",
                before,
                ObservedTarget: source,
                Detail: "Dynamic/boost refresh is active; mutation is refused until physical/virtual refresh semantics are release-validated.");
        }

        if (generationTracker.CurrentGeneration != target.SnapshotGeneration)
        {
            return new DisplayTargetApplyOutcome(
                DisplayTargetApplyDisposition.StaleSnapshot,
                "DISPLAY_STALE_SNAPSHOT",
                before,
                ObservedTarget: source,
                Detail: "Display generation changed after pre-apply resolution and before native validation.");
        }

        var native = nativeTargetApplier.ValidateAndApply(target);
        if (!native.IsApplied)
            return MapNativeFailure(native, before, source);

        // A successful SetDisplayConfig changes observable display state even if Windows notification
        // delivery is delayed. Advance our generation synchronously so pre-apply resolution cannot be reused.
        var racedDuringMutation = generationTracker.CurrentGeneration != target.SnapshotGeneration;
        var expectedReadBackGeneration = generationTracker.Invalidate("SetDisplayConfig applied");
        var after = snapshotReader.Read();
        var afterMatch = FindUniqueTarget(after, target.TargetKey, before);

        if (racedDuringMutation || after.Generation != expectedReadBackGeneration)
        {
            return new DisplayTargetApplyOutcome(
                DisplayTargetApplyDisposition.StaleSnapshot,
                "DISPLAY_STALE_SNAPSHOT",
                before,
                after,
                afterMatch.Path,
                Detail: "Display generation changed concurrently with apply/read-back; the resolved target cannot be trusted.");
        }

        if (afterMatch.Error is not null)
        {
            return new DisplayTargetApplyOutcome(
                afterMatch.Error.Disposition == DisplayTargetApplyDisposition.TargetNotFound
                    ? DisplayTargetApplyDisposition.VerificationFailed
                    : afterMatch.Error.Disposition,
                afterMatch.Error.Disposition == DisplayTargetApplyDisposition.TargetNotFound
                    ? "DISPLAY_VERIFICATION_FAILED"
                    : afterMatch.Error.ProductCode,
                before,
                after,
                afterMatch.Path,
                Detail: afterMatch.Error.Detail);
        }

        var observed = afterMatch.Path!;
        if (!observed.Active || !observed.TargetAvailable ||
            observed.SourceResolution != target.Resolution ||
            observed.Rotation != target.Rotation ||
            !RationalEquals(observed.RefreshRate, target.RefreshRate) ||
            !TopologyMatches(target, before, after))
        {
            return new DisplayTargetApplyOutcome(
                DisplayTargetApplyDisposition.VerificationFailed,
                "DISPLAY_VERIFICATION_FAILED",
                before,
                after,
                observed,
                Detail: "Fresh CCD read-back does not match one or more mandatory display mode/topology conditions.");
        }

        return new DisplayTargetApplyOutcome(
            DisplayTargetApplyDisposition.AppliedVerified,
            "DISPLAY_APPLIED_VERIFIED",
            before,
            after,
            observed);
    }

    private static DisplayTargetApplyOutcome MapNativeFailure(
        DisplayNativeMutationOutcome native,
        DisplaySnapshot before,
        DisplayPathEvidence observed) => native.Disposition switch
    {
        DisplayNativeMutationDisposition.TargetNotFound => new(
            DisplayTargetApplyDisposition.TargetNotFound, native.ProductCode, before,
            ObservedTarget: observed, NativeErrorCode: native.NativeErrorCode, Detail: native.Detail),
        DisplayNativeMutationDisposition.TargetUnavailable => new(
            DisplayTargetApplyDisposition.TargetUnavailable, native.ProductCode, before,
            ObservedTarget: observed, NativeErrorCode: native.NativeErrorCode, Detail: native.Detail),
        DisplayNativeMutationDisposition.UnsupportedCapability => new(
            DisplayTargetApplyDisposition.UnsupportedCapability, native.ProductCode, before,
            ObservedTarget: observed, NativeErrorCode: native.NativeErrorCode, Detail: native.Detail),
        DisplayNativeMutationDisposition.ValidationRejected => new(
            DisplayTargetApplyDisposition.OperationRejected, native.ProductCode, before,
            ObservedTarget: observed, NativeErrorCode: native.NativeErrorCode, Detail: native.Detail),
        DisplayNativeMutationDisposition.ApplyFailed => new(
            DisplayTargetApplyDisposition.TechnicalFailure, native.ProductCode, before,
            ObservedTarget: observed, NativeErrorCode: native.NativeErrorCode, Detail: native.Detail),
        _ => throw new InvalidOperationException($"Unexpected native display mutation disposition {native.Disposition}.")
    };

    private static (DisplayPathEvidence? Path, DisplayTargetApplyOutcome? Error) FindUniqueTarget(
        DisplaySnapshot snapshot,
        DisplayPathKey key,
        DisplaySnapshot? before = null)
    {
        var matches = snapshot.Paths.Where(path => path.TargetKey == key).Take(2).ToArray();
        if (matches.Length == 1)
            return (matches[0], null);

        var baseline = before ?? snapshot;
        return (null, new DisplayTargetApplyOutcome(
            matches.Length == 0
                ? DisplayTargetApplyDisposition.TargetNotFound
                : DisplayTargetApplyDisposition.TechnicalFailure,
            matches.Length == 0
                ? "DISPLAY_TARGET_NOT_FOUND"
                : "DISPLAY_DUPLICATE_CURRENT_PATH_KEY",
            baseline,
            before is null ? null : snapshot,
            Detail: matches.Length == 0
                ? "The resolved adapterLuid+targetId is absent from the fresh CCD snapshot."
                : "Fresh CCD evidence contains a duplicate operation-local target key."));
    }

    private static bool TopologyMatches(
        ResolvedDisplayTarget target,
        DisplaySnapshot before,
        DisplaySnapshot after)
    {
        var beforeActive = before.Paths
            .Where(static path => path.Active)
            .Select(static path => path.TargetKey)
            .OrderBy(static key => key.AdapterLuid)
            .ThenBy(static key => key.TargetId)
            .ToArray();
        var afterActive = after.Paths
            .Where(static path => path.Active)
            .Select(static path => path.TargetKey)
            .OrderBy(static key => key.AdapterLuid)
            .ThenBy(static key => key.TargetId)
            .ToArray();

        return target.TopologyIntent switch
        {
            DisplayTopologyIntent.PreserveActiveTopology => beforeActive.SequenceEqual(afterActive),
            DisplayTopologyIntent.SelectedOnly =>
                afterActive.Length == 1 && afterActive[0] == target.TargetKey,
            DisplayTopologyIntent.Extend => false,
            _ => false
        };
    }

    private static bool RationalEquals(DisplayRational? actual, DisplayRational expected)
    {
        if (!actual.HasValue) return false;
        return (ulong)actual.Value.Numerator * expected.Denominator ==
               (ulong)expected.Numerator * actual.Value.Denominator;
    }

    private static void ValidateTarget(ResolvedDisplayTarget target)
    {
        if (target.SnapshotGeneration < 1)
            throw new ArgumentOutOfRangeException(nameof(target), "Display target generation must be positive.");
        if (!target.Resolution.IsValid)
            throw new ArgumentOutOfRangeException(nameof(target), "Display target resolution must be positive.");
        if (target.Rotation is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(target), "Display rotation must be one of IDENTITY/90/180/270.");
    }
}
