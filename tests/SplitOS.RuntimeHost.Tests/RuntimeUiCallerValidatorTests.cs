using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Ipc.Windows;
using SplitOS.RuntimeHost;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class RuntimeUiCallerValidatorTests
{
    private const string ReleaseRoot = @"C:\Program Files\SplitOS\Releases\slice00-test";
    private readonly RuntimeUiCallerValidator _validator = new(ReleaseRoot);

    [TestMethod]
    [DataRow("Manager", "SplitOS.Manager.exe")]
    [DataRow("GameLauncher", "SplitOS.GameLauncher.exe")]
    public void ApprovedUiImagesInExpectedReleasePathAndSessionAreAllowed(
        string componentDirectory,
        string imageName)
    {
        var identity = new PipeClientIdentity(
            42,
            5,
            Path.GetFileNameWithoutExtension(imageName),
            $@"C:\Program Files\SplitOS\Releases\slice00-test\{componentDirectory}\{imageName}");

        Assert.IsTrue(_validator.Validate(identity, 5).Allowed);
    }

    [TestMethod]
    public void ApprovedFilenameOutsideTrustedReleasePathIsDenied()
    {
        var identity = new PipeClientIdentity(
            42,
            5,
            "SplitOS.Manager",
            @"C:\Temp\SplitOS.Manager.exe");

        var result = _validator.Validate(identity, 5);
        Assert.IsFalse(result.Allowed);
        Assert.AreEqual("CALLER_RELEASE_PATH_MISMATCH", result.Reason);
    }

    [TestMethod]
    public void UnknownUiImageIsDenied()
    {
        var identity = new PipeClientIdentity(43, 5, "RandomApp", @"C:\Temp\RandomApp.exe");
        var result = _validator.Validate(identity, 5);
        Assert.IsFalse(result.Allowed);
        Assert.AreEqual("CALLER_IMAGE_NOT_ALLOWED", result.Reason);
    }

    [TestMethod]
    public void CrossSessionUiCallerIsDenied()
    {
        var identity = new PipeClientIdentity(
            42,
            6,
            "SplitOS.Manager",
            @"C:\Program Files\SplitOS\Releases\slice00-test\Manager\SplitOS.Manager.exe");

        var result = _validator.Validate(identity, 5);
        Assert.IsFalse(result.Allowed);
        Assert.AreEqual("CALLER_SESSION_MISMATCH", result.Reason);
    }
}
