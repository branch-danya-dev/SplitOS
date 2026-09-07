using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Ipc.Windows;
using SplitOS.RuntimeHost;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class RuntimeUiCallerValidatorTests
{
    private readonly RuntimeUiCallerValidator _validator = new();

    [TestMethod]
    [DataRow("SplitOS.Manager.exe")]
    [DataRow("SplitOS.GameLauncher.exe")]
    public void ApprovedUiImagesInSameSessionAreAllowed(string imageName)
    {
        var identity = new PipeClientIdentity(42, 5, Path.GetFileNameWithoutExtension(imageName), $@"C:\SplitOS\{imageName}");
        Assert.IsTrue(_validator.Validate(identity, 5).Allowed);
    }

    [TestMethod]
    public void UnknownUiImageIsDenied()
    {
        var identity = new PipeClientIdentity(43, 5, "RandomApp", @"C:\Temp\RandomApp.exe");
        Assert.IsFalse(_validator.Validate(identity, 5).Allowed);
    }
}
