using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SplitOS.RuntimeHost.WindowsContext;

public readonly record struct DisplayConfigBufferSizingResult(
    int ErrorCode,
    uint PathCount,
    uint ModeCount);

public sealed record DisplayConfigQueryAttempt(
    int ErrorCode,
    IReadOnlyList<DisplayPathEvidence> Paths);

public interface IWindowsDisplayConfigInterop
{
    DisplayConfigBufferSizingResult GetActiveBufferSizes();
    DisplayConfigQueryAttempt QueryActive(uint pathCapacity, uint modeCapacity);
}

public sealed class WindowsDisplayConfigQuery(
    IWindowsDisplayConfigInterop interop) : IDisplayConfigQuery
{
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;
    private const int MaxQueryAttempts = 4;

    public IReadOnlyList<DisplayPathEvidence> QueryActivePaths()
    {
        for (var attempt = 0; attempt < MaxQueryAttempts; attempt++)
        {
            var sizing = interop.GetActiveBufferSizes();
            if (sizing.ErrorCode == ErrorInsufficientBuffer)
                continue;
            if (sizing.ErrorCode != ErrorSuccess)
                throw new Win32Exception(sizing.ErrorCode, "GetDisplayConfigBufferSizes failed.");

            var query = interop.QueryActive(sizing.PathCount, sizing.ModeCount);
            if (query.ErrorCode == ErrorInsufficientBuffer)
                continue;
            if (query.ErrorCode != ErrorSuccess)
                throw new Win32Exception(query.ErrorCode, "QueryDisplayConfig failed.");

            return query.Paths.ToArray();
        }

        throw new InvalidOperationException(
            "Display topology kept changing while QueryDisplayConfig buffers were being sized; no stale snapshot was returned.");
    }
}

public sealed class WindowsDisplayConfigInterop : IWindowsDisplayConfigInterop
{
    private const uint QueryOnlyActivePaths = 0x00000002;
    private const uint QueryVirtualModeAware = 0x00000010;
    private const uint QueryVirtualRefreshRateAware = 0x00000040;
    private const uint PathActive = 0x00000001;
    private const uint PathSupportsVirtualMode = 0x00000008;
    private const uint PathBoostRefreshRate = 0x00000010;
    private const uint InvalidModeIndex = 0xffffffff;
    private const uint InvalidVirtualModeIndex = 0xffff;
    private const uint ModeInfoTypeSource = 1;

    private static uint QueryFlags => QueryOnlyActivePaths |
                                      QueryVirtualModeAware |
                                      (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
                                          ? QueryVirtualRefreshRateAware
                                          : 0u);

    public DisplayConfigBufferSizingResult GetActiveBufferSizes()
    {
        var error = GetDisplayConfigBufferSizes(
            QueryFlags,
            out var pathCount,
            out var modeCount);
        return new DisplayConfigBufferSizingResult(error, pathCount, modeCount);
    }

    public DisplayConfigQueryAttempt QueryActive(uint pathCapacity, uint modeCapacity)
    {
        var paths = new DisplayConfigPathInfo[checked((int)pathCapacity)];
        var modes = new DisplayConfigModeInfo[checked((int)modeCapacity)];
        var pathCount = pathCapacity;
        var modeCount = modeCapacity;

        var error = QueryDisplayConfig(
            QueryFlags,
            ref pathCount,
            paths,
            ref modeCount,
            modes,
            IntPtr.Zero);

        if (error != 0)
            return new DisplayConfigQueryAttempt(error, Array.Empty<DisplayPathEvidence>());

        var actualPathCount = Math.Min(checked((int)pathCount), paths.Length);
        var actualModeCount = Math.Min(checked((int)modeCount), modes.Length);
        var evidence = new DisplayPathEvidence[actualPathCount];
        for (var index = 0; index < actualPathCount; index++)
        {
            var path = paths[index];
            DisplayRational? refresh = path.TargetInfo.RefreshRate.Denominator == 0
                ? null
                : new DisplayRational(
                    path.TargetInfo.RefreshRate.Numerator,
                    path.TargetInfo.RefreshRate.Denominator);

            DisplayPixelSize? sourceResolution = null;
            DisplayDesktopPoint? sourcePosition = null;
            var supportsVirtualMode = (path.Flags & PathSupportsVirtualMode) != 0;
            var sourceModeIndex = GetSourceModeIndex(path.SourceInfo.ModeInfoIdx, supportsVirtualMode);
            if (sourceModeIndex.HasValue && sourceModeIndex.Value < (uint)actualModeCount)
            {
                var sourceModeInfo = modes[checked((int)sourceModeIndex.Value)];
                if (sourceModeInfo.InfoType == ModeInfoTypeSource)
                {
                    sourceResolution = new DisplayPixelSize(
                        sourceModeInfo.ModeInfo.SourceMode.Width,
                        sourceModeInfo.ModeInfo.SourceMode.Height);
                    sourcePosition = new DisplayDesktopPoint(
                        sourceModeInfo.ModeInfo.SourceMode.Position.X,
                        sourceModeInfo.ModeInfo.SourceMode.Position.Y);
                }
            }

            evidence[index] = new DisplayPathEvidence(
                ToInt64(path.SourceInfo.AdapterId),
                path.SourceInfo.Id,
                new DisplayPathKey(ToInt64(path.TargetInfo.AdapterId), path.TargetInfo.Id),
                (path.Flags & PathActive) != 0,
                path.TargetInfo.TargetAvailable != 0,
                path.TargetInfo.OutputTechnology,
                path.TargetInfo.Rotation,
                path.TargetInfo.Scaling,
                refresh,
                sourceResolution,
                sourcePosition,
                supportsVirtualMode,
                (path.Flags & PathBoostRefreshRate) != 0);
        }

        return new DisplayConfigQueryAttempt(0, evidence);
    }

    private static uint? GetSourceModeIndex(uint packedModeInfo, bool supportsVirtualMode)
    {
        if (!supportsVirtualMode)
            return packedModeInfo == InvalidModeIndex ? null : packedModeInfo;

        var sourceModeIndex = (packedModeInfo >> 16) & 0xffff;
        return sourceModeIndex == InvalidVirtualModeIndex ? null : sourceModeIndex;
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
}
