using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.ModeRuntime;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class PowerModeActionContractTests
{
    private static readonly Guid Balanced = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");

    [TestMethod]
    public void DefinitionUsesCanonicalDurablePowerSemantics()
    {
        var actionId = Guid.NewGuid();

        var definition = PowerModeActionContract.CreateDefinition(
            actionId,
            30,
            "GAME_PERFORMANCE");

        Assert.AreEqual(actionId, definition.ActionId);
        Assert.AreEqual(30, definition.SequenceNo);
        Assert.AreEqual(PowerModeActionContract.OwningModule, definition.OwningModule);
        Assert.AreEqual(PowerModeActionContract.ActionType, definition.ActionType);
        Assert.AreEqual(PowerModeActionContract.TargetRef, definition.TargetRef);
        Assert.AreEqual(PowerModeActionContract.DesiredSchemaVersion, definition.DesiredSchemaVersion);
        Assert.AreEqual(PowerModeActionContract.RollbackClass, definition.RollbackClass);
        Assert.AreEqual(PowerModeActionContract.VerificationClass, definition.VerificationClass);
        Assert.IsTrue(definition.Mandatory);

        var desired = PowerModeActionContract.DeserializeDesired(definition.DesiredStateJson);
        Assert.AreEqual("GAME_PERFORMANCE", desired.PowerPolicyId);
        Assert.AreEqual(
            PowerModeActionContract.ComputeDesiredDigest(desired),
            definition.DesiredStateDigest);
        Assert.IsFalse(definition.DesiredStateJson.Contains(Balanced.ToString("D"), StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void DesiredStateRoundTripIsCanonicalAndDigestStable()
    {
        var desired = new PowerPolicyDesiredState("WORK_BALANCED");

        var json = PowerModeActionContract.SerializeDesired(desired);
        var rehydrated = PowerModeActionContract.DeserializeDesired(json);

        Assert.AreEqual(desired, rehydrated);
        Assert.AreEqual(
            PowerModeActionContract.ComputeDesiredDigest(desired),
            PowerModeActionContract.ComputeDesiredDigest(rehydrated));
    }

    [TestMethod]
    public void NonCanonicalOrWrongVersionDesiredJsonFailsClosed()
    {
        AssertThrows<ArgumentException>(() => PowerModeActionContract.DeserializeDesired(
            "{\"powerPolicyId\":\"WORK_BALANCED\",\"schemaVersion\":1}"));
        AssertThrows<ArgumentException>(() => PowerModeActionContract.DeserializeDesired(
            "{\"schemaVersion\":2,\"powerPolicyId\":\"WORK_BALANCED\"}"));
        AssertThrows<ArgumentException>(() => PowerModeActionContract.DeserializeDesired(
            " {\"schemaVersion\":1,\"powerPolicyId\":\"WORK_BALANCED\"}"));
    }

    [TestMethod]
    public void UnsafeOrMalformedSemanticPolicyIdFailsClosed()
    {
        foreach (var id in new[] { "game-performance", "GAME PERFORMANCE", "", "   ", "GAME/PERFORMANCE" })
        {
            AssertThrows<ArgumentException>(() =>
                PowerModeActionContract.CreateDefinition(Guid.NewGuid(), 1, id));
        }
    }

    [TestMethod]
    public void ExactSourceSchemePreStateRoundTripsWithStableDigest()
    {
        var preState = PowerModeActionContract.CapturePreState(Balanced);

        var json = PowerModeActionContract.SerializePreState(preState);
        var rehydrated = PowerModeActionContract.DeserializePreState(json);

        Assert.AreEqual(Balanced, rehydrated.ActiveSchemeId);
        Assert.AreEqual(
            PowerModeActionContract.ComputePreStateDigest(preState),
            PowerModeActionContract.ComputePreStateDigest(rehydrated));
    }

    [TestMethod]
    public void EmptySourceSchemePreStateFailsClosed()
    {
        AssertThrows<ArgumentException>(() => PowerModeActionContract.CapturePreState(Guid.Empty));
        AssertThrows<ArgumentException>(() => PowerModeActionContract.SerializePreState(new PowerSchemePreState(Guid.Empty)));
    }

    [TestMethod]
    public void NonCanonicalOrWrongVersionPreStateJsonFailsClosed()
    {
        AssertThrows<ArgumentException>(() => PowerModeActionContract.DeserializePreState(
            $"{{\"activeSchemeId\":\"{Balanced:D}\",\"schemaVersion\":1}}"));
        AssertThrows<ArgumentException>(() => PowerModeActionContract.DeserializePreState(
            $"{{\"schemaVersion\":2,\"activeSchemeId\":\"{Balanced:D}\"}}"));
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
