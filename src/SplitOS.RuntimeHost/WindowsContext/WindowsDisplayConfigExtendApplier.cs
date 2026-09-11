using System.Runtime.InteropServices;

namespace SplitOS.RuntimeHost.WindowsContext;

public sealed class WindowsDisplayConfigExtendApplier : IDisplayNativeExtendApplier
{
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;
    private const uint QueryAllPaths = 0x00000001;
    private const uint QueryVirtualModeAware = 0x00000010;
    private const uint QueryVirtualRefreshRateAware = 0x00000040;
    private const uint PathActive = 0x00000001;
    private const uint PathBoostRefreshRate = 0x00000010;
    private const uint InvalidModeIndex = 0xffffffff;
    private const uint RotationIdentity = 1;
    private const uint ScalingPreferred = 128;
    private const uint ScanLineOrderingUnspecified = 0;
    private const uint SdcUseSuppliedDisplayConfig = 0x00000020;
    private const uint SdcValidate = 0x00000040;
    private const uint SdcApply = 0x00000080;
    private const uint SdcAllowChanges = 0x00000400;
    private const uint SdcVirtualModeAware = 0x00008000;
    private const uint SdcVirtualRefreshRateAware = 0x00020000;
    private const int MaxQueryAttempts = 4;

    private static bool VirtualRefreshAware => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    private static uint QueryFlags => QueryAllPaths |
                                      QueryVirtualModeAware |
                                      (VirtualRefreshAware ? QueryVirtualRefreshRateAware : 0u);

    private static uint CommonSetFlags => SdcUseSuppliedDisplayConfig |
                                          SdcAllowChanges |
                                          SdcVirtualModeAware |
                                          (VirtualRefreshAware ? SdcVirtualRefreshRateAware : 0u);

    public DisplayNativeMutationOutcome ValidateAndApply(ResolvedDisplayExtendConnection connection)
    {
        if (!TryQueryAll(out var paths, out var modes, out var queryError))
        {
            return new DisplayNativeMutationOutcome(
                DisplayNativeMutationDisposition.ApplyFailed,
                "DISPLAY_EXTEND_QUERY_FAILED",
                queryError,
                "Fresh QDC_ALL_PATHS query failed before EXTEND validation.");
        }

        var activePaths = paths
            .Select((path, index) => (Path: path, Index: index))
            .Where(item => (item.Path.Flags & PathActive) != 0)
            .ToArray();

        if (activePaths.Any(item =>
                new DisplayPathKey(ToInt64(item.Path.TargetInfo.AdapterId), item.Path.TargetInfo.Id) == connection.TargetKey))
        {
            return new DisplayNativeMutationOutcome(
                DisplayNativeMutationDisposition.TargetUnavailable,
                "DISPLAY_EXTEND_TARGET_ALREADY_ACTIVE",
                Detail: "The selected physical target became active before native EXTEND validation.");
        }

        if (activePaths.Any(item =>
                ToInt64(item.Path.SourceInfo.AdapterId) == connection.SourceAdapterLuid &&
                item.Path.SourceInfo.Id == connection.SourceId))
        {
            return new DisplayNativeMutationOutcome(
                DisplayNativeMutationDisposition.TargetUnavailable,
                "DISPLAY_EXTEND_SOURCE_ALREADY_ACTIVE",
                Detail: "The resolved source became occupied before native EXTEND validation.");
        }

        var matches = paths
            .Select((path, index) => (Path: path, Index: index))
            .Where(item =>
                (item.Path.Flags & PathActive) == 0 &&
                ToInt64(item.Path.SourceInfo.AdapterId) == connection.SourceAdapterLuid &&
                item.Path.SourceInfo.Id == connection.SourceId &&
                new DisplayPathKey(ToInt64(item.Path.TargetInfo.AdapterId), item.Path.TargetInfo.Id) == connection.TargetKey)
            .Take(2)
            .ToArray();

        if (matches.Length == 0)
        {
            return new DisplayNativeMutationOutcome(
                DisplayNativeMutationDisposition.TargetNotFound,
                "DISPLAY_EXTEND_CONNECTION_NOT_FOUND",
                Detail: "The previously resolved inactive source-to-target path is absent from the fresh native catalog.");
        }
        if (matches.Length > 1)
        {
            return new DisplayNativeMutationOutcome(
                DisplayNativeMutationDisposition.ApplyFailed,
                "DISPLAY_EXTEND_CONNECTION_DUPLICATE",
                Detail: "Fresh native QDC_ALL_PATHS evidence contains the resolved source-to-target path more than once.");
        }

        var matched = matches[0];
        if (matched.Index != connection.PriorityOrdinal)
        {
            return new DisplayNativeMutationOutcome(
                DisplayNativeMutationDisposition.TargetUnavailable,
                "DISPLAY_EXTEND_CONNECTION_CHANGED",
                Detail: "The resolved connection moved in Windows path-priority order before native validation; stale resolution was not reused.");
        }

        var newPath = matched.Path;
        newPath.SourceInfo.ModeInfoIdx = InvalidModeIndex;
        newPath.TargetInfo.ModeInfoIdx = InvalidModeIndex;
        newPath.TargetInfo.OutputTechnology = connection.OutputTechnology;
        newPath.TargetInfo.Rotation = RotationIdentity;
        newPath.TargetInfo.Scaling = ScalingPreferred;
        newPath.TargetInfo.RefreshRate = new DisplayConfigRational { Numerator = 0, Denominator = 0 };
        newPath.TargetInfo.ScanLineOrdering = ScanLineOrderingUnspecified;
        newPath.Flags = (newPath.Flags | PathActive) & ~PathBoostRefreshRate;

        // QDC_ALL_PATHS returns active paths first in path-priority order. Preserve that order and
        // append the single resolved inactive connection as the lowest-priority newly active path.
        var suppliedPaths = activePaths.Select(item => item.Path).Append(newPath).ToArray();

        var validationError = SetDisplayConfig(
            checked((uint)suppliedPaths.Length),
            suppliedPaths,
            checked((uint)modes.Length),
            modes,
            CommonSetFlags | SdcValidate);
        if (validationError != ErrorSuccess)
        {
            return new DisplayNativeMutationOutcome(
                DisplayNativeMutationDisposition.ValidationRejected,
                "DISPLAY_EXTEND_OPERATION_REJECTED",
                validationError,
                "SetDisplayConfig rejected the exact active-path set plus resolved inactive connection during SDC_VALIDATE.");
        }

        var applyError = SetDisplayConfig(
            checked((uint)suppliedPaths.Length),
            suppliedPaths,
            checked((uint)modes.Length),
            modes,
            CommonSetFlags | SdcApply);
        if (applyError != ErrorSuccess)
        {
            return new DisplayNativeMutationOutcome(
                DisplayNativeMutationDisposition.ApplyFailed,
                "DISPLAY_EXTEND_TECHNICAL_FAILURE",
                applyError,
                "SetDisplayConfig failed while applying the validated temporary EXTEND topology.");
        }

        return new DisplayNativeMutationOutcome(
            DisplayNativeMutationDisposition.Applied,
            "DISPLAY_EXTEND_NATIVE_APPLIED");
    }

    private static bool TryQueryAll(
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
