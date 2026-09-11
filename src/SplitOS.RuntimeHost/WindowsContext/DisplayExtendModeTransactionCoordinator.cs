namespace SplitOS.RuntimeHost.WindowsContext;

public sealed record DisplayExtendModeRequest(
    PersistentDisplaySelector Selector,
    long SnapshotGeneration,
    DisplayPixelSize Resolution,
    DisplayRational RefreshRate,
    uint Rotation);

public interface IDisplayExtendTransactionTopologyStage
{
    DisplayExtendApplyOutcome Apply(DisplayExtendRequest request);
}

public interface IDisplayExtendTransactionModeStage
{
    DisplayTargetApplyOutcome Apply(ResolvedDisplayTarget target);
}

public interface IDisplayExtendTransactionRollbackStage
{
    DisplayExtendTopologyRollbackOutcome Rollback(DisplayExtendTopologyRollbackRequest request);
}

public sealed class DisplayExtendTransactionTopologyStage(DisplayExtendApplyCoordinator inner)
    : IDisplayExtendTransactionTopologyStage
{
    public DisplayExtendApplyOutcome Apply(DisplayExtendRequest request) => inner.Apply(request);
}

public sealed class DisplayExtendTransactionModeStage(DisplayTargetApplyCoordinator inner)
    : IDisplayExtendTransactionModeStage
{
    public DisplayTargetApplyOutcome Apply(ResolvedDisplayTarget target) => inner.Apply(target);
}

public sealed class DisplayExtendTransactionRollbackStage(DisplayExtendTopologyRollbackCoordinator inner)
    : IDisplayExtendTransactionRollbackStage
{
    public DisplayExtendTopologyRollbackOutcome Rollback(DisplayExtendTopologyRollbackRequest request) => inner.Rollback(request);
}

public enum DisplayExtendModeDisposition
{
    AppliedVerified,
    ExtendFailed,
    ModeFailedRolledBack,
    RecoveryRequired
}

public sealed record DisplayExtendModeOutcome(
    DisplayExtendModeDisposition Disposition,
    string ProductCode,
    DisplayExtendApplyOutcome ExtendOutcome,
    DisplayTargetApplyOutcome? ModeOutcome = null,
    DisplayExtendTopologyRollbackOutcome? RollbackOutcome = null,
    string? Detail = null)
{
    public bool IsVerified => Disposition == DisplayExtendModeDisposition.AppliedVerified;
    public bool IsRecovered => Disposition == DisplayExtendModeDisposition.ModeFailedRolledBack;
}

public sealed class DisplayExtendModeTransactionCoordinator(
    IDisplayExtendTransactionTopologyStage topologyStage,
    IDisplayExtendTransactionModeStage modeStage,
    IDisplayExtendTransactionRollbackStage rollbackStage,
    IDisplayGenerationTracker generationTracker)
{
    public DisplayExtendModeOutcome Apply(DisplayExtendModeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Selector);
        ValidateRequest(request);

        var extend = topologyStage.Apply(new DisplayExtendRequest(
            request.Selector,
            request.SnapshotGeneration));

        if (!extend.IsVerified)
        {
            if (extend.After is not null)
            {
                return new DisplayExtendModeOutcome(
                    DisplayExtendModeDisposition.RecoveryRequired,
                    "DISPLAY_EXTEND_MODE_RECOVERY_REQUIRED",
                    extend,
                    Detail: $"EXTEND native mutation produced read-back evidence but was not verified ({extend.ProductCode}); no guessed compensation was attempted.");
            }

            return new DisplayExtendModeOutcome(
                DisplayExtendModeDisposition.ExtendFailed,
                extend.ProductCode,
                extend,
                Detail: extend.Detail);
        }

        if (extend.After is null || extend.ObservedTarget is null)
        {
            return new DisplayExtendModeOutcome(
                DisplayExtendModeDisposition.RecoveryRequired,
                "DISPLAY_EXTEND_MODE_RECOVERY_REQUIRED",
                extend,
                Detail: "Verified EXTEND outcome is missing mandatory after/target evidence required for the mode stage.");
        }

        var modeTarget = new ResolvedDisplayTarget(
            extend.ObservedTarget.TargetKey,
            extend.After.Generation,
            request.Resolution,
            request.RefreshRate,
            request.Rotation,
            DisplayTopologyIntent.PreserveActiveTopology);

        var mode = modeStage.Apply(modeTarget);
        if (mode.IsVerified)
        {
            return new DisplayExtendModeOutcome(
                DisplayExtendModeDisposition.AppliedVerified,
                "DISPLAY_EXTEND_MODE_APPLIED_VERIFIED",
                extend,
                mode,
                Detail: "EXTEND topology and requested mode were independently applied and verified.");
        }

        // A mode-stage failure may happen before native mutation or after a successful SetDisplayConfig
        // whose read-back failed. Use the current generation as a compensation fence, but let the
        // rollback coordinator independently prove that the active connection set still equals the
        // verified EXTEND topology before it removes the added target.
        var compensationGeneration = generationTracker.CurrentGeneration;
        var rollback = rollbackStage.Rollback(new DisplayExtendTopologyRollbackRequest(
            extend.Before,
            extend.After,
            extend.ObservedTarget,
            compensationGeneration));

        if (rollback.IsVerified)
        {
            return new DisplayExtendModeOutcome(
                DisplayExtendModeDisposition.ModeFailedRolledBack,
                "DISPLAY_EXTEND_MODE_FAILED_ROLLED_BACK",
                extend,
                mode,
                rollback,
                $"Mode stage failed with {mode.ProductCode}; the temporary EXTEND topology was restored to its verified baseline.");
        }

        return new DisplayExtendModeOutcome(
            DisplayExtendModeDisposition.RecoveryRequired,
            "DISPLAY_EXTEND_MODE_RECOVERY_REQUIRED",
            extend,
            mode,
            rollback,
            $"Mode stage failed with {mode.ProductCode} and compensation was not verified ({rollback.ProductCode}).");
    }

    private static void ValidateRequest(DisplayExtendModeRequest request)
    {
        if (request.SnapshotGeneration < 1)
            throw new ArgumentOutOfRangeException(nameof(request), "Display generation must be positive.");
        if (!request.Resolution.IsValid)
            throw new ArgumentOutOfRangeException(nameof(request), "Requested display resolution must be positive.");
        if (request.RefreshRate.Denominator == 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Requested refresh rate denominator must be non-zero.");
        if (request.Rotation is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(request), "Display rotation must be one of IDENTITY/90/180/270.");
    }
}
