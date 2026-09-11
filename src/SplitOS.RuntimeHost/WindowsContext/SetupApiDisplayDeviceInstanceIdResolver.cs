using System.Runtime.InteropServices;

namespace SplitOS.RuntimeHost.WindowsContext;

public interface IDisplayDeviceInstanceIdResolver
{
    string? Resolve(string monitorDevicePath);
}

public sealed class SetupApiDisplayDeviceInstanceIdResolver : IDisplayDeviceInstanceIdResolver
{
    private const int ErrorInsufficientBuffer = 122;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    public string? Resolve(string monitorDevicePath)
    {
        if (string.IsNullOrWhiteSpace(monitorDevicePath) || !OperatingSystem.IsWindows())
            return null;

        var deviceInfoSet = SetupDiCreateDeviceInfoList(IntPtr.Zero, IntPtr.Zero);
        if (deviceInfoSet == InvalidHandleValue)
            return null;

        try
        {
            var interfaceData = new SpDeviceInterfaceData
            {
                CbSize = checked((uint)Marshal.SizeOf<SpDeviceInterfaceData>())
            };

            if (!SetupDiOpenDeviceInterfaceW(deviceInfoSet, monitorDevicePath, 0, ref interfaceData))
                return null;

            var deviceInfoData = new SpDevinfoData
            {
                CbSize = checked((uint)Marshal.SizeOf<SpDevinfoData>())
            };

            // Microsoft documents this NULL-detail call as the supported way to obtain only
            // SP_DEVINFO_DATA. ERROR_INSUFFICIENT_BUFFER is expected while DeviceInfoData is filled.
            var detailSucceeded = SetupDiGetDeviceInterfaceDetailW(
                deviceInfoSet,
                ref interfaceData,
                IntPtr.Zero,
                0,
                out _,
                ref deviceInfoData);
            if (!detailSucceeded && Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
                return null;

            var sizeSucceeded = SetupDiGetDeviceInstanceIdW(
                deviceInfoSet,
                ref deviceInfoData,
                IntPtr.Zero,
                0,
                out var requiredCharacters);
            if (!sizeSucceeded && Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
                return null;
            if (requiredCharacters <= 1)
                return null;

            var buffer = Marshal.AllocHGlobal(checked((int)requiredCharacters * sizeof(char)));
            try
            {
                if (!SetupDiGetDeviceInstanceIdW(
                        deviceInfoSet,
                        ref deviceInfoData,
                        buffer,
                        requiredCharacters,
                        out _))
                    return null;

                var instanceId = Marshal.PtrToStringUni(buffer);
                return string.IsNullOrWhiteSpace(instanceId) ? null : instanceId.Trim();
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public uint CbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public UIntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevinfoData
    {
        public uint CbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public UIntPtr Reserved;
    }

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiCreateDeviceInfoList(IntPtr classGuid, IntPtr hwndParent);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiOpenDeviceInterfaceW(
        IntPtr deviceInfoSet,
        string devicePath,
        uint openFlags,
        ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetailW(
        IntPtr deviceInfoSet,
        ref SpDeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize,
        out uint requiredSize,
        ref SpDevinfoData deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInstanceIdW(
        IntPtr deviceInfoSet,
        ref SpDevinfoData deviceInfoData,
        IntPtr deviceInstanceId,
        uint deviceInstanceIdSize,
        out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
}
