using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Broker.Service;

namespace SplitOS.Broker.Tests;

[TestClass]
public sealed class ManagedServiceCatalogTests
{
    [TestMethod]
    public void ReleaseCatalogOwnsMinimumScmAccessAndDependencyPolicy()
    {
        var catalog = new ReleaseManagedServiceCatalog();

        Assert.IsTrue(catalog.TryResolve("SEARCH_INDEXER", out var entry));
        Assert.AreEqual("WSearch", entry.WindowsServiceName);
        Assert.AreEqual(
            ManagedServiceScmAccess.QueryStatus,
            entry.AccessPolicy.QueryAccess);
        Assert.AreEqual(
            ManagedServiceScmAccess.QueryStatus | ManagedServiceScmAccess.Start,
            entry.AccessPolicy.StartAccess);
        Assert.AreEqual(
            ManagedServiceScmAccess.QueryStatus | ManagedServiceScmAccess.Stop,
            entry.AccessPolicy.StopAccess);
        Assert.AreEqual(
            ManagedServiceDependencyFallback.RejectMutation,
            entry.DependencyPolicy.Fallback);
        Assert.AreEqual(0, entry.DependencyPolicy.RequiredManagedServiceIds.Count);
    }

    [TestMethod]
    public void RawWindowsServiceNameAndUnknownIdsDoNotResolve()
    {
        var catalog = new ReleaseManagedServiceCatalog();

        Assert.IsFalse(catalog.TryResolve("WSearch", out _));
        Assert.IsFalse(catalog.TryResolve("ARBITRARY_SERVICE", out _));
        Assert.IsFalse(catalog.TryResolve(string.Empty, out _));
        Assert.IsFalse(catalog.TryResolve(null!, out _));
    }

    [TestMethod]
    public void AccessPolicyResolvesOnlyTheAllowlistedMutationMasks()
    {
        var policy = ManagedServiceAccessPolicy.QueryStartStop;

        Assert.AreEqual(
            ManagedServiceScmAccess.QueryStatus | ManagedServiceScmAccess.Start,
            policy.Resolve(ManagedServiceDesiredState.Running));
        Assert.AreEqual(
            ManagedServiceScmAccess.QueryStatus | ManagedServiceScmAccess.Stop,
            policy.Resolve(ManagedServiceDesiredState.Stopped));
        policy.Validate();
    }

    [TestMethod]
    public void AccessPolicyValidationRejectsBroaderThanRequiredRights()
    {
        var unsafePolicy = new ManagedServiceAccessPolicy(
            ManagedServiceScmAccess.QueryStatus | ManagedServiceScmAccess.Start,
            ManagedServiceScmAccess.QueryStatus | ManagedServiceScmAccess.Start,
            ManagedServiceScmAccess.QueryStatus | ManagedServiceScmAccess.Stop);

        Assert.ThrowsException<InvalidOperationException>(unsafePolicy.Validate);
    }
}
