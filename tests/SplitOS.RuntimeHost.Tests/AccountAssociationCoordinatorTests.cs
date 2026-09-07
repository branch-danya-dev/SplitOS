using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.ProtectedSecrets;
using SplitOS.Persistence.User;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class AccountAssociationCoordinatorTests
{
    [TestMethod]
    public async Task NoAssociationDoesNotReadSecretAndRemainsUnassociated()
    {
        var associationStore = new FakeAssociationStore(null);
        var secretStore = new FakeSecretStore(AccountSecretReadResult.Unreadable());
        var coordinator = new AccountAssociationCoordinator(
            associationStore,
            secretStore,
            new FakeWindowsUserContext("S-1-5-21-current"));

        var result = await coordinator.EvaluateAsync();

        Assert.AreEqual("UNASSOCIATED", result.AssociationState);
        Assert.AreEqual(0, secretStore.ReadCount);
        Assert.AreEqual(0, associationStore.MarkReauthCount);
    }

    [TestMethod]
    public async Task MatchingCurrentUserAndProtectedSecretPreserveActiveAssociation()
    {
        var association = ActiveAssociation("S-1-5-21-current", "acc_one");
        var associationStore = new FakeAssociationStore(association);
        var secretStore = new FakeSecretStore(AccountSecretReadResult.Available(Secret("acc_one")));
        var coordinator = new AccountAssociationCoordinator(
            associationStore,
            secretStore,
            new FakeWindowsUserContext("S-1-5-21-current"));

        var result = await coordinator.EvaluateAsync();

        Assert.AreEqual("ACTIVE", result.AssociationState);
        Assert.IsNull(result.Reason);
        Assert.AreEqual(0, associationStore.MarkReauthCount);
    }

    [TestMethod]
    public async Task MissingSecretDurablyConvergesActiveAssociationToReauthRequired()
    {
        var association = ActiveAssociation("S-1-5-21-current", "acc_one");
        var associationStore = new FakeAssociationStore(association);
        var coordinator = new AccountAssociationCoordinator(
            associationStore,
            new FakeSecretStore(AccountSecretReadResult.Missing()),
            new FakeWindowsUserContext("S-1-5-21-current"));

        var result = await coordinator.EvaluateAsync();

        Assert.AreEqual("REAUTH_REQUIRED", result.AssociationState);
        Assert.AreEqual("LOCAL_SECRET_MISSING", result.Reason);
        Assert.AreEqual(1, associationStore.MarkReauthCount);
        Assert.AreEqual("REAUTH_REQUIRED", associationStore.Current!.AssociationState);
        Assert.AreEqual(2, associationStore.Current.Revision);
    }

    [TestMethod]
    public async Task CopiedAssociationFromDifferentWindowsSidFailsClosedWithoutRewritingEvidence()
    {
        var association = ActiveAssociation("S-1-5-21-other", "acc_one");
        var associationStore = new FakeAssociationStore(association);
        var secretStore = new FakeSecretStore(AccountSecretReadResult.Available(Secret("acc_one")));
        var coordinator = new AccountAssociationCoordinator(
            associationStore,
            secretStore,
            new FakeWindowsUserContext("S-1-5-21-current"));

        var result = await coordinator.EvaluateAsync();

        Assert.AreEqual("REAUTH_REQUIRED", result.AssociationState);
        Assert.AreEqual("LOCAL_ASSOCIATION_CONTEXT_MISMATCH", result.Reason);
        Assert.AreEqual(0, secretStore.ReadCount);
        Assert.AreEqual(0, associationStore.MarkReauthCount);
        Assert.AreEqual("ACTIVE", associationStore.Current!.AssociationState);
    }

    [TestMethod]
    public async Task ProtectedSecretForDifferentAccountConvergesToReauthRequired()
    {
        var associationStore = new FakeAssociationStore(ActiveAssociation("S-1-5-21-current", "acc_one"));
        var coordinator = new AccountAssociationCoordinator(
            associationStore,
            new FakeSecretStore(AccountSecretReadResult.Available(Secret("acc_two"))),
            new FakeWindowsUserContext("S-1-5-21-current"));

        var result = await coordinator.EvaluateAsync();

        Assert.AreEqual("REAUTH_REQUIRED", result.AssociationState);
        Assert.AreEqual("LOCAL_SECRET_ACCOUNT_MISMATCH", result.Reason);
        Assert.AreEqual(1, associationStore.MarkReauthCount);
    }

    private static UserAccountAssociationRecord ActiveAssociation(string sid, string accountId)
    {
        var now = DateTimeOffset.UtcNow;
        return new UserAccountAssociationRecord(
            Guid.NewGuid().ToString("D"), sid, accountId, "ACTIVE", now, now,
            null, null, null, "account.v1", 1, now, Guid.NewGuid().ToString("D"));
    }

    private static AccountSecretEnvelope Secret(string accountId)
    {
        var issued = DateTimeOffset.UtcNow.AddMinutes(-1);
        return new AccountSecretEnvelope
        {
            AccountId = accountId,
            RefreshToken = "secret",
            RefreshIssuedUtc = issued,
            RefreshAbsoluteExpiryUtc = issued.AddDays(30)
        };
    }

    private sealed class FakeWindowsUserContext(string sid) : IWindowsUserContext
    {
        public string GetCurrentUserSid() => sid;
    }

    private sealed class FakeSecretStore(AccountSecretReadResult result) : IAccountSecretStore
    {
        public int ReadCount { get; private set; }

        public Task<AccountSecretReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return Task.FromResult(result);
        }

        public Task WriteAsync(AccountSecretEnvelope secret, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeAssociationStore(UserAccountAssociationRecord? association) : IUserAccountAssociationStore
    {
        public UserAccountAssociationRecord? Current { get; private set; } = association;
        public int MarkReauthCount { get; private set; }

        public Task<UserAccountAssociationRecord?> GetAccountAssociationAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Current);

        public Task<UserAssociationWriteOutcome> CreateActiveAssociationAsync(
            Guid associationId,
            string windowsUserSid,
            string accountId,
            DateTimeOffset authenticatedUtc,
            string secretReference,
            string? entitlementVersion,
            DateTimeOffset? entitlementObservedUtc,
            DateTimeOffset? lastServerUtc,
            Guid operationId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<UserAssociationWriteOutcome> MarkReauthRequiredAsync(
            int expectedRevision,
            Guid operationId,
            CancellationToken cancellationToken = default)
        {
            MarkReauthCount++;
            if (Current is null)
            {
                return Task.FromResult(new UserAssociationWriteOutcome(
                    UserAssociationWriteDisposition.Missing, null, null, null));
            }

            if (Current.Revision != expectedRevision)
            {
                return Task.FromResult(new UserAssociationWriteOutcome(
                    UserAssociationWriteDisposition.RevisionConflict,
                    Current,
                    Current.Revision,
                    null));
            }

            Current = Current with
            {
                AssociationState = "REAUTH_REQUIRED",
                Revision = Current.Revision + 1,
                UpdatedUtc = DateTimeOffset.UtcNow,
                UpdatedByOperationId = operationId.ToString("D")
            };
            return Task.FromResult(new UserAssociationWriteOutcome(
                UserAssociationWriteDisposition.Applied,
                Current,
                Current.Revision,
                null));
        }
    }
}
