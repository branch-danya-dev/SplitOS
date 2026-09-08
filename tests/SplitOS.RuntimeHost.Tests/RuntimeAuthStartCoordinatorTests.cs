using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Contracts.Protocol;
using SplitOS.RuntimeHost.Authentication;
using SplitOS.RuntimeHost.ProductIdentity;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class RuntimeAuthStartCoordinatorTests
{
    [TestMethod]
    public async Task AcceptedIdentityCompletesCanonicalLifecycleWithoutReturningTokens()
    {
        var accessToken = "access-secret-value";
        var refreshToken = "refresh-secret-value";
        var auth = new FakeAuthenticationFlow(new InteractiveIdentityAuthenticationResult(
            InteractiveIdentityAuthenticationDisposition.Accepted,
            "AUTH_IDENTITY_VALIDATED",
            Guid.NewGuid(),
            new ValidatedNativeAuthSession(
                "subject-1",
                accessToken,
                DateTimeOffset.UtcNow.AddMinutes(10),
                refreshToken,
                "openid offline_access")));
        var lifecycle = new FakeLifecycleFlow(new InteractiveSessionLifecycleResult(
            InteractiveSessionLifecycleDisposition.Associated,
            "ACCOUNT_ASSOCIATED",
            "account-1",
            "association-1",
            AuthenticatedEntitlementRefreshDisposition.Refreshed));
        var installation = new FakeInstallationIdentityProvider(new InstallationIdentityReadResult(
            InstallationIdentityReadStatus.Available,
            "install-1",
            "INSTALLATION_ID_AVAILABLE"));
        var coordinator = new RuntimeAuthStartCoordinator(auth, lifecycle, installation);
        var correlationId = Guid.NewGuid();
        var operationId = Guid.NewGuid();

        var result = await coordinator.StartAsync(correlationId, operationId);

        Assert.AreEqual("Associated", result.Disposition);
        Assert.AreEqual("ACCOUNT_ASSOCIATED", result.ProductCode);
        Assert.AreEqual("account-1", result.AccountId);
        Assert.AreEqual("association-1", result.AssociationId);
        Assert.IsTrue(result.HasFreshOnlineEntitlement);
        Assert.IsNotNull(lifecycle.ObservedContext);
        Assert.AreEqual("install-1", lifecycle.ObservedContext.InstallationId);
        Assert.AreEqual(correlationId, lifecycle.ObservedContext.CorrelationId);
        Assert.AreEqual(operationId, lifecycle.ObservedContext.OperationId);

        var publicProjection = result.ToString();
        Assert.IsFalse(publicProjection.Contains(accessToken, StringComparison.Ordinal));
        Assert.IsFalse(publicProjection.Contains(refreshToken, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task AuthenticationFailureCannotReachInstallationOrLifecycle()
    {
        var auth = new FakeAuthenticationFlow(new InteractiveIdentityAuthenticationResult(
            InteractiveIdentityAuthenticationDisposition.BackendUnavailable,
            "AUTH_BACKEND_UNAVAILABLE",
            Guid.NewGuid(),
            null));
        var lifecycle = new FakeLifecycleFlow(null);
        var installation = new FakeInstallationIdentityProvider(new InstallationIdentityReadResult(
            InstallationIdentityReadStatus.Available,
            "install-1",
            "INSTALLATION_ID_AVAILABLE"));
        var coordinator = new RuntimeAuthStartCoordinator(auth, lifecycle, installation);

        var result = await coordinator.StartAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.AreEqual("BackendUnavailable", result.Disposition);
        Assert.AreEqual("AUTH_BACKEND_UNAVAILABLE", result.ProductCode);
        Assert.AreEqual(0, installation.ReadCount);
        Assert.AreEqual(0, lifecycle.CallCount);
    }

    [TestMethod]
    public async Task MissingMachineInstallationIdentityFailsClosedBeforeAccountCommit()
    {
        var auth = new FakeAuthenticationFlow(new InteractiveIdentityAuthenticationResult(
            InteractiveIdentityAuthenticationDisposition.Accepted,
            "AUTH_IDENTITY_VALIDATED",
            Guid.NewGuid(),
            new ValidatedNativeAuthSession(
                "subject-1",
                "access-secret",
                DateTimeOffset.UtcNow.AddMinutes(10),
                "refresh-secret",
                "openid offline_access")));
        var lifecycle = new FakeLifecycleFlow(null);
        var installation = new FakeInstallationIdentityProvider(new InstallationIdentityReadResult(
            InstallationIdentityReadStatus.Missing,
            null,
            "INSTALLATION_ID_MISSING"));
        var coordinator = new RuntimeAuthStartCoordinator(auth, lifecycle, installation);

        var result = await coordinator.StartAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.AreEqual("Rejected", result.Disposition);
        Assert.AreEqual("INSTALLATION_ID_MISSING", result.ProductCode);
        Assert.AreEqual(0, lifecycle.CallCount);
    }

    [TestMethod]
    public async Task EmptyEnvelopeIdentityIsRejectedBeforeBrowserAuthentication()
    {
        var auth = new FakeAuthenticationFlow(new InteractiveIdentityAuthenticationResult(
            InteractiveIdentityAuthenticationDisposition.Accepted,
            "AUTH_IDENTITY_VALIDATED",
            Guid.NewGuid(),
            null));
        var lifecycle = new FakeLifecycleFlow(null);
        var installation = new FakeInstallationIdentityProvider(new InstallationIdentityReadResult(
            InstallationIdentityReadStatus.Available,
            "install-1",
            "INSTALLATION_ID_AVAILABLE"));
        var coordinator = new RuntimeAuthStartCoordinator(auth, lifecycle, installation);

        var result = await coordinator.StartAsync(Guid.Empty, Guid.NewGuid());

        Assert.AreEqual("AUTH_REQUEST_ID_INVALID", result.ProductCode);
        Assert.AreEqual(0, auth.CallCount);
    }

    [TestMethod]
    public async Task UnconfiguredProductionCommandFailsClosedWithoutTransactionOrAccountProjection()
    {
        IRuntimeAuthStartCommand command = new UnavailableRuntimeAuthStartCommand();

        var result = await command.StartAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.AreEqual("Unavailable", result.Disposition);
        Assert.AreEqual("AUTH_NOT_CONFIGURED", result.ProductCode);
        Assert.IsNull(result.AuthTransactionId);
        Assert.IsNull(result.AccountId);
        Assert.IsNull(result.AssociationId);
        Assert.IsFalse(result.HasFreshOnlineEntitlement);
    }

    private sealed class FakeAuthenticationFlow(InteractiveIdentityAuthenticationResult result)
        : IInteractiveIdentityAuthenticationFlow
    {
        public int CallCount { get; private set; }

        public Task<InteractiveIdentityAuthenticationResult> AuthenticateAsync(
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeLifecycleFlow(InteractiveSessionLifecycleResult? result)
        : IInteractiveSessionLifecycleCompletionFlow
    {
        public int CallCount { get; private set; }
        public InteractiveSessionLifecycleContext? ObservedContext { get; private set; }

        public Task<InteractiveSessionLifecycleResult> CompleteAsync(
            ValidatedNativeAuthSession session,
            InteractiveSessionLifecycleContext context,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            ObservedContext = context;
            if (result is null)
            {
                throw new AssertFailedException("Lifecycle must not be called in this scenario.");
            }
            return Task.FromResult(result);
        }
    }

    private sealed class FakeInstallationIdentityProvider(InstallationIdentityReadResult result)
        : IInstallationIdentityProvider
    {
        public int ReadCount { get; private set; }

        public ValueTask<InstallationIdentityReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return ValueTask.FromResult(result);
        }
    }
}
