using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class PowerSchemeApplyCoordinatorTests
{
    private static readonly Guid Balanced = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid Performance = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    [TestMethod]
    public void CatalogRejectsUnsafeDuplicateOrMalformedEntries()
    {
        AssertThrows<ArgumentException>(() => new PowerPolicyCatalogResolver(new[]
        {
            new PowerPolicyCatalogEntry("game-performance", PowerPolicyResolutionKind.Scheme, Performance)
        }));
        AssertThrows<ArgumentException>(() => new PowerPolicyCatalogResolver(new[]
        {
            new PowerPolicyCatalogEntry("GAME_PERFORMANCE", PowerPolicyResolutionKind.Scheme, Performance),
            new PowerPolicyCatalogEntry("GAME_PERFORMANCE", PowerPolicyResolutionKind.Scheme, Balanced)
        }));
        AssertThrows<ArgumentException>(() => new PowerPolicyCatalogResolver(new[]
        {
            new PowerPolicyCatalogEntry("GAME_PERFORMANCE", PowerPolicyResolutionKind.Scheme, null)
        }));
        AssertThrows<ArgumentException>(() => new PowerPolicyCatalogResolver(new[]
        {
            new PowerPolicyCatalogEntry("BASE_DEFAULT", PowerPolicyResolutionKind.NoChange, Balanced)
        }));
    }

    [TestMethod]
    public void UnknownPolicyIsRejectedBeforeAnyWindowsReadOrWrite()
    {
        var query = new SequenceQuery(Array.Empty<Guid>());
        var setter = new RecordingSetter();
        var coordinator = new PowerSchemeApplyCoordinator(
            Catalog(new PowerPolicyCatalogEntry("WORK_BALANCED", PowerPolicyResolutionKind.Scheme, Balanced)),
            query,
            setter);

        var result = coordinator.Apply("GAME_PERFORMANCE");

        Assert.AreEqual(PowerSchemeApplyDisposition.TargetNotFound, result.Disposition);
        Assert.AreEqual("POWER_POLICY_TARGET_NOT_FOUND", result.ProductCode);
        Assert.AreEqual(0, query.Calls);
        Assert.AreEqual(0, setter.Calls);
        Assert.IsFalse(result.OperationAttempted);
    }

    [TestMethod]
    public void DurableExpectedSourceDriftRejectsBeforeNativeMutation()
    {
        var query = new SequenceQuery(new[] { Performance });
        var setter = new RecordingSetter();
        var coordinator = new PowerSchemeApplyCoordinator(
            Catalog(new PowerPolicyCatalogEntry("GAME_PERFORMANCE", PowerPolicyResolutionKind.Scheme, Performance)),
            query,
            setter);

        var result = coordinator.Apply("GAME_PERFORMANCE", Balanced);

        Assert.AreEqual(PowerSchemeApplyDisposition.SourceDrift, result.Disposition);
        Assert.AreEqual("POWER_SCHEME_SOURCE_DRIFT", result.ProductCode);
        Assert.AreEqual(Performance, result.SourceSchemeId);
        Assert.AreEqual(1, query.Calls);
        Assert.AreEqual(0, setter.Calls);
        Assert.IsFalse(result.OperationAttempted);
        Assert.IsFalse(result.IsVerified);
    }

    [TestMethod]
    public void NoChangeStillCapturesActualSourceEvidenceWithoutMutation()
    {
        var query = new SequenceQuery(new[] { Balanced });
        var setter = new RecordingSetter();
        var coordinator = new PowerSchemeApplyCoordinator(
            Catalog(new PowerPolicyCatalogEntry("BASE_DEFAULT", PowerPolicyResolutionKind.NoChange, null)),
            query,
            setter);

        var result = coordinator.Apply("BASE_DEFAULT");

        Assert.AreEqual(PowerSchemeApplyDisposition.NoChangeVerified, result.Disposition);
        Assert.AreEqual(Balanced, result.SourceSchemeId);
        Assert.AreEqual(Balanced, result.ObservedSchemeId);
        Assert.IsNull(result.TargetSchemeId);
        Assert.AreEqual(1, query.Calls);
        Assert.AreEqual(0, setter.Calls);
        Assert.IsTrue(result.IsVerified);
    }

    [TestMethod]
    public void AlreadySatisfiedDoesNotSubmitNativeMutation()
    {
        var query = new SequenceQuery(new[] { Performance });
        var setter = new RecordingSetter();
        var coordinator = new PowerSchemeApplyCoordinator(
            Catalog(new PowerPolicyCatalogEntry("GAME_PERFORMANCE", PowerPolicyResolutionKind.Scheme, Performance)),
            query,
            setter);

        var result = coordinator.Apply("GAME_PERFORMANCE");

        Assert.AreEqual(PowerSchemeApplyDisposition.AlreadySatisfied, result.Disposition);
        Assert.AreEqual(Performance, result.SourceSchemeId);
        Assert.AreEqual(Performance, result.TargetSchemeId);
        Assert.AreEqual(1, query.Calls);
        Assert.AreEqual(0, setter.Calls);
        Assert.IsTrue(result.IsVerified);
    }

    [TestMethod]
    public void NativeSetRequiresFreshMatchingReadBackBeforeVerifiedSuccess()
    {
        var query = new SequenceQuery(new[] { Balanced, Performance });
        var setter = new RecordingSetter();
        var coordinator = new PowerSchemeApplyCoordinator(
            Catalog(new PowerPolicyCatalogEntry("GAME_PERFORMANCE", PowerPolicyResolutionKind.Scheme, Performance)),
            query,
            setter);

        var result = coordinator.Apply("GAME_PERFORMANCE");

        Assert.AreEqual(PowerSchemeApplyDisposition.AppliedVerified, result.Disposition);
        Assert.AreEqual(Balanced, result.SourceSchemeId);
        Assert.AreEqual(Performance, result.TargetSchemeId);
        Assert.AreEqual(Performance, result.ObservedSchemeId);
        Assert.AreEqual(2, query.Calls);
        Assert.AreEqual(1, setter.Calls);
        Assert.AreEqual(Performance, setter.LastSchemeId);
        Assert.IsTrue(result.OperationAttempted);
        Assert.IsTrue(result.IsVerified);
    }

    [TestMethod]
    public void SuccessfulSetWithMismatchedReadBackFailsVerification()
    {
        var query = new SequenceQuery(new[] { Balanced, Balanced });
        var setter = new RecordingSetter();
        var coordinator = new PowerSchemeApplyCoordinator(
            Catalog(new PowerPolicyCatalogEntry("GAME_PERFORMANCE", PowerPolicyResolutionKind.Scheme, Performance)),
            query,
            setter);

        var result = coordinator.Apply("GAME_PERFORMANCE");

        Assert.AreEqual(PowerSchemeApplyDisposition.VerificationFailed, result.Disposition);
        Assert.AreEqual("POWER_SCHEME_READBACK_MISMATCH", result.ProductCode);
        Assert.AreEqual(Balanced, result.ObservedSchemeId);
        Assert.IsFalse(result.IsVerified);
    }

    [TestMethod]
    public void NativeSetErrorStillPerformsFreshReadBackAndNeverReportsSuccess()
    {
        var query = new SequenceQuery(new[] { Balanced, Balanced });
        var setter = new RecordingSetter(errorCode: 87);
        var coordinator = new PowerSchemeApplyCoordinator(
            Catalog(new PowerPolicyCatalogEntry("GAME_PERFORMANCE", PowerPolicyResolutionKind.Scheme, Performance)),
            query,
            setter);

        var result = coordinator.Apply("GAME_PERFORMANCE");

        Assert.AreEqual(PowerSchemeApplyDisposition.OperationRejected, result.Disposition);
        Assert.AreEqual("POWER_SCHEME_SET_REJECTED", result.ProductCode);
        Assert.AreEqual(2, query.Calls);
        Assert.AreEqual(1, setter.Calls);
        Assert.IsTrue(result.OperationAttempted);
        Assert.IsFalse(result.IsVerified);
    }

    private static PowerPolicyCatalogResolver Catalog(params PowerPolicyCatalogEntry[] entries)
        => new(entries);

    private sealed class SequenceQuery(IEnumerable<Guid> values) : IPowerSchemeQuery
    {
        private readonly Queue<Guid> _values = new(values);
        public int Calls { get; private set; }

        public Guid QueryActiveScheme()
        {
            Calls++;
            if (_values.Count == 0)
                throw new AssertFailedException("Unexpected power scheme query.");
            return _values.Dequeue();
        }
    }

    private sealed class RecordingSetter(int errorCode = 0) : IPowerSchemeSetter
    {
        public int Calls { get; private set; }
        public Guid? LastSchemeId { get; private set; }

        public PowerSetSchemeAttempt SetActiveScheme(Guid schemeId)
        {
            Calls++;
            LastSchemeId = schemeId;
            return new PowerSetSchemeAttempt(errorCode);
        }
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
