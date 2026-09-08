using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.ProtectedSecrets;

namespace SplitOS.Persistence.Tests;

[TestClass]
public sealed class ProtectedSecretAclTests
{
    [TestMethod]
    public async Task ProtectedSecretDirectoryAndBlobAllowOnlyCurrentWindowsUser()
    {
        using var storage = new TestStorage();
        var secretPath = storage.PathFor("secrets", "account.v1.dat");
        var store = new DpapiAccountSecretStore(secretPath);

        await store.WriteAsync(CreateSecret());

        using var identity = WindowsIdentity.GetCurrent();
        var currentSid = identity.User
            ?? throw new InvalidOperationException("Current Windows user SID is unavailable in test.");

        var directory = new DirectoryInfo(Path.GetDirectoryName(secretPath)!);
        var directorySecurity = directory.GetAccessControl(AccessControlSections.Access);
        Assert.IsTrue(directorySecurity.AreAccessRulesProtected);
        AssertOnlyCurrentUserAllows(directorySecurity, currentSid);

        var file = new FileInfo(secretPath);
        var fileSecurity = file.GetAccessControl(AccessControlSections.Access);
        Assert.IsTrue(fileSecurity.AreAccessRulesProtected);
        AssertOnlyCurrentUserAllows(fileSecurity, currentSid);
    }

    [TestMethod]
    public void StoreConstructionRepairsBroadDirectoryAllowRule()
    {
        using var storage = new TestStorage();
        var secretPath = storage.PathFor("secrets", "account.v1.dat");
        var directoryPath = Path.GetDirectoryName(secretPath)!;
        Directory.CreateDirectory(directoryPath);

        var directory = new DirectoryInfo(directoryPath);
        var weakSecurity = directory.GetAccessControl();
        var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        weakSecurity.AddAccessRule(new FileSystemAccessRule(
            everyone,
            FileSystemRights.ReadAndExecute,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        directory.SetAccessControl(weakSecurity);

        _ = new DpapiAccountSecretStore(secretPath);

        using var identity = WindowsIdentity.GetCurrent();
        var currentSid = identity.User
            ?? throw new InvalidOperationException("Current Windows user SID is unavailable in test.");
        var repaired = directory.GetAccessControl(AccessControlSections.Access);
        Assert.IsTrue(repaired.AreAccessRulesProtected);
        AssertOnlyCurrentUserAllows(repaired, currentSid);
    }

    private static void AssertOnlyCurrentUserAllows(FileSystemSecurity security, SecurityIdentifier currentSid)
    {
        var rules = security.GetAccessRules(
            includeExplicit: true,
            includeInherited: true,
            targetType: typeof(SecurityIdentifier));

        var currentUserAllowFound = false;
        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.AccessControlType != AccessControlType.Allow)
            {
                continue;
            }

            Assert.AreEqual(currentSid.Value, ((SecurityIdentifier)rule.IdentityReference).Value);
            currentUserAllowFound = true;
        }

        Assert.IsTrue(currentUserAllowFound, "Current Windows user must retain an allow ACE on protected secret storage.");
    }

    private static AccountSecretEnvelope CreateSecret()
    {
        var issued = DateTimeOffset.UtcNow.AddMinutes(-1);
        return new AccountSecretEnvelope
        {
            AccountId = "acc_acl_test",
            RefreshToken = "refresh-acl-test",
            RefreshIssuedUtc = issued,
            RefreshAbsoluteExpiryUtc = issued.AddDays(30)
        };
    }
}
