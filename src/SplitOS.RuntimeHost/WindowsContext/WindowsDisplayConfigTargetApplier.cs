using System.Runtime.InteropServices;

namespace SplitOS.RuntimeHost.WindowsContext;

public sealed class WindowsDisplayConfigTargetApplier : IDisplayNativeTargetApplier
{
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;
    private const uint QueryOnlyActivePaths = 0x00000002;
    private const uint QueryVirtualModeAware = 0x00000010;
    private const uint QueryVirtualRefreshRateAware = 0x00000040;
    private const uint PathSupportsVirtualMode = 0x00000008;
    private const uint PathBoostRefreshRate = 0x00000010;
    private const uint InvalidModeIndex = 0xffffffff;
    private const uint InvalidVirtualModeIndex = 0xffff;
    private const uint ModeInfoTypeSource = 1;
    private const uint SdcUseSuppliedDisplayConfig = 0x00000020;
    private const uint SdcValidate = 0x00000040;
    private const uint SdcApply = 0x00000080;
    private const uint SdcAllowChanges = 0x00000400;
    private const uint SdcVirtualModeAware = 0x00008000;
    private const uint SdcVirtualRefreshRateAware = 0x00020000;
    private const int MaxQueryAttempts = 4;

    private static bool VirtualRefreshAware => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    private static uint QueryFlags => QueryOnlyActivePaths |
                                      QueryVirtualModeAware |
                                      (VirtualRefreshAware ? QueryVirtualRefreshRateAware : 0u);

    private static uint CommonSetFlags => SdcUseSuppliedDisplayConfig |
                                          SdcAllowChanges |
                                          SdcVirtualModeAware |
                                          (VirtualRefreshAware ? SdcVirtualRefreshRateAware : 0u);

    public DisplayNativeMutationOutcome ValidateAndApply(ResolvedDisplayTarget target)
    {
        if (!TryQueryActive(out var paths, out var modes, out var queryError))
        {
            return new DisplayNativeMutationOutcome(
                DisplayNativeMutationDisposition.ApplyFailed,
                "DISPLAY_QUERY_FAILED",
                queryError,
                "Fresh native CCD query failed before display validation.");
        }

        if (paths.Any(static path => (path.Flags & PathBoostRefreshRate) != 0))
        {
            return new DisplayNativeMutationOutcome(
                DisplayNativeMutationDisposition.UnsupportedCapability,
                "DISPLAY_DYNAMIC_REFRESH_MUTATION_UNSUPPORTED",
                Detail: "At least one active path uses Windows dynamic/boost refresh; preserving physical/virtual refresh semantics is not release-validated yet.");
        }

        var matchingIndexes = paths
            .Select((path, index) => (path, index))
            .Where(item => new DisplayPathKey(ToInt64(item.path.TargetInfo.AdapterId), item.path.TargetInfo.Id) == target.TargetKey)
            .Select(item => item.index)
            .Take(2)
            .ToArray();

        if (matchingIndexes.Length == 0)
        {
            return new DisplayNativeMutationOutcome(
                DisplayNativeMutationDisposition.TargetNotFound,
                "DISPLAY_TARGET_NOT_FOUND");
        }
        if (matchingIndexes.Length > 1)
        {
            return new DisplayNativeMutationOutcome(
                DisplayNativeMutationDisposition.ApplyFailed,
                "DISPLAY_DUPLICATE_CURRENT_PATH_KEY",
                Detail: "Native CCD query returned duplicate adapterLuid+targetId entries.");
        }

        var pathIndex = matchingIndexes[0];
        var path = paths[pathIndex];
        if (path.TargetInfo.TargetAvailable == 0)
        {
            return new DisplayNativeMutationOutcome(
                DisplayNativeMutationDisposition.TargetUnavailable,
                "DISPLAY_TARGET_UNAVAILABLE");
        }

        var supportsVirtualMode = (path.Flags & PathSupportsVirtualMode) != 0;
        var sourceModeIndex = GetSourceModeIndex(path.SourceInfo.ModeInfoIdx, supportsVirtualMode);
        if (!sourceModeIndex.HasValue || sourceModeIndex.Value >= modes.Length ||
            modes[sourceModeIndex.Value].InfoType != ModeInfoTypeSource)
        {
            return new DisplayNativeMutationOutcome(
                DisplayNativeMutationDisposition.UnsupportedCapability,
                "DISPLAY_SOURCE_MODE_UNRESOLVED",
                Detail: "The active CCD path does not expose a usable source mode for resolution mutation.");
        }

        var sourceModeInfo = modes[sourceModeIndex.Value];
        sourceModeInfo.ModeInfo.SourceMode.Width = target.Resolution.Width;
        sourceModeInfo.ModeInfo.SourceMode.Height = target.Resolution.Height;
        modes[sourceModeIndex.Value] = sourceModeInfo;

        path.TargetInfo.Rotation = target.Rotation;
        path.TargetInfo.RefreshRate = new DisplayConfigRational
        {
            Numerator = target.RefreshRate.Numerator,
            Denominator = target.RefreshRate.Denominator
        };
        path.TargetInfo.ModeInfoIdx = InvalidateTargetModeIndex(path.TargetInfo.ModeInfoIdx, supportsVirtualMode);
        paths[pathIndex] = path;

        var validationError = SetDisplayConfig(
            checked((uint)paths.Length),
            paths,
            checked((uint)modes.Length),
            modes,
            CommonSetFlags | SdcValidate);
        if (validationError != ErrorSuccess)
        {
            return new DisplayNativeMutationOutcome(
                DisplayNativeMutationDisposition.ValidationRejected,
                "DISPLAY_OPERATION_REJECTED",
                validationError,
                "SetDisplayConfig rejected the supplied target during SDC_VALIDATE.");
        }

        var applyError = SetDisplayConfig(
            checked((uint)paths.Length),
            paths,
            checked((uint)modes.Length),
            modes,
            CommonSetFlags | SdcApply);
        if (applyError != ErrorSuccess)
        {
            return new DisplayNativeMutationOutcome(
                DisplayNativeMutationDisposition.ApplyFailed,
                "DISPLAY_TECHNICAL_FAILURE",
                applyError,
                "SetDisplayConfig failed while applying the validated temporary target.");
        }

        return new DisplayNativeMutationOutcome(
            DisplayNativeMutationDisposition.Applied,
            "DISPLAY_NATIVE_APPLIED");
    }

    private static bool TryQueryActive(
        out DisplayConfigPathInfo[] paths,
        out DisplayConfigModeInfo[] modes,
        out int error)
    {
        for (var attempt = 0; attempt < MaxQueryAttempts; attempt++)
        {
            error = GetDisplayConfigBufferSizes(QueryFlags, out var pathCount, out var modeCount);
            if (error == ErrorInsufficientBuffer)
                continue;
            if (error != ErrorSuccess)
            {
                paths = Array.Empty<DisplayConfigPathInfo>();
                modes = Array.Empty<DisplayConfigModeInfo>();
                return false;
            }

            var pathBuffer = new DisplayConfigPathInfo[checked((int)pathCount)];
            var modeBuffer = new DisplayConfigModeInfo[checked((int)modeCount)];
            var actualPathCount = pathCount;
            var actualModeCount = modeCount;
            error = QueryDisplayConfig(
                QueryFlags,
                ref actualPathCount,
                pathBuffer,
                ref actualModeCount,
                modeBuffer,
                IntPtr.Zero);
            if (error == ErrorInsufficientBuffer)
                continue;
            if (error != ErrorSuccess)
            {
                paths = Array.Empty<DisplayConfigPathInfo>();
                modes = Array.Empty<DisplayConfigModeInfo>();
                return false;
            }

            paths = pathBuffer.Take(checked((int)actualPathCount)).ToArray();
            modes = modeBuffer.Take(checked((int)actualModeCount)).ToArray();
            return true;
        }

        paths = Array.Empty<DisplayConfigPathInfo>();
        modes = Array.Empty<DisplayConfigModeInfo>();
        error = ErrorInsufficientBuffer;
        return false;
    }

    private static uint? GetSourceModeIndex(uint packedModeInfo, bool supportsVirtualMode)
    {
        if (!supportsVirtualMode)
            return packedModeInfo == InvalidModeIndex ? null : packedModeInfo;

        var sourceModeIndex = (packedModeInfo >> 16) & 0xffff;
        return sourceModeIndex == InvalidVirtualModeIndex ? null : sourceModeIndex;
    }

    private static uint InvalidateTargetModeIndex(uint packedModeInfo, bool supportsVirtualMode)
    {
        if (!supportsVirtualMode)
            return InvalidModeIndex;

        // Virtual-mode-aware target info packs desktopModeInfoIdx in the low word and
        // targetModeInfoIdx in the high word. Preserve desktop-image association while
        // asking Windows best-mode logic to resolve a target signal mode.
        return (packedModeInfo & 0x0000ffff) | 0xffff0000;
    }

    private static long ToInt64(DisplayConfigLuid value) =>
        unchecked(((long)value.HighPart << 32) | value.LowPart);

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigLuid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigRational
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigSourceMode
    {
        public uint Width;
        public uint Height;
        public uint PixelFormat;
        public DisplayConfigPoint Position;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathSourceInfo
    {
        public DisplayConfigLuid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathTargetInfo
    {
        public DisplayConfigLuid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public int OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public DisplayConfigRational RefreshRate;
        public uint ScanLineOrdering;
        public int TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathInfo
    {
        public DisplayConfigPathSourceInfo SourceInfo;
        public DisplayConfigPathTargetInfo TargetInfo;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Explicit, Size = 48)]
    private struct DisplayConfigModeInfoUnion
    {
        [FieldOffset(0)] public DisplayConfigSourceMode SourceMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigModeInfo
    {
        public uint InfoType;
        public uint Id;
        public DisplayConfigLuid AdapterId;
        public DisplayConfigModeInfoUnion ModeInfo;
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(
        uint flags,
        out uint numPathArrayElements,
        out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [Out] DisplayConfigPathInfo[] pathInfoArray,
        ref uint numModeInfoArrayElements,
        [Out] DisplayConfigModeInfo[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int SetDisplayConfig(
        uint numPathArrayElements,
        [In] DisplayConfigPathInfo[] pathArray,
        uint numModeInfoArrayElements,
        [In] DisplayConfigModeInfo[] modeInfoArray,
        uint flags);
}
