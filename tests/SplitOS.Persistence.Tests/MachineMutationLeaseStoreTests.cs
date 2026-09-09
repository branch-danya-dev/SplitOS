using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.Machine;

namespace SplitOS.Persistence.Tests;

[TestClass]
public sealed class MachineMutationLeaseStoreTests
{
    private static readonly DateTimeOffset StartUtc =
        new(2026, 9, 9, 4, 30, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task AcquireReleaseAndReacquireAdvancesFenceMonotonically()
    {
        using var storage = new TestStorage();
        var time = new ManualTimeProvider(StartUtc);
        var store = await CreateInitializedStoreAsync(storage, time);
        var firstOperation = Guid.NewGuid();

        var first = await store.TryAcquireAsync(
            MachineMutationType.Mode,
            firstOperation,
            Guid.NewGuid(),
            "console-session-1",
            TimeSpan.FromMinutes(2));

        Assert.AreEqual(MachineMutationLeaseAcquireDisposition.Acquired, first.Disposition);
        Assert.AreEqual(1L, first.Lease.FenceToken);
        Assert.IsTrue(first.Lease.IsHeld);

        var released = await store.ReleaseAsync(
            first.Lease.LeaseId!.Value,
            first.Lease.FenceToken,
            firstOperation);
        Assert.AreEqual(MachineMutationLeaseReleaseDisposition.Released, released.Disposition);
        Assert.IsFalse(released.Lease.IsHeld);
        Assert.AreEqual(1L, released.Lease.FenceToken);

        var second = await store.TryAcquireAsync(
            MachineMutationType.Update,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "console-session-1",
            TimeSpan.FromMinutes(2));

        Assert.AreEqual(MachineMutationLeaseAcquireDisposition.Acquired, second.Disposition);
        Assert.AreEqual(2L, second.Lease.FenceToken);
        Assert.AreEqual(MachineMutationType.Update, second.Lease.MutationType);
    }

    [TestMethod]
    public async Task IdenticalOwnerReacquireIsIdempotentAndDoesNotAdvanceFence()
    {
        using var storage = new TestStorage();
        var store = await CreateInitializedStoreAsync(storage, new ManualTimeProvider(StartUtc));
        var operation = Guid.NewGuid();
        var correlation = Guid.NewGuid();

        var first = await store.TryAcquireAsync(
            MachineMutationType.Mode,
            operation,
            correlation,
            "console-session-1",
            TimeSpan.FromMinutes(2));
        var replay = await store.TryAcquireAsync(
            MachineMutationType.Mode,
            operation,
            correlation,
            "console-session-1",
            TimeSpan.FromMinutes(2));

        Assert.AreEqual(MachineMutationLeaseAcquireDisposition.AlreadyOwned, replay.Disposition);
        Assert.AreEqual(first.Lease.LeaseId, replay.Lease.LeaseId);
        Assert.AreEqual(first.Lease.FenceToken, replay.Lease.FenceToken);
        Assert.AreEqual(first.Lease.Revision, replay.Lease.Revision);
    }

    [TestMethod]
    public async Task ActiveUpdateOrRecoveryLeaseProducesSpecificBusyEvidence()
    {
        using var updateStorage = new TestStorage();
        var updateStore = await CreateInitializedStoreAsync(updateStorage, new ManualTimeProvider(StartUtc));
        await updateStore.TryAcquireAsync(
            MachineMutationType.Update,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "console-session-1",
            TimeSpan.FromMinutes(2));

        var blockedByUpdate = await updateStore.TryAcquireAsync(
            MachineMutationType.Mode,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "console-session-1",
            TimeSpan.FromMinutes(2));
        Assert.AreEqual(MachineMutationLeaseAcquireDisposition.Busy, blockedByUpdate.Disposition);
        Assert.AreEqual("BUSY_UPDATE", blockedByUpdate.ProductCode);

        using var recoveryStorage = new TestStorage();
        var recoveryStore = await CreateInitializedStoreAsync(recoveryStorage, new ManualTimeProvider(StartUtc));
        await recoveryStore.TryAcquireAsync(
            MachineMutationType.Recovery,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "console-session-1",
            TimeSpan.FromMinutes(2));

        var blockedByRecovery = await recoveryStore.TryAcquireAsync(
            MachineMutationType.Mode,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "console-session-1",
            TimeSpan.FromMinutes(2));
        Assert.AreEqual(MachineMutationLeaseAcquireDisposition.Busy, blockedByRecovery.Disposition);
        Assert.AreEqual("BUSY_RECOVERY", blockedByRecovery.ProductCode);
    }

    [TestMethod]
    public async Task StaleFenceCannotRenewOrReleaseNewerOwner()
    {
        using var storage = new TestStorage();
        var store = await CreateInitializedStoreAsync(storage, new ManualTimeProvider(StartUtc));
        var firstOperation = Guid.NewGuid();
        var first = await store.TryAcquireAsync(
            MachineMutationType.Mode,
            firstOperation,
            Guid.NewGuid(),
            "console-session-1",
            TimeSpan.FromMinutes(2));
        await store.ReleaseAsync(first.Lease.LeaseId!.Value, first.Lease.FenceToken, firstOperation);

        var secondOperation = Guid.NewGuid();
        var second = await store.TryAcquireAsync(
            MachineMutationType.Mode,
            secondOperation,
            Guid.NewGuid(),
            "console-session-1",
            TimeSpan.FromMinutes(2));
        Assert.IsTrue(second.Lease.FenceToken > first.Lease.FenceToken);

        var staleRenew = await store.RenewAsync(
            first.Lease.LeaseId.Value,
            first.Lease.FenceToken,
            firstOperation,
            TimeSpan.FromMinutes(2));
        Assert.AreEqual(MachineMutationLeaseRenewDisposition.StaleOwner, staleRenew.Disposition);
        Assert.AreEqual(second.Lease.LeaseId, staleRenew.Lease.LeaseId);

        var staleRelease = await store.ReleaseAsync(
            first.Lease.LeaseId.Value,
            first.Lease.FenceToken,
            firstOperation);
        Assert.AreEqual(MachineMutationLeaseReleaseDisposition.StaleOwner, staleRelease.Disposition);
        Assert.AreEqual(second.Lease.LeaseId, staleRelease.Lease.LeaseId);

        var current = await store.GetAsync();
        Assert.AreEqual(second.Lease.LeaseId, current.LeaseId);
        Assert.AreEqual(second.Lease.FenceToken, current.FenceToken);
    }

    [TestMethod]
    public async Task ExpiredLeaseCannotBeRenewedReleasedOrTakenOverWithoutReconciliation()
    {
        using var storage = new TestStorage();
        var time = new ManualTimeProvider(StartUtc);
        var store = await CreateInitializedStoreAsync(storage, time);
        var operation = Guid.NewGuid();
        var lease = await store.TryAcquireAsync(
            MachineMutationType.Mode,
            operation,
            Guid.NewGuid(),
            "console-session-1",
            TimeSpan.FromSeconds(30));

        time.Advance(TimeSpan.FromSeconds(31));

        var takeover = await store.TryAcquireAsync(
            MachineMutationType.Update,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "console-session-1",
            TimeSpan.FromMinutes(2));
        Assert.AreEqual(MachineMutationLeaseAcquireDisposition.ReconciliationRequired, takeover.Disposition);
        Assert.AreEqual(lease.Lease.LeaseId, takeover.Lease.LeaseId);
        Assert.AreEqual(lease.Lease.FenceToken, takeover.Lease.FenceToken);

        var renew = await store.RenewAsync(
            lease.Lease.LeaseId!.Value,
            lease.Lease.FenceToken,
            operation,
            TimeSpan.FromMinutes(2));
        Assert.AreEqual(MachineMutationLeaseRenewDisposition.ReconciliationRequired, renew.Disposition);

        var release = await store.ReleaseAsync(
            lease.Lease.LeaseId.Value,
            lease.Lease.FenceToken,
            operation);
        Assert.AreEqual(MachineMutationLeaseReleaseDisposition.ReconciliationRequired, release.Disposition);

        var current = await store.GetAsync();
        Assert.IsTrue(current.IsHeld);
        Assert.AreEqual(lease.Lease.LeaseId, current.LeaseId);
    }

    [TestMethod]
    public async Task CurrentOwnerCanRenewBeforeExpiryWithoutChangingFence()
    {
        using var storage = new TestStorage();
        var time = new ManualTimeProvider(StartUtc);
        var store = await CreateInitializedStoreAsync(storage, time);
        var operation = Guid.NewGuid();
        var acquired = await store.TryAcquireAsync(
            MachineMutationType.Mode,
            operation,
            Guid.NewGuid(),
            "console-session-1",
            TimeSpan.FromMinutes(1));

        time.Advance(TimeSpan.FromSeconds(20));
        var renewed = await store.RenewAsync(
            acquired.Lease.LeaseId!.Value,
            acquired.Lease.FenceToken,
            operation,
            TimeSpan.FromMinutes(2));

        Assert.AreEqual(MachineMutationLeaseRenewDisposition.Renewed, renewed.Disposition);
        Assert.AreEqual(acquired.Lease.FenceToken, renewed.Lease.FenceToken);
        Assert.IsTrue(renewed.Lease.ExpiresUtc > acquired.Lease.ExpiresUtc);
        Assert.IsTrue(renewed.Lease.Revision > acquired.Lease.Revision);
    }

    [TestMethod]
    public async Task LeaseRepositoryCannotFabricateMachineCanonicalStore()
    {
        using var storage = new TestStorage();
        var store = new MachineMutationLeaseStore(
            storage.PathFor("machine.db"),
            storage.PathFor("machine-store.initialized"),
            storage.PathFor("machine-store.quarantined.json"),
            new ManualTimeProvider(StartUtc));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => store.InitializeAsync());
        Assert.IsFalse(File.Exists(storage.PathFor("machine.db")));
    }

    [TestMethod]
    public async Task LeaseLifetimeIsBounded()
    {
        using var storage = new TestStorage();
        var store = await CreateInitializedStoreAsync(storage, new ManualTimeProvider(StartUtc));

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => store.TryAcquireAsync(
            MachineMutationType.Mode,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "console-session-1",
            TimeSpan.Zero));

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => store.TryAcquireAsync(
            MachineMutationType.Mode,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "console-session-1",
            MachineMutationLeaseStore.MaximumLeaseLifetime + TimeSpan.FromSeconds(1)));
    }

    private static async Task<MachineMutationLeaseStore> CreateInitializedStoreAsync(
        TestStorage storage,
        TimeProvider timeProvider)
    {
        var db = storage.PathFor("machine.db");
        var marker = storage.PathFor("machine-store.initialized");
        var quarantine = storage.PathFor("machine-store.quarantined.json");
        var machine = new MachineStateStore(
            db,
            marker,
            storage.PathFor("maintenance", "backups"),
            storage.PathFor("maintenance", "quarantine"),
            quarantine);
        await machine.InitializeAsync();

        var lease = new MachineMutationLeaseStore(db, marker, quarantine, timeProvider);
        await lease.InitializeAsync();
        return lease;
    }

    private sealed class ManualTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtc;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan amount)
        {
            if (amount < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(amount));
            _utcNow = _utcNow.Add(amount);
        }
    }
}
