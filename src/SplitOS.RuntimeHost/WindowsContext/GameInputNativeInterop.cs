using System.Runtime.InteropServices;

namespace SplitOS.RuntimeHost.WindowsContext;

/// <summary>
/// Read-only subset of the public Microsoft GameInput v3 ABI used by SplitOS.
/// The definitions intentionally stop at the methods and GameInputDeviceInfo prefix required for
/// device observation. Microsoft.GameInput is referenced separately to provision the supported PC
/// redistributable; no gameplay-output or force-feedback primitive is exposed by this adapter.
/// </summary>
internal static class GameInputNativeV3
{
    [Flags]
    internal enum GameInputKind : int
    {
        Unknown = 0x00000000,
        ControllerAxis = 0x00000002,
        ControllerButton = 0x00000004,
        ControllerSwitch = 0x00000008,
        Controller = 0x0000000E,
        Keyboard = 0x00000010,
        Mouse = 0x00000020,
        ArcadeStick = 0x00010000,
        FlightStick = 0x00020000,
        Gamepad = 0x00040000,
        RacingWheel = 0x00080000,
    }

    internal enum GameInputEnumerationKind : int
    {
        NoEnumeration = 0,
        AsyncEnumeration = 1,
        BlockingEnumeration = 2,
    }

    [Flags]
    internal enum GameInputDeviceStatus : int
    {
        NoStatus = 0x00000000,
        Connected = 0x00000001,
        HapticInfoReady = 0x00200000,
        AnyStatus = unchecked((int)0xFFFFFFFF),
    }

    internal enum GameInputDeviceFamily : int
    {
        Virtual = -1,
        Unknown = 0,
        XboxOne = 1,
        Xbox360 = 2,
        Hid = 3,
        I8042 = 4,
        Aggregate = 5,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AppLocalDeviceId
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GameInputUsage
    {
        public ushort Page;
        public ushort Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GameInputVersion
    {
        public ushort Major;
        public ushort Minor;
        public ushort Build;
        public ushort Revision;
    }

    // Prefix of the v3 GameInputDeviceInfo structure through supportedInput. SplitOS does not read
    // fields after SupportedInput in this slice, so no later capability/output pointers are marshaled.
    [StructLayout(LayoutKind.Sequential)]
    internal struct GameInputDeviceInfoPrefix
    {
        public ushort VendorId;
        public ushort ProductId;
        public ushort RevisionNumber;
        public GameInputUsage Usage;
        public GameInputVersion HardwareVersion;
        public GameInputVersion FirmwareVersion;
        public AppLocalDeviceId DeviceId;
        public AppLocalDeviceId DeviceRootId;
        public GameInputDeviceFamily DeviceFamily;
        public GameInputKind SupportedInput;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate void GameInputReadingCallback(
        ulong callbackToken,
        IntPtr context,
        [MarshalAs(UnmanagedType.Interface)] IGameInputReading reading);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate void GameInputDeviceCallback(
        ulong callbackToken,
        IntPtr context,
        [MarshalAs(UnmanagedType.Interface)] IGameInputDevice device,
        ulong timestamp,
        GameInputDeviceStatus currentStatus,
        GameInputDeviceStatus previousStatus);

    [ComImport, Guid("C81C4CDE-ED1A-4631-A30F-C556A6241A1F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IGameInputReading
    {
    }

    [ComImport, Guid("63E2F38B-A399-4275-8AE7-D4C6E524D12A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IGameInputDevice
    {
        [PreserveSig]
        int GetDeviceInfo(out IntPtr info);

        // Retained only to preserve the public vtable slot before GetDeviceStatus. SplitOS never
        // invokes this output-capability path from the evidence adapter.
        [PreserveSig]
        int GetHapticInfo(IntPtr info);

        [PreserveSig]
        GameInputDeviceStatus GetDeviceStatus();
    }

    [ComImport, Guid("20EFC1C7-5D9A-43BA-B26F-B807FA48609C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IGameInput
    {
        [PreserveSig]
        ulong GetCurrentTimestamp();

        [PreserveSig]
        int GetCurrentReading(
            GameInputKind inputKind,
            [MarshalAs(UnmanagedType.Interface)] IGameInputDevice? device,
            [MarshalAs(UnmanagedType.Interface)] out IGameInputReading reading);

        [PreserveSig]
        int GetNextReading(
            [MarshalAs(UnmanagedType.Interface)] IGameInputReading referenceReading,
            GameInputKind inputKind,
            [MarshalAs(UnmanagedType.Interface)] IGameInputDevice? device,
            [MarshalAs(UnmanagedType.Interface)] out IGameInputReading reading);

        [PreserveSig]
        int GetPreviousReading(
            [MarshalAs(UnmanagedType.Interface)] IGameInputReading referenceReading,
            GameInputKind inputKind,
            [MarshalAs(UnmanagedType.Interface)] IGameInputDevice? device,
            [MarshalAs(UnmanagedType.Interface)] out IGameInputReading reading);

        [PreserveSig]
        int RegisterReadingCallback(
            [MarshalAs(UnmanagedType.Interface)] IGameInputDevice? device,
            GameInputKind inputKind,
            IntPtr context,
            [MarshalAs(UnmanagedType.FunctionPtr)] GameInputReadingCallback callbackFunc,
            out ulong callbackToken);

        [PreserveSig]
        int RegisterDeviceCallback(
            [MarshalAs(UnmanagedType.Interface)] IGameInputDevice? device,
            GameInputKind inputKind,
            GameInputDeviceStatus statusFilter,
            GameInputEnumerationKind enumerationKind,
            IntPtr context,
            [MarshalAs(UnmanagedType.FunctionPtr)] GameInputDeviceCallback callbackFunc,
            out ulong callbackToken);

        [PreserveSig]
        int RegisterSystemButtonCallback(
            [MarshalAs(UnmanagedType.Interface)] IGameInputDevice? device,
            int buttonFilter,
            IntPtr context,
            IntPtr callbackFunc,
            out ulong callbackToken);

        [PreserveSig]
        int RegisterKeyboardLayoutCallback(
            [MarshalAs(UnmanagedType.Interface)] IGameInputDevice? device,
            IntPtr context,
            IntPtr callbackFunc,
            out ulong callbackToken);

        // Retained to preserve the public IGameInput vtable. SplitOS shutdown uses
        // UnregisterCallback directly because that operation itself prevents future dispatch and
        // blocks until any in-flight callback has completed.
        [PreserveSig]
        void StopCallback(ulong callbackToken);

        [PreserveSig]
        [return: MarshalAs(UnmanagedType.I1)]
        bool UnregisterCallback(ulong callbackToken);
    }

    // GameInput v3's public C++ GameInputCreate(IGameInput**) is an inline wrapper around this
    // version-aware export. Calling the legacy GameInputCreate export directly returns the legacy
    // activation object, which does not expose the v3 IGameInput IID.
    [DllImport("GameInput.dll", ExactSpelling = true)]
    internal static extern int GameInputInitialize(
        ref Guid interfaceId,
        out IntPtr interfacePointer);
}

public sealed class GameInputRuntimeUnavailableException : Exception
{
    public GameInputRuntimeUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class GameInputNativeOperationException : Exception
{
    public GameInputNativeOperationException(string operation, int nativeHResult)
        : base($"{operation} failed with HRESULT 0x{unchecked((uint)nativeHResult):X8}.")
    {
        NativeHResult = nativeHResult;
        HResult = nativeHResult;
    }

    public int NativeHResult { get; }
}

public sealed class WindowsGameInputSessionFactory : IGameInputSessionFactory
{
    public IGameInputSession Create()
    {
        var interfaceId = typeof(GameInputNativeV3.IGameInput).GUID;
        IntPtr interfacePointer;
        int hresult;

        try
        {
            hresult = GameInputNativeV3.GameInputInitialize(
                ref interfaceId,
                out interfacePointer);
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or
            EntryPointNotFoundException or
            BadImageFormatException)
        {
            throw new GameInputRuntimeUnavailableException(
                "The Microsoft GameInput v3 runtime is unavailable or incompatible.",
                exception);
        }

        if (hresult < 0)
        {
            throw new GameInputRuntimeUnavailableException(
                $"GameInputInitialize failed with HRESULT 0x{unchecked((uint)hresult):X8}.",
                Marshal.GetExceptionForHR(hresult));
        }

        if (interfacePointer == IntPtr.Zero)
        {
            throw new GameInputRuntimeUnavailableException(
                "GameInputInitialize succeeded without returning IGameInput.");
        }

        try
        {
            var gameInput = Marshal.GetObjectForIUnknown(interfacePointer) as GameInputNativeV3.IGameInput;
            if (gameInput is null)
            {
                throw new GameInputRuntimeUnavailableException(
                    "GameInputInitialize returned an object that could not be materialized as v3 IGameInput.");
            }

            return new WindowsGameInputSession(gameInput, TimeProvider.System);
        }
        finally
        {
            // Balance the interface reference returned by GameInputInitialize; the RCW retains its
            // own COM lifetime until WindowsGameInputSession.Dispose releases it.
            _ = Marshal.Release(interfacePointer);
        }
    }
}

internal sealed class WindowsGameInputSession(
    GameInputNativeV3.IGameInput gameInput,
    TimeProvider timeProvider) : IGameInputSession
{
    private const int ConnectedStatus = 0x00000001;
    private static readonly GameInputNativeV3.GameInputKind RelevantKinds =
        GameInputNativeV3.GameInputKind.Controller |
        GameInputNativeV3.GameInputKind.Keyboard |
        GameInputNativeV3.GameInputKind.Mouse |
        GameInputNativeV3.GameInputKind.ArcadeStick |
        GameInputNativeV3.GameInputKind.FlightStick |
        GameInputNativeV3.GameInputKind.Gamepad |
        GameInputNativeV3.GameInputKind.RacingWheel;

    private readonly object _gate = new();
    private readonly GameInputNativeV3.GameInputDeviceCallback _deviceCallback = OnDeviceCallbackStatic;
    private readonly GCHandle _selfHandle = GCHandle.Alloc(new SessionCallbackTarget());
    private Action<GameInputDeviceChange>? _changeSink;
    private ulong _callbackToken;
    private bool _started;
    private bool _disposed;

    public bool IsStarted
    {
        get
        {
            lock (_gate)
                return _started;
        }
    }

    public void Start(Action<GameInputDeviceChange> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
                return;

            ((SessionCallbackTarget)_selfHandle.Target!).Attach(this);
            _changeSink = callback;

            var context = GCHandle.ToIntPtr(_selfHandle);
            var hresult = gameInput.RegisterDeviceCallback(
                null,
                RelevantKinds,
                GameInputNativeV3.GameInputDeviceStatus.Connected,
                GameInputNativeV3.GameInputEnumerationKind.BlockingEnumeration,
                context,
                _deviceCallback,
                out var token);

            if (hresult < 0)
            {
                _changeSink = null;
                throw new GameInputNativeOperationException(
                    "IGameInput.RegisterDeviceCallback",
                    hresult);
            }

            _callbackToken = token;
            _started = true;
        }
    }

    public void StopObservation()
    {
        lock (_gate)
        {
            if (!_started)
                return;

            var token = _callbackToken;

            // UnregisterCallback is the synchronization boundary: on success no future callback can
            // be dispatched and any callback already executing has finished. Keep the managed
            // callback context alive unless native removal is positively confirmed.
            if (!gameInput.UnregisterCallback(token))
            {
                throw new InvalidOperationException(
                    $"IGameInput.UnregisterCallback did not unregister token {token}.");
            }

            _callbackToken = 0;
            _started = false;
            _changeSink = null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
        }

        // If callback unregistration fails, keep the callback target/COM object alive and propagate
        // the failure rather than freeing native callback state that may still be referenced.
        StopObservation();

        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _changeSink = null;
            ((SessionCallbackTarget)_selfHandle.Target!).Detach();
            if (_selfHandle.IsAllocated)
                _selfHandle.Free();
            if (Marshal.IsComObject(gameInput))
                _ = Marshal.FinalReleaseComObject(gameInput);
        }
    }

    private sealed class SessionCallbackTarget
    {
        private WindowsGameInputSession? _session;

        public void Attach(WindowsGameInputSession session)
            => Volatile.Write(ref _session, session);

        public void Detach()
            => Volatile.Write(ref _session, null);

        public void OnDeviceCallback(
            GameInputNativeV3.IGameInputDevice device,
            ulong timestamp,
            GameInputNativeV3.GameInputDeviceStatus currentStatus,
            GameInputNativeV3.GameInputDeviceStatus previousStatus)
            => Volatile.Read(ref _session)?.OnDeviceCallback(device, timestamp, currentStatus, previousStatus);
    }

    private static void OnDeviceCallbackStatic(
        ulong callbackToken,
        IntPtr context,
        GameInputNativeV3.IGameInputDevice device,
        ulong timestamp,
        GameInputNativeV3.GameInputDeviceStatus currentStatus,
        GameInputNativeV3.GameInputDeviceStatus previousStatus)
    {
        _ = callbackToken;

        try
        {
            if (context == IntPtr.Zero)
                return;

            var handle = GCHandle.FromIntPtr(context);
            if (handle.Target is SessionCallbackTarget target)
                target.OnDeviceCallback(device, timestamp, currentStatus, previousStatus);
        }
        catch
        {
            // Exceptions must never escape a native GameInput callback boundary.
        }
    }

    private void OnDeviceCallback(
        GameInputNativeV3.IGameInputDevice device,
        ulong timestamp,
        GameInputNativeV3.GameInputDeviceStatus currentStatus,
        GameInputNativeV3.GameInputDeviceStatus previousStatus)
    {
        var observedUtc = timeProvider.GetUtcNow();
        GameInputDeviceEvidence? evidence = null;

        try
        {
            evidence = ReadDeviceEvidence(device, currentStatus, observedUtc);
        }
        catch
        {
            // Generation invalidation still matters even if optional per-device metadata cannot be read.
        }

        var change = new GameInputDeviceChange(
            evidence,
            (int)currentStatus,
            (int)previousStatus,
            timestamp,
            observedUtc);

        try
        {
            Volatile.Read(ref _changeSink)?.Invoke(change);
        }
        catch
        {
            // Consumer failures must not cross the unmanaged callback boundary.
        }
    }

    private static GameInputDeviceEvidence? ReadDeviceEvidence(
        GameInputNativeV3.IGameInputDevice device,
        GameInputNativeV3.GameInputDeviceStatus currentStatus,
        DateTimeOffset observedUtc)
    {
        if (device is null)
            return null;

        var hresult = device.GetDeviceInfo(out var infoPointer);
        if (hresult < 0 || infoPointer == IntPtr.Zero)
            return null;

        var info = Marshal.PtrToStructure<GameInputNativeV3.GameInputDeviceInfoPrefix>(infoPointer);
        var deviceIdBytes = info.DeviceId.Value;
        if (deviceIdBytes is null || deviceIdBytes.Length != 32)
            return null;

        var deviceId = Convert.ToHexString(deviceIdBytes);
        if (deviceId.AsSpan().Trim('0').Length == 0)
            return null;

        return new GameInputDeviceEvidence(
            deviceId,
            ((int)currentStatus & ConnectedStatus) != 0,
            (SplitOSInputKinds)(int)info.SupportedInput,
            info.VendorId,
            info.ProductId,
            (int)info.DeviceFamily,
            (int)currentStatus,
            observedUtc);
    }
}
