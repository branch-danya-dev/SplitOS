using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class PersistentDisplaySelectorTests
{
    private readonly PersistentDisplaySelectorResolver _resolver = new();

    [TestMethod]
    public void ExactPnpInstanceWinsWhenMonitorDevicePathChanges()
    {
        var expected = Path(1, Identity("MONITOR#NEW", "Panel", 10, 20, 1, 5, 100, "DISPLAY\\ABC123\\UID1"));
        var other = Path(2, Identity("MONITOR#OLD", "Panel", 10, 20, 1, 5, 100, "DISPLAY\\OTHER\\UID2"));
        var selector = new PersistentDisplaySelector(
            MonitorDevicePath: "MONITOR#OLD",
            PnpDeviceInstanceId: "display\\abc123\\uid1");

        var result = _resolver.Resolve(selector, Snapshot(expected, other));

        Assert.AreEqual(DisplaySelectorResolutionDisposition.Exact, result.Disposition);
        Assert.AreEqual("DISPLAY_SELECTOR_PNP_INSTANCE_EXACT", result.ProductCode);
        Assert.AreEqual(expected.TargetKey, result.Path!.TargetKey);
    }

    [TestMethod]
    public void DuplicatePnpInstanceEvidenceFailsClosedAsAmbiguous()
    {
        var selector = new PersistentDisplaySelector(PnpDeviceInstanceId: "DISPLAY\\DUP\\UID");
        var result = _resolver.Resolve(selector, Snapshot(
            Path(1, Identity("A", "Panel", 1, 1, 1, 5, 10, "DISPLAY\\DUP\\UID")),
            Path(2, Identity("B", "Panel", 1, 1, 2, 5, 10, "DISPLAY\\DUP\\UID"))));

        Assert.AreEqual(DisplaySelectorResolutionDisposition.Ambiguous, result.Disposition);
        Assert.AreEqual("DISPLAY_SELECTOR_PNP_INSTANCE_AMBIGUOUS", result.ProductCode);
        Assert.IsNull(result.Path);
    }

    [TestMethod]
    public void ExactDevicePathWinsOverWeakerIdentity()
    {
        var expected = Path(1, Identity("MONITOR#A", "Same", 10, 20, 1, 5, 100));
        var other = Path(2, Identity("MONITOR#B", "Same", 10, 20, 1, 5, 100));
        var selector = new PersistentDisplaySelector(MonitorDevicePath: "monitor#a", FriendlyMonitorName: "Same", AllowWeakFallback: true);

        var result = _resolver.Resolve(selector, Snapshot(expected, other));

        Assert.AreEqual(DisplaySelectorResolutionDisposition.Exact, result.Disposition);
        Assert.AreEqual(expected.TargetKey, result.Path!.TargetKey);
    }

    [TestMethod]
    public void DuplicateFriendlyNamesAreAmbiguousWhenWeakFallbackIsExplicitlyAllowed()
    {
        var selector = new PersistentDisplaySelector(FriendlyMonitorName: "Generic Monitor", AllowWeakFallback: true);
        var result = _resolver.Resolve(selector, Snapshot(
            Path(1, Identity("A", "Generic Monitor", 1, 1, 1, 5, 10)),
            Path(2, Identity("B", "Generic Monitor", 2, 2, 2, 5, 20))));

        Assert.AreEqual(DisplaySelectorResolutionDisposition.Ambiguous, result.Disposition);
    }

    [TestMethod]
    public void DuplicateEdidPairNeedsConnectorRelationshipToBecomeUnique()
    {
        var first = Path(1, Identity("A", "Panel", 11, 22, 1, 5, 100));
        var second = Path(2, Identity("B", "Panel", 11, 22, 2, 5, 100));
        var snapshot = Snapshot(first, second);

        var ambiguous = _resolver.Resolve(new PersistentDisplaySelector(
            EdidManufactureId: 11, EdidProductCodeId: 22), snapshot);
        var resolved = _resolver.Resolve(new PersistentDisplaySelector(
            EdidManufactureId: 11, EdidProductCodeId: 22, ConnectorInstance: 2, OutputTechnology: 5, AdapterLuidHint: 100), snapshot);

        Assert.AreEqual(DisplaySelectorResolutionDisposition.Ambiguous, ambiguous.Disposition);
        Assert.AreEqual(DisplaySelectorResolutionDisposition.UniqueFallback, resolved.Disposition);
        Assert.AreEqual(second.TargetKey, resolved.Path!.TargetKey);
    }

    [TestMethod]
    public void MatchingIdentityThatWindowsMarksUnavailableReturnsUnavailable()
    {
        var path = Path(1, Identity("MONITOR#TV", "TV", 10, 20, 1, 5, 100)) with { TargetAvailable = false };
        var result = _resolver.Resolve(new PersistentDisplaySelector(MonitorDevicePath: "MONITOR#TV"), Snapshot(path));

        Assert.AreEqual(DisplaySelectorResolutionDisposition.Unavailable, result.Disposition);
        Assert.AreEqual(path.TargetKey, result.Path!.TargetKey);
    }

    [TestMethod]
    public void MissingStrongIdentityDoesNotSilentlyUseFriendlyNameWithoutPolicyPermission()
    {
        var path = Path(1, Identity("MONITOR#OTHER", "TV", 10, 20, 1, 5, 100));
        var selector = new PersistentDisplaySelector(MonitorDevicePath: "MONITOR#MISSING", FriendlyMonitorName: "TV");

        var result = _resolver.Resolve(selector, Snapshot(path));

        Assert.AreEqual(DisplaySelectorResolutionDisposition.NotFound, result.Disposition);
    }

    [TestMethod]
    public void PartialEdidSelectorIsRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => _resolver.Resolve(
            new PersistentDisplaySelector(EdidManufactureId: 10),
            Snapshot()));
    }

    private static DisplayTargetIdentityEvidence Identity(
        string devicePath,
        string friendlyName,
        ushort manufacturer,
        ushort product,
        uint connector,
        int outputTechnology,
        long adapter,
        string? pnpDeviceInstanceId = null) =>
        new(devicePath, friendlyName, manufacturer, product, connector, outputTechnology, adapter, pnpDeviceInstanceId);

    private static DisplayPathEvidence Path(uint targetId, DisplayTargetIdentityEvidence identity) => new(
        SourceAdapterLuid: identity.AdapterLuidHint,
        SourceId: targetId,
        TargetKey: new DisplayPathKey(identity.AdapterLuidHint, targetId),
        Active: true,
        TargetAvailable: true,
        OutputTechnology: identity.OutputTechnology,
        Rotation: 1,
        Scaling: 1,
        RefreshRate: new DisplayRational(60, 1),
        Identity: identity);

    private static DisplaySnapshot Snapshot(params DisplayPathEvidence[] paths) =>
        new(1, DateTimeOffset.UtcNow, paths);
}
