using System.Net;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.ProtectedSecrets;
using SplitOS.Persistence.User;
using SplitOS.RuntimeHost.Authentication;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class NativeSessionRefreshServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 11, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task SuccessfulRefreshRotatesSecretWithoutExtendingAbsoluteLifetime()
    {
        using var handler = new RefreshHandler(RefreshMode.Success);
        using var http = new HttpClient(handler);
        var associationStore = new FakeAssociationStore(CreateAssociation());
        var original = CreateSecret();
        var secretStore = new FakeSecretStore(original);
        var service = CreateService(http, associationStore, secretStore);

        var result = await service.RefreshAsync();

        Assert.AreEqual(NativeSessionRefreshDisposition.Refreshed, result.Disposition);
        Assert.AreEqual("SESSION_REFRESHED", result.ProductCode);
        Assert.IsNotNull(result.Session);
        Assert.AreEqual("acc_test_01", result.Session.AccountId);
        Assert.AreEqual("ACCESS_R2", result.Session.AccessToken);
        Assert.AreEqual(Now.AddMinutes(15), result.Session.AccessTokenExpiresUtc);
        Assert.IsFalse(result.Session.ToString().Contains("ACCESS_R2", StringComparison.Ordinal));

        Assert.IsNotNull(secretStore.Current);
        Assert.AreEqual("REFRESH_R2", secretStore.Current.RefreshToken);
        Assert.AreEqual(original.RefreshAbsoluteExpiryUtc, secretStore.Current.RefreshAbsoluteExpiryUtc);
        Assert.AreEqual(original.RefreshTokenFamilyId, secretStore.Current.RefreshTokenFamilyId);
        Assert.AreEqual(original.LastTrustedServerUtc, secretStore.Current.LastTrustedServerUtc);
        Assert.AreEqual(original.OfflineEntitlementAssertion, secretStore.Current.OfflineEntitlementAssertion);
        Assert.AreEqual(original.OfflineAssertionStoredUtc, secretStore.Current.OfflineAssertionStoredUtc);
        Assert.AreEqual(Now, secretStore.Current.RefreshIssuedUtc);
        Assert.AreEqual(1, secretStore.WriteCount);
        Assert.AreEqual(0, secretStore.DeleteCount);
        Assert.AreEqual(0, associationStore.MarkReauthCount);

        Assert.AreEqual(1, handler.SendCount);
        Assert.AreEqual(CreateAuthority().TokenEndpoint, handler.LastRequestUri);
        Assert.IsNull(handler.LastAuthorizationHeader);
        StringAssert.Contains(handler.LastFormBody!, "grant_type=refresh_token");
        StringAssert.Contains(handler.LastFormBody!, "refresh_token=REFRESH_R1");
        StringAssert.Contains(handler.LastFormBody!, "client_id=splitos-windows-native-v1");
        Assert.IsFalse(handler.LastFormBody!.Contains("client_secret", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task InvalidGrantClearsReusableEvidenceAndMarksReauthRequired()
    {
        using var handler = new RefreshHandler(RefreshMode.InvalidGrant);
        using var http = new HttpClient(handler);
        var associationStore = new FakeAssociationStore(CreateAssociation());
        var secretStore = new FakeSecretStore(CreateSecret());
        var service = CreateService(http, associationStore, secretStore);

        var result = await service.RefreshAsync();

        Assert.AreEqual(NativeSessionRefreshDisposition.ReauthRequired, result.Disposition);
        Assert.AreEqual("REFRESH_TOKEN_REVOKED", result.ProductCode);
        Assert.IsNull(secretStore.Current);
        Assert.AreEqual(1, secretStore.DeleteCount);
        Assert.AreEqual(1, associationStore.MarkReauthCount);
        Assert.AreEqual("REAUTH_REQUIRED", associationStore.Current!.AssociationState);
    }

    [TestMethod]
    public async Task ExplicitServerFailureKeepsCurrentTokenForBoundedRetry()
    {
        using var handler = new RefreshHandler(RefreshMode.ServiceUnavailable);
        using var http = new HttpClient(handler);
        var associationStore = new FakeAssociationStore(CreateAssociation());
        var secretStore = new FakeSecretStore(CreateSecret());
        var service = CreateService(http, associationStore, secretStore);

        var result = await service.RefreshAsync();

        Assert.AreEqual(NativeSessionRefreshDisposition.BackendUnavailable, result.Disposition);
        Assert.IsTrue(result.Retryable);
        Assert.AreEqual("REFRESH_R1", secretStore.Current!.RefreshToken);
        Assert.AreEqual(0, secretStore.DeleteCount);
        Assert.AreEqual(0, associationStore.MarkReauthCount);
    }

    [TestMethod]
    public async Task RotatedServerTokenWithLocalWriteFailureForcesReauth()
    {
        using var handler = new RefreshHandler(RefreshMode.Success);
        using var http = new HttpClient(handler);
        var associationStore = new FakeAssociationStore(CreateAssociation());
        var secretStore = new FakeSecretStore(CreateSecret()) { ThrowOnWrite = true };
        var service = CreateService(http, associationStore, secretStore);

        var result = await service.RefreshAsync();

        Assert.AreEqual(NativeSessionRefreshDisposition.ReauthRequired, result.Disposition);
        Assert.AreEqual("TOKEN_ROTATED_BUT_LOCAL_PERSIST_FAILED", result.ProductCode);
        Assert.IsNull(secretStore.Current);
        Assert.AreEqual(1, secretStore.WriteCount);
        Assert.AreEqual(1, secretStore.DeleteCount);
        Assert.AreEqual("REAUTH_REQUIRED", associationStore.Current!.AssociationState);
    }

    [TestMethod]
    public async Task MalformedSuccessfulResponseIsTreatedAsAmbiguousRotation()
    {
        using var handler = new RefreshHandler(RefreshMode.MalformedSuccess);
        using var http = new HttpClient(handler);
        var associationStore = new FakeAssociationStore(CreateAssociation());
        var secretStore = new FakeSecretStore(CreateSecret());
        var service = CreateService(http, associationStore, secretStore);

        var result = await service.RefreshAsync();

        Assert.AreEqual(NativeSessionRefreshDisposition.ReauthRequired, result.Disposition);
        Assert.AreEqual("REFRESH_RESULT_INVALID", result.ProductCode);
        Assert.IsNull(secretStore.Current);
        Assert.AreEqual("REAUTH_REQUIRED", associationStore.Current!.AssociationState);
    }

    [TestMethod]
    public async Task TransportFailureIsAmbiguousAndNeverBlindlyReplaysOldToken()
    {
        using var handler = new RefreshHandler(RefreshMode.TransportFailure);
        using var http = new HttpClient(handler);
        var associationStore = new FakeAssociationStore(CreateAssociation());
        var secretStore = new FakeSecretStore(CreateSecret());
        var service = CreateService(http, associationStore, secretStore);

        var result = await service.RefreshAsync();

        Assert.AreEqual(NativeSessionRefreshDisposition.ReauthRequired, result.Disposition);
        Assert.AreEqual("REFRESH_RESULT_UNKNOWN", result.ProductCode);
        Assert.IsNull(secretStore.Current);
        Assert.AreEqual("REAUTH_REQUIRED", associationStore.Current!.AssociationState);
    }

    [TestMethod]
    public async Task ExpiredProtectedRefreshTokenNeverReachesNetwork()
    {
        using var handler = new RefreshHandler(RefreshMode.Success);
        using var http = new HttpClient(handler);
        var associationStore = new FakeAssociationStore(CreateAssociation());
        var expired = CreateSecret(Now.AddSeconds(-1));
        var secretStore = new FakeSecretStore(expired);
        var service = CreateService(http, associationStore, secretStore);

        var result = await service.RefreshAsync();

        Assert.AreEqual(NativeSessionRefreshDisposition.ReauthRequired, result.Disposition);
        Assert.AreEqual("REFRESH_TOKEN_EXPIRED", result.ProductCode);
        Assert.AreEqual(0, handler.SendCount);
        Assert.IsNull(secretStore.Current);
        Assert.AreEqual("REAUTH_REQUIRED", associationStore.Current!.AssociationState);
    }

    [TestMethod]
    public async Task ConcurrentRefreshAttemptCannotCreateCompetingRotation()
    {
        using var handler = new RefreshHandler(RefreshMode.BlockedSuccess);
        using var http = new HttpClient(handler);
        var associationStore = new FakeAssociationStore(CreateAssociation());
        var secretStore = new FakeSecretStore(CreateSecret());
        var service = CreateService(http, associationStore, secretStore);

        var first = service.RefreshAsync();
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var second = await service.RefreshAsync();
        Assert.AreEqual(NativeSessionRefreshDisposition.AlreadyInProgress, second.Disposition);
        Assert.AreEqual("SESSION_REFRESH_ALREADY_IN_PROGRESS", second.ProductCode);
        Assert.AreEqual(1, handler.SendCount);

        handler.Release.TrySetResult(true);
        var firstResult = await first;
        Assert.AreEqual(NativeSessionRefreshDisposition.Refreshed, firstResult.Disposition);
    }

    private static NativeSessionRefreshService CreateService(
        HttpClient http,
        FakeAssociationStore associationStore,
        FakeSecretStore secretStore)
        => new(
            http,
            CreateAuthority(),
            associationStore,
            secretStore,
            new FakeWindowsUserContext("S-1-5-21-test"),
            new RuntimeStateRefreshSignal(),
            new FixedTimeProvider(Now));

    private static NativeAuthAuthorityConfiguration CreateAuthority()
        => new(
            new Uri("https://identity.example.test/"),
            new Uri("https://identity.example.test/.well-known/openid-configuration"),
            new Uri("https://identity.example.test/oauth/authorize"),
            new Uri("https://identity.example.test/oauth/token"),
            new Uri("https://identity.example.test/.well-known/jwks.json"),
            "splitos-windows-native-v1",
            new[] { "openid", "profile", "offline_access" },
            new[] { "RS256" },
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(10));

    private static UserAccountAssociationRecord CreateAssociation()
        => new(
            Guid.Parse("413d4610-a0f0-4e5f-8072-f5c949c7be46").ToString("D"),
            "S-1-5-21-test",
            "acc_test_01",
            "ACTIVE",
            Now.AddDays(-30),
            Now.AddDays(-30),
            "42",
            Now.AddMinutes(-10),
            Now.AddMinutes(-10),
            "account.v1",
            3,
            Now.AddMinutes(-10),
            Guid.NewGuid().ToString("D"));

    private static AccountSecretEnvelope CreateSecret(DateTimeOffset? absoluteExpiryUtc = null)
        => new()
        {
            AccountId = "acc_test_01",
            RefreshToken = "REFRESH_R1",
            RefreshTokenFamilyId = "family-01",
            RefreshIssuedUtc = Now.AddDays(-30),
            RefreshAbsoluteExpiryUtc = absoluteExpiryUtc ?? Now.AddDays(60),
            LastTrustedServerUtc = Now.AddMinutes(-10),
            OfflineEntitlementAssertion = "header.payload.signature",
            OfflineAssertionStoredUtc = Now.AddDays(-1)
        };

    private enum RefreshMode
    {
        Success,
        InvalidGrant,
        ServiceUnavailable,
        MalformedSuccess,
        TransportFailure,
        BlockedSuccess
    }

    private sealed class RefreshHandler(RefreshMode mode) : HttpMessageHandler
    {
        public int SendCount { get; private set; }
        public Uri? LastRequestUri { get; private set; }
        public string? LastAuthorizationHeader { get; private set; }
        public string? LastFormBody { get; private set; }
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SendCount++;
            LastRequestUri = request.RequestUri;
            LastAuthorizationHeader = request.Headers.Authorization?.ToString();
            LastFormBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Started.TrySetResult(true);

            if (mode == RefreshMode.TransportFailure)
            {
                throw new HttpRequestException("simulated transport ambiguity");
            }

            if (mode == RefreshMode.BlockedSuccess)
            {
                await Release.Task.WaitAsync(cancellationToken);
            }

            return mode switch
            {
                RefreshMode.InvalidGrant => Json(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}"),
                RefreshMode.ServiceUnavailable => Json(HttpStatusCode.ServiceUnavailable, "{\"error\":\"temporarily_unavailable\"}"),
                RefreshMode.MalformedSuccess => Json(HttpStatusCode.OK, "{\"access_token\":\"ACCESS_R2\",\"token_type\":\"Bearer\",\"expires_in\":900}"),
                _ => Json(HttpStatusCode.OK, "{\"access_token\":\"ACCESS_R2\",\"token_type\":\"Bearer\",\"expires_in\":900,\"refresh_token\":\"REFRESH_R2\",\"scope\":\"openid profile offline_access\"}")
            };
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json)
            => new(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
    }

    private sealed class FakeAssociationStore(UserAccountAssociationRecord? current) : IUserAccountAssociationStore
    {
        public UserAccountAssociationRecord? Current { get; private set; } = current;
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
            => Task.FromResult(new UserAssociationWriteOutcome(
                UserAssociationWriteDisposition.AlreadyExists,
                Current,
                Current?.Revision,
                "not used by refresh tests"));

        public Task<UserAssociationWriteOutcome> MarkReauthRequiredAsync(
            int expectedRevision,
            Guid operationId,
            CancellationToken cancellationToken = default)
        {
            MarkReauthCount++;
            if (Current is null)
            {
                return Task.FromResult(new UserAssociationWriteOutcome(UserAssociationWriteDisposition.Missing, null, null, null));
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
                UpdatedUtc = Now,
                UpdatedByOperationId = operationId.ToString("D")
            };
            return Task.FromResult(new UserAssociationWriteOutcome(
                UserAssociationWriteDisposition.Applied,
                Current,
                Current.Revision,
                null));
        }
    }

    private sealed class FakeSecretStore(AccountSecretEnvelope? initial) : IAccountSecretStore
    {
        public AccountSecretEnvelope? Current { get; private set; } = initial;
        public int WriteCount { get; private set; }
        public int DeleteCount { get; private set; }
        public bool ThrowOnWrite { get; init; }
        public bool ThrowOnDelete { get; init; }

        public Task<AccountSecretReadResult> ReadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Current is null
                ? AccountSecretReadResult.Missing()
                : AccountSecretReadResult.Available(Current));

        public Task WriteAsync(AccountSecretEnvelope secret, CancellationToken cancellationToken = default)
        {
            WriteCount++;
            if (ThrowOnWrite) throw new IOException("simulated DPAPI persistence failure");
            Current = secret;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(CancellationToken cancellationToken = default)
        {
            DeleteCount++;
            if (ThrowOnDelete) throw new IOException("simulated cleanup failure");
            Current = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeWindowsUserContext(string sid) : IWindowsUserContext
    {
        public string GetCurrentUserSid() => sid;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
