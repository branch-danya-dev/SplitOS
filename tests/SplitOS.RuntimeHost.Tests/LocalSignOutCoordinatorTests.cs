using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.ProtectedSecrets;
using SplitOS.Persistence.User;
using SplitOS.RuntimeHost.ProductIdentity;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class LocalSignOutCoordinatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 19, 0, 0, TimeSpan.Zero);
    private const string Sid = "S-1-5-21-signout-test";
    private const string AccountId = "acc_test_01";
    private const string AssociationId = "413d4610-a0f0-4e5f-8072-f5c949c7be46";

    [TestMethod]
    public async Task SuccessfulSignOutDisablesAuthorityBeforeRemovingSecretAndAssociation()
    {
        var events = new List<string>();
        var associationStore = new MemoryAssociationStore(Association(), events);
        var signOutStore = new MemorySignOutStore(associationStore, events);
        var secretStore = new MemorySecretStore(Secret(), events);
        var evidence = Evidence();
        var coordinator = CreateCoordinator(associationStore, signOutStore, secretStore, evidence, Sid);

        var result = await coordinator.SignOutAsync();

        Assert.AreEqual(LocalSignOutDisposition.SignedOut, result.Disposition);
        Assert.AreEqual("ACCOUNT_SIGNED_OUT", result.ProductCode);
        Assert.IsTrue(result.LocalSecretRemoved);
        Assert.IsTrue(result.AssociationRemoved);
        Assert.IsNull(evidence.Read());
        Assert.IsNull(secretStore.Current);
        Assert.IsNull(associationStore.Current);
        CollectionAssert.AreEqual(
            new[] { "mark-reauth", "delete-secret", "remove-association" },
            events);
    }

    [TestMethod]
    public async Task SecretDeleteFailureStillRemovesAssociationAndRequiresRecovery()
    {
        var events = new List<string>();
        var associationStore = new MemoryAssociationStore(Association(), events);
        var signOutStore = new MemorySignOutStore(associationStore, events);
        var secretStore = new MemorySecretStore(Secret(), events) { ThrowOnDelete = true };
        var evidence = Evidence();

        var result = await CreateCoordinator(associationStore, signOutStore, secretStore, evidence, Sid).SignOutAsync();

        Assert.AreEqual(LocalSignOutDisposition.RecoveryRequired, result.Disposition);
        Assert.AreEqual("LOCAL_SECRET_RECONCILIATION_REQUIRED", result.ProductCode);
        Assert.IsFalse(result.LocalSecretRemoved);
        Assert.IsTrue(result.AssociationRemoved);
        Assert.IsNull(associationStore.Current);
        Assert.IsNull(evidence.Read());
        Assert.IsNotNull(secretStore.Current);
    }

    [TestMethod]
    public async Task AssociationRemovalFailureLeavesReauthRequiredAndCannotRetainPremiumAuthority()
    {
        var events = new List<string>();
        var associationStore = new MemoryAssociationStore(Association(), events);
        var signOutStore = new MemorySignOutStore(associationStore, events) { ForceConflict = true };
        var secretStore = new MemorySecretStore(Secret(), events);
        var evidence = Evidence();

        var result = await CreateCoordinator(associationStore, signOutStore, secretStore, evidence, Sid).SignOutAsync();

        Assert.AreEqual(LocalSignOutDisposition.RecoveryRequired, result.Disposition);
        Assert.AreEqual("LOCAL_SIGN_OUT_RECONCILIATION_REQUIRED", result.ProductCode);
        Assert.IsTrue(result.LocalSecretRemoved);
        Assert.IsFalse(result.AssociationRemoved);
        Assert.IsNotNull(associationStore.Current);
        Assert.AreEqual("REAUTH_REQUIRED", associationStore.Current.AssociationState);
        Assert.IsNull(evidence.Read());
        Assert.IsNull(secretStore.Current);
    }

    [TestMethod]
    public async Task RevisionConflictRetriesOnlySameAssociationContext()
    {
        var events = new List<string>();
        var associationStore = new MemoryAssociationStore(Association(), events)
        {
            ForceFirstMarkRevisionConflict = true
        };
        var signOutStore = new MemorySignOutStore(associationStore, events);
        var secretStore = new MemorySecretStore(Secret(), events);

        var result = await CreateCoordinator(
            associationStore,
            signOutStore,
            secretStore,
            Evidence(),
            Sid).SignOutAsync();

        Assert.AreEqual(LocalSignOutDisposition.SignedOut, result.Disposition);
        Assert.AreEqual(2, associationStore.MarkCalls);
        Assert.IsNull(associationStore.Current);
    }

    [TestMethod]
    public async Task ChangedAssociationDuringRevisionConflictFailsClosedWithoutDeletingCredentials()
    {
        var events = new List<string>();
        var associationStore = new MemoryAssociationStore(Association(), events)
        {
            ReplaceAccountOnFirstMarkConflict = true
        };
        var signOutStore = new MemorySignOutStore(associationStore, events);
        var secretStore = new MemorySecretStore(Secret(), events);
        var evidence = Evidence();

        var result = await CreateCoordinator(associationStore, signOutStore, secretStore, evidence, Sid).SignOutAsync();

        Assert.AreEqual(LocalSignOutDisposition.RecoveryRequired, result.Disposition);
        Assert.AreEqual("LOCAL_ASSOCIATION_CHANGED", result.ProductCode);
        Assert.AreEqual(0, secretStore.DeleteCalls);
        Assert.AreEqual(0, signOutStore.RemoveCalls);
        Assert.IsNull(evidence.Read());
    }

    [TestMethod]
    public async Task SidMismatchDoesNotMutateDurableCredentialsOrAssociation()
    {
        var events = new List<string>();
        var associationStore = new MemoryAssociationStore(Association(), events);
        var signOutStore = new MemorySignOutStore(associationStore, events);
        var secretStore = new MemorySecretStore(Secret(), events);
        var evidence = Evidence();

        var result = await CreateCoordinator(
            associationStore,
            signOutStore,
            secretStore,
            evidence,
            "S-1-5-21-different-user").SignOutAsync();

        Assert.AreEqual(LocalSignOutDisposition.ContextMismatch, result.Disposition);
        Assert.AreEqual("LOCAL_ASSOCIATION_CONTEXT_MISMATCH", result.ProductCode);
        Assert.IsNotNull(associationStore.Current);
        Assert.AreEqual("ACTIVE", associationStore.Current.AssociationState);
        Assert.IsNotNull(secretStore.Current);
        Assert.AreEqual(0, secretStore.DeleteCalls);
        Assert.IsNull(evidence.Read());
    }

    [TestMethod]
    public async Task UnassociatedStateCleansOrphanSecretWithoutFabricatingAssociation()
    {
        var events = new List<string>();
        var associationStore = new MemoryAssociationStore(null, events);
        var signOutStore = new MemorySignOutStore(associationStore, events);
        var secretStore = new MemorySecretStore(Secret(), events);
        var evidence = Evidence();

        var result = await CreateCoordinator(associationStore, signOutStore, secretStore, evidence, Sid).SignOutAsync();

        Assert.AreEqual(LocalSignOutDisposition.AlreadySignedOut, result.Disposition);
        Assert.IsNull(secretStore.Current);
        Assert.IsNull(associationStore.Current);
        Assert.IsNull(evidence.Read());
        Assert.AreEqual(0, signOutStore.RemoveCalls);
    }

    [TestMethod]
    public async Task ConcurrentSignOutCannotCreateCompetingCleanup()
    {
        var events = new List<string>();
        var associationStore = new MemoryAssociationStore(Association(), events)
        {
            BlockMark = true
        };
        var signOutStore = new MemorySignOutStore(associationStore, events);
        var secretStore = new MemorySecretStore(Secret(), events);
        var coordinator = CreateCoordinator(associationStore, signOutStore, secretStore, Evidence(), Sid);

        var first = coordinator.SignOutAsync();
        await associationStore.MarkStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var second = await coordinator.SignOutAsync();
        Assert.AreEqual(LocalSignOutDisposition.AlreadyInProgress, second.Disposition);

        associationStore.MarkRelease.TrySetResult(true);
        var firstResult = await first;
        Assert.AreEqual(LocalSignOutDisposition.SignedOut, firstResult.Disposition);
    }

    private static LocalSignOutCoordinator CreateCoordinator(
        MemoryAssociationStore associationStore,
        MemorySignOutStore signOutStore,
        MemorySecretStore secretStore,
        OnlineEntitlementEvidenceState evidence,
        string sid)
        => new(
            associationStore,
            signOutStore,
            secretStore,
            new FixedWindowsUserContext(sid),
            evidence,
            new RuntimeStateRefreshSignal());

    private static OnlineEntitlementEvidenceState Evidence()
    {
        var state = new OnlineEntitlementEvidenceState();
        state.Publish(
            AssociationId,
            new SplitOSEntitlementSnapshot(
                AccountId,
                42,
                "PRO",
                "ACTIVE",
                Now.AddDays(-1),
                Now.AddDays(30),
                new[] { "runtime.managed_modes" },
                true,
                Now),
            Now);
        return state;
    }

    private static UserAccountAssociationRecord Association()
        => new(
            AssociationId,
            Sid,
            AccountId,
            "ACTIVE",
            Now.AddDays(-30),
            Now.AddDays(-1),
            "42",
            Now.AddMinutes(-10),
            Now.AddMinutes(-10),
            "account.v1",
            5,
            Now.AddMinutes(-10),
            Guid.NewGuid().ToString("D"));

    private static AccountSecretEnvelope Secret()
        => new()
        {
            AccountId = AccountId,
            RefreshToken = "REFRESH_SECRET",
            RefreshIssuedUtc = Now.AddDays(-1),
            RefreshAbsoluteExpiryUtc = Now.AddDays(30),
            LastTrustedServerUtc = Now.AddMinutes(-10),
            LastTrustedServerObservationLocalUtc = Now.AddMinutes(-10),
            LastValidAssertionJti = "assertion-42",
            OfflineEntitlementAssertion = "header.payload.signature",
            OfflineAssertionStoredUtc = Now.AddHours(-1)
        };

    private sealed class MemoryAssociationStore(
        UserAccountAssociationRecord? initial,
        List<string> events) : IUserAccountAssociationStore
    {
        public UserAccountAssociationRecord? Current { get; set; } = initial;
        public int MarkCalls { get; private set; }
        public bool ForceFirstMarkRevisionConflict { get; init; }
        public bool ReplaceAccountOnFirstMarkConflict { get; init; }
        public bool BlockMark { get; init; }
        public TaskCompletionSource<bool> MarkStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> MarkRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

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
            => Task.FromResult(new UserAssociationWriteOutcome(
                UserAssociationWriteDisposition.AlreadyExists,
                Current,
                Current?.Revision,
                "not used"));

        public async Task<UserAssociationWriteOutcome> MarkReauthRequiredAsync(
            int expectedRevision,
            Guid operationId,
            CancellationToken cancellationToken = default)
        {
            MarkCalls++;
            MarkStarted.TrySetResult(true);
            if (BlockMark && MarkCalls == 1) await MarkRelease.Task.ConfigureAwait(false);
            if (Current is null)
                return new UserAssociationWriteOutcome(UserAssociationWriteDisposition.Missing, null, null, null);

            if (MarkCalls == 1 && (ForceFirstMarkRevisionConflict || ReplaceAccountOnFirstMarkConflict))
            {
                Current = Current with
                {
                    Revision = Current.Revision + 1,
                    AccountId = ReplaceAccountOnFirstMarkConflict ? "acc_other" : Current.AccountId,
                    UpdatedUtc = Now
                };
                return new UserAssociationWriteOutcome(
                    UserAssociationWriteDisposition.RevisionConflict,
                    Current,
                    Current.Revision,
                    null);
            }

            if (Current.Revision != expectedRevision)
                return new UserAssociationWriteOutcome(
                    UserAssociationWriteDisposition.RevisionConflict,
                    Current,
                    Current.Revision,
                    null);

            events.Add("mark-reauth");
            Current = Current with
            {
                AssociationState = "REAUTH_REQUIRED",
                Revision = Current.Revision + 1,
                UpdatedUtc = Now,
                UpdatedByOperationId = operationId.ToString("D")
            };
            return new UserAssociationWriteOutcome(
                UserAssociationWriteDisposition.Applied,
                Current,
                Current.Revision,
                null);
        }
    }

    private sealed class MemorySignOutStore(
        MemoryAssociationStore associationStore,
        List<string> events) : IUserAccountAssociationSignOutStore
    {
        public bool ForceConflict { get; init; }
        public int RemoveCalls { get; private set; }

        public Task<UserAssociationRemovalOutcome> RemoveReauthAssociationAsync(
            string associationId,
            string windowsUserSid,
            string accountId,
            int expectedRevision,
            Guid operationId,
            CancellationToken cancellationToken = default)
        {
            RemoveCalls++;
            events.Add("remove-association");
            var current = associationStore.Current;
            if (ForceConflict)
                return Task.FromResult(new UserAssociationRemovalOutcome(
                    UserAssociationRemovalDisposition.RevisionConflict,
                    current,
                    current?.Revision,
                    "simulated conflict"));
            if (current is null)
                return Task.FromResult(new UserAssociationRemovalOutcome(
                    UserAssociationRemovalDisposition.Missing,
                    null,
                    null,
                    null));

            Assert.AreEqual("REAUTH_REQUIRED", current.AssociationState);
            Assert.AreEqual(expectedRevision, current.Revision);
            associationStore.Current = null;
            return Task.FromResult(new UserAssociationRemovalOutcome(
                UserAssociationRemovalDisposition.Removed,
                null,
                expectedRevision,
                operationId.ToString("D")));
        }
    }

    private sealed class MemorySecretStore(AccountSecretEnvelope? initial, List<string> events) : IAccountSecretStore
    {
        public AccountSecretEnvelope? Current { get; private set; } = initial;
        public int DeleteCalls { get; private set; }
        public bool ThrowOnDelete { get; init; }

        public Task<AccountSecretReadResult> ReadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Current is null
                ? AccountSecretReadResult.Missing()
                : AccountSecretReadResult.Available(Current));

        public Task WriteAsync(AccountSecretEnvelope secret, CancellationToken cancellationToken = default)
        {
            Current = secret;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(CancellationToken cancellationToken = default)
        {
            DeleteCalls++;
            events.Add("delete-secret");
            if (ThrowOnDelete) throw new IOException("simulated secret delete failure");
            Current = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedWindowsUserContext(string sid) : IWindowsUserContext
    {
        public string GetCurrentUserSid() => sid;
    }
}
