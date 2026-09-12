using System.Runtime.InteropServices;

namespace SplitOS.RuntimeHost.WindowsContext;

[StructLayout(LayoutKind.Sequential)]
public readonly struct CoreAudioPropertyKey : IEquatable<CoreAudioPropertyKey>
{
    public CoreAudioPropertyKey(Guid formatId, uint propertyId)
    {
        FormatId = formatId;
        PropertyId = propertyId;
    }

    public readonly Guid FormatId;
    public readonly uint PropertyId;

    public bool Equals(CoreAudioPropertyKey other)
        => FormatId == other.FormatId && PropertyId == other.PropertyId;

    public override bool Equals(object? obj)
        => obj is CoreAudioPropertyKey other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(FormatId, PropertyId);

    public static bool operator ==(CoreAudioPropertyKey left, CoreAudioPropertyKey right) => left.Equals(right);
    public static bool operator !=(CoreAudioPropertyKey left, CoreAudioPropertyKey right) => !left.Equals(right);
}

public static class CoreAudioPropertyKeys
{
    // Windows 11 24H2+ feature-detected property. Never assume it is present just because the
    // compile-time key is known.
    public static CoreAudioPropertyKey AudioEndpointStableId { get; } = new(
        new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"),
        12);

    public static CoreAudioPropertyKey DeviceFriendlyName { get; } = new(
        new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
        14);
}

public sealed class CoreAudioNativeOperationException(
    string operation,
    int hresult) : ExternalException($"{operation} failed with HRESULT 0x{hresult:X8}.", hresult)
{
    public string Operation { get; } = operation;
}

/// <summary>
/// Read-only Windows Core Audio/MMDevice projection. This adapter enumerates endpoint evidence and
/// role-specific defaults; it contains no default-device setter, volume mutation or per-app routing.
/// </summary>
public sealed class WindowsCoreAudioSnapshotQuery(
    TimeProvider? timeProvider = null) : ICoreAudioSnapshotQuery
{
    private const uint DeviceStateMaskAll = 0x0000000f;
    private const int ErrorNotFoundHresult = unchecked((int)0x80070490);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public AudioObservedState Query()
    {
        var observedUtc = _timeProvider.GetUtcNow();
        var enumerator = CoreAudioComFactory.CreateDeviceEnumerator();
        try
        {
            var render = Enumerate(enumerator, AudioEndpointFlow.Render, observedUtc);
            var capture = Enumerate(enumerator, AudioEndpointFlow.Capture, observedUtc);
            var defaults = QueryDefaults(enumerator, observedUtc);

            return new AudioObservedState(render, capture, defaults);
        }
        finally
        {
            CoreAudioComFactory.Release(enumerator);
        }
    }

    private static IReadOnlyList<AudioEndpointEvidence> Enumerate(
        IMMDeviceEnumerator enumerator,
        AudioEndpointFlow flow,
        DateTimeOffset observedUtc)
    {
        var hr = enumerator.EnumAudioEndpoints((EDataFlow)flow, DeviceStateMaskAll, out var collection);
        ThrowIfFailed(hr, $"IMMDeviceEnumerator.EnumAudioEndpoints({flow})");
        if (collection is null)
            throw new InvalidDataException("Core Audio enumeration succeeded without a device collection.");

        try
        {
            hr = collection.GetCount(out var count);
            ThrowIfFailed(hr, "IMMDeviceCollection.GetCount");

            var endpoints = new List<AudioEndpointEvidence>(checked((int)count));
            for (uint index = 0; index < count; index++)
            {
                hr = collection.Item(index, out var device);
                ThrowIfFailed(hr, $"IMMDeviceCollection.Item({index})");
                if (device is null)
                    throw new InvalidDataException("Core Audio collection returned a null device.");

                try
                {
                    endpoints.Add(ReadEndpoint(device, flow, observedUtc));
                }
                finally
                {
                    CoreAudioComFactory.Release(device);
                }
            }

            return endpoints
                .OrderBy(static endpoint => endpoint.EndpointId, StringComparer.Ordinal)
                .ToArray();
        }
        finally
        {
            CoreAudioComFactory.Release(collection);
        }
    }

    private static AudioEndpointEvidence ReadEndpoint(
        IMMDevice device,
        AudioEndpointFlow flow,
        DateTimeOffset observedUtc)
    {
        var hr = device.GetId(out var endpointId);
        ThrowIfFailed(hr, "IMMDevice.GetId");
        if (string.IsNullOrWhiteSpace(endpointId))
            throw new InvalidDataException("IMMDevice.GetId returned an empty endpoint ID.");

        hr = device.GetState(out var state);
        ThrowIfFailed(hr, "IMMDevice.GetState");

        return new AudioEndpointEvidence(
            flow,
            (AudioEndpointState)state,
            endpointId,
            TryReadStringProperty(device, CoreAudioPropertyKeys.AudioEndpointStableId),
            TryReadStringProperty(device, CoreAudioPropertyKeys.DeviceFriendlyName),
            observedUtc);
    }

    private static IReadOnlyList<AudioDefaultEndpointEvidence> QueryDefaults(
        IMMDeviceEnumerator enumerator,
        DateTimeOffset observedUtc)
    {
        var defaults = new List<AudioDefaultEndpointEvidence>(6);

        foreach (var flow in Enum.GetValues<AudioEndpointFlow>())
        {
            foreach (var role in Enum.GetValues<AudioEndpointRole>())
            {
                var hr = enumerator.GetDefaultAudioEndpoint((EDataFlow)flow, (ERole)role, out var device);
                if (hr == ErrorNotFoundHresult)
                {
                    defaults.Add(new AudioDefaultEndpointEvidence(flow, role, null, null, observedUtc));
                    continue;
                }

                ThrowIfFailed(hr, $"IMMDeviceEnumerator.GetDefaultAudioEndpoint({flow}, {role})");
                if (device is null)
                    throw new InvalidDataException("Core Audio default query succeeded without a device.");

                try
                {
                    hr = device.GetId(out var endpointId);
                    ThrowIfFailed(hr, "IMMDevice.GetId(default)");
                    if (string.IsNullOrWhiteSpace(endpointId))
                        throw new InvalidDataException("Default IMMDevice returned an empty endpoint ID.");

                    defaults.Add(new AudioDefaultEndpointEvidence(
                        flow,
                        role,
                        endpointId,
                        TryReadStringProperty(device, CoreAudioPropertyKeys.AudioEndpointStableId),
                        observedUtc));
                }
                finally
                {
                    CoreAudioComFactory.Release(device);
                }
            }
        }

        return defaults;
    }

    private static string? TryReadStringProperty(IMMDevice device, CoreAudioPropertyKey key)
    {
        const uint StgmRead = 0;
        const ushort VtEmpty = 0;
        const ushort VtLpwstr = 31;

        var hr = device.OpenPropertyStore(StgmRead, out var propertyStore);
        if (hr < 0 || propertyStore is null)
            return null;

        try
        {
            var mutableKey = key;
            hr = propertyStore.GetValue(ref mutableKey, out var value);
            if (hr < 0)
                return null;

            try
            {
                if (value.VariantType == VtEmpty || value.PointerValue == IntPtr.Zero)
                    return null;
                if (value.VariantType != VtLpwstr)
                    return null;

                var text = Marshal.PtrToStringUni(value.PointerValue);
                return string.IsNullOrEmpty(text) ? null : text;
            }
            finally
            {
                _ = PropVariantClear(ref value);
            }
        }
        finally
        {
            CoreAudioComFactory.Release(propertyStore);
        }
    }

    private static void ThrowIfFailed(int hr, string operation)
    {
        if (hr < 0)
            throw new CoreAudioNativeOperationException(operation, hr);
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);
}

public sealed class WindowsCoreAudioNotificationSessionFactory(
    TimeProvider? timeProvider = null) : ICoreAudioNotificationSessionFactory
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public ICoreAudioNotificationSession Create()
        => new WindowsCoreAudioNotificationSession(_timeProvider);
}

/// <summary>
/// Process-lifetime MMDevice notification registration. Native callbacks are deliberately bounded
/// to constructing an invalidation record and invoking the in-memory generation callback.
/// </summary>
public sealed class WindowsCoreAudioNotificationSession : ICoreAudioNotificationSession
{
    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;
    private IMMDeviceEnumerator? _enumerator;
    private CoreAudioNotificationClient? _notificationClient;
    private bool _started;
    private bool _disposed;

    public WindowsCoreAudioNotificationSession(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool IsStarted
    {
        get
        {
            lock (_gate)
                return _started;
        }
    }

    public void Start(Action<AudioNotificationChange> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
                return;

            var enumerator = CoreAudioComFactory.CreateDeviceEnumerator();
            var client = new CoreAudioNotificationClient(callback, _timeProvider);
            var hr = enumerator.RegisterEndpointNotificationCallback(client);
            if (hr < 0)
            {
                CoreAudioComFactory.Release(enumerator);
                throw new CoreAudioNativeOperationException(
                    "IMMDeviceEnumerator.RegisterEndpointNotificationCallback",
                    hr);
            }

            _enumerator = enumerator;
            _notificationClient = client;
            _started = true;
        }
    }

    public void StopObservation()
    {
        lock (_gate)
        {
            StopUnderLock();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            StopUnderLock();
            _disposed = true;
        }
    }

    private void StopUnderLock()
    {
        if (!_started)
            return;
        if (_enumerator is null || _notificationClient is null)
            throw new InvalidDataException("Core Audio notification session lost its registered callback state.");

        var hr = _enumerator.UnregisterEndpointNotificationCallback(_notificationClient);
        if (hr < 0)
        {
            throw new CoreAudioNativeOperationException(
                "IMMDeviceEnumerator.UnregisterEndpointNotificationCallback",
                hr);
        }

        _started = false;
        _notificationClient = null;
        CoreAudioComFactory.Release(_enumerator);
        _enumerator = null;
    }
}

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class CoreAudioNotificationClient(
    Action<AudioNotificationChange> callback,
    TimeProvider timeProvider) : IMMNotificationClient
{
    public int OnDeviceStateChanged(string? deviceId, uint newState)
    {
        _ = newState;
        return Dispatch(new AudioNotificationChange(
            AudioInvalidationKind.DeviceStateChanged,
            deviceId,
            null,
            null,
            timeProvider.GetUtcNow()));
    }

    public int OnDeviceAdded(string? deviceId)
        => Dispatch(new AudioNotificationChange(
            AudioInvalidationKind.DeviceAdded,
            deviceId,
            null,
            null,
            timeProvider.GetUtcNow()));

    public int OnDeviceRemoved(string? deviceId)
        => Dispatch(new AudioNotificationChange(
            AudioInvalidationKind.DeviceRemoved,
            deviceId,
            null,
            null,
            timeProvider.GetUtcNow()));

    public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId)
        => Dispatch(new AudioNotificationChange(
            AudioInvalidationKind.DefaultDeviceChanged,
            defaultDeviceId,
            CoreAudioEnumConversion.ToFlowOrNull(flow),
            CoreAudioEnumConversion.ToRoleOrNull(role),
            timeProvider.GetUtcNow()));

    public int OnPropertyValueChanged(string? deviceId, CoreAudioPropertyKey key)
    {
        _ = key;
        return Dispatch(new AudioNotificationChange(
            AudioInvalidationKind.PropertyChanged,
            deviceId,
            null,
            null,
            timeProvider.GetUtcNow()));
    }

    private int Dispatch(AudioNotificationChange change)
    {
        try
        {
            callback(change);
        }
        catch
        {
            // Never let managed exceptions escape a native MMDevice notification callback.
        }

        return 0;
    }
}

internal static class CoreAudioEnumConversion
{
    public static AudioEndpointFlow? ToFlowOrNull(EDataFlow flow)
        => flow switch
        {
            EDataFlow.Render => AudioEndpointFlow.Render,
            EDataFlow.Capture => AudioEndpointFlow.Capture,
            _ => null,
        };

    public static AudioEndpointRole? ToRoleOrNull(ERole role)
        => role switch
        {
            ERole.Console => AudioEndpointRole.Console,
            ERole.Multimedia => AudioEndpointRole.Multimedia,
            ERole.Communications => AudioEndpointRole.Communications,
            _ => null,
        };
}

internal static class CoreAudioComFactory
{
    private static readonly Guid MmDeviceEnumeratorClsid = new("bcde0395-e52f-467c-8e3d-c4579291692e");

    public static IMMDeviceEnumerator CreateDeviceEnumerator()
    {
        var type = Type.GetTypeFromCLSID(MmDeviceEnumeratorClsid, throwOnError: true)
            ?? throw new InvalidOperationException("MMDeviceEnumerator COM type was not available.");
        var instance = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("MMDeviceEnumerator COM activation returned null.");

        if (instance is IMMDeviceEnumerator enumerator)
            return enumerator;

        if (Marshal.IsComObject(instance))
            Marshal.FinalReleaseComObject(instance);

        throw new InvalidCastException("MMDeviceEnumerator does not expose IMMDeviceEnumerator.");
    }

    public static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
            Marshal.FinalReleaseComObject(value);
    }
}

internal enum EDataFlow
{
    Render = 0,
    Capture = 1,
    All = 2,
}

internal enum ERole
{
    Console = 0,
    Multimedia = 1,
    Communications = 2,
}

[StructLayout(LayoutKind.Explicit, Size = 16)]
internal struct PropVariant
{
    [FieldOffset(0)]
    public ushort VariantType;

    [FieldOffset(8)]
    public IntPtr PointerValue;
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection? devices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice? endpoint);

    [PreserveSig]
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice? device);

    [PreserveSig]
    int RegisterEndpointNotificationCallback(IMMNotificationClient client);

    [PreserveSig]
    int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-C0A70E2B24D9")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    [PreserveSig]
    int GetCount(out uint deviceCount);

    [PreserveSig]
    int Item(uint deviceIndex, out IMMDevice? device);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig]
    int Activate(ref Guid interfaceId, uint classContext, IntPtr activationParams, out IntPtr interfacePointer);

    [PreserveSig]
    int OpenPropertyStore(uint accessMode, out IPropertyStore? properties);

    [PreserveSig]
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

    [PreserveSig]
    int GetState(out uint state);
}

[ComImport]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig]
    int GetCount(out uint propertyCount);

    [PreserveSig]
    int GetAt(uint propertyIndex, out CoreAudioPropertyKey key);

    [PreserveSig]
    int GetValue(ref CoreAudioPropertyKey key, out PropVariant value);

    [PreserveSig]
    int SetValue(ref CoreAudioPropertyKey key, ref PropVariant value);

    [PreserveSig]
    int Commit();
}

[Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[ComVisible(true)]
internal interface IMMNotificationClient
{
    [PreserveSig]
    int OnDeviceStateChanged(
        [MarshalAs(UnmanagedType.LPWStr)] string? deviceId,
        uint newState);

    [PreserveSig]
    int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string? deviceId);

    [PreserveSig]
    int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string? deviceId);

    [PreserveSig]
    int OnDefaultDeviceChanged(
        EDataFlow flow,
        ERole role,
        [MarshalAs(UnmanagedType.LPWStr)] string? defaultDeviceId);

    [PreserveSig]
    int OnPropertyValueChanged(
        [MarshalAs(UnmanagedType.LPWStr)] string? deviceId,
        CoreAudioPropertyKey key);
}
