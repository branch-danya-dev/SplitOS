using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.ProductIdentity;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class RuntimeSignOutCommandTests
{
    [TestMethod]
    public async Task SignedOutProjectsOnlyBoundedCleanupStatus()
    {
        var flow = new FakeLocalSignOutFlow(new LocalSignOutResult(
            LocalSignOutDisposition.SignedOut,
            "ACCOUNT_SIGNED_OUT",
            true,
            true));
        var command = new RuntimeSignOutCommand(flow);

        var result = await command.SignOutAsync();

        Assert.AreEqual("SignedOut", result.Disposition);
        Assert.AreEqual("ACCOUNT_SIGNED_OUT", result.ProductCode);
        Assert.IsTrue(result.LocalSecretRemoved);
        Assert.IsTrue(result.AssociationRemoved);
        Assert.AreEqual(1, flow.CallCount);
    }

    [TestMethod]
    public async Task RecoveryRequiredIsNotMisreportedAsSuccessfulSignOut()
    {
        var flow = new FakeLocalSignOutFlow(new LocalSignOutResult(
            LocalSignOutDisposition.RecoveryRequired,
            "LOCAL_SECRET_RECONCILIATION_REQUIRED",
            false,
            true));
        var command = new RuntimeSignOutCommand(flow);

        var result = await command.SignOutAsync();

        Assert.AreEqual("RecoveryRequired", result.Disposition);
        Assert.AreEqual("LOCAL_SECRET_RECONCILIATION_REQUIRED", result.ProductCode);
        Assert.IsFalse(result.LocalSecretRemoved);
        Assert.IsTrue(result.AssociationRemoved);
    }

    [TestMethod]
    public async Task ContextMismatchRemainsFailClosed()
    {
        var flow = new FakeLocalSignOutFlow(new LocalSignOutResult(
            LocalSignOutDisposition.ContextMismatch,
            "LOCAL_ASSOCIATION_CONTEXT_MISMATCH",
            false,
            false));
        var command = new RuntimeSignOutCommand(flow);

        var result = await command.SignOutAsync();

        Assert.AreEqual("ContextMismatch", result.Disposition);
        Assert.AreEqual("LOCAL_ASSOCIATION_CONTEXT_MISMATCH", result.ProductCode);
        Assert.IsFalse(result.LocalSecretRemoved);
        Assert.IsFalse(result.AssociationRemoved);
    }

    private sealed class FakeLocalSignOutFlow(LocalSignOutResult result) : ILocalSignOutFlow
    {
        public int CallCount { get; private set; }

        public Task<LocalSignOutResult> SignOutAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }
}
