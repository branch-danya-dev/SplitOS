using System.Net;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.ProtectedSecrets;
using SplitOS.Persistence.User;
using SplitOS.RuntimeHost.Authentication;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class NativeSessionRefreshProofContinuityTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 18, 30, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task SuccessfulRotationPreservesTrustedTimeAndAssertionIdentityMetadata()
    {
        using var http = new HttpClient(new SuccessRefreshHandler());
        var association = CreateAssociation();
        var secret = new AccountSecretEnvelope
        {
            AccountId = association.AccountId,
            RefreshToken = "REFRESH_R1",
            RefreshTokenFamilyId = "family-01",
            RefreshIssuedUtc = Now.AddDays(-2),
            RefreshAbsoluteExpiryUtc = Now.AddDays(60),
            LastTrustedServerUtc = Now.AddMinutes(-20),
            LastTrustedServerObservationLocalUtc = Now.AddMinutes(-19),
            LastValidAssertionJti = "assertion-jti-42",
            OfflineEntitlementAssertion = "header.payload.signature",
            OfflineAssertionStoredUtc = Now.AddHours(-2)
        };
        var secretStore = new MemorySecretStore(secret);
        var service = new NativeSessionRefreshService(
            http,
            CreateAuthority(),
            new MemoryAssociationStore(association),
            secretStore,
            new FixedWindowsUserContext(association.WindowsUserSid),
            new RuntimeStateRefreshSignal(),
            new FixedTimeProvider(Now));

        var result = await service.RefreshAsync();

        Assert.AreEqual(NativeSessionRefreshDisposition.Refreshed, result.Disposition);
        Assert.IsNotNull(secretStore.Current);
        Assert.AreEqual("REFRESH_R2", secretStore.Current.RefreshToken);
        Assert.AreEqual(secret.LastTrustedServerUtc, secretStore.Current.LastTrustedServerUtc);
        Assert.AreEqual(secret.LastTrustedServerObservationLocalUtc, secretStore.Current.LastTrustedServerObservationLocalUtc);
        Assert.AreEqual(secret.LastValidAssertionJti, secretStore.Current.LastValidAssertionJti);
        Assert.AreEqual(secret.OfflineEntitlementAssertion, secretStore.Current.OfflineEntitlementAssertion);
        Assert.AreEqual(secret.OfflineAssertionStoredUtc, secretStore.Current.OfflineAssertionStoredUtc);
        Assert.AreEqual(secret.RefreshAbsoluteExpiryUtc, secretStore.Current.RefreshAbsoluteExpiryUtc);
    }

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
            "S-1-5-21-proof-continuity",
            "acc_test_01",
            "ACTIVE",
            Now.AddDays(-30),
            Now.AddDays(-2),
            "42",
            Now.AddMinutes(-20),
            Now.AddMinutes(-20),
            "account.v1",
            7,
            Now.AddMinutes(-20),
            Guid.NewGuid().ToString("D"));

    private sealed class SuccessRefreshHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"access_token\":\"ACCESS_R2\",\"token_type\":\"Bearer\",\"expires_in\":900,\"refresh_token\":\"REFRESH_R2\",\"scope\":\"openid profile offline_access\"}",
                    Encoding.UTF8,
                    "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class MemorySecretStore(AccountSecretEnvelope initial) : IAccountSecretStore
    {
        public AccountSecretEnvelope? Current { get; private set; } = initial;

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
            Current = null;
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryAssociationStore(UserAccountAssociationRecord current) : IUserAccountAssociationStore
    {
        public Task<UserAccountAssociationRecord?> GetAccountAssociationAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<UserAccountAssociationRecord?>(current);

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
                current.Revision,
                "not used"));

        public Task<UserAssociationWriteOutcome> MarkReauthRequiredAsync(
            int expectedRevision,
            Guid operationId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new UserAssociationWriteOutcome(
                UserAssociationWriteDisposition.Unchanged,
                current,
                current.Revision,
                "not used"));
    }

    private sealed class FixedWindowsUserContext(string sid) : IWindowsUserContext
    {
        public string GetCurrentUserSid() => sid;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
