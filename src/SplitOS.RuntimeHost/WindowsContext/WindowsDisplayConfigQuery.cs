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

public sealed record DisplayConnectionCandidate(
    int PriorityOrdinal,
    long SourceAdapterLuid,
    uint SourceId,
    DisplayPathKey TargetKey,
    bool Active,
    DisplayTargetIdentityEvidence? Identity);

public sealed record DisplayConnectionQueryAttempt(
    int ErrorCode,
    IReadOnlyList<DisplayConnectionCandidate> Candidates);

public interface IWindowsDisplayConfigInterop
{
    DisplayConfigBufferSizingResult GetActiveBufferSizes();
    DisplayConfigQueryAttempt QueryActive(uint pathCapacity, uint modeCapacity);
}

public interface IWindowsDisplayConnectionInterop
{
    DisplayConfigBufferSizingResult GetAllPathBufferSizes();
    DisplayConnectionQueryAttempt QueryAll(uint pathCapacity, uint modeCapacity);
}

public interface IDisplayConnectionCandidateQuery
{
    IReadOnlyList<DisplayConnectionCandidate> QueryAllCandidates();
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

public sealed class WindowsDisplayConnectionCandidateQuery(
    IWindowsDisplayConnectionInterop interop) : IDisplayConnectionCandidateQuery
{
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;
    private const int MaxQueryAttempts = 4;

    public IReadOnlyList<DisplayConnectionCandidate> QueryAllCandidates()
    {
        for (var attempt = 0; attempt < MaxQueryAttempts; attempt++)
        {
            var sizing = interop.GetAllPathBufferSizes();
            if (sizing.ErrorCode == ErrorInsufficientBuffer)
                continue;
            if (sizing.ErrorCode != ErrorSuccess)
                throw new Win32Exception(sizing.ErrorCode, "GetDisplayConfigBufferSizes(QDC_ALL_PATHS) failed.");

            var query = interop.QueryAll(sizing.PathCount, sizing.ModeCount);
            if (query.ErrorCode == ErrorInsufficientBuffer)
                continue;
            if (query.ErrorCode != ErrorSuccess)
                throw new Win32Exception(query.ErrorCode, "QueryDisplayConfig(QDC_ALL_PATHS) failed.");

            return query.Candidates.ToArray();
        }

        throw new InvalidOperationException(
            "Display topology kept changing while QDC_ALL_PATHS buffers were being sized; no stale connection catalog was returned.");
    }
}

public sealed class WindowsDisplayConfigInterop(
    IDisplayDeviceInstanceIdResolver? deviceInstanceIdResolver = null) :
    IWindowsDisplayConfigInterop,
    IWindowsDisplayConnectionInterop
{
    private const uint QueryAllPaths = 0x00000001;
    private const uint QueryOnlyActivePaths = 0x00000002;
    private const uint QueryVirtualModeAware = 0x00000010;
    private const uint QueryVirtualRefreshRateAware = 0x00000040;
    private const uint PathActive = 0x00000001;
    private const uint PathSupportsVirtualMode = 0x00000008;
    private const uint PathBoostRefreshRate = 0x00000010;
    private const uint InvalidModeIndex = 0xffffffff;
    private const uint InvalidVirtualModeIndex = 0xffff;
    private const uint ModeInfoTypeSource = 1;
    private const int DeviceInfoGetTargetName = 2;
    private const uint TargetNameEdidIdsValid = 0x00000004;

    private static bool VirtualRefreshAware => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    private static uint ActiveQueryFlags => QueryOnlyActivePaths |
                                            QueryVirtualModeAware |
                                            (VirtualRefreshAware ? QueryVirtualRefreshRateAware : 0u);

    private static uint AllPathQueryFlags => QueryAllPaths |
                                             QueryVirtualModeAware |
                                             (VirtualRefreshAware ? QueryVirtualRefreshRateAware : 0u);

    public DisplayConfigBufferSizingResult GetActiveBufferSizes() => GetBufferSizes(ActiveQueryFlags);

    public DisplayConfigBufferSizingResult GetAllPathBufferSizes() => GetBufferSizes(AllPathQueryFlags);

    public DisplayConfigQueryAttempt QueryActive(uint pathCapacity, uint modeCapacity)
    {
        var native = QueryNative(ActiveQueryFlags, pathCapacity, modeCapacity);
        if (native.ErrorCode != 0)
            return new DisplayConfigQueryAttempt(native.ErrorCode, Array.Empty<DisplayPathEvidence>());

        var evidence = new DisplayPathEvidence[native.Paths.Length];
        for (var index = 0; index < native.Paths.Length; index++)
        {
            var path = native.Paths[index];
            DisplayRational? refresh = path.TargetInfo.RefreshRate.Denominator == 0
                ? null
                : new DisplayRational(
                    path.TargetInfo.RefreshRate.Numerator,
                    path.TargetInfo.RefreshRate.Denominator);

            DisplayPixelSize? sourceResolution = null;
            DisplayDesktopPoint? sourcePosition = null;
            var supportsVirtualMode = (path.Flags & PathSupportsVirtualMode) != 0;
            var sourceModeIndex = GetSourceModeIndex(path.SourceInfo.ModeInfoIdx, supportsVirtualMode);
            if (sourceModeIndex.HasValue && sourceModeIndex.Value < (uint)native.Modes.Length)
            {
                var sourceModeInfo = native.Modes[checked((int)sourceModeIndex.Value)];
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
                (path.Flags & PathBoostRefreshRate) != 0,
                ReadTargetIdentity(path.TargetInfo));
        }

        return new DisplayConfigQueryAttempt(0, evidence);
    }

    public DisplayConnectionQueryAttempt QueryAll(uint pathCapacity, uint modeCapacity)
    {
        var native = QueryNative(AllPathQueryFlags, pathCapacity, modeCapacity);
        if (native.ErrorCode != 0)
            return new DisplayConnectionQueryAttempt(native.ErrorCode, Array.Empty<DisplayConnectionCandidate>());

        var identityByTarget = new Dictionary<DisplayPathKey, DisplayTargetIdentityEvidence?>();
        var candidates = new DisplayConnectionCandidate[native.Paths.Length];
        for (var index = 0; index < native.Paths.Length; index++)
        {
            var path = native.Paths[index];
            var targetKey = new DisplayPathKey(ToInt64(path.TargetInfo.AdapterId), path.TargetInfo.Id);
            if (!identityByTarget.TryGetValue(targetKey, out var identity))
            {
                identity = ReadTargetIdentity(path.TargetInfo);
                identityByTarget.Add(targetKey, identity);
            }

            candidates[index] = new DisplayConnectionCandidate(
                PriorityOrdinal: index,
                SourceAdapterLuid: ToInt64(path.SourceInfo.AdapterId),
                SourceId: path.SourceInfo.Id,
                TargetKey: targetKey,
                Active: (path.Flags & PathActive) != 0,
                Identity: identity);
        }

        return new DisplayConnectionQueryAttempt(0, candidates);
    }

    private static DisplayConfigBufferSizingResult GetBufferSizes(uint flags)
    {
        var error = GetDisplayConfigBufferSizes(flags, out var pathCount, out var modeCount);
        return new DisplayConfigBufferSizingResult(error, pathCount, modeCount);
    }

    private static NativeQueryAttempt QueryNative(uint flags, uint pathCapacity, uint modeCapacity)
    {
        var paths = new DisplayConfigPathInfo[checked((int)pathCapacity)];
        var modes = new DisplayConfigModeInfo[checked((int)modeCapacity)];
        var pathCount = pathCapacity;
        var modeCount = modeCapacity;

        var error = QueryDisplayConfig(
            flags,
            ref pathCount,
            paths,
            ref modeCount,
            modes,
            IntPtr.Zero);
        if (error != 0)
            return new NativeQueryAttempt(error, Array.Empty<DisplayConfigPathInfo>(), Array.Empty<DisplayConfigModeInfo>());

        return new NativeQueryAttempt(
            0,
            paths.Take(checked((int)pathCount)).ToArray(),
            modes.Take(checked((int)modeCount)).ToArray());
    }

    private DisplayTargetIdentityEvidence? ReadTargetIdentity(DisplayConfigPathTargetInfo target)
    {
        var packet = new DisplayConfigTargetDeviceName
        {
            Header = new DisplayConfigDeviceInfoHeader
            {
                Type = DeviceInfoGetTargetName,
                Size = checked((uint)Marshal.SizeOf<DisplayConfigTargetDeviceName>()),
                AdapterId = target.AdapterId,
                Id = target.Id
            },
            MonitorFriendlyDeviceName = string.Empty,
            MonitorDevicePath = string.Empty
        };

        if (DisplayConfigGetDeviceInfo(ref packet) != 0)
            return null;

        var monitorDevicePath = Normalize(packet.MonitorDevicePath);
        var pnpDeviceInstanceId = monitorDevicePath is null
            ? null
            : Normalize(deviceInstanceIdResolver?.Resolve(monitorDevicePath));
        var edidValid = (packet.Flags & TargetNameEdidIdsValid) != 0;
        return new DisplayTargetIdentityEvidence(
            monitorDevicePath,
            Normalize(packet.MonitorFriendlyDeviceName),
            edidValid ? packet.EdidManufactureId : null,
            edidValid ? packet.EdidProductCodeId : null,
            packet.ConnectorInstance,
            packet.OutputTechnology,
            ToInt64(target.AdapterId),
            pnpDeviceInstanceId);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.TrimEnd('\0').Trim();

    private static uint? GetSourceModeIndex(uint packedModeInfo, bool supportsVirtualMode)
    {
        if (!supportsVirtualMode)
            return packedModeInfo == InvalidModeIndex ? null : packedModeInfo;

        var sourceModeIndex = (packedModeInfo >> 16) & 0xffff;
        return sourceModeIndex == InvalidVirtualModeIndex ? null : sourceModeIndex;
    }

    private static long ToInt64(DisplayConfigLuid value) =>
        unchecked(((long)value.HighPart << 32) | value.LowPart);

    private sealed record NativeQueryAttempt(
        int ErrorCode,
        DisplayConfigPathInfo[] Paths,
        DisplayConfigModeInfo[] Modes);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigDeviceInfoHeader
    {
        public int Type;
        public uint Size;
        public DisplayConfigLuid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigTargetDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;
        public uint Flags;
        public int OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string MonitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string MonitorDevicePath;
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
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigTargetDeviceName requestPacket);
}
