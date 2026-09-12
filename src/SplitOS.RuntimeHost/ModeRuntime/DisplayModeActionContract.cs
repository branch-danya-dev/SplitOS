using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SplitOS.Contracts.Protocol;
using SplitOS.Persistence.Machine;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.ModeRuntime;

public sealed record DisplayTopologyExtendDesiredState(PersistentDisplaySelector Selector);

public sealed record DisplayTargetModeDesiredState(
    PersistentDisplaySelector Selector,
    uint Width,
    uint Height,
    uint RefreshNumerator,
    uint RefreshDenominator,
    uint Rotation);

/// <summary>
/// Canonical durable semantics for display actions. Persistent intent contains only stable physical
/// selector evidence and requested display semantics; operation-local CCD path keys and display
/// generation values are deliberately absent and must be resolved fresh at execution time.
/// </summary>
public static class DisplayModeActionContract
{
    public const int DesiredSchemaVersion = 1;
    public const string OwningModule = "windows-display";
    public const string TopologyExtendActionType = "display-topology.extend";
    public const string TargetModeActionType = "display-target.mode.apply";
    public const string TargetRef = "persistent-display-target";
    public const string RollbackClass = "restore_pre_state";
    public const string TopologyVerificationClass = "display.topology.read-back";
    public const string ModeVerificationClass = "display.target-mode.read-back";

    public static PersistedModeActionDefinition CreateTopologyExtendDefinition(
        Guid actionId,
        int sequenceNo,
        PersistentDisplaySelector selector,
        bool mandatory = true)
    {
        var desired = Normalize(new DisplayTopologyExtendDesiredState(selector));
        var json = SerializeTopologyExtend(desired);
        return new PersistedModeActionDefinition(
            actionId,
            sequenceNo,
            OwningModule,
            TopologyExtendActionType,
            TargetRef,
            DesiredSchemaVersion,
            json,
            Sha256(json),
            mandatory,
            RollbackClass,
            TopologyVerificationClass);
    }

    public static PersistedModeActionDefinition CreateTargetModeDefinition(
        Guid actionId,
        int sequenceNo,
        DisplayTargetModeDesiredState desired,
        bool mandatory = true)
    {
        var normalized = Normalize(desired);
        var json = SerializeTargetMode(normalized);
        return new PersistedModeActionDefinition(
            actionId,
            sequenceNo,
            OwningModule,
            TargetModeActionType,
            TargetRef,
            DesiredSchemaVersion,
            json,
            Sha256(json),
            mandatory,
            RollbackClass,
            ModeVerificationClass);
    }

    public static string SerializeTopologyExtend(DisplayTopologyExtendDesiredState desired)
    {
        var normalized = Normalize(desired);
        return JsonSerializer.Serialize(
            new TopologyExtendDocument(DesiredSchemaVersion, normalized.Selector),
            ProtocolJson.Options);
    }

    public static DisplayTopologyExtendDesiredState DeserializeTopologyExtend(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Display topology desired-state JSON is missing.", nameof(json));

        var document = JsonSerializer.Deserialize<TopologyExtendDocument>(json, ProtocolJson.Options)
            ?? throw new JsonException("Display topology desired-state document is null.");
        if (document.SchemaVersion != DesiredSchemaVersion)
            throw new ArgumentException($"Display topology desired-state schema {document.SchemaVersion} is not supported.", nameof(json));

        var normalized = Normalize(new DisplayTopologyExtendDesiredState(
            document.Selector ?? throw new ArgumentException("Display topology selector is missing.", nameof(json))));
        if (!string.Equals(json, SerializeTopologyExtend(normalized), StringComparison.Ordinal))
            throw new ArgumentException("Display topology desired-state JSON is not in canonical serialization form.", nameof(json));
        return normalized;
    }

    public static string SerializeTargetMode(DisplayTargetModeDesiredState desired)
    {
        var normalized = Normalize(desired);
        return JsonSerializer.Serialize(
            new TargetModeDocument(
                DesiredSchemaVersion,
                normalized.Selector,
                normalized.Width,
                normalized.Height,
                normalized.RefreshNumerator,
                normalized.RefreshDenominator,
                normalized.Rotation),
            ProtocolJson.Options);
    }

    public static DisplayTargetModeDesiredState DeserializeTargetMode(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Display target-mode desired-state JSON is missing.", nameof(json));

        var document = JsonSerializer.Deserialize<TargetModeDocument>(json, ProtocolJson.Options)
            ?? throw new JsonException("Display target-mode desired-state document is null.");
        if (document.SchemaVersion != DesiredSchemaVersion)
            throw new ArgumentException($"Display target-mode desired-state schema {document.SchemaVersion} is not supported.", nameof(json));

        var normalized = Normalize(new DisplayTargetModeDesiredState(
            document.Selector ?? throw new ArgumentException("Display target-mode selector is missing.", nameof(json)),
            document.Width,
            document.Height,
            document.RefreshNumerator,
            document.RefreshDenominator,
            document.Rotation));
        if (!string.Equals(json, SerializeTargetMode(normalized), StringComparison.Ordinal))
            throw new ArgumentException("Display target-mode desired-state JSON is not in canonical serialization form.", nameof(json));
        return normalized;
    }

    public static string ComputeTopologyExtendDigest(DisplayTopologyExtendDesiredState desired)
        => Sha256(SerializeTopologyExtend(desired));

    public static string ComputeTargetModeDigest(DisplayTargetModeDesiredState desired)
        => Sha256(SerializeTargetMode(desired));

    public static DisplayTopologyExtendDesiredState Normalize(DisplayTopologyExtendDesiredState desired)
    {
        ArgumentNullException.ThrowIfNull(desired);
        return new DisplayTopologyExtendDesiredState(NormalizeSelector(desired.Selector));
    }

    public static DisplayTargetModeDesiredState Normalize(DisplayTargetModeDesiredState desired)
    {
        ArgumentNullException.ThrowIfNull(desired);
        var selector = NormalizeSelector(desired.Selector);
        if (desired.Width == 0 || desired.Height == 0)
            throw new ArgumentOutOfRangeException(nameof(desired), "Display target-mode resolution must be positive.");
        if (desired.RefreshDenominator == 0)
            throw new ArgumentOutOfRangeException(nameof(desired), "Display target-mode refresh denominator must be non-zero.");
        if (desired.Rotation is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(desired), "Display rotation must be one of IDENTITY/90/180/270.");

        return desired with { Selector = selector };
    }

    public static PersistentDisplaySelector NormalizeSelector(PersistentDisplaySelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var normalized = selector with
        {
            MonitorDevicePath = NormalizeOptional(selector.MonitorDevicePath),
            FriendlyMonitorName = NormalizeOptional(selector.FriendlyMonitorName),
            PnpDeviceInstanceId = NormalizeOptional(selector.PnpDeviceInstanceId)
        };

        if (normalized.EdidManufactureId.HasValue != normalized.EdidProductCodeId.HasValue)
            throw new ArgumentException("EDID manufacture and product identifiers must be provided together.", nameof(selector));

        var hasStrong = normalized.PnpDeviceInstanceId is not null || normalized.MonitorDevicePath is not null;
        var hasFallback = normalized.EdidManufactureId.HasValue && normalized.EdidProductCodeId.HasValue;
        var hasWeak = normalized.AllowWeakFallback && normalized.FriendlyMonitorName is not null;
        if (!hasStrong && !hasFallback && !hasWeak)
            throw new ArgumentException("Display selector has no usable persistent identity evidence.", nameof(selector));
        if (!normalized.AllowWeakFallback && normalized.FriendlyMonitorName is not null && !hasStrong && !hasFallback)
            throw new ArgumentException("Friendly monitor name cannot be the only selector unless weak fallback is explicitly enabled.", nameof(selector));

        return normalized;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Sha256(string json)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();

    private sealed record TopologyExtendDocument(int SchemaVersion, PersistentDisplaySelector? Selector);

    private sealed record TargetModeDocument(
        int SchemaVersion,
        PersistentDisplaySelector? Selector,
        uint Width,
        uint Height,
        uint RefreshNumerator,
        uint RefreshDenominator,
        uint Rotation);
}
