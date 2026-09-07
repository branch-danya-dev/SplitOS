using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Ipc.Windows;

namespace SplitOS.IntegrationTests;

[TestClass]
public sealed class WindowsNamedPipeSecurityTests
{
    [TestMethod]
    public void BrokerPipeSecurityIsProtectedAndScopedToSystemSessionUserAndNotNetwork()
    {
        var sessionUser = WindowsIdentity.GetCurrent().User
            ?? throw new AssertFailedException("Current test user SID is unavailable.");

        var security = WindowsNamedPipeServerFactory.BuildBrokerPipeSecurity(sessionUser);
        Assert.IsTrue(security.AreAccessRulesProtected);

        var rules = security
            .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<PipeAccessRule>()
            .ToArray();

        var localSystem = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var network = new SecurityIdentifier(WellKnownSidType.NetworkSid, null);

        Assert.IsTrue(rules.Any(rule =>
            Equals(rule.IdentityReference, network) &&
            rule.AccessControlType == AccessControlType.Deny &&
            (rule.PipeAccessRights & PipeAccessRights.FullControl) == PipeAccessRights.FullControl));

        Assert.IsTrue(rules.Any(rule =>
            Equals(rule.IdentityReference, localSystem) &&
            rule.AccessControlType == AccessControlType.Allow &&
            (rule.PipeAccessRights & PipeAccessRights.FullControl) == PipeAccessRights.FullControl));

        Assert.IsTrue(rules.Any(rule =>
            Equals(rule.IdentityReference, sessionUser) &&
            rule.AccessControlType == AccessControlType.Allow &&
            (rule.PipeAccessRights & PipeAccessRights.ReadWrite) == PipeAccessRights.ReadWrite));
    }

    [TestMethod]
    public void CurrentUserOnlyRuntimePipeCanBeCreated()
    {
        using var pipe = WindowsNamedPipeServerFactory.CreateCurrentUserOnly(
            $"SplitOS.Test.{Guid.NewGuid():N}",
            maxInstances: 1);

        Assert.IsNotNull(pipe);
    }
}
