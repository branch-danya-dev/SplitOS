using System.Security.AccessControl;
using System.Security.Principal;

namespace SplitOS.Persistence.ProtectedSecrets;

internal static class SecretStorageAcl
{
    public static void EnsurePrivatePath(string secretPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("SplitOS protected secret ACLs require Windows.");
        }

        var directoryPath = Path.GetDirectoryName(secretPath);
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new InvalidOperationException("Protected secret path has no parent directory.");
        }

        Directory.CreateDirectory(directoryPath);
        using var identity = WindowsIdentity.GetCurrent();
        var userSid = identity.User
            ?? throw new InvalidOperationException("Current Windows user SID is unavailable for protected secret ACL configuration.");

        ApplyDirectoryAcl(directoryPath, userSid);
        if (File.Exists(secretPath))
        {
            ApplyFileAcl(secretPath, userSid);
        }
    }

    private static void ApplyDirectoryAcl(string directoryPath, SecurityIdentifier userSid)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(userSid);
        security.AddAccessRule(new FileSystemAccessRule(
            userSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        new DirectoryInfo(directoryPath).SetAccessControl(security);
    }

    private static void ApplyFileAcl(string filePath, SecurityIdentifier userSid)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(userSid);
        security.AddAccessRule(new FileSystemAccessRule(
            userSid,
            FileSystemRights.FullControl,
            AccessControlType.Allow));

        new FileInfo(filePath).SetAccessControl(security);
    }
}
