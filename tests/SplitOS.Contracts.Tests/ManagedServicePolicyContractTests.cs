using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Contracts.Protocol;

namespace SplitOS.Contracts.Tests;

[TestClass]
public sealed class ManagedServicePolicyContractTests
{
    [TestMethod]
    public void DesiredStateDigestIsStableAcrossInputOrdering()
    {
        var first = new[]
        {
            new ManagedServicePolicyEntry("SEARCH_INDEXER", "STOPPED"),
            new ManagedServicePolicyEntry("SECONDARY_FIXTURE", "RUNNING")
        };
        var reversed = first.Reverse().ToArray();

        Assert.AreEqual(
            ManagedServicePolicyActionContract.SerializeDesiredState(first),
            ManagedServicePolicyActionContract.SerializeDesiredState(reversed));
        Assert.AreEqual(
            ManagedServicePolicyActionContract.ComputeDesiredStateDigest(first),
            ManagedServicePolicyActionContract.ComputeDesiredStateDigest(reversed));
    }

    [TestMethod]
    public void PreStateDigestIsStableAcrossInputOrdering()
    {
        var first = new[]
        {
            new ManagedServicePreStateEntry("SEARCH_INDEXER", "RUNNING"),
            new ManagedServicePreStateEntry("SECONDARY_FIXTURE", "STOPPED")
        };
        var reversed = first.Reverse().ToArray();

        Assert.AreEqual(
            ManagedServicePolicyActionContract.SerializePreState(first),
            ManagedServicePolicyActionContract.SerializePreState(reversed));
        Assert.AreEqual(
            ManagedServicePolicyActionContract.ComputePreStateDigest(first),
            ManagedServicePolicyActionContract.ComputePreStateDigest(reversed));
    }

    [TestMethod]
    public void PreStateRequiresStableRollbackState()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            ManagedServicePolicyActionContract.NormalizePreState(
            [
                new ManagedServicePreStateEntry("SEARCH_INDEXER", "UNKNOWN")
            ]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            ManagedServicePolicyActionContract.NormalizePreState(
            [
                new ManagedServicePreStateEntry("SEARCH_INDEXER", "PAUSED")
            ]));
    }

    [TestMethod]
    public void DuplicateManagedTargetIsRejectedBeforeCanonicalization()
    {
        var duplicate = new[]
        {
            new ManagedServicePolicyEntry("SEARCH_INDEXER", "STOPPED"),
            new ManagedServicePolicyEntry("SEARCH_INDEXER", "RUNNING")
        };

        Assert.ThrowsExactly<ArgumentException>(() =>
            ManagedServicePolicyActionContract.NormalizeEntries(duplicate));
    }

    [TestMethod]
    public void InvalidDesiredStateAndUnboundedIdentifierAreRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            ManagedServicePolicyActionContract.NormalizeEntries(
            [
                new ManagedServicePolicyEntry("SEARCH_INDEXER", "PAUSED")
            ]));

        Assert.ThrowsExactly<ArgumentException>(() =>
            ManagedServicePolicyActionContract.NormalizeEntries(
            [
                new ManagedServicePolicyEntry("raw\\service", "STOPPED")
            ]));
    }
}
