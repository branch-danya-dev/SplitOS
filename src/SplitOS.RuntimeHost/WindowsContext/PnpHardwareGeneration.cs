using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;

namespace SplitOS.RuntimeHost.WindowsContext;

public sealed record HardwareGenerationChange(
    long Generation,
    uint NativeAction,
    DateTimeOffset ObservedUtc);

public interface IHardwareGenerationTracker
{
    long CurrentGeneration { get; }
    HardwareGenerationChange? LastChange { get; }
    HardwareGenerationChange Invalidate(uint nativeAction);
}

/// <summary>
/// Process-local hardware freshness generation. It is deliberately not persisted: after RuntimeHost
/// restart live hardware must be freshly observed again.
/// </summary>
public sealed class HardwareGenerationTracker(TimeProvider? timeProvider = null) : IHardwareGenerationTracker
{
    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private long _generation = 1;
    private HardwareGenerationChange? _lastChange;

    public long CurrentGeneration
    {
        get
        {
            lock (_gate)
                return _generation;
        }
    }

    public HardwareGenerationChange? LastChange
    {
        get
        {
            lock (_gate)
                return _lastChange;
        }
    }

    public HardwareGenerationChange Invalidate(uint nativeAction)
    {
        lock (_gate)
        {
            var change = new HardwareGenerationChange(
                checked(++_generation),
                nativeAction,
                _timeProvider.GetUtcNow());
            _lastChange = change;
            return change;
        }
    }
}

public sealed class PnpHardwareGenerationInvalidator(IHardwareGenerationTracker generationTracker)
{
    public HardwareGenerationChange InvalidateForNativeAction(uint nativeAction)
        => generationTracker.Invalidate(nativeAction);
}

public delegate uint PnpHardwareNotificationCallback(
    IntPtr notificationHandle,
    IntPtr context,
    uint action,
    IntPtr eventData,
    uint eventDataSize);

public sealed record PnpNotificationRegistrationAttempt(
    uint ConfigRet,
    IntPtr NotificationHandle);

public interface IConfigurationManagerPnpInterop
{
    PnpNotificationRegistrationAttempt RegisterAllDeviceInstances(PnpHardwareNotificationCallback callback);
    uint Unregister(IntPtr notificationHandle);
}

public sealed class ConfigurationManagerNotificationException(
    string operation,
    uint configRet) : Exception($"{operation} failed with CONFIGRET 0x{configRet:X8}.")
{
    public uint ConfigRet { get; } = configRet;
}

/// <summary>
/// Native Configuration Manager adapter. Registration is intentionally broad at the device-instance
/// layer: any PnP instance change invalidates the generic hardware generation; higher-level adapters
/// decide which refreshed evidence matters to a mode or game-profile decision.
/// </summary>
public sealed class ConfigurationManagerPnpInterop : IConfigurationManagerPnpInterop
{
    private const uint CrSuccess = 0;
    private const uint NotifyFilterFlagAllDeviceInstances = 0x00000002;
    private const uint NotifyFilterTypeDeviceInstance = 2;
    private const int MaxDeviceIdLength = 200;

    public PnpNotificationRegistrationAttempt RegisterAllDeviceInstances(PnpHardwareNotificationCallback callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        var filter = new CmNotifyFilter
        {
            Size = checked((uint)Marshal.SizeOf<CmNotifyFilter>()),
            Flags = NotifyFilterFlagAllDeviceInstances,
            FilterType = NotifyFilterTypeDeviceInstance,
            Reserved = 0,
            DeviceInstance = new CmNotifyDeviceInstanceFilterData
            {
                InstanceId = string.Empty
            }
        };

        var configRet = CM_Register_Notification(
            ref filter,
            IntPtr.Zero,
            callback,
            out var notificationHandle);

        return new PnpNotificationRegistrationAttempt(configRet, notificationHandle);
    }

    public uint Unregister(IntPtr notificationHandle)
    {
        if (notificationHandle == IntPtr.Zero)
            throw new ArgumentException("PnP notification handle must not be zero.", nameof(notificationHandle));

        return CM_Unregister_Notification(notificationHandle);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CmNotifyFilter
    {
        public uint Size;
        public uint Flags;
        public uint FilterType;
        public uint Reserved;
        public CmNotifyDeviceInstanceFilterData DeviceInstance;
    }

    // CM_NOTIFY_FILTER contains a union. DeviceInstance is its largest member
    // (WCHAR InstanceId[MAX_DEVICE_ID_LEN]), so representing that member directly preserves the
    // native union storage size while this adapter uses only DEVICEINSTANCE filters.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CmNotifyDeviceInstanceFilterData
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxDeviceIdLength)]
        public string InstanceId;
    }

    [DllImport("CfgMgr32.dll", SetLastError = false)]
    private static extern uint CM_Register_Notification(
        ref CmNotifyFilter filter,
        IntPtr context,
        PnpHardwareNotificationCallback callback,
        out IntPtr notificationHandle);

    [DllImport("CfgMgr32.dll", SetLastError = false)]
    private static extern uint CM_Unregister_Notification(IntPtr notificationHandle);
}

public interface IPnpHardwareNotificationSource : IDisposable
{
    bool IsRegistered { get; }
    void Start();
    void Stop();
}

/// <summary>
/// Owns the process-lifetime CM_Register_Notification registration. The native callback performs
/// only an in-memory generation invalidation and returns immediately; reconciliation stays outside
/// the callback boundary.
/// </summary>
public sealed class WindowsPnpHardwareNotificationSource : IPnpHardwareNotificationSource
{
    private const uint CrSuccess = 0;
    private const uint ErrorSuccess = 0;

    private readonly object _gate = new();
    private readonly IConfigurationManagerPnpInterop _interop;
    private readonly PnpHardwareGenerationInvalidator _invalidator;
    private readonly PnpHardwareNotificationCallback _callback;
    private IntPtr _notificationHandle;

    public WindowsPnpHardwareNotificationSource(
        IConfigurationManagerPnpInterop interop,
        PnpHardwareGenerationInvalidator invalidator)
    {
        _interop = interop;
        _invalidator = invalidator;
        _callback = OnNotification;
    }

    public bool IsRegistered
    {
        get
        {
            lock (_gate)
                return _notificationHandle != IntPtr.Zero;
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_notificationHandle != IntPtr.Zero)
                return;

            var attempt = _interop.RegisterAllDeviceInstances(_callback);
            if (attempt.ConfigRet != CrSuccess)
            {
                throw new ConfigurationManagerNotificationException(
                    "CM_Register_Notification",
                    attempt.ConfigRet);
            }

            if (attempt.NotificationHandle == IntPtr.Zero)
            {
                throw new InvalidDataException(
                    "CM_Register_Notification returned success without a usable notification handle.");
            }

            _notificationHandle = attempt.NotificationHandle;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_notificationHandle == IntPtr.Zero)
                return;

            var configRet = _interop.Unregister(_notificationHandle);
            if (configRet != CrSuccess)
            {
                throw new ConfigurationManagerNotificationException(
                    "CM_Unregister_Notification",
                    configRet);
            }

            _notificationHandle = IntPtr.Zero;
        }
    }

    public void Dispose() => Stop();

    private uint OnNotification(
        IntPtr notificationHandle,
        IntPtr context,
        uint action,
        IntPtr eventData,
        uint eventDataSize)
    {
        _ = notificationHandle;
        _ = context;
        _ = eventData;
        _ = eventDataSize;

        _invalidator.InvalidateForNativeAction(action);
        return ErrorSuccess;
    }
}

public sealed class PnpHardwareGenerationMonitor(
    IPnpHardwareNotificationSource notificationSource) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        notificationSource.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        notificationSource.Stop();
        return Task.CompletedTask;
    }
}
