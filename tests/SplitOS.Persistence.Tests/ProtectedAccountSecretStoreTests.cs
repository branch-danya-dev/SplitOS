using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.ProtectedSecrets;

namespace SplitOS.Persistence.Tests;

[TestClass]
public sealed class ProtectedAccountSecretStoreTests
{
    [TestMethod]
    public async Task RoundTripUsesCurrentUserDpapiAndPersistsOpaqueCiphertext()
    {
        using var storage = new TestStorage();
        var path = storage.PathFor("secrets", "account.v1.dat");
        var store = new DpapiAccountSecretStore(path);
        var secret = CreateSecret("refresh-token-one", "offline-assertion-one");

        await store.WriteAsync(secret);

        var persisted = await File.ReadAllBytesAsync(path);
        Assert.IsTrue(persisted.AsSpan().IndexOf(Encoding.UTF8.GetBytes(secret.RefreshToken)) < 0);
        Assert.IsTrue(persisted.AsSpan().IndexOf(Encoding.UTF8.GetBytes(secret.OfflineEntitlementAssertion!)) < 0);

        var read = await store.ReadAsync();
        Assert.AreEqual(AccountSecretReadStatus.Available, read.Status);
        Assert.IsNotNull(read.Secret);
        Assert.AreEqual(secret.AccountId, read.Secret.AccountId);
        Assert.AreEqual(secret.RefreshToken, read.Secret.RefreshToken);
        Assert.AreEqual(secret.RefreshTokenFamilyId, read.Secret.RefreshTokenFamilyId);
        Assert.AreEqual(secret.RefreshIssuedUtc, read.Secret.RefreshIssuedUtc);
        Assert.AreEqual(secret.RefreshAbsoluteExpiryUtc, read.Secret.RefreshAbsoluteExpiryUtc);
        Assert.AreEqual(secret.OfflineEntitlementAssertion, read.Secret.OfflineEntitlementAssertion);
    }

    [TestMethod]
    public async Task RotationAtomicallyReplacesExistingProtectedBlob()
    {
        using var storage = new TestStorage();
        var path = storage.PathFor("secrets", "account.v1.dat");
        var store = new DpapiAccountSecretStore(path);

        await store.WriteAsync(CreateSecret("refresh-token-one", null));
        var firstCiphertext = await File.ReadAllBytesAsync(path);

        await store.WriteAsync(CreateSecret("refresh-token-two", null));
        var secondCiphertext = await File.ReadAllBytesAsync(path);
        var read = await store.ReadAsync();

        Assert.IsFalse(firstCiphertext.SequenceEqual(secondCiphertext));
        Assert.AreEqual(AccountSecretReadStatus.Available, read.Status);
        Assert.AreEqual("refresh-token-two", read.Secret!.RefreshToken);
        Assert.IsFalse(File.Exists(path + ".new"));
    }

    [TestMethod]
    public async Task CorruptedCiphertextFailsClosedAsUnreadable()
    {
        using var storage = new TestStorage();
        var path = storage.PathFor("secrets", "account.v1.dat");
        var store = new DpapiAccountSecretStore(path);

        await store.WriteAsync(CreateSecret("refresh-token-one", null));
        var bytes = await File.ReadAllBytesAsync(path);
        bytes[^1] ^= 0x5A;
        await File.WriteAllBytesAsync(path, bytes);

        var read = await store.ReadAsync();

        Assert.AreEqual(AccountSecretReadStatus.Unreadable, read.Status);
        Assert.IsNull(read.Secret);
    }

    [TestMethod]
    public async Task DeleteRemovesProtectedBlobAndReturnsMissing()
    {
        using var storage = new TestStorage();
        var path = storage.PathFor("secrets", "account.v1.dat");
        var store = new DpapiAccountSecretStore(path);

        await store.WriteAsync(CreateSecret("refresh-token-one", null));
        await store.DeleteAsync();
        var read = await store.ReadAsync();

        Assert.IsFalse(File.Exists(path));
        Assert.IsFalse(File.Exists(path + ".new"));
        Assert.AreEqual(AccountSecretReadStatus.Missing, read.Status);
    }

    [TestMethod]
    public void SecretEnvelopeStringRepresentationIsRedacted()
    {
        var secret = CreateSecret("refresh-token-sensitive", "offline-sensitive");

        var representation = secret.ToString();

        Assert.IsFalse(representation.Contains(secret.RefreshToken, StringComparison.Ordinal));
        Assert.IsFalse(representation.Contains(secret.OfflineEntitlementAssertion!, StringComparison.Ordinal));
        Assert.IsTrue(representation.Contains("[REDACTED]", StringComparison.Ordinal));
    }

    private static AccountSecretEnvelope CreateSecret(string refreshToken, string? offlineAssertion)
    {
        var issued = DateTimeOffset.UtcNow.AddMinutes(-5);
        return new AccountSecretEnvelope
        {
            AccountId = "acc_test",
            RefreshToken = refreshToken,
            RefreshTokenFamilyId = "family_test",
            RefreshIssuedUtc = issued,
            RefreshAbsoluteExpiryUtc = issued.AddDays(30),
            LastTrustedServerUtc = issued.AddMinutes(1),
            OfflineEntitlementAssertion = offlineAssertion,
            OfflineAssertionStoredUtc = offlineAssertion is null ? null : issued.AddMinutes(2)
        };
    }
}
