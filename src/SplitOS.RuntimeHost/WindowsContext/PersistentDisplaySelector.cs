namespace SplitOS.RuntimeHost.WindowsContext;

public sealed record PersistentDisplaySelector(
    string? MonitorDevicePath = null,
    ushort? EdidManufactureId = null,
    ushort? EdidProductCodeId = null,
    uint? ConnectorInstance = null,
    int? OutputTechnology = null,
    long? AdapterLuidHint = null,
    string? FriendlyMonitorName = null,
    bool AllowWeakFallback = false,
    string? PnpDeviceInstanceId = null)
{
    public bool HasEdidPair => EdidManufactureId.HasValue && EdidProductCodeId.HasValue;
}

public enum DisplaySelectorResolutionDisposition
{
    Exact,
    UniqueFallback,
    Ambiguous,
    NotFound,
    Unavailable
}

public sealed record DisplaySelectorResolution(
    DisplaySelectorResolutionDisposition Disposition,
    DisplayPathEvidence? Path,
    string ProductCode,
    string? Detail = null)
{
    public bool IsResolved =>
        Disposition is DisplaySelectorResolutionDisposition.Exact or DisplaySelectorResolutionDisposition.UniqueFallback;
}

public sealed class PersistentDisplaySelectorResolver
{
    public DisplaySelectorResolution Resolve(PersistentDisplaySelector selector, DisplaySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(snapshot);
        Validate(selector);

        var paths = snapshot.Paths.Where(static path => path.Identity is not null).ToArray();

        if (!string.IsNullOrWhiteSpace(selector.PnpDeviceInstanceId))
        {
            var exactPnp = paths.Where(path => string.Equals(
                    path.Identity!.PnpDeviceInstanceId,
                    selector.PnpDeviceInstanceId,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (exactPnp.Length > 1)
                return Ambiguous("DISPLAY_SELECTOR_PNP_INSTANCE_AMBIGUOUS");
            if (exactPnp.Length == 1)
                return ResolveAvailability(exactPnp[0], DisplaySelectorResolutionDisposition.Exact, "DISPLAY_SELECTOR_PNP_INSTANCE_EXACT");
        }

        if (!string.IsNullOrWhiteSpace(selector.MonitorDevicePath))
        {
            var exact = paths.Where(path => string.Equals(
                    path.Identity!.MonitorDevicePath,
                    selector.MonitorDevicePath,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (exact.Length > 1)
                return Ambiguous("DISPLAY_SELECTOR_DEVICE_PATH_AMBIGUOUS");
            if (exact.Length == 1)
                return ResolveAvailability(exact[0], DisplaySelectorResolutionDisposition.Exact, "DISPLAY_SELECTOR_EXACT");
        }

        if (selector.HasEdidPair)
        {
            var fallback = paths.Where(path => MatchesEdidRelationship(selector, path.Identity!)).ToArray();
            if (fallback.Length > 1)
                return Ambiguous("DISPLAY_SELECTOR_FALLBACK_AMBIGUOUS");
            if (fallback.Length == 1)
                return ResolveAvailability(fallback[0], DisplaySelectorResolutionDisposition.UniqueFallback, "DISPLAY_SELECTOR_UNIQUE_FALLBACK");
        }

        if (selector.AllowWeakFallback && !string.IsNullOrWhiteSpace(selector.FriendlyMonitorName))
        {
            var weak = paths.Where(path => MatchesWeakSelector(selector, path.Identity!)).ToArray();
            if (weak.Length > 1)
                return Ambiguous("DISPLAY_SELECTOR_WEAK_FALLBACK_AMBIGUOUS");
            if (weak.Length == 1)
                return ResolveAvailability(weak[0], DisplaySelectorResolutionDisposition.UniqueFallback, "DISPLAY_SELECTOR_UNIQUE_WEAK_FALLBACK");
        }

        return new DisplaySelectorResolution(
            DisplaySelectorResolutionDisposition.NotFound,
            null,
            "DISPLAY_SELECTOR_NOT_FOUND",
            "No current display path satisfies the persistent selector without guessing.");
    }

    private static bool MatchesEdidRelationship(
        PersistentDisplaySelector selector,
        DisplayTargetIdentityEvidence identity)
    {
        if (identity.EdidManufactureId != selector.EdidManufactureId ||
            identity.EdidProductCodeId != selector.EdidProductCodeId)
            return false;
        if (selector.ConnectorInstance.HasValue && identity.ConnectorInstance != selector.ConnectorInstance.Value)
            return false;
        if (selector.OutputTechnology.HasValue && identity.OutputTechnology != selector.OutputTechnology.Value)
            return false;
        if (selector.AdapterLuidHint.HasValue && identity.AdapterLuidHint != selector.AdapterLuidHint.Value)
            return false;
        return true;
    }

    private static bool MatchesWeakSelector(
        PersistentDisplaySelector selector,
        DisplayTargetIdentityEvidence identity)
    {
        if (!string.Equals(identity.FriendlyMonitorName, selector.FriendlyMonitorName, StringComparison.OrdinalIgnoreCase))
            return false;
        if (selector.OutputTechnology.HasValue && identity.OutputTechnology != selector.OutputTechnology.Value)
            return false;
        if (selector.ConnectorInstance.HasValue && identity.ConnectorInstance != selector.ConnectorInstance.Value)
            return false;
        if (selector.AdapterLuidHint.HasValue && identity.AdapterLuidHint != selector.AdapterLuidHint.Value)
            return false;
        return true;
    }

    private static DisplaySelectorResolution ResolveAvailability(
        DisplayPathEvidence path,
        DisplaySelectorResolutionDisposition resolvedDisposition,
        string successCode)
    {
        if (!path.TargetAvailable)
        {
            return new DisplaySelectorResolution(
                DisplaySelectorResolutionDisposition.Unavailable,
                path,
                "DISPLAY_SELECTOR_UNAVAILABLE",
                "The selector matched a current target identity, but Windows reports targetAvailable == FALSE.");
        }

        return new DisplaySelectorResolution(resolvedDisposition, path, successCode);
    }

    private static DisplaySelectorResolution Ambiguous(string productCode) => new(
        DisplaySelectorResolutionDisposition.Ambiguous,
        null,
        productCode,
        "More than one current display path satisfies the selector evidence; SplitOS refuses first-match selection.");

    private static void Validate(PersistentDisplaySelector selector)
    {
        if (selector.EdidManufactureId.HasValue != selector.EdidProductCodeId.HasValue)
            throw new ArgumentException("EDID manufacture and product identifiers must be provided together.", nameof(selector));

        var hasStrong = !string.IsNullOrWhiteSpace(selector.PnpDeviceInstanceId) ||
                        !string.IsNullOrWhiteSpace(selector.MonitorDevicePath);
        var hasFallback = selector.HasEdidPair;
        var hasWeak = selector.AllowWeakFallback && !string.IsNullOrWhiteSpace(selector.FriendlyMonitorName);
        if (!hasStrong && !hasFallback && !hasWeak)
            throw new ArgumentException("Display selector has no usable identity evidence.", nameof(selector));
    }
}
