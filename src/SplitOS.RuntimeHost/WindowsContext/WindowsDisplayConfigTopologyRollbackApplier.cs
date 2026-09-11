using System.Runtime.InteropServices;

namespace SplitOS.RuntimeHost.WindowsContext;

public sealed class WindowsDisplayConfigTopologyRollbackApplier : IDisplayNativeTopologyRollbackApplier
{
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;
    private const uint QueryOnlyActivePaths = 0x00000002;
    private const uint QueryVirtualModeAware = 0x00000010;
    private const uint QueryVirtualRefreshRateAware = 0x00000040;
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

    public DisplayNativeMutationOutcome ValidateAndApply(ResolvedDisplayTopologyRollback rollback)
    {
        ArgumentNullException.ThrowIfNull(rollback);
        ArgumentNullException.ThrowIfNull(rollback.BaselineActivePaths);
        ArgumentNullException.ThrowIfNull(rollback.ExtendedTarget);

        if (!TryQueryActive(out var paths, out var modes, out var queryError))
        {
            return new DisplayNativeMutationOutcome(
                DisplayNativeMutationDisposition.ApplyFailed,
                "DISPLAY_ROLLBACK_QUERY_FAILED",
                queryError,
                "Fresh active CCD query failed before topology rollback validation.");
        }

        var baselineKeys = rollback.BaselineActivePaths.Select(ConnectionKey.FromEvidence).ToHashSet();
        var extendedKey = ConnectionKey.FromEvidence(rollback.ExtendedTarget);
        if (baselineKeys.Count != rollback.BaselineActivePaths.Count || baselineKeys.Contains(extendedKey))
        {
            return new DisplayNativeMutationOutcome(
                DisplayNativeMutationDisposition.ApplyFailed,
                "DISPLAY_ROLLBACK_INVALID_BASELINE",
                Detail: "Rollback evidence does not describe a unique baseline plus one distinct EXTEND connection.");
        }

        var current = paths.Select((path, index) => new CurrentPath(path, index, ConnectionKey.FromNative(path))).ToArray();
        var expected = baselineKeys.Append(extendedKey).ToHashSet();
        var currentKeys = current.Select(static item => item.Key).ToHashSet();

        if (currentKeys.Count != current.Length || !currentKeys.SetEquals(expected))
        {
            return new DisplayNativeMutationOutcome(
                DisplayNativeMutationDisposition.TargetUnavailable,
                "DISPLAY_ROLLBACK_TOPOLOGY_CHANGED",
                Detail: "Fresh active CCD topology no longer equals the verified baseline plus EXTEND connection.");
        }

        var extendedMatches = current.Where(item => item.Key == extendedKey).Take(2).ToArray();
        if (extendedMatches.Length != 1)
        {
            return new DisplayNativeMutationOutcome(
                DisplayNativeMutationDisposition.TargetNotFound,
                "DISPLAY_ROLLBACK_EXTENDED_CONNECTION_NOT_FOUND",
                Detail: "The exact EXTEND source-to-target connection is no longer uniquely active.");
        }

        var suppliedPaths = current
            .Where(item => item.Key != extendedKey)
            .Select(static item => item.Path)
            .ToArray();

        if (suppliedPaths.Length == 0)
        {
            return new DisplayNativeMutationOutcome(
                DisplayNativeMutationDisposition.UnsupportedCapability,
                "DISPLAY_ROLLBACK_EMPTY_BASELINE_UNSUPPORTED",
                Detail: "SplitOS will not validate a rollback that would intentionally leave the active display topology empty.");
        }

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
                "DISPLAY_ROLLBACK_OPERATION_REJECTED",
                validationError,
                "SetDisplayConfig rejected the exact baseline active path set during SDC_VALIDATE.");
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
                "DISPLAY_ROLLBACK_TECHNICAL_FAILURE",
                applyError,
                "SetDisplayConfig failed while restoring the validated baseline topology.");
        }

        return new DisplayNativeMutationOutcome(
            DisplayNativeMutationDisposition.Applied,
            "DISPLAY_ROLLBACK_NATIVE_APPLIED");
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

    private readonly record struct ConnectionKey(
        long SourceAdapterLuid,
        uint SourceId,
        long TargetAdapterLuid,
        uint TargetId)
    {
        public static ConnectionKey FromEvidence(DisplayPathEvidence path) => new(
            path.SourceAdapterLuid,
            path.SourceId,
            path.TargetKey.AdapterLuid,
            path.TargetKey.TargetId);

        public static ConnectionKey FromNative(DisplayConfigPathInfo path) => new(
            ToInt64(path.SourceInfo.AdapterId),
            path.SourceInfo.Id,
            ToInt64(path.TargetInfo.AdapterId),
            path.TargetInfo.Id);
    }

    private sealed record CurrentPath(DisplayConfigPathInfo Path, int Index, ConnectionKey Key);

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
