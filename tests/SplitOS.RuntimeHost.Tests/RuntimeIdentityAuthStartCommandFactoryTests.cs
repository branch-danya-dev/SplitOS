using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.ProtectedSecrets;
using SplitOS.Persistence.User;
using SplitOS.RuntimeHost.Authentication;
using SplitOS.RuntimeHost.ProductIdentity;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class RuntimeIdentityAuthStartCommandFactoryTests
{
    [TestMethod]
    public void VerifiedMetadataBuildsCompleteRuntimeAuthCoordinator()
    {
        using var http = new HttpClient(new NoNetworkHandler());
        var associationStore = new NoOpAssociationStore();
        var secretStore = new NoOpSecretStore();
        var windowsUser = new FixedWindowsUserContext();
        var factory = new RuntimeIdentityAuthStartCommandFactory(
            http,
            windowsUser,
            new AccountAssociationCoordinator(associationStore, secretStore, windowsUser),
            associationStore,
            new NoOpReactivationStore(),
            secretStore,
            new RuntimeStateRefreshSignal(),
            new OnlineEntitlementEvidenceState(),
            new FixedInstallationIdentityProvider());

        var command = factory.Create(Metadata());

        Assert.IsInstanceOfType<RuntimeAuthStartCoordinator>(command);
    }

    [TestMethod]
    public void InvalidProductApiConfigurationIsRejectedBeforeInteractiveAuthCanStart()
    {
        using var http = new HttpClient(new NoNetworkHandler());
        var associationStore = new NoOpAssociationStore();
        var secretStore = new NoOpSecretStore();
        var windowsUser = new FixedWindowsUserContext();
        var factory = new RuntimeIdentityAuthStartCommandFactory(
            http,
            windowsUser,
            new AccountAssociationCoordinator(associationStore, secretStore, windowsUser),
            associationStore,
            new NoOpReactivationStore(),
            secretStore,
            new RuntimeStateRefreshSignal(),
            new OnlineEntitlementEvidenceState(),
            new FixedInstallationIdentityProvider());
        var valid = Metadata();
        var invalid = valid with
        {
            ProductApi = new ProductApiConfiguration(
                new Uri("https://api.splitos.test/v1/account"),
                new Uri("https://attacker.invalid/v1/entitlements/current"))
        };

        Assert.ThrowsExactly<ArgumentException>(() => factory.Create(invalid));
    }

    [TestMethod]
    public void ProductApiAuthorityDerivesOnlyCodeOwnedV1Paths()
    {
        var configuration = ProductApiConfiguration.FromAuthority(new Uri("https://api.splitos.test/"));

        Assert.AreEqual("https://api.splitos.test/v1/account", configuration.AccountEndpoint.AbsoluteUri);
        Assert.AreEqual(
            "https://api.splitos.test/v1/entitlements/current",
            configuration.CurrentEntitlementEndpoint.AbsoluteUri);
        Assert.ThrowsExactly<ArgumentException>(() =>
            ProductApiConfiguration.FromAuthority(new Uri("https://api.splitos.test/custom/")));
    }

    private static VerifiedNativeAuthAuthorityMetadata Metadata()
        => new(
            NativeAuthReleaseTrustConfiguration.ProductionTrustDomain,
            7,
            3,
            new NativeAuthAuthorityConfiguration(
                new Uri("https://identity.splitos.test/"),
                new Uri("https://identity.splitos.test/.well-known/openid-configuration"),
                new Uri("https://identity.splitos.test/authorize"),
                new Uri("https://identity.splitos.test/token"),
                new Uri("https://identity.splitos.test/.well-known/jwks.json"),
                "splitos-native",
                new[] { "openid", "offline_access" },
                new[] { "RS256" },
                TimeSpan.FromMinutes(2),
                TimeSpan.FromMinutes(10)),
            ProductApiConfiguration.FromAuthority(new Uri("https://api.splitos.test/")));

    private sealed class FixedWindowsUserContext : IWindowsUserContext
    {
        public string GetCurrentUserSid() => "S-1-5-21-1000";
    }

    private sealed class FixedInstallationIdentityProvider : IInstallationIdentityProvider
    {
        public ValueTask<InstallationIdentityReadResult> ReadAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new InstallationIdentityReadResult(
                InstallationIdentityReadStatus.Available,
                "installation-test",
                "INSTALLATION_ID_AVAILABLE"));
    }

    private sealed class NoOpAssociationStore : IUserAccountAssociationStore
    {
        public Task<UserAccountAssociationRecord?> GetAccountAssociationAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

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
            => throw new NotSupportedException();
    }

    private sealed class NoOpReactivationStore : IUserAccountAssociationReactivationStore
    {
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
            => throw new NotSupportedException();
    }

    private sealed class NoOpSecretStore : IAccountSecretStore
    {
        public Task<AccountSecretReadResult> ReadAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task WriteAsync(AccountSecretEnvelope secret, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}
