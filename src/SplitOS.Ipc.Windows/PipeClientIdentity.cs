using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
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
            // Fail-closed authorization decisions can reject a caller whose image path cannot be established.
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
    public static uint ActiveConsoleSessionId => WTSGetActiveConsoleSessionId();

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();
}
