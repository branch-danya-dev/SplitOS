using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SplitOS.Contracts.Protocol;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.ModeRuntime;

public sealed record DisplayTopologyPreState(
    IReadOnlyList<PersistentDisplaySelector> ActiveTargets);

public sealed record DisplayTargetModePreState(
    PersistentDisplaySelector Selector,
    IReadOnlyList<PersistentDisplaySelector> ActiveTargets,
    uint Width,
    uint Height,
    uint RefreshNumerator,
    uint RefreshDenominator,
    uint Rotation);

/// <summary>
/// Canonical rollback evidence captured before any durable display mutation begins. Unlike CCD
/// operation keys, the persisted pre-state contains only physical selector evidence and exact mode
/// values that can be re-resolved after a Runtime restart.
/// </summary>
public static class DisplayModeActionPreStateContract
{
    public const int PreStateSchemaVersion = 1;

    public static DisplayTopologyPreState CaptureTopology(DisplaySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Normalize(new DisplayTopologyPreState(CaptureActiveSelectors(snapshot)));
    }

    public static DisplayTargetModePreState CaptureTargetMode(DisplaySnapshot snapshot, DisplayPathEvidence path)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(path);
        if (!path.Active || !path.TargetAvailable || path.Identity is null)
            throw new InvalidDataException("Display target-mode pre-state requires one active, available target with stable identity evidence.");
        if (path.SourceResolution is null || path.RefreshRate is null)
            throw new InvalidDataException("Display target-mode pre-state requires exact resolution and refresh evidence.");
        if (path.BoostRefreshRate)
            throw new InvalidDataException("Dynamic/boost refresh pre-state is not supported for durable mode mutation.");
        if (!snapshot.Paths.Any(candidate => candidate.TargetKey == path.TargetKey && candidate.Active))
            throw new InvalidDataException("Display target-mode evidence does not belong to the captured active snapshot.");

        return Normalize(new DisplayTargetModePreState(
            SelectorFromIdentity(path.Identity),
            CaptureActiveSelectors(snapshot),
            path.SourceResolution.Value.Width,
            path.SourceResolution.Value.Height,
            path.RefreshRate.Value.Numerator,
            path.RefreshRate.Value.Denominator,
            path.Rotation));
    }

    public static string SerializeTopology(DisplayTopologyPreState state)
    {
        var normalized = Normalize(state);
        return JsonSerializer.Serialize(
            new TopologyDocument(PreStateSchemaVersion, normalized.ActiveTargets),
            ProtocolJson.Options);
    }

    public static DisplayTopologyPreState DeserializeTopology(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Display topology pre-state JSON is missing.", nameof(json));
        var document = JsonSerializer.Deserialize<TopologyDocument>(json, ProtocolJson.Options)
            ?? throw new JsonException("Display topology pre-state document is null.");
        if (document.SchemaVersion != PreStateSchemaVersion)
            throw new ArgumentException($"Display topology pre-state schema {document.SchemaVersion} is not supported.", nameof(json));
        var normalized = Normalize(new DisplayTopologyPreState(
            document.ActiveTargets ?? throw new ArgumentException("Display topology pre-state targets are missing.", nameof(json))));
        if (!string.Equals(json, SerializeTopology(normalized), StringComparison.Ordinal))
            throw new ArgumentException("Display topology pre-state JSON is not in canonical serialization form.", nameof(json));
        return normalized;
    }

    public static string SerializeTargetMode(DisplayTargetModePreState state)
    {
        var normalized = Normalize(state);
        return JsonSerializer.Serialize(
            new TargetModeDocument(
                PreStateSchemaVersion,
                normalized.Selector,
                normalized.ActiveTargets,
                normalized.Width,
                normalized.Height,
                normalized.RefreshNumerator,
                normalized.RefreshDenominator,
                normalized.Rotation),
            ProtocolJson.Options);
    }

    public static DisplayTargetModePreState DeserializeTargetMode(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Display target-mode pre-state JSON is missing.", nameof(json));
        var document = JsonSerializer.Deserialize<TargetModeDocument>(json, ProtocolJson.Options)
            ?? throw new JsonException("Display target-mode pre-state document is null.");
        if (document.SchemaVersion != PreStateSchemaVersion)
            throw new ArgumentException($"Display target-mode pre-state schema {document.SchemaVersion} is not supported.", nameof(json));
        var normalized = Normalize(new DisplayTargetModePreState(
            document.Selector ?? throw new ArgumentException("Display target-mode pre-state selector is missing.", nameof(json)),
            document.ActiveTargets ?? throw new ArgumentException("Display target-mode topology pre-state is missing.", nameof(json)),
            document.Width,
            document.Height,
            document.RefreshNumerator,
            document.RefreshDenominator,
            document.Rotation));
        if (!string.Equals(json, SerializeTargetMode(normalized), StringComparison.Ordinal))
            throw new ArgumentException("Display target-mode pre-state JSON is not in canonical serialization form.", nameof(json));
        return normalized;
    }

    public static string ComputeTopologyDigest(DisplayTopologyPreState state)
        => Sha256(SerializeTopology(state));

    public static string ComputeTargetModeDigest(DisplayTargetModePreState state)
        => Sha256(SerializeTargetMode(state));

    public static DisplayTopologyPreState Normalize(DisplayTopologyPreState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new DisplayTopologyPreState(NormalizeTargetSet(state.ActiveTargets));
    }

    public static DisplayTargetModePreState Normalize(DisplayTargetModePreState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var selector = DisplayModeActionContract.NormalizeSelector(state.Selector);
        var activeTargets = NormalizeTargetSet(state.ActiveTargets);
        if (state.Width == 0 || state.Height == 0)
            throw new ArgumentOutOfRangeException(nameof(state), "Display target-mode pre-state resolution must be positive.");
        if (state.RefreshDenominator == 0)
            throw new ArgumentOutOfRangeException(nameof(state), "Display target-mode pre-state refresh denominator must be non-zero.");
        if (state.Rotation is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(state), "Display target-mode pre-state rotation is invalid.");
        return state with { Selector = selector, ActiveTargets = activeTargets };
    }

    public static PersistentDisplaySelector SelectorFromIdentity(DisplayTargetIdentityEvidence identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return DisplayModeActionContract.NormalizeSelector(new PersistentDisplaySelector(
            MonitorDevicePath: identity.MonitorDevicePath,
            EdidManufactureId: identity.EdidManufactureId,
            EdidProductCodeId: identity.EdidProductCodeId,
            ConnectorInstance: identity.ConnectorInstance,
            OutputTechnology: identity.OutputTechnology,
            AdapterLuidHint: identity.AdapterLuidHint,
            FriendlyMonitorName: identity.FriendlyMonitorName,
            AllowWeakFallback: false,
            PnpDeviceInstanceId: identity.PnpDeviceInstanceId));
    }

    public static IReadOnlyList<PersistentDisplaySelector> CaptureActiveSelectors(DisplaySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var active = snapshot.Paths.Where(static path => path.Active).ToArray();
        if (active.Length == 0)
            throw new InvalidDataException("Display topology pre-state has no active targets.");

        var selectors = new List<PersistentDisplaySelector>(active.Length);
        foreach (var path in active)
        {
            if (!path.TargetAvailable || path.Identity is null)
                throw new InvalidDataException("Every active display target must be available and have stable identity evidence before mutation.");
            selectors.Add(SelectorFromIdentity(path.Identity));
        }
        return NormalizeTargetSet(selectors);
    }

    public static bool TargetSetsEqual(
        IReadOnlyList<PersistentDisplaySelector> expected,
        IReadOnlyList<PersistentDisplaySelector> actual)
    {
        var left = NormalizeTargetSet(expected).Select(SelectorKey).ToArray();
        var right = NormalizeTargetSet(actual).Select(SelectorKey).ToArray();
        return left.SequenceEqual(right, StringComparer.Ordinal);
    }

    private static IReadOnlyList<PersistentDisplaySelector> NormalizeTargetSet(
        IReadOnlyList<PersistentDisplaySelector> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count == 0)
            throw new ArgumentException("Display topology pre-state must contain at least one active target.", nameof(targets));

        var normalized = targets
            .Select(DisplayModeActionContract.NormalizeSelector)
            .Select(selector => (Selector: selector, Key: SelectorKey(selector)))
            .OrderBy(static item => item.Key, StringComparer.Ordinal)
            .ToArray();
        if (normalized.Select(static item => item.Key).Distinct(StringComparer.Ordinal).Count() != normalized.Length)
            throw new InvalidDataException("Display topology pre-state contains duplicate physical target identity evidence.");
        return normalized.Select(static item => item.Selector).ToArray();
    }

    private static string SelectorKey(PersistentDisplaySelector selector)
        => JsonSerializer.Serialize(selector, ProtocolJson.Options);

    private static string Sha256(string json)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();

    private sealed record TopologyDocument(
        int SchemaVersion,
        IReadOnlyList<PersistentDisplaySelector>? ActiveTargets);

    private sealed record TargetModeDocument(
        int SchemaVersion,
        PersistentDisplaySelector? Selector,
        IReadOnlyList<PersistentDisplaySelector>? ActiveTargets,
        uint Width,
        uint Height,
        uint RefreshNumerator,
        uint RefreshDenominator,
        uint Rotation);
}
