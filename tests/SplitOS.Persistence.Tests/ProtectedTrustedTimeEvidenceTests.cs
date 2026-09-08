using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Persistence.ProtectedSecrets;

namespace SplitOS.Persistence.Tests;

[TestClass]
public sealed class ProtectedTrustedTimeEvidenceTests
{
    [TestMethod]
    public async Task TrustedTimeAndAssertionIdentityRoundTripInsideDpapiEnvelope()
    {
        var root = Path.Combine(Path.GetTempPath(), "SplitOS-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "Secrets", "account.v1.dat");
        try
        {
            var store = new DpapiAccountSecretStore(path);
            var serverUtc = new DateTimeOffset(2026, 9, 8, 15, 25, 0, TimeSpan.Zero);
            var observedLocalUtc = new DateTimeOffset(2026, 9, 8, 15, 25, 3, TimeSpan.Zero);
            await store.WriteAsync(new AccountSecretEnvelope
            {
                AccountId = "acc_test_01",
                RefreshToken = "REFRESH_SECRET",
                RefreshIssuedUtc = serverUtc.AddDays(-1),
                RefreshAbsoluteExpiryUtc = serverUtc.AddDays(30),
                LastTrustedServerUtc = serverUtc,
                LastTrustedServerObservationLocalUtc = observedLocalUtc,
                LastValidAssertionJti = "assertion-test-01",
                OfflineEntitlementAssertion = "header.payload.signature",
                OfflineAssertionStoredUtc = observedLocalUtc
            });

            var read = await store.ReadAsync();

            Assert.AreEqual(AccountSecretReadStatus.Available, read.Status);
            Assert.IsNotNull(read.Secret);
            Assert.AreEqual(serverUtc, read.Secret.LastTrustedServerUtc);
            Assert.AreEqual(observedLocalUtc, read.Secret.LastTrustedServerObservationLocalUtc);
            Assert.AreEqual("assertion-test-01", read.Secret.LastValidAssertionJti);
            Assert.AreEqual("header.payload.signature", read.Secret.OfflineEntitlementAssertion);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
