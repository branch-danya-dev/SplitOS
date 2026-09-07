using System.Security.Principal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Broker.Service;
using SplitOS.Ipc.Windows;

namespace SplitOS.Broker.Tests;

[TestClass]
public sealed class BrokerCallerValidatorTests
{
    private const string ReleaseRoot = @"C:\Program Files\SplitOS\Releases\slice00-test";
    private readonly BrokerCallerValidator _validator = new(ReleaseRoot);

    [TestMethod]
    public void RuntimeHostInExpectedReleasePathAndSessionIsAllowed()
    {
        var identity = new PipeClientIdentity(
            100,
            7,
            "SplitOS.RuntimeHost",
            @"C:\Program Files\SplitOS\Releases\slice00-test\RuntimeHost\SplitOS.RuntimeHost.exe");

        var result = _validator.Validate(identity, 7);
        Assert.IsTrue(result.Allowed);
    }

    [TestMethod]
    public void RuntimeHostFilenameOutsideTrustedReleasePathIsDenied()
    {
        var identity = new PipeClientIdentity(
            100,
            7,
            "SplitOS.RuntimeHost",
            @"C:\Temp\SplitOS.RuntimeHost.exe");

        var result = _validator.Validate(identity, 7);
        Assert.IsFalse(result.Allowed);
        Assert.AreEqual("CALLER_RELEASE_PATH_MISMATCH", result.Reason);
    }

    [TestMethod]
    public void ManagerCannotActAsBrokerCaller()
    {
        var identity = new PipeClientIdentity(
            101,
            7,
            "SplitOS.Manager",
            @"C:\Program Files\SplitOS\Releases\slice00-test\Manager\SplitOS.Manager.exe");

        var result = _validator.Validate(identity, 7);
        Assert.IsFalse(result.Allowed);
        Assert.AreEqual("CALLER_IMAGE_NOT_RUNTIMEHOST", result.Reason);
    }

    [TestMethod]
    public void CrossSessionCallerIsDenied()
    {
        var identity = new PipeClientIdentity(
            100,
            8,
            "SplitOS.RuntimeHost",
            @"C:\Program Files\SplitOS\Releases\slice00-test\RuntimeHost\SplitOS.RuntimeHost.exe");

        var result = _validator.Validate(identity, 7);
        Assert.IsFalse(result.Allowed);
        Assert.AreEqual("CALLER_SESSION_MISMATCH", result.Reason);
    }

    [TestMethod]
    public void LocalSystemIdentityIsRecognized()
    {
        var localSystem = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var builtinUsers = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

        Assert.IsTrue(BrokerServiceIdentity.IsLocalSystem(localSystem));
        Assert.IsFalse(BrokerServiceIdentity.IsLocalSystem(builtinUsers));
    }
}
