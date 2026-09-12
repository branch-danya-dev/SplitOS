using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SplitOS.Contracts.Protocol;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.ModeRuntime;

public sealed record DisplayTopologyTargetModePreState(
    PersistentDisplaySelector Selector,
    uint Width,
    uint Height,
    uint RefreshNumerator,
    uint RefreshDenominator,
    uint Rotation);

public sealed record DisplayTopologyPreState(
    IReadOnlyList<PersistentDisplaySelector> ActiveTargets,
    IReadOnlyList<DisplayTopologyTargetModePreState> ActiveTargetModes);

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
    // v1 topology evidence only persisted the active physical selector set. No production mode plan
    // emits display actions yet, so v2 deliberately fails closed on v1 and adds exact per-target mode
    // evidence before display actions are allowed into a real transition plan.
    public const int PreStateSchemaVersion = 2;

    public static DisplayTopologyPreState CaptureTopology(DisplaySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var active = ActivePaths(snapshot);
        var selectors = new List<PersistentDisplaySelector>(active.Length);
        var modes = new List<DisplayTopologyTargetModePreState>(active.Length);
        foreach (var path in active)
        {
            ValidateStableActivePath(path, "Display topology pre-state");
            if (path.SourceResolution is null || path.RefreshRate is null)
                throw new InvalidDataException("Every active display target requires exact resolution and refresh evidence before durable topology mutation.");
            if (path.BoostRefreshRate)
                throw new InvalidDataException("Dynamic/boost refresh topology pre-state is not supported for durable topology mutation.");

            var selector = SelectorFromIdentity(path.Identity!);
            selectors.Add(selector);
            modes.Add(new DisplayTopologyTargetModePreState(
                selector,
                path.SourceResolution.Value.Width,
                path.SourceResolution.Value.Height,
                path.RefreshRate.Value.Numerator,
                path.RefreshRate.Value.Denominator,
                path.Rotation));
        }

        return Normalize(new DisplayTopologyPreState(selectors, modes));
    }

    public static DisplayTargetModePreState CaptureTargetMode(DisplaySnapshot snapshot, DisplayPathEvidence path)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(path);
        ValidateStableActivePath(path, "Display target-mode pre-state");
        if (path.SourceResolution is null || path.RefreshRate is null)
            throw new InvalidDataException("Display target-mode pre-state requires exact resolution and refresh evidence.");
        if (path.BoostRefreshRate)
            throw new InvalidDataException("Dynamic/boost refresh pre-state is not supported for durable mode mutation.");
        if (!snapshot.Paths.Any(candidate => candidate.TargetKey == path.TargetKey && candidate.Active))
            throw new InvalidDataException("Display target-mode evidence does not belong to the captured active snapshot.");

        return Normalize(new DisplayTargetModePreState(
            SelectorFromIdentity(path.Identity!),
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
            new TopologyDocument(
                PreStateSchemaVersion,
                normalized.ActiveTargets,
                normalized.ActiveTargetModes),
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
            document.ActiveTargets ?? throw new ArgumentException("Display topology pre-state targets are missing.", nameof(json)),
            document.ActiveTargetModes ?? throw new ArgumentException("Display topology per-target mode evidence is missing.", nameof(json))));
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
        var activeTargets = NormalizeTargetSet(state.ActiveTargets);
        ArgumentNullException.ThrowIfNull(state.ActiveTargetModes);
        if (state.ActiveTargetModes.Count != activeTargets.Count)
            throw new InvalidDataException("Display topology pre-state must bind exactly one mode evidence record to every active physical target.");

        var modes = state.ActiveTargetModes
            .Select(NormalizeTopologyTargetMode)
            .Select(mode => (Mode: mode, Key: SelectorKey(mode.Selector)))
            .OrderBy(static item => item.Key, StringComparer.Ordinal)
            .ToArray();
        if (modes.Select(static item => item.Key).Distinct(StringComparer.Ordinal).Count() != modes.Length)
            throw new InvalidDataException("Display topology pre-state contains duplicate per-target mode identity evidence.");

        var targetKeys = activeTargets.Select(SelectorKey).ToArray();
        var modeKeys = modes.Select(static item => item.Key).ToArray();
        if (!targetKeys.SequenceEqual(modeKeys, StringComparer.Ordinal))
            throw new InvalidDataException("Display topology pre-state target-set and per-target mode evidence do not describe the same physical displays.");

        return new DisplayTopologyPreState(
            activeTargets,
            modes.Select(static item => item.Mode).ToArray());
    }

    public static DisplayTargetModePreState Normalize(DisplayTargetModePreState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var selector = DisplayModeActionContract.NormalizeSelector(state.Selector);
        var activeTargets = NormalizeTargetSet(state.ActiveTargets);
        ValidateMode(state.Width, state.Height, state.RefreshDenominator, state.Rotation, nameof(state));
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
        var active = ActivePaths(snapshot);
        var selectors = new List<PersistentDisplaySelector>(active.Length);
        foreach (var path in active)
        {
            ValidateStableActivePath(path, "Display topology pre-state");
            selectors.Add(SelectorFromIdentity(path.Identity!));
        }
        return NormalizeTargetSet(selectors);
    }

    public static IReadOnlyList<DisplayTopologyTargetModePreState> CaptureActiveTargetModes(DisplaySnapshot snapshot)
        => CaptureTopology(snapshot).ActiveTargetModes;

    public static bool TargetSetsEqual(
        IReadOnlyList<PersistentDisplaySelector> expected,
        IReadOnlyList<PersistentDisplaySelector> actual)
    {
        var left = NormalizeTargetSet(expected).Select(SelectorKey).ToArray();
        var right = NormalizeTargetSet(actual).Select(SelectorKey).ToArray();
        return left.SequenceEqual(right, StringComparer.Ordinal);
    }

    public static bool TopologyModesEqual(
        IReadOnlyList<DisplayTopologyTargetModePreState> expected,
        IReadOnlyList<DisplayTopologyTargetModePreState> actual)
    {
        var left = NormalizeTopologyModes(expected);
        var right = NormalizeTopologyModes(actual);
        if (left.Length != right.Length)
            return false;

        for (var index = 0; index < left.Length; index++)
        {
            if (!string.Equals(left[index].Key, right[index].Key, StringComparison.Ordinal))
                return false;
            if (!ModesEqual(left[index].Mode, right[index].Mode))
                return false;
        }
        return true;
    }

    private static DisplayPathEvidence[] ActivePaths(DisplaySnapshot snapshot)
    {
        var active = snapshot.Paths.Where(static path => path.Active).ToArray();
        if (active.Length == 0)
            throw new InvalidDataException("Display topology pre-state has no active targets.");
        return active;
    }

    private static void ValidateStableActivePath(DisplayPathEvidence path, string context)
    {
        if (!path.Active || !path.TargetAvailable || path.Identity is null)
            throw new InvalidDataException($"{context} requires active, available targets with stable identity evidence.");
    }

    private static DisplayTopologyTargetModePreState NormalizeTopologyTargetMode(
        DisplayTopologyTargetModePreState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var selector = DisplayModeActionContract.NormalizeSelector(state.Selector);
        ValidateMode(state.Width, state.Height, state.RefreshDenominator, state.Rotation, nameof(state));
        return state with { Selector = selector };
    }

    private static (DisplayTopologyTargetModePreState Mode, string Key)[] NormalizeTopologyModes(
        IReadOnlyList<DisplayTopologyTargetModePreState> modes)
    {
        ArgumentNullException.ThrowIfNull(modes);
        var normalized = modes
            .Select(NormalizeTopologyTargetMode)
            .Select(mode => (Mode: mode, Key: SelectorKey(mode.Selector)))
            .OrderBy(static item => item.Key, StringComparer.Ordinal)
            .ToArray();
        if (normalized.Select(static item => item.Key).Distinct(StringComparer.Ordinal).Count() != normalized.Length)
            throw new InvalidDataException("Display topology mode evidence contains duplicate physical target identities.");
        return normalized;
    }

    private static bool ModesEqual(
        DisplayTopologyTargetModePreState left,
        DisplayTopologyTargetModePreState right)
        => left.Width == right.Width &&
           left.Height == right.Height &&
           left.Rotation == right.Rotation &&
           (ulong)left.RefreshNumerator * right.RefreshDenominator ==
           (ulong)right.RefreshNumerator * left.RefreshDenominator;

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

    private static void ValidateMode(
        uint width,
        uint height,
        uint refreshDenominator,
        uint rotation,
        string parameterName)
    {
        if (width == 0 || height == 0)
            throw new ArgumentOutOfRangeException(parameterName, "Display pre-state resolution must be positive.");
        if (refreshDenominator == 0)
            throw new ArgumentOutOfRangeException(parameterName, "Display pre-state refresh denominator must be non-zero.");
        if (rotation is < 1 or > 4)
            throw new ArgumentOutOfRangeException(parameterName, "Display pre-state rotation is invalid.");
    }

    private static string SelectorKey(PersistentDisplaySelector selector)
        => JsonSerializer.Serialize(selector, ProtocolJson.Options);

    private static string Sha256(string json)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();

    private sealed record TopologyDocument(
        int SchemaVersion,
        IReadOnlyList<PersistentDisplaySelector>? ActiveTargets,
        IReadOnlyList<DisplayTopologyTargetModePreState>? ActiveTargetModes);

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
