using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SplitOS.RuntimeHost.WindowsContext;

public sealed record PowerSchemeSnapshot(
    Guid ActiveSchemeId,
    DateTimeOffset ObservedUtc);

public sealed record PowerActiveSchemeQueryAttempt(
    int ErrorCode,
    Guid? ActiveSchemeId);

public interface IWindowsPowerSchemeInterop
{
    PowerActiveSchemeQueryAttempt GetActiveScheme();
}

public interface IPowerSchemeQuery
{
    Guid QueryActiveScheme();
}

public interface IPowerSchemeSnapshotReader
{
    PowerSchemeSnapshot Read();
}

/// <summary>
/// Normalizes the public PowrProf active-scheme query into one fail-closed current-user power identity.
/// No fallback/default scheme is fabricated when Windows cannot provide authoritative evidence.
/// </summary>
public sealed class WindowsPowerSchemeQuery(
    IWindowsPowerSchemeInterop interop) : IPowerSchemeQuery
{
    private const int ErrorSuccess = 0;

    public Guid QueryActiveScheme()
    {
        var attempt = interop.GetActiveScheme();
        if (attempt.ErrorCode != ErrorSuccess)
            throw new Win32Exception(attempt.ErrorCode, "PowerGetActiveScheme failed.");
        if (!attempt.ActiveSchemeId.HasValue || attempt.ActiveSchemeId.Value == Guid.Empty)
            throw new InvalidDataException("PowerGetActiveScheme returned success without a usable active scheme GUID.");

        return attempt.ActiveSchemeId.Value;
    }
}

public sealed class PowerSchemeSnapshotReader(
    IPowerSchemeQuery query,
    TimeProvider? timeProvider = null) : IPowerSchemeSnapshotReader
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public PowerSchemeSnapshot Read()
        => new(query.QueryActiveScheme(), _timeProvider.GetUtcNow());
}

/// <summary>
/// Thin native wrapper for the documented PowrProf current-user active-scheme API.
/// PowerGetActiveScheme allocates the GUID with LocalAlloc; ownership is released with LocalFree.
/// </summary>
public sealed class PowrProfPowerSchemeInterop : IWindowsPowerSchemeInterop
{
    public PowerActiveSchemeQueryAttempt GetActiveScheme()
    {
        var error = checked((int)PowerGetActiveScheme(IntPtr.Zero, out var schemePointer));
        if (error != 0)
        {
            if (schemePointer != IntPtr.Zero)
                _ = LocalFree(schemePointer);
            return new PowerActiveSchemeQueryAttempt(error, null);
        }

        if (schemePointer == IntPtr.Zero)
            return new PowerActiveSchemeQueryAttempt(0, null);

        try
        {
            return new PowerActiveSchemeQueryAttempt(
                0,
                Marshal.PtrToStructure<Guid>(schemePointer));
        }
        finally
        {
            _ = LocalFree(schemePointer);
        }
    }

    [DllImport("powrprof.dll", SetLastError = false)]
    private static extern uint PowerGetActiveScheme(
        IntPtr userRootPowerKey,
        out IntPtr activePolicyGuid);

    [DllImport("kernel32.dll", SetLastError = false)]
    private static extern IntPtr LocalFree(IntPtr memory);
}
