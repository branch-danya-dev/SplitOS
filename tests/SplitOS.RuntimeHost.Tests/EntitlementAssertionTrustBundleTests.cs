using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.ProductIdentity;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class EntitlementAssertionTrustBundleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 19, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void ActiveAndRotatingKeysAreExposedForVerificationOverlap()
    {
        using var rsa1 = RSA.Create(2048);
        using var rsa2 = RSA.Create(2048);
        var bundle = CreateBundle(
            Key(rsa1, "ent-v1", EntitlementAssertionKeyLifecycle.Active, Now.AddDays(-30)),
            Key(rsa2, "ent-v2", EntitlementAssertionKeyLifecycle.Rotating, Now.AddHours(-1)));

        var trust = bundle.CreateValidationTrust(EntitlementAssertionTrustBundle.ProductionTrustDomain, Now);

        Assert.AreEqual("https://identity.splitos.test", trust.Issuer);
        CollectionAssert.AreEquivalent(new[] { "ent-v1", "ent-v2" }, trust.SigningKeys.Select(key => key.KeyId).ToArray());
        CollectionAssert.AreEquivalent(new[] { SecurityAlgorithms.RsaSha256 }, trust.AllowedAlgorithms.ToArray());
    }

    [TestMethod]
    public void RetiredAndRevokedKeysNeverReachOfflineValidatorTrust()
    {
        using var activeRsa = RSA.Create(2048);
        using var retiredRsa = RSA.Create(2048);
        using var revokedRsa = RSA.Create(2048);
        var bundle = CreateBundle(
            Key(activeRsa, "ent-active", EntitlementAssertionKeyLifecycle.Active, Now.AddDays(-2)),
            Key(retiredRsa, "ent-retired", EntitlementAssertionKeyLifecycle.Retired, Now.AddDays(-30)),
            Key(revokedRsa, "ent-revoked", EntitlementAssertionKeyLifecycle.Revoked, Now.AddDays(-30)));

        var trust = bundle.CreateValidationTrust(EntitlementAssertionTrustBundle.ProductionTrustDomain, Now);

        Assert.AreEqual(1, trust.SigningKeys.Count);
        Assert.AreEqual("ent-active", trust.SigningKeys[0].KeyId);
    }

    [TestMethod]
    public void KeyOutsideItsActivationWindowIsNotTrusted()
    {
        using var currentRsa = RSA.Create(2048);
        using var futureRsa = RSA.Create(2048);
        var bundle = CreateBundle(
            Key(currentRsa, "ent-current", EntitlementAssertionKeyLifecycle.Active, Now.AddDays(-1)),
            Key(futureRsa, "ent-future", EntitlementAssertionKeyLifecycle.Rotating, Now.AddMinutes(5)));

        var trust = bundle.CreateValidationTrust(EntitlementAssertionTrustBundle.ProductionTrustDomain, Now);

        Assert.AreEqual(1, trust.SigningKeys.Count);
        Assert.AreEqual("ent-current", trust.SigningKeys[0].KeyId);
    }

    [TestMethod]
    public void ProductionRuntimeRejectsDevelopmentTrustDomain()
    {
        using var rsa = RSA.Create(2048);
        var bundle = new EntitlementAssertionTrustBundle(
            "splitos-development",
            "https://identity.splitos.test",
            1,
            1,
            new[] { Key(rsa, "dev-ent", EntitlementAssertionKeyLifecycle.Active, Now.AddDays(-1)) });

        AssertThrows<InvalidDataException>(() =>
            bundle.CreateValidationTrust(EntitlementAssertionTrustBundle.ProductionTrustDomain, Now));
    }

    [TestMethod]
    public void DuplicateKeyIdsAreRejectedBeforeAnyKeyCanBecomeAuthority()
    {
        using var rsa1 = RSA.Create(2048);
        using var rsa2 = RSA.Create(2048);
        var bundle = CreateBundle(
            Key(rsa1, "duplicate", EntitlementAssertionKeyLifecycle.Active, Now.AddDays(-1)),
            Key(rsa2, "duplicate", EntitlementAssertionKeyLifecycle.Rotating, Now.AddDays(-1)));

        AssertThrows<InvalidDataException>(() =>
            bundle.CreateValidationTrust(EntitlementAssertionTrustBundle.ProductionTrustDomain, Now));
    }

    [TestMethod]
    public void NoUsableVerificationKeyFailsClosed()
    {
        using var rsa = RSA.Create(2048);
        var bundle = CreateBundle(
            Key(rsa, "ent-retired", EntitlementAssertionKeyLifecycle.Retired, Now.AddDays(-30)));

        AssertThrows<InvalidDataException>(() =>
            bundle.CreateValidationTrust(EntitlementAssertionTrustBundle.ProductionTrustDomain, Now));
    }

    [TestMethod]
    public async Task ProviderUsesOnlyAuthenticatedBundleBoundary()
    {
        using var rsa = RSA.Create(2048);
        var bundle = CreateBundle(
            Key(rsa, "ent-v1", EntitlementAssertionKeyLifecycle.Active, Now.AddDays(-1)));
        var provider = new ReleaseOwnedOfflineEntitlementTrustProvider(
            new FakeBundleProvider(bundle),
            EntitlementAssertionTrustBundle.ProductionTrustDomain,
            new FixedTimeProvider(Now));

        var trust = await provider.ReadAsync();

        Assert.AreEqual(1, trust.SigningKeys.Count);
        Assert.AreEqual("ent-v1", trust.SigningKeys[0].KeyId);
    }

    private static EntitlementAssertionTrustBundle CreateBundle(params EntitlementAssertionTrustKey[] keys)
        => new(
            EntitlementAssertionTrustBundle.ProductionTrustDomain,
            "https://identity.splitos.test",
            7,
            3,
            keys);

    private static EntitlementAssertionTrustKey Key(
        RSA rsa,
        string keyId,
        EntitlementAssertionKeyLifecycle lifecycle,
        DateTimeOffset notBeforeUtc)
        => new(
            new RsaSecurityKey(rsa) { KeyId = keyId },
            SecurityAlgorithms.RsaSha256,
            lifecycle,
            notBeforeUtc,
            null);

    private static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
            Assert.Fail($"Expected exception {typeof(TException).Name}.");
        }
        catch (TException)
        {
        }
    }

    private sealed class FakeBundleProvider(EntitlementAssertionTrustBundle bundle)
        : IEntitlementAssertionTrustBundleProvider
    {
        public ValueTask<EntitlementAssertionTrustBundle> ReadAuthenticatedAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(bundle);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
