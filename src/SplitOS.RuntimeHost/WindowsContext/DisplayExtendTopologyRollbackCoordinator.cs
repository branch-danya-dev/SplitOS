namespace SplitOS.RuntimeHost.WindowsContext;

public sealed record DisplayExtendTopologyRollbackRequest(
    DisplaySnapshot Baseline,
    DisplaySnapshot Extended,
    DisplayPathEvidence ExtendedTarget);

public sealed record ResolvedDisplayTopologyRollback(
    long SnapshotGeneration,
    IReadOnlyList<DisplayPathEvidence> BaselineActivePaths,
    DisplayPathEvidence ExtendedTarget);

public interface IDisplayNativeTopologyRollbackApplier
{
    DisplayNativeMutationOutcome ValidateAndApply(ResolvedDisplayTopologyRollback rollback);
}

public enum DisplayExtendTopologyRollbackDisposition
{
    RolledBackVerified,
    StaleSnapshot,
    TopologyDrift,
    OperationRejected,
    TechnicalFailure,
    VerificationFailed
}

public sealed record DisplayExtendTopologyRollbackOutcome(
    DisplayExtendTopologyRollbackDisposition Disposition,
    string ProductCode,
    DisplaySnapshot Baseline,
    DisplaySnapshot BeforeRollback,
    DisplaySnapshot? AfterRollback = null,
    int? NativeErrorCode = null,
    string? Detail = null)
{
    public bool IsVerified => Disposition == DisplayExtendTopologyRollbackDisposition.RolledBackVerified;
}

public sealed class DisplayExtendTopologyRollbackCoordinator(
    IDisplaySnapshotReader snapshotReader,
    IDisplayGenerationTracker generationTracker,
    IDisplayNativeTopologyRollbackApplier nativeRollbackApplier)
{
    public DisplayExtendTopologyRollbackOutcome Rollback(DisplayExtendTopologyRollbackRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Baseline);
        ArgumentNullException.ThrowIfNull(request.Extended);
        ArgumentNullException.ThrowIfNull(request.ExtendedTarget);

        ValidateRequest(request);

        var beforeRollback = snapshotReader.Read();
        if (beforeRollback.Generation != request.Extended.Generation ||
            generationTracker.CurrentGeneration != request.Extended.Generation)
        {
            return new DisplayExtendTopologyRollbackOutcome(
                DisplayExtendTopologyRollbackDisposition.StaleSnapshot,
                "DISPLAY_STALE_SNAPSHOT",
                request.Baseline,
                beforeRollback,
                Detail: "Display generation changed after verified EXTEND and before topology rollback.");
        }

        if (!TopologyMatchesVerifiedExtend(request.Baseline, request.Extended, request.ExtendedTarget) ||
            !SnapshotsHaveSameActiveConnections(request.Extended, beforeRollback))
        {
            return new DisplayExtendTopologyRollbackOutcome(
                DisplayExtendTopologyRollbackDisposition.TopologyDrift,
                "DISPLAY_ROLLBACK_TOPOLOGY_DRIFT",
                request.Baseline,
                beforeRollback,
                Detail: "Fresh pre-rollback topology no longer matches the verified EXTEND result; rollback will not overwrite external display changes.");
        }

        var resolved = new ResolvedDisplayTopologyRollback(
            request.Extended.Generation,
            request.Baseline.Paths.Where(static path => path.Active).ToArray(),
            request.ExtendedTarget);

        var native = nativeRollbackApplier.ValidateAndApply(resolved);
        if (!native.IsApplied)
            return MapNativeFailure(native, request.Baseline, beforeRollback);

        var racedDuringMutation = generationTracker.CurrentGeneration != request.Extended.Generation;
        var expectedReadBackGeneration = generationTracker.Invalidate("SetDisplayConfig EXTEND rollback applied");
        var afterRollback = snapshotReader.Read();

        if (racedDuringMutation || afterRollback.Generation != expectedReadBackGeneration)
        {
            return new DisplayExtendTopologyRollbackOutcome(
                DisplayExtendTopologyRollbackDisposition.StaleSnapshot,
                "DISPLAY_STALE_SNAPSHOT",
                request.Baseline,
                beforeRollback,
                afterRollback,
                Detail: "Display generation changed concurrently with rollback apply/read-back.");
        }

        if (!SnapshotsHaveSameActiveConnections(request.Baseline, afterRollback))
        {
            return new DisplayExtendTopologyRollbackOutcome(
                DisplayExtendTopologyRollbackDisposition.VerificationFailed,
                "DISPLAY_ROLLBACK_VERIFICATION_FAILED",
                request.Baseline,
                beforeRollback,
                afterRollback,
                Detail: "Fresh CCD read-back does not match the exact baseline active source-to-target connection set.");
        }

        return new DisplayExtendTopologyRollbackOutcome(
            DisplayExtendTopologyRollbackDisposition.RolledBackVerified,
            "DISPLAY_EXTEND_ROLLED_BACK_VERIFIED",
            request.Baseline,
            beforeRollback,
            afterRollback);
    }

    private static void ValidateRequest(DisplayExtendTopologyRollbackRequest request)
    {
        if (request.Baseline.Generation < 1 || request.Extended.Generation < 1)
            throw new ArgumentOutOfRangeException(nameof(request), "Display generations must be positive.");
        if (request.Extended.Generation <= request.Baseline.Generation)
            throw new ArgumentException("Verified EXTEND snapshot must be newer than its baseline snapshot.", nameof(request));
        if (!request.ExtendedTarget.Active || !request.ExtendedTarget.TargetAvailable)
            throw new ArgumentException("Rollback requires the verified active EXTEND target evidence.", nameof(request));
    }

    private static bool TopologyMatchesVerifiedExtend(
        DisplaySnapshot baseline,
        DisplaySnapshot extended,
        DisplayPathEvidence extendedTarget)
    {
        var baselineConnections = ActiveConnectionKeys(baseline);
        var extendedConnections = ActiveConnectionKeys(extended);
        var added = ConnectionKey(extendedTarget);

        if (baselineConnections.Contains(added))
            return false;

        var expected = baselineConnections.Append(added).OrderBy(static key => key).ToArray();
        return expected.SequenceEqual(extendedConnections);
    }

    private static bool SnapshotsHaveSameActiveConnections(DisplaySnapshot expected, DisplaySnapshot actual) =>
        ActiveConnectionKeys(expected).SequenceEqual(ActiveConnectionKeys(actual));

    private static string[] ActiveConnectionKeys(DisplaySnapshot snapshot) =>
        snapshot.Paths
            .Where(static path => path.Active)
            .Select(ConnectionKey)
            .OrderBy(static key => key)
            .ToArray();

    private static string ConnectionKey(DisplayPathEvidence path) =>
        FormattableString.Invariant($"{path.SourceAdapterLuid:X16}:{path.SourceId:X8}->{path.TargetKey.AdapterLuid:X16}:{path.TargetKey.TargetId:X8}");

    private static DisplayExtendTopologyRollbackOutcome MapNativeFailure(
        DisplayNativeMutationOutcome native,
        DisplaySnapshot baseline,
        DisplaySnapshot beforeRollback)
    {
        var disposition = native.Disposition switch
        {
            DisplayNativeMutationDisposition.ValidationRejected => DisplayExtendTopologyRollbackDisposition.OperationRejected,
            DisplayNativeMutationDisposition.TargetNotFound => DisplayExtendTopologyRollbackDisposition.TopologyDrift,
            DisplayNativeMutationDisposition.TargetUnavailable => DisplayExtendTopologyRollbackDisposition.TopologyDrift,
            DisplayNativeMutationDisposition.UnsupportedCapability => DisplayExtendTopologyRollbackDisposition.TechnicalFailure,
            DisplayNativeMutationDisposition.ApplyFailed => DisplayExtendTopologyRollbackDisposition.TechnicalFailure,
            _ => throw new InvalidOperationException($"Unexpected native rollback disposition {native.Disposition}.")
        };

        return new DisplayExtendTopologyRollbackOutcome(
            disposition,
            native.ProductCode,
            baseline,
            beforeRollback,
            NativeErrorCode: native.NativeErrorCode,
            Detail: native.Detail);
    }
}
