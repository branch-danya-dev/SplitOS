using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace SplitOS.RuntimeHost.WindowsContext;

public sealed record ProcessInstanceIdentity(
    int ProcessId,
    DateTimeOffset ProcessCreationTimeUtc);

public sealed record ProcessEvidenceObservation(
    int ProcessId,
    int? SessionId,
    string? ImagePath,
    DateTimeOffset? ProcessCreationTimeUtc,
    DateTimeOffset ObservedUtc)
{
    /// <summary>
    /// PID alone is not stable enough to distinguish a later process that reuses the same identifier.
    /// Identity is therefore available only when Windows also returned the process creation time.
    /// </summary>
    public ProcessInstanceIdentity? ReuseProtectedIdentity
        => ProcessCreationTimeUtc is { } creationTime
            ? new ProcessInstanceIdentity(ProcessId, creationTime)
            : null;
}

public sealed record ProcessEvidenceSnapshot(
    DateTimeOffset ObservedUtc,
    IReadOnlyList<ProcessEvidenceObservation> Processes);

public sealed record WindowsProcessProbeAttempt(
    int ProcessId,
    int? SessionId,
    string? ImagePath,
    long? ProcessCreationFileTimeUtc);

public interface IWindowsProcessEvidenceInterop
{
    IReadOnlyList<int> EnumerateProcessIds();
    WindowsProcessProbeAttempt Probe(int processId);
}

public interface IProcessEvidenceSnapshotReader
{
    ProcessEvidenceSnapshot Read();
}

/// <summary>
/// Produces one bounded observation of the Windows process table. The process list itself is
/// authoritative for this instant; fields that require opening/querying an individual process are
/// intentionally nullable because a process may exit or deny query access between enumeration and
/// inspection. No missing image/session/creation evidence is fabricated.
/// </summary>
public sealed class ProcessEvidenceSnapshotReader(
    IWindowsProcessEvidenceInterop interop,
    TimeProvider? timeProvider = null) : IProcessEvidenceSnapshotReader
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public ProcessEvidenceSnapshot Read()
    {
        var observedUtc = _timeProvider.GetUtcNow();
        var processIds = interop.EnumerateProcessIds()
            .Where(static processId => processId > 0)
            .Distinct()
            .OrderBy(static processId => processId)
            .ToArray();

        var observations = new ProcessEvidenceObservation[processIds.Length];
        for (var index = 0; index < processIds.Length; index++)
        {
            var processId = processIds[index];
            var attempt = interop.Probe(processId);
            if (attempt.ProcessId != processId)
            {
                throw new InvalidDataException(
                    $"Process probe identity mismatch. Requested PID {processId}, received PID {attempt.ProcessId}.");
            }

            observations[index] = new ProcessEvidenceObservation(
                processId,
                attempt.SessionId,
                NormalizeImagePath(attempt.ImagePath),
                ConvertCreationTime(attempt.ProcessCreationFileTimeUtc),
                observedUtc);
        }

        return new ProcessEvidenceSnapshot(observedUtc, observations);
    }

    internal static string? NormalizeImagePath(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            return null;

        var normalized = imagePath.Trim().TrimEnd('\0').Replace('/', '\\');
        return normalized.Length == 0 ? null : normalized;
    }

    private static DateTimeOffset? ConvertCreationTime(long? fileTimeUtc)
    {
        if (!fileTimeUtc.HasValue || fileTimeUtc.Value <= 0)
            return null;

        try
        {
            return new DateTimeOffset(DateTime.FromFileTimeUtc(fileTimeUtc.Value));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}

/// <summary>
/// Thin native adapter over documented process-enumeration/query APIs. Per-process inspection asks
/// only for PROCESS_QUERY_LIMITED_INFORMATION. Failure to inspect one process is represented as
/// missing optional evidence rather than failure of the entire process-table observation.
/// </summary>
public sealed class WindowsProcessEvidenceInterop : IWindowsProcessEvidenceInterop
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int InitialProcessCapacity = 256;
    private const int MaxProcessCapacity = 65_536;
    private const int MaxPathChars = 32_768;

    public IReadOnlyList<int> EnumerateProcessIds()
    {
        var capacity = InitialProcessCapacity;
        while (capacity <= MaxProcessCapacity)
        {
            var nativeIds = new uint[capacity];
            var bufferBytes = checked((uint)(nativeIds.Length * sizeof(uint)));
            if (!EnumProcesses(nativeIds, bufferBytes, out var returnedBytes))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "EnumProcesses failed while reading process evidence.");
            }

            if (returnedBytes < bufferBytes)
            {
                var count = checked((int)(returnedBytes / sizeof(uint)));
                var processIds = new int[count];
                for (var index = 0; index < count; index++)
                    processIds[index] = checked((int)nativeIds[index]);

                return processIds;
            }

            capacity = checked(capacity * 2);
        }

        throw new InvalidDataException(
            $"Process enumeration exceeded the bounded capacity of {MaxProcessCapacity} entries.");
    }

    public WindowsProcessProbeAttempt Probe(int processId)
    {
        if (processId <= 0)
            throw new ArgumentOutOfRangeException(nameof(processId), "Process ID must be positive.");

        int? sessionId = null;
        if (ProcessIdToSessionId(checked((uint)processId), out var nativeSessionId))
            sessionId = checked((int)nativeSessionId);

        var processHandle = OpenProcess(
            ProcessQueryLimitedInformation,
            inheritHandle: false,
            checked((uint)processId));
        if (processHandle == IntPtr.Zero)
            return new WindowsProcessProbeAttempt(processId, sessionId, null, null);

        try
        {
            string? imagePath = null;
            var pathCapacity = (uint)MaxPathChars;
            var pathBuilder = new StringBuilder(MaxPathChars);
            if (QueryFullProcessImageName(processHandle, 0, pathBuilder, ref pathCapacity))
                imagePath = pathBuilder.ToString(0, checked((int)pathCapacity));

            long? creationFileTimeUtc = null;
            if (GetProcessTimes(
                    processHandle,
                    out var creationTime,
                    out _,
                    out _,
                    out _))
            {
                creationFileTimeUtc = ToFileTime(creationTime);
            }

            return new WindowsProcessProbeAttempt(
                processId,
                sessionId,
                imagePath,
                creationFileTimeUtc);
        }
        finally
        {
            _ = CloseHandle(processHandle);
        }
    }

    private static long ToFileTime(NativeFileTime fileTime)
    {
        var value = ((ulong)fileTime.HighDateTime << 32) | fileTime.LowDateTime;
        return unchecked((long)value);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeFileTime
    {
        public readonly uint LowDateTime;
        public readonly uint HighDateTime;
    }

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumProcesses(
        [Out] uint[] processIds,
        uint bufferBytes,
        out uint returnedBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        IntPtr processHandle,
        uint flags,
        [Out] StringBuilder executablePath,
        ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ProcessIdToSessionId(
        uint processId,
        out uint sessionId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(
        IntPtr processHandle,
        out NativeFileTime creationTime,
        out NativeFileTime exitTime,
        out NativeFileTime kernelTime,
        out NativeFileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
