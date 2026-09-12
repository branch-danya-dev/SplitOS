using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.ModeRuntime;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class DisplayModeActionPreStateContractTests
{
    [TestMethod]
    public void TopologyPreStatePersistsStablePhysicalTargetSetAndExactModes()
    {
        var snapshot = Snapshot(
            Path(2, "DISPLAY\\B\\0", 1920, 1080, 60, 1),
            Path(1, "DISPLAY\\A\\0", 2560, 1440, 60000, 1001));

        var state = DisplayModeActionPreStateContract.CaptureTopology(snapshot);
        var json = DisplayModeActionPreStateContract.SerializeTopology(state);
        var roundTrip = DisplayModeActionPreStateContract.DeserializeTopology(json);

        Assert.AreEqual(2, roundTrip.ActiveTargets.Count);
        Assert.AreEqual(2, roundTrip.ActiveTargetModes.Count);
        var first = roundTrip.ActiveTargetModes.Single(mode => mode.Selector.PnpDeviceInstanceId == "DISPLAY\\A\\0");
        Assert.AreEqual(2560u, first.Width);
        Assert.AreEqual(1440u, first.Height);
        Assert.AreEqual(60000u, first.RefreshNumerator);
        Assert.AreEqual(1001u, first.RefreshDenominator);
        Assert.IsFalse(json.Contains("targetId", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("generation", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("sourceId", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(
            DisplayModeActionPreStateContract.ComputeTopologyDigest(state),
            DisplayModeActionPreStateContract.ComputeTopologyDigest(roundTrip));
    }

    [TestMethod]
    public void TargetModePreStateBindsExactModeToWholeActiveTopology()
    {
        var first = Path(1, "DISPLAY\\A\\0", 2560, 1440, 60000, 1001, rotation: 1);
        var second = Path(2, "DISPLAY\\B\\0", 1920, 1080, 60, 1, rotation: 1);
        var snapshot = Snapshot(first, second);

        var state = DisplayModeActionPreStateContract.CaptureTargetMode(snapshot, first);
        var json = DisplayModeActionPreStateContract.SerializeTargetMode(state);
        var roundTrip = DisplayModeActionPreStateContract.DeserializeTargetMode(json);

        Assert.AreEqual(2, roundTrip.ActiveTargets.Count);
        Assert.AreEqual(2560u, roundTrip.Width);
        Assert.AreEqual(1440u, roundTrip.Height);
        Assert.AreEqual(60000u, roundTrip.RefreshNumerator);
        Assert.AreEqual(1001u, roundTrip.RefreshDenominator);
        Assert.AreEqual("DISPLAY\\A\\0", roundTrip.Selector.PnpDeviceInstanceId);
        Assert.IsFalse(json.Contains("targetId", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("generation", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void TargetSetsAreOrderIndependentButIdentitySensitive()
    {
        var first = DisplayModeActionPreStateContract.SelectorFromIdentity(Identity("DISPLAY\\A\\0", 1));
        var second = DisplayModeActionPreStateContract.SelectorFromIdentity(Identity("DISPLAY\\B\\0", 2));
        var changed = DisplayModeActionPreStateContract.SelectorFromIdentity(Identity("DISPLAY\\C\\0", 3));

        Assert.IsTrue(DisplayModeActionPreStateContract.TargetSetsEqual(
            new[] { first, second },
            new[] { second, first }));
        Assert.IsFalse(DisplayModeActionPreStateContract.TargetSetsEqual(
            new[] { first, second },
            new[] { first, changed }));
    }

    [TestMethod]
    public void TopologyModesAreOrderIndependentAndRationallyExact()
    {
        var snapshot = Snapshot(
            Path(1, "DISPLAY\\A\\0", 2560, 1440, 60000, 1001),
            Path(2, "DISPLAY\\B\\0", 1920, 1080, 60, 1));
        var expected = DisplayModeActionPreStateContract.CaptureActiveTargetModes(snapshot);
        var equivalent = expected.Reverse().ToArray();
        var changed = equivalent
            .Select(mode => mode.Selector.PnpDeviceInstanceId == "DISPLAY\\A\\0"
                ? mode with { RefreshNumerator = 60, RefreshDenominator = 1 }
                : mode)
            .ToArray();

        Assert.IsTrue(DisplayModeActionPreStateContract.TopologyModesEqual(expected, equivalent));
        Assert.IsFalse(DisplayModeActionPreStateContract.TopologyModesEqual(expected, changed));
    }

    [TestMethod]
    public void TopologyPreStateRejectsModeEvidenceForDifferentPhysicalTargetSet()
    {
        var first = DisplayModeActionPreStateContract.SelectorFromIdentity(Identity("DISPLAY\\A\\0", 1));
        var second = DisplayModeActionPreStateContract.SelectorFromIdentity(Identity("DISPLAY\\B\\0", 2));
        var mismatched = new DisplayTopologyPreState(
            new[] { first },
            new[] { new DisplayTopologyTargetModePreState(second, 1920, 1080, 60, 1, 1) });

        AssertThrows<InvalidDataException>(() => DisplayModeActionPreStateContract.Normalize(mismatched));
    }

    [TestMethod]
    public void CaptureFailsClosedWithoutStableIdentityEvidence()
    {
        var path = Path(1, "DISPLAY\\A\\0") with { Identity = null };
        var snapshot = Snapshot(path);

        AssertThrows<InvalidDataException>(() => DisplayModeActionPreStateContract.CaptureTopology(snapshot));
        AssertThrows<InvalidDataException>(() => DisplayModeActionPreStateContract.CaptureTargetMode(snapshot, path));
    }

    [TestMethod]
    public void CaptureFailsClosedForDynamicRefresh()
    {
        var path = Path(1, "DISPLAY\\A\\0") with { BoostRefreshRate = true };
        var snapshot = Snapshot(path);

        AssertThrows<InvalidDataException>(() => DisplayModeActionPreStateContract.CaptureTopology(snapshot));
        AssertThrows<InvalidDataException>(() => DisplayModeActionPreStateContract.CaptureTargetMode(snapshot, path));
    }

    private static DisplaySnapshot Snapshot(params DisplayPathEvidence[] paths)
        => new(7, DateTimeOffset.UtcNow, paths);

    private static DisplayPathEvidence Path(
        uint targetId,
        string pnp,
        uint width = 1920,
        uint height = 1080,
        uint refreshNumerator = 60,
        uint refreshDenominator = 1,
        uint rotation = 1)
        => new(
            SourceAdapterLuid: 10,
            SourceId: targetId,
            TargetKey: new DisplayPathKey(20, targetId),
            Active: true,
            TargetAvailable: true,
            OutputTechnology: 5,
            Rotation: rotation,
            Scaling: 128,
            RefreshRate: new DisplayRational(refreshNumerator, refreshDenominator),
            SourceResolution: new DisplayPixelSize(width, height),
            SourcePosition: new DisplayDesktopPoint((int)targetId * 100, 0),
            SupportsVirtualMode: true,
            BoostRefreshRate: false,
            Identity: Identity(pnp, targetId));

    private static DisplayTargetIdentityEvidence Identity(string pnp, uint connector)
        => new(
            MonitorDevicePath: $"\\\\?\\DISPLAY#{connector}",
            FriendlyMonitorName: $"Panel {connector}",
            EdidManufactureId: 1234,
            EdidProductCodeId: (ushort)(5000 + connector),
            ConnectorInstance: connector,
            OutputTechnology: 5,
            AdapterLuidHint: 20,
            PnpDeviceInstanceId: pnp);

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
