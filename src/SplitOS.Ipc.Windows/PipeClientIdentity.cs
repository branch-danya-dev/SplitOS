using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace SplitOS.Ipc.Windows;

public sealed record PipeClientIdentity(
    uint ProcessId,
    uint SessionId,
    string ProcessName,
    string? ImagePath);

public static class PipeClientIdentityReader
{
    public static PipeClientIdentity Read(NamedPipeServerStream server)
    {
        ArgumentNullException.ThrowIfNull(server);

        if (!GetNamedPipeClientProcessId(server.SafePipeHandle, out var processId))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetNamedPipeClientProcessId failed.");
        }

        if (!GetNamedPipeClientSessionId(server.SafePipeHandle, out var sessionId))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetNamedPipeClientSessionId failed.");
        }

        using var process = Process.GetProcessById(checked((int)processId));
        string? imagePath = null;
        try
        {
            imagePath = process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            // Authorization fails closed when the image path cannot be established.
        }

        return new PipeClientIdentity(processId, sessionId, process.ProcessName, imagePath);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientSessionId(SafePipeHandle pipe, out uint clientSessionId);
}

public static class WindowsSessionInfo
{
    public const uint NoConsoleSession = uint.MaxValue;

    public static uint ActiveConsoleSessionId => WTSGetActiveConsoleSessionId();

    public static SecurityIdentifier GetLoggedOnUserSid(uint sessionId)
    {
        if (sessionId == NoConsoleSession)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionId), "No physical console session is attached.");
        }

        if (!WTSQueryUserToken(sessionId, out var token))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WTSQueryUserToken failed.");
        }

        using (token)
        using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
        {
            return identity.User
                ?? throw new InvalidOperationException("Logged-on Windows user SID is unavailable.");
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQueryUserToken(uint sessionId, out SafeAccessTokenHandle token);
}
