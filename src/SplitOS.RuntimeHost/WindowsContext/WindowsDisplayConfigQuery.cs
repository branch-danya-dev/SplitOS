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

    public DisplayConfigBufferSizingResult GetActiveBufferSizes()
    {
        var error = GetDisplayConfigBufferSizes(
            QueryOnlyActivePaths,
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
            QueryOnlyActivePaths,
            ref pathCount,
            paths,
            ref modeCount,
            modes,
            IntPtr.Zero);

        if (error != 0)
            return new DisplayConfigQueryAttempt(error, Array.Empty<DisplayPathEvidence>());

        var actualCount = Math.Min(checked((int)pathCount), paths.Length);
        var evidence = new DisplayPathEvidence[actualCount];
        for (var index = 0; index < actualCount; index++)
        {
            var path = paths[index];
            var refresh = path.TargetInfo.RefreshRate.Denominator == 0
                ? null
                : new DisplayRational(
                    path.TargetInfo.RefreshRate.Numerator,
                    path.TargetInfo.RefreshRate.Denominator);

            evidence[index] = new DisplayPathEvidence(
                ToInt64(path.SourceInfo.AdapterId),
                path.SourceInfo.Id,
                new DisplayPathKey(ToInt64(path.TargetInfo.AdapterId), path.TargetInfo.Id),
                (path.Flags & 0x00000001) != 0,
                path.TargetInfo.TargetAvailable != 0,
                path.TargetInfo.OutputTechnology,
                path.TargetInfo.Rotation,
                path.TargetInfo.Scaling,
                refresh);
        }

        return new DisplayConfigQueryAttempt(0, evidence);
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
