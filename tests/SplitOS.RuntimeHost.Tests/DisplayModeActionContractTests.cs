using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.ModeRuntime;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class DisplayModeActionContractTests
{
    [TestMethod]
    public void TopologyExtendDefinitionUsesStableCanonicalSelectorOnly()
    {
        var selector = StrongSelector();
        var definition = DisplayModeActionContract.CreateTopologyExtendDefinition(Guid.NewGuid(), 10, selector);
        var desired = DisplayModeActionContract.DeserializeTopologyExtend(definition.DesiredStateJson!);

        Assert.AreEqual(DisplayModeActionContract.OwningModule, definition.OwningModule);
        Assert.AreEqual(DisplayModeActionContract.TopologyExtendActionType, definition.ActionType);
        Assert.AreEqual(DisplayModeActionContract.TargetRef, definition.TargetRef);
        Assert.AreEqual(DisplayModeActionContract.RollbackClass, definition.RollbackClass);
        Assert.AreEqual(DisplayModeActionContract.TopologyVerificationClass, definition.VerificationClass);
        Assert.AreEqual(selector.PnpDeviceInstanceId, desired.Selector.PnpDeviceInstanceId);
        Assert.AreEqual(
            DisplayModeActionContract.ComputeTopologyExtendDigest(desired),
            definition.DesiredStateDigest);
        Assert.IsFalse(definition.DesiredStateJson!.Contains("adapterLuid", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(definition.DesiredStateJson.Contains("targetId", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(definition.DesiredStateJson.Contains("generation", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void TargetModePreservesExactRationalRefresh()
    {
        var desired = new DisplayTargetModeDesiredState(StrongSelector(), 3840, 2160, 60000, 1001, 1);
        var definition = DisplayModeActionContract.CreateTargetModeDefinition(Guid.NewGuid(), 20, desired);
        var roundTrip = DisplayModeActionContract.DeserializeTargetMode(definition.DesiredStateJson!);

        Assert.AreEqual(60000u, roundTrip.RefreshNumerator);
        Assert.AreEqual(1001u, roundTrip.RefreshDenominator);
        Assert.AreEqual(3840u, roundTrip.Width);
        Assert.AreEqual(2160u, roundTrip.Height);
        Assert.AreEqual(DisplayModeActionContract.TargetModeActionType, definition.ActionType);
        Assert.AreEqual(DisplayModeActionContract.ModeVerificationClass, definition.VerificationClass);
        Assert.AreEqual(DisplayModeActionContract.ComputeTargetModeDigest(roundTrip), definition.DesiredStateDigest);
    }

    [TestMethod]
    public void CanonicalSelectorTrimsStableIdentityStringsDeterministically()
    {
        var selector = StrongSelector() with
        {
            PnpDeviceInstanceId = "  DISPLAY\\ACME123\\INSTANCE0  ",
            MonitorDevicePath = "  ",
            FriendlyMonitorName = "  Panel  "
        };
        var first = DisplayModeActionContract.SerializeTopologyExtend(new(selector));
        var second = DisplayModeActionContract.SerializeTopologyExtend(new(selector with
        {
            PnpDeviceInstanceId = "DISPLAY\\ACME123\\INSTANCE0",
            MonitorDevicePath = null,
            FriendlyMonitorName = "Panel"
        }));

        Assert.AreEqual(first, second);
        var roundTrip = DisplayModeActionContract.DeserializeTopologyExtend(first);
        Assert.AreEqual("DISPLAY\\ACME123\\INSTANCE0", roundTrip.Selector.PnpDeviceInstanceId);
        Assert.IsNull(roundTrip.Selector.MonitorDevicePath);
        Assert.AreEqual("Panel", roundTrip.Selector.FriendlyMonitorName);
    }

    [TestMethod]
    public void PartialEdidSelectorIsRejected()
    {
        var selector = new PersistentDisplaySelector(EdidManufactureId: 1234);
        AssertThrows<ArgumentException>(() =>
            DisplayModeActionContract.SerializeTopologyExtend(new(selector)));
    }

    [TestMethod]
    public void FriendlyNameOnlyRequiresExplicitWeakFallback()
    {
        var selector = new PersistentDisplaySelector(FriendlyMonitorName: "Same Name");
        AssertThrows<ArgumentException>(() =>
            DisplayModeActionContract.SerializeTopologyExtend(new(selector)));

        var allowed = selector with { AllowWeakFallback = true };
        var json = DisplayModeActionContract.SerializeTopologyExtend(new(allowed));
        Assert.AreEqual("Same Name", DisplayModeActionContract.DeserializeTopologyExtend(json).Selector.FriendlyMonitorName);
    }

    [TestMethod]
    public void InvalidTargetModeParametersAreRejected()
    {
        var selector = StrongSelector();
        AssertThrows<ArgumentOutOfRangeException>(() =>
            DisplayModeActionContract.SerializeTargetMode(new(selector, 0, 1080, 60, 1, 1)));
        AssertThrows<ArgumentOutOfRangeException>(() =>
            DisplayModeActionContract.SerializeTargetMode(new(selector, 1920, 1080, 60000, 0, 1)));
        AssertThrows<ArgumentOutOfRangeException>(() =>
            DisplayModeActionContract.SerializeTargetMode(new(selector, 1920, 1080, 60, 1, 5)));
    }

    [TestMethod]
    public void NonCanonicalDesiredJsonIsRejected()
    {
        var canonical = DisplayModeActionContract.SerializeTopologyExtend(new(StrongSelector()));
        var nonCanonical = canonical.Replace(":", ": ", StringComparison.Ordinal);
        Assert.AreNotEqual(canonical, nonCanonical);
        AssertThrows<ArgumentException>(() =>
            DisplayModeActionContract.DeserializeTopologyExtend(nonCanonical));
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

    private static PersistentDisplaySelector StrongSelector() => new(
        PnpDeviceInstanceId: "DISPLAY\\ACME123\\INSTANCE0",
        EdidManufactureId: 1234,
        EdidProductCodeId: 5678,
        ConnectorInstance: 1,
        OutputTechnology: 5,
        FriendlyMonitorName: "Panel");
}
