using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.Authentication;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class NativeAuthTransactionManagerTests
{
    private static readonly Uri Redirect = new("http://127.0.0.1:49152/oauth/callback");

    [TestMethod]
    public void StartCreatesPkceS256MemoryOnlyTransaction()
    {
        var manager = CreateManager(out _);

        var result = manager.Start(Redirect);
        var transaction = result.Transaction;
        var query = ParseQuery(transaction.AuthorizationUri);

        Assert.AreEqual(NativeAuthStartDisposition.Started, result.Disposition);
        Assert.AreEqual(43, transaction.CodeVerifier.Length);
        Assert.AreEqual("S256", transaction.CodeChallengeMethod);
        Assert.AreEqual(ExpectedChallenge(transaction.CodeVerifier), transaction.CodeChallenge);
        Assert.AreEqual("code", query["response_type"]);
        Assert.AreEqual("splitos-windows-native-v1", query["client_id"]);
        Assert.AreEqual(Redirect.AbsoluteUri, query["redirect_uri"]);
        Assert.AreEqual("openid profile email", query["scope"]);
        Assert.AreEqual(transaction.State, query["state"]);
        Assert.AreEqual(transaction.Nonce, query["nonce"]);
        Assert.AreEqual(transaction.CodeChallenge, query["code_challenge"]);
        Assert.AreEqual("S256", query["code_challenge_method"]);
        Assert.IsTrue(transaction.WindowsUserSidReference.StartsWith("sha256:", StringComparison.Ordinal));
        Assert.IsFalse(transaction.WindowsUserSidReference.Contains("S-1-5-21-test", StringComparison.Ordinal));
    }

    [TestMethod]
    public void SecondStartReturnsExistingTransactionInsteadOfCreatingCompetingAuthAttempt()
    {
        var manager = CreateManager(out _);

        var first = manager.Start(Redirect);
        var second = manager.Start(new Uri("http://127.0.0.1:49153/oauth/callback"));

        Assert.AreEqual(NativeAuthStartDisposition.AlreadyInProgress, second.Disposition);
        Assert.AreEqual(first.Transaction.AuthTransactionId, second.Transaction.AuthTransactionId);
        Assert.AreEqual(first.Transaction.State, second.Transaction.State);
        Assert.AreEqual(first.Transaction.CodeVerifier, second.Transaction.CodeVerifier);
    }

    [TestMethod]
    public void ValidCallbackReturnsSingleUseCodeExchangeContext()
    {
        var manager = CreateManager(out _);
        var transaction = manager.Start(Redirect).Transaction;
        var callback = Callback(transaction, "code=authorization-code-123");

        var accepted = manager.ConsumeCallback(callback);
        var repeated = manager.ConsumeCallback(callback);

        Assert.AreEqual(NativeAuthCallbackDisposition.Accepted, accepted.Disposition);
        Assert.AreEqual("AUTH_CODE_ACCEPTED", accepted.ProductCode);
        Assert.IsNotNull(accepted.ExchangeContext);
        Assert.AreEqual("authorization-code-123", accepted.ExchangeContext.AuthorizationCode);
        Assert.AreEqual(transaction.CodeVerifier, accepted.ExchangeContext.CodeVerifier);
        Assert.AreEqual(transaction.Nonce, accepted.ExchangeContext.Nonce);
        Assert.AreEqual(Redirect, accepted.ExchangeContext.RedirectUri);
        Assert.AreEqual("splitos-windows-native-v1", accepted.ExchangeContext.ClientId);
        Assert.AreEqual(NativeAuthCallbackDisposition.NoActiveTransaction, repeated.Disposition);
        Assert.IsNull(manager.GetActiveSnapshot());
    }

    [TestMethod]
    public void StateMismatchDestroysTransactionAndFailsClosed()
    {
        var manager = CreateManager(out _);
        var transaction = manager.Start(Redirect).Transaction;
        var callback = new Uri($"{Redirect}?code=authorization-code&state=wrong-{transaction.State}");

        var result = manager.ConsumeCallback(callback);

        Assert.AreEqual(NativeAuthCallbackDisposition.ResultRejected, result.Disposition);
        Assert.AreEqual("AUTH_RESULT_REJECTED", result.ProductCode);
        Assert.IsNull(result.ExchangeContext);
        Assert.IsNull(manager.GetActiveSnapshot());
    }

    [TestMethod]
    public void ExpiredTransactionRejectsCallbackAndAllowsFreshStart()
    {
        var manager = CreateManager(out var clock);
        var first = manager.Start(Redirect).Transaction;
        clock.Advance(TimeSpan.FromMinutes(10));

        var expired = manager.ConsumeCallback(Callback(first, "code=late-code"));
        var restarted = manager.Start(new Uri("http://127.0.0.1:49153/oauth/callback"));

        Assert.AreEqual(NativeAuthCallbackDisposition.Expired, expired.Disposition);
        Assert.AreEqual("AUTH_TIMEOUT", expired.ProductCode);
        Assert.AreEqual(NativeAuthStartDisposition.Started, restarted.Disposition);
        Assert.AreNotEqual(first.AuthTransactionId, restarted.Transaction.AuthTransactionId);
    }

    [TestMethod]
    public void KnownAuthorizationServerErrorsMapToControlledProductOutcomes()
    {
        var manager = CreateManager(out _);
        var transaction = manager.Start(Redirect).Transaction;

        var result = manager.ConsumeCallback(Callback(transaction, "error=access_denied"));

        Assert.AreEqual(NativeAuthCallbackDisposition.Cancelled, result.Disposition);
        Assert.AreEqual("AUTH_CANCELLED", result.ProductCode);
        Assert.IsNull(result.ExchangeContext);
    }

    [TestMethod]
    public void RedirectMustUseExactIpv4LoopbackCallbackShape()
    {
        var manager = CreateManager(out _);

        AssertThrows<ArgumentException>(() => manager.Start(new Uri("http://localhost:49152/oauth/callback")));
        AssertThrows<ArgumentException>(() => manager.Start(new Uri("https://127.0.0.1:49152/oauth/callback")));
        AssertThrows<ArgumentException>(() => manager.Start(new Uri("http://127.0.0.1:49152/other")));
        AssertThrows<ArgumentException>(() => manager.Start(new Uri("http://127.0.0.1/oauth/callback")));
    }

    [TestMethod]
    public void SensitiveAuthObjectsDoNotRenderTransactionSecrets()
    {
        var manager = CreateManager(out _);
        var transaction = manager.Start(Redirect).Transaction;
        var callback = manager.ConsumeCallback(Callback(transaction, "code=very-secret-auth-code"));

        var transactionText = transaction.ToString();
        var callbackText = callback.ToString();
        var exchangeText = callback.ExchangeContext!.ToString();

        Assert.IsFalse(transactionText.Contains(transaction.State, StringComparison.Ordinal));
        Assert.IsFalse(transactionText.Contains(transaction.Nonce, StringComparison.Ordinal));
        Assert.IsFalse(transactionText.Contains(transaction.CodeVerifier, StringComparison.Ordinal));
        Assert.IsFalse(callbackText.Contains("very-secret-auth-code", StringComparison.Ordinal));
        Assert.IsFalse(exchangeText.Contains("very-secret-auth-code", StringComparison.Ordinal));
        Assert.IsFalse(exchangeText.Contains(transaction.CodeVerifier, StringComparison.Ordinal));
        Assert.IsFalse(exchangeText.Contains(transaction.Nonce, StringComparison.Ordinal));
    }

    [TestMethod]
    public void InvalidClientConfigurationFailsBeforeAnyTransactionCanStart()
    {
        AssertThrows<ArgumentException>(() => new NativeAuthTransactionManager(
            new NativeAuthClientOptions(
                new Uri("http://auth.example.test/authorize"),
                "splitos-windows-native-v1",
                ["openid"],
                TimeSpan.FromMinutes(10)),
            new FakeWindowsUserContext()));

        AssertThrows<ArgumentOutOfRangeException>(() => new NativeAuthTransactionManager(
            new NativeAuthClientOptions(
                new Uri("https://auth.example.test/authorize"),
                "splitos-windows-native-v1",
                ["openid"],
                TimeSpan.FromMinutes(11)),
            new FakeWindowsUserContext()));
    }

    private static NativeAuthTransactionManager CreateManager(out ManualTimeProvider clock)
    {
        clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 8, 7, 0, 0, TimeSpan.Zero));
        return new NativeAuthTransactionManager(
            new NativeAuthClientOptions(
                new Uri("https://auth.example.test/authorize"),
                "splitos-windows-native-v1",
                ["openid", "profile", "email"],
                TimeSpan.FromMinutes(10)),
            new FakeWindowsUserContext(),
            clock);
    }

    private static TException AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }
        catch (Exception exception)
        {
            Assert.Fail($"Expected {typeof(TException).Name}, got {exception.GetType().Name}.");
            throw;
        }

        Assert.Fail($"Expected {typeof(TException).Name}, but no exception was thrown.");
        throw new InvalidOperationException("Unreachable after failed assertion.");
    }

    private static Uri Callback(NativeAuthTransaction transaction, string outcomeQuery)
        => new($"{transaction.RedirectUri}?{outcomeQuery}&state={Uri.EscapeDataString(transaction.State)}");

    private static string ExpectedChallenge(string verifier)
        => Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static Dictionary<string, string> ParseQuery(Uri uri)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = WebUtility.UrlDecode(separator >= 0 ? pair[..separator] : pair);
            var value = WebUtility.UrlDecode(separator >= 0 ? pair[(separator + 1)..] : string.Empty);
            result.Add(key, value);
        }
        return result;
    }

    private sealed class FakeWindowsUserContext : IWindowsUserContext
    {
        public string GetCurrentUserSid() => "S-1-5-21-test";
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    }
}
