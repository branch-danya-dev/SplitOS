using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.User;

namespace SplitOS.Persistence.Tests;

[TestClass]
public sealed class UserAssociationReactivationStoreTests
{
    [TestMethod]
    public async Task ReauthenticationReactivatesExistingAssociationWithoutChangingIdentity()
    {
        using var storage = new TestStorage();
        var databasePath = storage.PathFor("user.db");
        var stateStore = new UserStateStore(databasePath);
        await stateStore.InitializeAsync();

        var associationId = Guid.Parse("9e392504-80a4-4fa0-b73a-1a50912be53f");
        var created = await stateStore.CreateActiveAssociationAsync(
            associationId,
            "S-1-5-21-test",
            "acc_one",
            DateTimeOffset.UtcNow.AddDays(-10),
            "account.v1",
            "41",
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(-1),
            Guid.NewGuid());
        Assert.AreEqual(UserAssociationWriteDisposition.Applied, created.Disposition);

        var reauthRequired = await stateStore.MarkReauthRequiredAsync(1, Guid.NewGuid());
        Assert.AreEqual(UserAssociationWriteDisposition.Applied, reauthRequired.Disposition);
        Assert.AreEqual(2, reauthRequired.Record!.Revision);

        var authenticatedUtc = DateTimeOffset.UtcNow;
        var entitlementObservedUtc = authenticatedUtc.AddSeconds(1);
        var serverUtc = authenticatedUtc.AddSeconds(2);
        var operationId = Guid.Parse("bc8f7035-e870-48f7-867c-617c4cfc6807");
        var reactivationStore = new UserAssociationReactivationStore(databasePath);

        var result = await reactivationStore.ReactivateSameAccountAsync(
            reauthRequired.Record.AssociationId,
            reauthRequired.Record.WindowsUserSid,
            reauthRequired.Record.AccountId,
            reauthRequired.Record.Revision,
            authenticatedUtc,
            "account.v1",
            "42",
            entitlementObservedUtc,
            serverUtc,
            operationId);

        Assert.AreEqual(UserAssociationWriteDisposition.Applied, result.Disposition);
        Assert.IsNotNull(result.Record);
        Assert.AreEqual(associationId.ToString("D"), result.Record.AssociationId);
        Assert.AreEqual(created.Record!.AssociatedUtc, result.Record.AssociatedUtc);
        Assert.AreEqual("acc_one", result.Record.AccountId);
        Assert.AreEqual("S-1-5-21-test", result.Record.WindowsUserSid);
        Assert.AreEqual("ACTIVE", result.Record.AssociationState);
        Assert.AreEqual(authenticatedUtc, result.Record.LastAuthenticatedUtc);
        Assert.AreEqual("42", result.Record.LastEntitlementVersion);
        Assert.AreEqual(entitlementObservedUtc, result.Record.LastEntitlementObservedUtc);
        Assert.AreEqual(serverUtc, result.Record.LastServerUtc);
        Assert.AreEqual("account.v1", result.Record.SecretReference);
        Assert.AreEqual(3, result.Record.Revision);
        Assert.AreEqual(operationId.ToString("D"), result.Record.UpdatedByOperationId);

        var persisted = await stateStore.GetAccountAssociationAsync();
        Assert.IsNotNull(persisted);
        Assert.AreEqual(result.Record, persisted);
    }

    [TestMethod]
    public async Task ReactivationRejectsStaleOrDifferentCanonicalIdentity()
    {
        using var storage = new TestStorage();
        var databasePath = storage.PathFor("user.db");
        var stateStore = new UserStateStore(databasePath);
        await stateStore.InitializeAsync();

        var created = await stateStore.CreateActiveAssociationAsync(
            Guid.NewGuid(),
            "S-1-5-21-test",
            "acc_one",
            DateTimeOffset.UtcNow.AddDays(-10),
            "account.v1",
            null,
            null,
            null,
            Guid.NewGuid());
        var reauthRequired = await stateStore.MarkReauthRequiredAsync(created.Record!.Revision, Guid.NewGuid());
        Assert.AreEqual(UserAssociationWriteDisposition.Applied, reauthRequired.Disposition);

        var reactivationStore = new UserAssociationReactivationStore(databasePath);
        var wrongAccount = await reactivationStore.ReactivateSameAccountAsync(
            reauthRequired.Record!.AssociationId,
            reauthRequired.Record.WindowsUserSid,
            "acc_two",
            reauthRequired.Record.Revision,
            DateTimeOffset.UtcNow,
            "account.v1",
            null,
            null,
            null,
            Guid.NewGuid());

        Assert.AreEqual(UserAssociationWriteDisposition.RevisionConflict, wrongAccount.Disposition);
        var afterWrongAccount = await stateStore.GetAccountAssociationAsync();
        Assert.IsNotNull(afterWrongAccount);
        Assert.AreEqual("acc_one", afterWrongAccount.AccountId);
        Assert.AreEqual("REAUTH_REQUIRED", afterWrongAccount.AssociationState);
        Assert.AreEqual(reauthRequired.Record.Revision, afterWrongAccount.Revision);

        var stale = await reactivationStore.ReactivateSameAccountAsync(
            reauthRequired.Record.AssociationId,
            reauthRequired.Record.WindowsUserSid,
            reauthRequired.Record.AccountId,
            reauthRequired.Record.Revision + 1,
            DateTimeOffset.UtcNow,
            "account.v1",
            null,
            null,
            null,
            Guid.NewGuid());

        Assert.AreEqual(UserAssociationWriteDisposition.RevisionConflict, stale.Disposition);
        var afterStale = await stateStore.GetAccountAssociationAsync();
        Assert.IsNotNull(afterStale);
        Assert.AreEqual("REAUTH_REQUIRED", afterStale.AssociationState);
        Assert.AreEqual(reauthRequired.Record.Revision, afterStale.Revision);
    }
}
