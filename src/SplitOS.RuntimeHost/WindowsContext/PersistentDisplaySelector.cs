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

public enum DisplayTargetIdentityResolutionDisposition
{
    Exact,
    UniqueFallback,
    Ambiguous,
    NotFound
}

public sealed record DisplayTargetIdentityCandidate(
    DisplayPathKey TargetKey,
    DisplayTargetIdentityEvidence Identity);

public sealed record DisplayTargetIdentityResolution(
    DisplayTargetIdentityResolutionDisposition Disposition,
    DisplayPathKey? TargetKey,
    string ProductCode,
    string? Detail = null)
{
    public bool IsResolved =>
        Disposition is DisplayTargetIdentityResolutionDisposition.Exact or
            DisplayTargetIdentityResolutionDisposition.UniqueFallback;
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
        ArgumentNullException.ThrowIfNull(snapshot);

        var paths = snapshot.Paths.Where(static path => path.Identity is not null).ToArray();
        var identityResolution = ResolveIdentity(
            selector,
            paths.Select(path => new DisplayTargetIdentityCandidate(path.TargetKey, path.Identity!)));

        if (!identityResolution.IsResolved || !identityResolution.TargetKey.HasValue)
        {
            return new DisplaySelectorResolution(
                identityResolution.Disposition == DisplayTargetIdentityResolutionDisposition.Ambiguous
                    ? DisplaySelectorResolutionDisposition.Ambiguous
                    : DisplaySelectorResolutionDisposition.NotFound,
                null,
                identityResolution.ProductCode,
                identityResolution.Detail);
        }

        var matchingPaths = paths
            .Where(path => path.TargetKey == identityResolution.TargetKey.Value)
            .Take(2)
            .ToArray();
        if (matchingPaths.Length != 1)
        {
            return new DisplaySelectorResolution(
                DisplaySelectorResolutionDisposition.Ambiguous,
                null,
                "DISPLAY_SELECTOR_PATH_KEY_AMBIGUOUS",
                "The resolved physical target maps to more than one current active path; SplitOS refuses first-match selection.");
        }

        return ResolveAvailability(
            matchingPaths[0],
            identityResolution.Disposition == DisplayTargetIdentityResolutionDisposition.Exact
                ? DisplaySelectorResolutionDisposition.Exact
                : DisplaySelectorResolutionDisposition.UniqueFallback,
            identityResolution.ProductCode);
    }

    public DisplayTargetIdentityResolution ResolveIdentity(
        PersistentDisplaySelector selector,
        IEnumerable<DisplayTargetIdentityCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(candidates);
        Validate(selector);

        var grouped = candidates
            .GroupBy(candidate => candidate.TargetKey)
            .Select(group =>
            {
                var identities = group.Select(candidate => candidate.Identity).Distinct().Take(2).ToArray();
                return (TargetKey: group.Key, Identities: identities);
            })
            .ToArray();

        if (grouped.Any(group => group.Identities.Length > 1))
        {
            return AmbiguousIdentity(
                "DISPLAY_SELECTOR_TARGET_IDENTITY_CONFLICT",
                "The same adapterLuid+targetId was observed with conflicting physical identity evidence.");
        }

        var targets = grouped
            .Where(group => group.Identities.Length == 1)
            .Select(group => new DisplayTargetIdentityCandidate(group.TargetKey, group.Identities[0]))
            .ToArray();

        if (!string.IsNullOrWhiteSpace(selector.PnpDeviceInstanceId))
        {
            var exactPnp = targets.Where(candidate => string.Equals(
                    candidate.Identity.PnpDeviceInstanceId,
                    selector.PnpDeviceInstanceId,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (exactPnp.Length > 1)
                return AmbiguousIdentity("DISPLAY_SELECTOR_PNP_INSTANCE_AMBIGUOUS");
            if (exactPnp.Length == 1)
                return ResolvedIdentity(exactPnp[0].TargetKey, DisplayTargetIdentityResolutionDisposition.Exact, "DISPLAY_SELECTOR_PNP_INSTANCE_EXACT");
        }

        if (!string.IsNullOrWhiteSpace(selector.MonitorDevicePath))
        {
            var exactPath = targets.Where(candidate => string.Equals(
                    candidate.Identity.MonitorDevicePath,
                    selector.MonitorDevicePath,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (exactPath.Length > 1)
                return AmbiguousIdentity("DISPLAY_SELECTOR_DEVICE_PATH_AMBIGUOUS");
            if (exactPath.Length == 1)
                return ResolvedIdentity(exactPath[0].TargetKey, DisplayTargetIdentityResolutionDisposition.Exact, "DISPLAY_SELECTOR_EXACT");
        }

        if (selector.HasEdidPair)
        {
            var fallback = targets.Where(candidate => MatchesEdidRelationship(selector, candidate.Identity)).ToArray();
            if (fallback.Length > 1)
                return AmbiguousIdentity("DISPLAY_SELECTOR_FALLBACK_AMBIGUOUS");
            if (fallback.Length == 1)
                return ResolvedIdentity(fallback[0].TargetKey, DisplayTargetIdentityResolutionDisposition.UniqueFallback, "DISPLAY_SELECTOR_UNIQUE_FALLBACK");
        }

        if (selector.AllowWeakFallback && !string.IsNullOrWhiteSpace(selector.FriendlyMonitorName))
        {
            var weak = targets.Where(candidate => MatchesWeakSelector(selector, candidate.Identity)).ToArray();
            if (weak.Length > 1)
                return AmbiguousIdentity("DISPLAY_SELECTOR_WEAK_FALLBACK_AMBIGUOUS");
            if (weak.Length == 1)
                return ResolvedIdentity(weak[0].TargetKey, DisplayTargetIdentityResolutionDisposition.UniqueFallback, "DISPLAY_SELECTOR_UNIQUE_WEAK_FALLBACK");
        }

        return new DisplayTargetIdentityResolution(
            DisplayTargetIdentityResolutionDisposition.NotFound,
            null,
            "DISPLAY_SELECTOR_NOT_FOUND",
            "No current physical display target satisfies the persistent selector without guessing.");
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

    private static DisplayTargetIdentityResolution ResolvedIdentity(
        DisplayPathKey targetKey,
        DisplayTargetIdentityResolutionDisposition disposition,
        string productCode) => new(disposition, targetKey, productCode);

    private static DisplayTargetIdentityResolution AmbiguousIdentity(
        string productCode,
        string? detail = null) => new(
        DisplayTargetIdentityResolutionDisposition.Ambiguous,
        null,
        productCode,
        detail ?? "More than one physical display target satisfies the selector evidence; SplitOS refuses first-match selection.");

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
