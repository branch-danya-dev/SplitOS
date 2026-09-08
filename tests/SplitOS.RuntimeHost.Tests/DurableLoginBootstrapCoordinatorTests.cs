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
public sealed class DurableLoginBootstrapCoordinatorTests
{
    private static readonly Uri AccountEndpoint = new("https://api.example.test/v1/account");
    private static readonly Uri EntitlementEndpoint = new("https://api.example.test/v1/entitlements/current");
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 11, 15, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task InitialLoginPersistsSecretBeforeAssociationAndPublishesResolvedEvidence()
    {
        var trace = new List<string>();
        var associationStore = new RecordingAssociationStore(trace);
        var secretStore = new RecordingSecretStore(trace);
        using var handler = new FixtureHandler(trace);
        using var http = new HttpClient(handler);
        var coordinator = CreateCoordinator(http, associationStore, secretStore);

        var result = await coordinator.BootstrapInitialAssociationAsync(CreateSession(), CreateContext());

        Assert.AreEqual(DurableLoginBootstrapDisposition.Associated, result.Disposition);
        Assert.AreEqual("ACCOUNT_ASSOCIATED", result.ProductCode);
        Assert.AreEqual("acc_test_01", result.AccountId);
        Assert.IsTrue(result.EntitlementResolved);
        Assert.IsTrue(result.IsDurablyAssociated);
        Assert.AreEqual(1, associationStore.CreateCalls);
        Assert.IsNotNull(associationStore.Current);
        Assert.AreEqual("ACTIVE", associationStore.Current.AssociationState);
        Assert.AreEqual("42", associationStore.Current.LastEntitlementVersion);
        Assert.AreEqual(Now, associationStore.Current.LastEntitlementObservedUtc);
        Assert.AreEqual(Now, associationStore.Current.LastServerUtc);
        Assert.AreEqual("account.v1", associationStore.Current.SecretReference);

        Assert.IsNotNull(secretStore.Stored);
        Assert.AreEqual("acc_test_01", secretStore.Stored.AccountId);
        Assert.AreEqual("REFRESH_SECRET", secretStore.Stored.RefreshToken);
        Assert.AreEqual(Now, secretStore.Stored.RefreshIssuedUtc);
        Assert.AreEqual(Now.AddDays(90), secretStore.Stored.RefreshAbsoluteExpiryUtc);
        Assert.AreEqual(Now, secretStore.Stored.LastTrustedServerUtc);

        CollectionAssert.AreEqual(
            new[]
            {
                "association.read",
                "account",
                "entitlement",
                "secret.delete",
                "secret.write",
                "association.create"
            },
            trace);
    }

    [TestMethod]
    public async Task EntitlementUnavailableStillCreatesDegradedAccountAssociationWithoutPremiumProof()
    {
        var trace = new List<string>();
        var associationStore = new RecordingAssociationStore(trace);
        var secretStore = new RecordingSecretStore(trace);
        using var handler = new FixtureHandler(
            trace,
            entitlementStatusCode: HttpStatusCode.ServiceUnavailable,
            entitlementErrorCode: "TEMPORARILY_UNAVAILABLE");
        using var http = new HttpClient(handler);
        var coordinator = CreateCoordinator(http, associationStore, secretStore);

        var result = await coordinator.BootstrapInitialAssociationAsync(CreateSession(), CreateContext());

        Assert.AreEqual(DurableLoginBootstrapDisposition.AssociatedDegraded, result.Disposition);
        Assert.AreEqual("TEMPORARILY_UNAVAILABLE", result.ProductCode);
        Assert.IsFalse(result.EntitlementResolved);
        Assert.IsTrue(result.IsDurablyAssociated);
        Assert.IsNotNull(associationStore.Current);
        Assert.IsNull(associationStore.Current.LastEntitlementVersion);
        Assert.IsNull(associationStore.Current.LastEntitlementObservedUtc);
        Assert.IsNull(associationStore.Current.LastServerUtc);
        Assert.IsNotNull(secretStore.Stored);
        Assert.IsNull(secretStore.Stored.LastTrustedServerUtc);
    }

    [TestMethod]
    public async Task MissingRefreshTokenCannotBecomeDurableAssociation()
    {
        var trace = new List<string>();
        var associationStore = new RecordingAssociationStore(trace);
        var secretStore = new RecordingSecretStore(trace);
        using var handler = new FixtureHandler(trace);
        using var http = new HttpClient(handler);
        var coordinator = CreateCoordinator(http, associationStore, secretStore);
        var session = CreateSession() with { RefreshToken = null };

        var result = await coordinator.BootstrapInitialAssociationAsync(session, CreateContext());

        Assert.AreEqual(DurableLoginBootstrapDisposition.ReusableSessionRequired, result.Disposition);
        Assert.AreEqual("AUTH_REUSABLE_SESSION_REQUIRED", result.ProductCode);
        Assert.AreEqual(0, associationStore.CreateCalls);
        Assert.AreEqual(0, secretStore.WriteCalls);
        Assert.AreEqual(0, handler.RequestCount);
    }

    [TestMethod]
    public async Task DisabledAccountDoesNotPersistCredentialsOrAssociation()
    {
        var trace = new List<string>();
        var associationStore = new RecordingAssociationStore(trace);
        var secretStore = new RecordingSecretStore(trace);
        using var handler = new FixtureHandler(trace, accountStatus: "DISABLED");
        using var http = new HttpClient(handler);
        var coordinator = CreateCoordinator(http, associationStore, secretStore);

        var result = await coordinator.BootstrapInitialAssociationAsync(CreateSession(), CreateContext());

        Assert.AreEqual(DurableLoginBootstrapDisposition.AccountDisabled, result.Disposition);
        Assert.AreEqual("ACCOUNT_DISABLED", result.ProductCode);
        Assert.AreEqual(0, associationStore.CreateCalls);
        Assert.AreEqual(0, secretStore.WriteCalls);
        CollectionAssert.AreEqual(new[] { "association.read", "account" }, trace);
    }

    [TestMethod]
    public async Task EntitlementAuthFailureAbortsBeforeReusableCredentialPersistence()
    {
        var trace = new List<string>();
        var associationStore = new RecordingAssociationStore(trace);
        var secretStore = new RecordingSecretStore(trace);
        using var handler = new FixtureHandler(
            trace,
            entitlementStatusCode: HttpStatusCode.Unauthorized,
            entitlementErrorCode: "TOKEN_REVOKED");
        using var http = new HttpClient(handler);
        var coordinator = CreateCoordinator(http, associationStore, secretStore);

        var result = await coordinator.BootstrapInitialAssociationAsync(CreateSession(), CreateContext());

        Assert.AreEqual(DurableLoginBootstrapDisposition.AuthRequired, result.Disposition);
        Assert.AreEqual("TOKEN_REVOKED", result.ProductCode);
        Assert.AreEqual(0, associationStore.CreateCalls);
        Assert.AreEqual(0, secretStore.WriteCalls);
        CollectionAssert.AreEqual(new[] { "association.read", "account", "entitlement" }, trace);
    }

    [TestMethod]
    public async Task AssociationCommitFailureDeletesFreshSecretAndDoesNotReportLoginSuccess()
    {
        var trace = new List<string>();
        var associationStore = new RecordingAssociationStore(trace)
        {
            CreateDisposition = UserAssociationWriteDisposition.AlreadyExists
        };
        var secretStore = new RecordingSecretStore(trace);
        using var handler = new FixtureHandler(trace);
        using var http = new HttpClient(handler);
        var coordinator = CreateCoordinator(http, associationStore, secretStore);

        var result = await coordinator.BootstrapInitialAssociationAsync(CreateSession(), CreateContext());

        Assert.AreEqual(DurableLoginBootstrapDisposition.PersistenceFailed, result.Disposition);
        Assert.AreEqual("LOCAL_ASSOCIATION_RACE", result.ProductCode);
        Assert.IsFalse(result.IsDurablyAssociated);
        Assert.AreEqual(1, secretStore.WriteCalls);
        Assert.AreEqual(2, secretStore.DeleteCalls);
        Assert.IsNull(secretStore.Stored);
        CollectionAssert.AreEqual(
            new[]
            {
                "association.read",
                "account",
                "entitlement",
                "secret.delete",
                "secret.write",
                "association.create",
                "secret.delete"
            },
            trace);
    }

    [TestMethod]
    public async Task FailedSecretRollbackEscalatesToRecoveryRequiredInsteadOfClaimingAssociation()
    {
        var trace = new List<string>();
        var associationStore = new RecordingAssociationStore(trace)
        {
            CreateDisposition = UserAssociationWriteDisposition.AlreadyExists
        };
        var secretStore = new RecordingSecretStore(trace)
        {
            FailDeleteCallNumber = 2
        };
        using var handler = new FixtureHandler(trace);
        using var http = new HttpClient(handler);
        var coordinator = CreateCoordinator(http, associationStore, secretStore);

        var result = await coordinator.BootstrapInitialAssociationAsync(CreateSession(), CreateContext());

        Assert.AreEqual(DurableLoginBootstrapDisposition.RecoveryRequired, result.Disposition);
        Assert.AreEqual("LOCAL_SECRET_RECONCILIATION_REQUIRED", result.ProductCode);
        Assert.IsFalse(result.IsDurablyAssociated);
        Assert.IsNotNull(secretStore.Stored);
        Assert.IsNull(associationStore.Current);
    }

    [TestMethod]
    public async Task ExistingDifferentAccountRequiresExplicitSwitchWithoutTouchingSecrets()
    {
        var trace = new List<string>();
        var associationStore = new RecordingAssociationStore(trace)
        {
            Current = ExistingAssociation("acc_old", "ACTIVE")
        };
        var secretStore = new RecordingSecretStore(trace);
        using var handler = new FixtureHandler(trace);
        using var http = new HttpClient(handler);
        var coordinator = CreateCoordinator(http, associationStore, secretStore);

        var result = await coordinator.BootstrapInitialAssociationAsync(CreateSession(), CreateContext());

        Assert.AreEqual(DurableLoginBootstrapDisposition.AccountSwitchRequired, result.Disposition);
        Assert.AreEqual("ACCOUNT_SWITCH_REQUIRED", result.ProductCode);
        Assert.AreEqual("acc_old", result.AccountId);
        Assert.AreEqual(0, secretStore.DeleteCalls);
        Assert.AreEqual(0, secretStore.WriteCalls);
        Assert.AreEqual(0, associationStore.CreateCalls);
        CollectionAssert.AreEqual(new[] { "association.read", "account" }, trace);
    }

    [TestMethod]
    public async Task ExistingSameAccountIsNotSilentlyRewrittenByInitialBootstrap()
    {
        var trace = new List<string>();
        var associationStore = new RecordingAssociationStore(trace)
        {
            Current = ExistingAssociation("acc_test_01", "REAUTH_REQUIRED")
        };
        var secretStore = new RecordingSecretStore(trace);
        using var handler = new FixtureHandler(trace);
        using var http = new HttpClient(handler);
        var coordinator = CreateCoordinator(http, associationStore, secretStore);

        var result = await coordinator.BootstrapInitialAssociationAsync(CreateSession(), CreateContext());

        Assert.AreEqual(DurableLoginBootstrapDisposition.AlreadyAssociated, result.Disposition);
        Assert.AreEqual("ACCOUNT_ALREADY_ASSOCIATED", result.ProductCode);
        Assert.AreEqual(0, secretStore.DeleteCalls);
        Assert.AreEqual(0, secretStore.WriteCalls);
        Assert.AreEqual(0, associationStore.CreateCalls);
        CollectionAssert.AreEqual(new[] { "association.read", "account" }, trace);
    }

    [TestMethod]
    public async Task ExpiredAccessTokenIsRejectedBeforeBackendAndPersistence()
    {
        var trace = new List<string>();
        var associationStore = new RecordingAssociationStore(trace);
        var secretStore = new RecordingSecretStore(trace);
        using var handler = new FixtureHandler(trace);
        using var http = new HttpClient(handler);
        var coordinator = CreateCoordinator(http, associationStore, secretStore);
        var session = CreateSession() with { AccessTokenExpiresUtc = Now };

        var result = await coordinator.BootstrapInitialAssociationAsync(session, CreateContext());

        Assert.AreEqual(DurableLoginBootstrapDisposition.AuthRequired, result.Disposition);
        Assert.AreEqual("AUTH_SESSION_EXPIRED", result.ProductCode);
        Assert.AreEqual(0, handler.RequestCount);
        Assert.AreEqual(0, associationStore.CreateCalls);
        Assert.AreEqual(0, secretStore.WriteCalls);
    }

    private static DurableLoginBootstrapCoordinator CreateCoordinator(
        HttpClient http,
        RecordingAssociationStore associationStore,
        RecordingSecretStore secretStore)
    {
        var productClient = new ProductApiClient(
            http,
            new ProductApiConfiguration(AccountEndpoint, EntitlementEndpoint));
        return new DurableLoginBootstrapCoordinator(
            productClient,
            associationStore,
            secretStore,
            new FixedWindowsUserContext("S-1-5-21-test"),
            new RuntimeStateRefreshSignal(),
            DurableLoginBootstrapPolicy.Default,
            new FixedTimeProvider(Now));
    }

    private static ValidatedNativeAuthSession CreateSession()
        => new(
            "oidc-subject-test",
            "ACCESS_SECRET",
            Now.AddMinutes(15),
            "REFRESH_SECRET",
            "openid offline_access");

    private static DurableLoginBootstrapContext CreateContext()
        => new(
            "1.2.3-test",
            "installation-test-01",
            Guid.Parse("84f20ced-adbc-46bf-9432-598f4cfde213"),
            Guid.Parse("e969b6fd-e28b-4ee5-8e74-d5c3490c5955"));

    private static UserAccountAssociationRecord ExistingAssociation(string accountId, string state)
        => new(
            Guid.Parse("62de3c65-e803-46d7-a8af-b9983693b714").ToString("D"),
            "S-1-5-21-test",
            accountId,
            state,
            Now.AddDays(-10),
            Now.AddDays(-1),
            "41",
            Now.AddDays(-1),
            Now.AddDays(-1),
            "account.v1",
            3,
            Now.AddDays(-1),
            Guid.Parse("f698ac11-7460-4890-b537-c504323cb52a").ToString("D"));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FixedWindowsUserContext(string sid) : IWindowsUserContext
    {
        public string GetCurrentUserSid() => sid;
    }

    private sealed class RecordingAssociationStore(List<string> trace) : IUserAccountAssociationStore
    {
        public UserAccountAssociationRecord? Current { get; set; }
        public UserAssociationWriteDisposition CreateDisposition { get; init; } = UserAssociationWriteDisposition.Applied;
        public int CreateCalls { get; private set; }

        public Task<UserAccountAssociationRecord?> GetAccountAssociationAsync(CancellationToken cancellationToken = default)
        {
            trace.Add("association.read");
            return Task.FromResult(Current);
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
        {
            trace.Add("association.create");
            CreateCalls++;

            if (CreateDisposition != UserAssociationWriteDisposition.Applied)
            {
                var competing = ExistingAssociation("acc_competing", "ACTIVE");
                return Task.FromResult(new UserAssociationWriteOutcome(
                    CreateDisposition,
                    competing,
                    competing.Revision,
                    "Synthetic competing association."));
            }

            Current = new UserAccountAssociationRecord(
                associationId.ToString("D"),
                windowsUserSid,
                accountId,
                "ACTIVE",
                authenticatedUtc,
                authenticatedUtc,
                entitlementVersion,
                entitlementObservedUtc,
                lastServerUtc,
                secretReference,
                1,
                authenticatedUtc,
                operationId.ToString("D"));
            return Task.FromResult(new UserAssociationWriteOutcome(
                UserAssociationWriteDisposition.Applied,
                Current,
                1,
                null));
        }

        public Task<UserAssociationWriteOutcome> MarkReauthRequiredAsync(
            int expectedRevision,
            Guid operationId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new UserAssociationWriteOutcome(
                UserAssociationWriteDisposition.Missing,
                null,
                null,
                "Not used by bootstrap tests."));
    }

    private sealed class RecordingSecretStore(List<string> trace) : IAccountSecretStore
    {
        public AccountSecretEnvelope? Stored { get; private set; }
        public int WriteCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public int? FailDeleteCallNumber { get; init; }
        public bool FailWrite { get; init; }

        public Task<AccountSecretReadResult> ReadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Stored is null
                ? AccountSecretReadResult.Missing()
                : AccountSecretReadResult.Available(Stored));

        public Task WriteAsync(AccountSecretEnvelope secret, CancellationToken cancellationToken = default)
        {
            trace.Add("secret.write");
            WriteCalls++;
            if (FailWrite)
            {
                throw new IOException("Synthetic secret write failure.");
            }

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
        string accountStatus = "ACTIVE",
        HttpStatusCode accountStatusCode = HttpStatusCode.OK,
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
                if (accountStatusCode != HttpStatusCode.OK)
                {
                    return Task.FromResult(Error(accountStatusCode, "TEMPORARILY_UNAVAILABLE"));
                }

                return Task.FromResult(Json(HttpStatusCode.OK, new
                {
                    accountId = "acc_test_01",
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
                    accountId = "acc_test_01",
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
