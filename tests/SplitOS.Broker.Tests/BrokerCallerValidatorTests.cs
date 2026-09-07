using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Broker.Service;
using SplitOS.Ipc.Windows;

namespace SplitOS.Broker.Tests;

[TestClass]
public sealed class BrokerCallerValidatorTests
{
    private readonly BrokerCallerValidator _validator = new();

    [TestMethod]
    public void RuntimeHostInExpectedSessionIsAllowed()
    {
        var identity = new PipeClientIdentity(100, 7, "SplitOS.RuntimeHost", @"C:\Program Files\SplitOS\SplitOS.RuntimeHost.exe");
        var result = _validator.Validate(identity, 7);
        Assert.IsTrue(result.Allowed);
    }

    [TestMethod]
    public void ManagerCannotActAsBrokerCaller()
    {
        var identity = new PipeClientIdentity(101, 7, "SplitOS.Manager", @"C:\Program Files\SplitOS\SplitOS.Manager.exe");
        var result = _validator.Validate(identity, 7);
        Assert.IsFalse(result.Allowed);
        Assert.AreEqual("CALLER_IMAGE_NOT_RUNTIMEHOST", result.Reason);
    }

    [TestMethod]
    public void CrossSessionCallerIsDenied()
    {
        var identity = new PipeClientIdentity(100, 8, "SplitOS.RuntimeHost", @"C:\Program Files\SplitOS\SplitOS.RuntimeHost.exe");
        var result = _validator.Validate(identity, 7);
        Assert.IsFalse(result.Allowed);
        Assert.AreEqual("CALLER_SESSION_MISMATCH", result.Reason);
    }
}
