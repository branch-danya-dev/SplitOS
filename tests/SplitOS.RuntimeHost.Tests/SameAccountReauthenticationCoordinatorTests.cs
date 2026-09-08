using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.ProtectedSecrets;
using SplitOS.Persistence.User;
using SplitOS.RuntimeHost.Authentication;
using SplitOS.RuntimeHost.ProductIdentity;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class SameAccountReauthenticationCoordinatorTests
{
    private static readonly Uri AccountEndpoint = new("https://api.example.test/v1/account");
    private static readonly Uri EntitlementEndpoint = new("https://api.example.test/v1/entitlements/current");
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 20, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task ReauthPersistsFreshSecretBeforeReactivatingSameAssociation()
    {
        var trace = new List<string>();
        var association = ExistingAssociation("acc_test_01", "REAUTH_REQUIRED");
        var associationStore = new RecordingAssociationStore(trace, association);
        var reactivationStore = new RecordingReactivationStore(trace, association);
        var secretStore = new RecordingSecretStore(trace, OldSecret());
        using var handler = new FixtureHandler(trace);
        using var http = new HttpClient(handler);
        var coordinator = CreateCoordinator(http, associationStore, reactivationStore, secretStore);

        var result = await coordinator.ReactivateAsync(CreateSession(), CreateContext());

        Assert.AreEqual(SameAccountReauthenticationDisposition.Reactivated, result.Disposition);
        Assert.AreEqual("ACCOUNT_REAUTHENTICATED", result.ProductCode);
        Assert.IsTrue(result.IsActive);
        Assert.IsTrue(result.EntitlementResolved);
        Assert.AreEqual(association.AssociationId, result.AssociationId);
        Assert.AreEqual("acc_test_01", result.AccountId);

        Assert.IsNotNull(secretStore.Stored);
        Assert.AreEqual("NEW_REFRESH_SECRET", secretStore.Stored.RefreshToken);
        Assert.AreEqual(Now, secretStore.Stored.RefreshIssuedUtc);
        Assert.AreEqual(Now.AddDays(90), secretStore.Stored.RefreshAbsoluteExpiryUtc);
        Assert.AreEqual(Now, secretStore.Stored.LastTrustedServerUtc);
        Assert.IsNull(secretStore.Stored.OfflineEntitlementAssertion);
        Assert.IsNull(secretStore.Stored.OfflineAssertionStoredUtc);

        Assert.IsNotNull(reactivationStore.Current);
        Assert.AreEqual(association.AssociationId, reactivationStore.Current.AssociationId);
        Assert.AreEqual(association.AssociatedUtc, reactivationStore.Current.AssociatedUtc);
        Assert.AreEqual("ACTIVE", reactivationStore.Current.AssociationState);
        Assert.AreEqual(association.Revision + 1, reactivationStore.Current.Revision);
        Assert.AreEqual("42", reactivationStore.Current.LastEntitlementVersion);
        Assert.AreEqual(Now, reactivationStore.Current.LastEntitlementObservedUtc);
        Assert.AreEqual(Now, reactivationStore.Current.LastServerUtc);

        CollectionAssert.AreEqual(
            new[]
            {
                "association.read",
                "account",
                "entitlement",
                "secret.delete",
                "secret.write",
                "association.reactivate"
            },
            trace);
    }

    [TestMethod]
    public async Task DifferentBackendAccountRequiresExplicitSwitchWithoutTouchingSecret()
    {
        var trace = new List<string>();
        var association = ExistingAssociation("acc_old", "REAUTH_REQUIRED");
        var associationStore = new RecordingAssociationStore(trace, association);
        var reactivationStore = new RecordingReactivationStore(trace, association);
        var secretStore = new RecordingSecretStore(trace, OldSecret("acc_old"));
        using var handler = new FixtureHandler(trace, accountId: "acc_new");
        using var http = new HttpClient(handler);
        var coordinator = CreateCoordinator(http, associationStore, reactivationStore, secretStore);

        var result = await coordinator.ReactivateAsync(CreateSession(), CreateContext());

        Assert.AreEqual(SameAccountReauthenticationDisposition.AccountSwitchRequired, result.Disposition);
        Assert.AreEqual("ACCOUNT_SWITCH_REQUIRED", result.ProductCode);
        Assert.AreEqual("acc_old", result.AccountId);
        Assert.AreEqual(0, secretStore.DeleteCalls);
        Assert.AreEqual(0, secretStore.WriteCalls);
        Assert.AreEqual(0, reactivationStore.Calls);
        CollectionAssert.AreEqual(new[] { "association.read", "account" }, trace);
    }

    [TestMethod]
    public async Task ActiveAssociationDoesNotRunReauthenticationFlow()
    {
        var trace = new List<string>();
        var association = ExistingAssociation("acc_test_01", "ACTIVE");
        var associationStore = new RecordingAssociationStore(trace, association);
        var reactivationStore = new RecordingReactivationStore(trace, association);
        var secretStore = new RecordingSecretStore(trace, OldSecret());
        using var handler = new FixtureHandler(trace);
        using var http = new HttpClient(handler);
        var coordinator = CreateCoordinator(http, associationStore, reactivationStore, secretStore);

        var result = await coordinator.ReactivateAsync(CreateSession(), CreateContext());

        Assert.AreEqual(SameAccountReauthenticationDisposition.NotRequired, result.Disposition);
        Assert.AreEqual("ACCOUNT_REAUTH_NOT_REQUIRED", result.ProductCode);
        Assert.AreEqual(0, handler.RequestCount);
        Assert.AreEqual(0, secretStore.DeleteCalls);
        Assert.AreEqual(0, secretStore.WriteCalls);
        Assert.AreEqual(0, reactivationStore.Calls);
        CollectionAssert.AreEqual(new[] { "association.read" }, trace);
    }

    [TestMethod]
    public async Task EntitlementUnavailableReactivatesIdentityWithoutPremiumEvidence()
    {
        var trace = new List<string>();
        var association = ExistingAssociation("acc_test_01", "REAUTH_REQUIRED");
        var associationStore = new RecordingAssociationStore(trace, association);
        var reactivationStore = new RecordingReactivationStore(trace, association);
        var secretStore = new RecordingSecretStore(trace, OldSecret());
        using var handler = new FixtureHandler(
            trace,
            entitlementStatusCode: HttpStatusCode.ServiceUnavailable,
            entitlementErrorCode: "TEMPORARILY_UNAVAILABLE");
        using var http = new HttpClient(handler);
        var coordinator = CreateCoordinator(http, associationStore, reactivationStore, secretStore);

        var result = await coordinator.ReactivateAsync(CreateSession(), CreateContext());

        Assert.AreEqual(SameAccountReauthenticationDisposition.ReactivatedDegraded, result.Disposition);
        Assert.AreEqual("TEMPORARILY_UNAVAILABLE", result.ProductCode);
        Assert.IsTrue(result.IsActive);
        Assert.IsFalse(result.EntitlementResolved);
        Assert.IsNotNull(secretStore.Stored);
        Assert.IsNull(secretStore.Stored.LastTrustedServerUtc);
        Assert.IsNotNull(reactivationStore.Current);
        Assert.AreEqual("ACTIVE", reactivationStore.Current.AssociationState);
        Assert.IsNull(reactivationStore.Current.LastEntitlementVersion);
        Assert.IsNull(reactivationStore.Current.LastEntitlementObservedUtc);
        Assert.IsNull(reactivationStore.Current.LastServerUtc);
    }

    [TestMethod]
    public async Task MissingRefreshTokenCannotReactivateDurableSession()
    {
        var trace = new List<string>();
        var association = ExistingAssociation("acc_test_01", "REAUTH_REQUIRED");
        var associationStore = new RecordingAssociationStore(trace, association);
        var reactivationStore = new RecordingReactivationStore(trace, association);
        var secretStore = new RecordingSecretStore(trace, OldSecret());
        using var handler = new FixtureHandler(trace);
        using var http = new HttpClient(handler);
        var coordinator = CreateCoordinator(http, associationStore, reactivationStore, secretStore);
        var session = CreateSession() with { RefreshToken = null };

        var result = await coordinator.ReactivateAsync(session, CreateContext());

        Assert.AreEqual(SameAccountReauthenticationDisposition.ReusableSessionRequired, result.Disposition);
        Assert.AreEqual("AUTH_REUSABLE_SESSION_REQUIRED", result.ProductCode);
        Assert.AreEqual(0, handler.RequestCount);
        Assert.AreEqual(0, secretStore.DeleteCalls);
        Assert.AreEqual(0, reactivationStore.Calls);
    }

    [TestMethod]
    public async Task ReactivationRaceDeletesFreshSecretAndDoesNotClaimSuccess()
    {
        var trace = new List<string>();
        var association = ExistingAssociation("acc_test_01", "REAUTH_REQUIRED");
        var associationStore = new RecordingAssociationStore(trace, association);
        var reactivationStore = new RecordingReactivationStore(trace, association)
        {
            Disposition = UserAssociationWriteDisposition.RevisionConflict
        };
        var secretStore = new RecordingSecretStore(trace, OldSecret());
        using var handler = new FixtureHandler(trace);
        using var http = new HttpClient(handler);
        var coordinator = CreateCoordinator(http, associationStore, reactivationStore, secretStore);

        var result = await coordinator.ReactivateAsync(CreateSession(), CreateContext());

        Assert.AreEqual(SameAccountReauthenticationDisposition.PersistenceFailed, result.Disposition);
        Assert.AreEqual("LOCAL_ASSOCIATION_RACE", result.ProductCode);
        Assert.IsFalse(result.IsActive);
        Assert.AreEqual(1, secretStore.WriteCalls);
        Assert.AreEqual(2, secretStore.DeleteCalls);
        Assert.IsNull(secretStore.Stored);
        Assert.AreEqual("REAUTH_REQUIRED", reactivationStore.Current!.AssociationState);
    }

    [TestMethod]
    public async Task FailedRollbackEscalatesToRecoveryRequired()
    {
        var trace = new List<string>();
        var association = ExistingAssociation("acc_test_01", "REAUTH_REQUIRED");
        var associationStore = new RecordingAssociationStore(trace, association);
        var reactivationStore = new RecordingReactivationStore(trace, association)
        {
            Disposition = UserAssociationWriteDisposition.RevisionConflict
        };
        var secretStore = new RecordingSecretStore(trace, OldSecret())
        {
            FailDeleteCallNumber = 2
        };
        using var handler = new FixtureHandler(trace);
        using var http = new HttpClient(handler);
        var coordinator = CreateCoordinator(http, associationStore, reactivationStore, secretStore);

        var result = await coordinator.ReactivateAsync(CreateSession(), CreateContext());

        Assert.AreEqual(SameAccountReauthenticationDisposition.RecoveryRequired, result.Disposition);
        Assert.AreEqual("LOCAL_SECRET_RECONCILIATION_REQUIRED", result.ProductCode);
        Assert.IsFalse(result.IsActive);
        Assert.IsNotNull(secretStore.Stored);
    }

    [TestMethod]
    public async Task WindowsSidMismatchIsRejectedBeforeBackendOrSecretMutation()
    {
        var trace = new List<string>();
        var association = ExistingAssociation("acc_test_01", "REAUTH_REQUIRED") with
        {
            WindowsUserSid = "S-1-5-21-other"
        };
        var associationStore = new RecordingAssociationStore(trace, association);
        var reactivationStore = new RecordingReactivationStore(trace, association);
        var secretStore = new RecordingSecretStore(trace, OldSecret());
        using var handler = new FixtureHandler(trace);
        using var http = new HttpClient(handler);
        var coordinator = CreateCoordinator(http, associationStore, reactivationStore, secretStore);

        var result = await coordinator.ReactivateAsync(CreateSession(), CreateContext());

        Assert.AreEqual(SameAccountReauthenticationDisposition.Rejected, result.Disposition);
        Assert.AreEqual("LOCAL_ASSOCIATION_CONTEXT_MISMATCH", result.ProductCode);
        Assert.AreEqual(0, handler.RequestCount);
        Assert.AreEqual(0, secretStore.DeleteCalls);
        Assert.AreEqual(0, reactivationStore.Calls);
    }

    private static SameAccountReauthenticationCoordinator CreateCoordinator(
        HttpClient http,
        RecordingAssociationStore associationStore,
        RecordingReactivationStore reactivationStore,
        RecordingSecretStore secretStore)
    {
        var productClient = new ProductApiClient(
            http,
            new ProductApiConfiguration(AccountEndpoint, EntitlementEndpoint));
        return new SameAccountReauthenticationCoordinator(
            productClient,
            associationStore,
            reactivationStore,
            secretStore,
            new FixedWindowsUserContext("S-1-5-21-test"),
            new RuntimeStateRefreshSignal(),
            SameAccountReauthenticationPolicy.Default,
            new FixedTimeProvider(Now));
    }

    private static ValidatedNativeAuthSession CreateSession()
        => new(
            "oidc-subject-test",
            "NEW_ACCESS_SECRET",
            Now.AddMinutes(15),
            "NEW_REFRESH_SECRET",
            "openid offline_access");

    private static SameAccountReauthenticationContext CreateContext()
        => new(
            "1.2.3-test",
            "installation-test-01",
            Guid.Parse("5846b359-0279-4cd5-a5e6-c08592ef210f"),
            Guid.Parse("74beea75-c789-40f2-9fbe-18f70a8ab055"));

    private static UserAccountAssociationRecord ExistingAssociation(string accountId, string state)
        => new(
            Guid.Parse("ecdd5d14-ec89-40c5-9eb0-25656d243462").ToString("D"),
            "S-1-5-21-test",
            accountId,
            state,
            Now.AddDays(-30),
            Now.AddDays(-10),
            "41",
            Now.AddDays(-1),
            Now.AddDays(-1),
            "account.v1",
            4,
            Now.AddDays(-1),
            Guid.Parse("6fd30d0b-edaa-448d-99ac-f0436190b236").ToString("D"));

    private static AccountSecretEnvelope OldSecret(string accountId = "acc_test_01")
        => new()
        {
            AccountId = accountId,
            RefreshToken = "OLD_UNUSABLE_REFRESH",
            RefreshTokenFamilyId = "old-family",
            RefreshIssuedUtc = Now.AddDays(-30),
            RefreshAbsoluteExpiryUtc = Now.AddDays(30),
            LastTrustedServerUtc = Now.AddDays(-1),
            OfflineEntitlementAssertion = "old.offline.assertion",
            OfflineAssertionStoredUtc = Now.AddDays(-1)
        };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FixedWindowsUserContext(string sid) : IWindowsUserContext
    {
        public string GetCurrentUserSid() => sid;
    }

    private sealed class RecordingAssociationStore(
        List<string> trace,
        UserAccountAssociationRecord? current) : IUserAccountAssociationStore
    {
        public Task<UserAccountAssociationRecord?> GetAccountAssociationAsync(CancellationToken cancellationToken = default)
        {
            trace.Add("association.read");
            return Task.FromResult(current);
        }

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
                current,
                current?.Revision,
                "Not used by reauthentication tests."));

        public Task<UserAssociationWriteOutcome> MarkReauthRequiredAsync(
            int expectedRevision,
            Guid operationId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new UserAssociationWriteOutcome(
                UserAssociationWriteDisposition.Unchanged,
                current,
                current?.Revision,
                "Not used by reauthentication tests."));
    }

    private sealed class RecordingReactivationStore(
        List<string> trace,
        UserAccountAssociationRecord current) : IUserAccountAssociationReactivationStore
    {
        public UserAccountAssociationRecord? Current { get; private set; } = current;
        public int Calls { get; private set; }
        public UserAssociationWriteDisposition Disposition { get; init; } = UserAssociationWriteDisposition.Applied;

        public Task<UserAssociationWriteOutcome> ReactivateSameAccountAsync(
            string associationId,
            string windowsUserSid,
            string accountId,
            int expectedRevision,
            DateTimeOffset authenticatedUtc,
            string secretReference,
            string? entitlementVersion,
            DateTimeOffset? entitlementObservedUtc,
            DateTimeOffset? lastServerUtc,
            Guid operationId,
            CancellationToken cancellationToken = default)
        {
            trace.Add("association.reactivate");
            Calls++;
            if (Disposition != UserAssociationWriteDisposition.Applied)
            {
                return Task.FromResult(new UserAssociationWriteOutcome(
                    Disposition,
                    Current,
                    Current?.Revision,
                    "Synthetic reactivation race."));
            }

            Current = Current! with
            {
                AssociationState = "ACTIVE",
                LastAuthenticatedUtc = authenticatedUtc,
                LastEntitlementVersion = entitlementVersion,
                LastEntitlementObservedUtc = entitlementObservedUtc,
                LastServerUtc = lastServerUtc,
                SecretReference = secretReference,
                Revision = expectedRevision + 1,
                UpdatedUtc = authenticatedUtc,
                UpdatedByOperationId = operationId.ToString("D")
            };
            return Task.FromResult(new UserAssociationWriteOutcome(
                UserAssociationWriteDisposition.Applied,
                Current,
                Current.Revision,
                null));
        }
    }

    private sealed class RecordingSecretStore(
        List<string> trace,
        AccountSecretEnvelope? initial) : IAccountSecretStore
    {
        public AccountSecretEnvelope? Stored { get; private set; } = initial;
        public int WriteCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public int? FailDeleteCallNumber { get; init; }

        public Task<AccountSecretReadResult> ReadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Stored is null
                ? AccountSecretReadResult.Missing()
                : AccountSecretReadResult.Available(Stored));

        public Task WriteAsync(AccountSecretEnvelope secret, CancellationToken cancellationToken = default)
        {
            trace.Add("secret.write");
            WriteCalls++;
            Stored = secret;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(CancellationToken cancellationToken = default)
        {
            trace.Add("secret.delete");
            DeleteCalls++;
            if (FailDeleteCallNumber == DeleteCalls)
            {
                throw new IOException("Synthetic secret delete failure.");
            }

            Stored = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FixtureHandler(
        List<string> trace,
        string accountId = "acc_test_01",
        string accountStatus = "ACTIVE",
        HttpStatusCode entitlementStatusCode = HttpStatusCode.OK,
        string entitlementErrorCode = "TEMPORARILY_UNAVAILABLE") : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (request.RequestUri == AccountEndpoint)
            {
                trace.Add("account");
                return Task.FromResult(Json(HttpStatusCode.OK, new
                {
                    accountId,
                    status = accountStatus,
                    displayName = "Daniel",
                    email = "user@example.test",
                    emailVerified = true,
                    createdUtc = Now.AddYears(-1).ToString("O")
                }));
            }

            if (request.RequestUri == EntitlementEndpoint)
            {
                trace.Add("entitlement");
                if (entitlementStatusCode != HttpStatusCode.OK)
                {
                    return Task.FromResult(Error(entitlementStatusCode, entitlementErrorCode));
                }

                return Task.FromResult(Json(HttpStatusCode.OK, new
                {
                    accountId,
                    entitlementVersion = 42,
                    plan = "PRO",
                    status = "ACTIVE",
                    validFrom = Now.AddDays(-1).ToString("O"),
                    validUntil = Now.AddDays(30).ToString("O"),
                    capabilities = new[] { "runtime.managed_modes", "game.launcher" },
                    offlineEligible = true,
                    serverUtc = Now.ToString("O")
                }));
            }

            return Task.FromResult(Error(HttpStatusCode.NotFound, "NOT_FOUND"));
        }

        private static HttpResponseMessage Error(HttpStatusCode status, string code)
            => Json(status, new
            {
                error = new
                {
                    code,
                    message = "Human text must not drive behavior.",
                    retryable = status == HttpStatusCode.TooManyRequests || (int)status >= 500,
                    correlationId = "server-correlation"
                }
            });

        private static HttpResponseMessage Json(HttpStatusCode status, object value)
            => new(status)
            {
                Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
            };
    }
}
