using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace SplitOS.RuntimeHost.ModeRuntime;

public interface IControlSessionIdentity
{
    string GetCurrentKey();
}

/// <summary>A process-independent Windows logon identity, restricted to the active console.</summary>
public sealed class WindowsControlSessionIdentity : IControlSessionIdentity
{
    public string GetCurrentKey()
    {
        using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        if (!GetTokenInformation(identity.AccessToken, 10, out var statistics,
                Marshal.SizeOf<TokenStatistics>(), out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var id = statistics.AuthenticationId;
        var status = LsaGetLogonSessionData(ref id, out var buffer);
        if (status != 0) throw new Win32Exception((int)LsaNtStatusToWinError(status));
        try
        {
            if (buffer == IntPtr.Zero) throw new InvalidDataException("Windows logon information is missing.");
            var data = Marshal.PtrToStructure<LogonSessionPrefix>(buffer);
            if (data.Size < Marshal.SizeOf<LogonSessionPrefix>() || data.Sid == IntPtr.Zero || data.LogonTime <= 0 ||
                data.LogonId.Low != id.Low || data.LogonId.High != id.High ||
                data.Session != WTSGetActiveConsoleSessionId() || data.LogonType is not (2 or 10 or 11 or 12))
                throw new InvalidDataException("Current process does not own a supported active-console logon.");
            var sid = new SecurityIdentifier(data.Sid).Value;
            if (sid != identity.User?.Value) throw new InvalidDataException("Windows logon user mismatch.");
            return FormatKey(sid, id.High, id.Low, data.LogonTime, data.Session);
        }
        finally { if (buffer != IntPtr.Zero) _ = LsaFreeReturnBuffer(buffer); }
    }

    public static string FormatKey(string sid, uint high, uint low, long logonTime, uint session)
    {
        if (string.IsNullOrWhiteSpace(sid) || sid.Length > 184 || sid.Any(char.IsControl) || logonTime <= 0 || session == uint.MaxValue)
            throw new ArgumentException("Invalid Windows logon identity.");
        return FormattableString.Invariant($"winlogon-v1:{sid}:{high:x8}{low:x8}:{logonTime:x16}:{session:x8}");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid { public uint Low; public uint High; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TokenStatistics
    {
        public Luid TokenId; public Luid AuthenticationId; public long ExpirationTime;
        public int TokenType; public int ImpersonationLevel;
        public uint DynamicCharged; public uint DynamicAvailable; public uint GroupCount; public uint PrivilegeCount;
        public Luid ModifiedId;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct LsaString { public ushort Length; public ushort MaximumLength; public IntPtr Buffer; }
    [StructLayout(LayoutKind.Sequential)]
    private struct LogonSessionPrefix
    {
        public uint Size; public Luid LogonId; public LsaString UserName; public LsaString LogonDomain;
        public LsaString AuthenticationPackage; public uint LogonType; public uint Session; public IntPtr Sid;
        public long LogonTime;
    }
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass,
        out TokenStatistics information, int length, out int returnLength);
    [DllImport("secur32.dll")]
    private static extern uint LsaGetLogonSessionData(ref Luid logonId, out IntPtr data);
    [DllImport("secur32.dll")]
    private static extern uint LsaFreeReturnBuffer(IntPtr buffer);
    [DllImport("advapi32.dll")]
    private static extern uint LsaNtStatusToWinError(uint status);
    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();
}
