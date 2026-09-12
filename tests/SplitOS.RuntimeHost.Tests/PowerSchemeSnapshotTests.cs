using System.ComponentModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class PowerSchemeSnapshotTests
{
    [TestMethod]
    public void QueryReturnsExactActiveSchemeGuid()
    {
        var expected = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
        var query = new WindowsPowerSchemeQuery(new FakeInterop(new(0, expected)));

        var actual = query.QueryActiveScheme();

        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void QuerySurfacesNativeErrorWithoutInventingFallback()
    {
        var query = new WindowsPowerSchemeQuery(new FakeInterop(new(5, null)));

        var error = AssertThrows<Win32Exception>(() => query.QueryActiveScheme());

        Assert.AreEqual(5, error.NativeErrorCode);
    }

    [TestMethod]
    public void QueryRejectsSuccessfulResponseWithoutUsableSchemeIdentity()
    {
        foreach (var attempt in new[]
                 {
                     new PowerActiveSchemeQueryAttempt(0, null),
                     new PowerActiveSchemeQueryAttempt(0, Guid.Empty)
                 })
        {
            var query = new WindowsPowerSchemeQuery(new FakeInterop(attempt));
            AssertThrows<InvalidDataException>(() => query.QueryActiveScheme());
        }
    }

    [TestMethod]
    public void SnapshotUsesAuthoritativeQueryAndObservationTime()
    {
        var scheme = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
        var observed = new DateTimeOffset(2026, 9, 12, 4, 0, 0, TimeSpan.Zero);
        var reader = new PowerSchemeSnapshotReader(
            new WindowsPowerSchemeQuery(new FakeInterop(new(0, scheme))),
            new FixedTimeProvider(observed));

        var snapshot = reader.Read();

        Assert.AreEqual(scheme, snapshot.ActiveSchemeId);
        Assert.AreEqual(observed, snapshot.ObservedUtc);
    }

    private sealed class FakeInterop(PowerActiveSchemeQueryAttempt result) : IWindowsPowerSchemeInterop
    {
        public PowerActiveSchemeQueryAttempt GetActiveScheme() => result;

        public PowerSetSchemeAttempt SetActiveScheme(Guid schemeId) => new(0);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private static T AssertThrows<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T exception)
        {
            return exception;
        }
        catch (Exception exception)
        {
            Assert.Fail($"Expected {typeof(T).Name}, got {exception.GetType().Name}: {exception.Message}");
            throw;
        }

        Assert.Fail($"Expected {typeof(T).Name}.");
        throw new InvalidOperationException();
    }
}
