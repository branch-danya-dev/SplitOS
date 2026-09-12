using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.ModeRuntime;

public sealed record DurableDisplayModeState(
    PersistentDisplaySelector Selector,
    DisplayPixelSize Resolution,
    DisplayRational RefreshRate,
    uint Rotation,
    DisplayTopologyIntent TopologyIntent = DisplayTopologyIntent.PreserveActiveTopology);

/// <summary>
/// Canonical durable representation for the first display action vertical. The action intentionally
/// admits only PRESERVE_ACTIVE_TOPOLOGY until topology-changing operations have crash-safe durable
/// compensation evidence. Persistent selectors, never operation-local target ids, cross journal/restart boundaries.
/// </summary>
public static class DisplayModeActionContract
{
    public const string OwningModule = "windows-display";
    public const string ActionType = "apply-display-mode";
    public const string TargetRef = "display.persistent-selector";
    public const int DesiredSchemaVersion = 1;
    public const string RollbackClass = "RESTORE_PRE_STATE";
    public const string VerificationClass = "DISPLAY_READ_BACK";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string Serialize(DurableDisplayModeState state)
    {
        Validate(state);
        return JsonSerializer.Serialize(state, JsonOptions);
    }

    public static DurableDisplayModeState Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Durable display state JSON must not be empty.", nameof(json));

        DurableDisplayModeState state;
        try
        {
            state = JsonSerializer.Deserialize<DurableDisplayModeState>(json, JsonOptions)
                ?? throw new InvalidDataException("Durable display state JSON produced a null payload.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Durable display state JSON is malformed.", ex);
        }

        Validate(state);
        var canonical = Serialize(state);
        if (!string.Equals(canonical, json, StringComparison.Ordinal))
            throw new InvalidDataException("Durable display state JSON is not in canonical form.");
        return state;
    }

    public static string ComputeDigest(string canonicalJson)
    {
        if (canonicalJson is null) throw new ArgumentNullException(nameof(canonicalJson));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson))).ToLowerInvariant();
    }

    public static void ValidateDigest(string canonicalJson, string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest) || digest.Length != 64 || digest.Any(static c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("Durable display state digest is not a SHA-256 hexadecimal digest.");
        var expected = ComputeDigest(canonicalJson);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expected),
                Encoding.ASCII.GetBytes(digest.ToLowerInvariant())))
            throw new InvalidDataException("Durable display state digest does not match its canonical payload.");
    }

    public static DurableDisplayModeState CapturePreState(DisplayPathEvidence path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!path.Active || !path.TargetAvailable)
            throw new InvalidDataException("Display pre-state can be captured only from an active and available path.");
        if (!path.SourceResolution.HasValue || !path.SourceResolution.Value.IsValid)
            throw new InvalidDataException("Display pre-state omitted a usable source resolution.");
        if (!path.RefreshRate.HasValue)
            throw new InvalidDataException("Display pre-state omitted exact rational refresh evidence.");
        if (path.Identity is null)
            throw new InvalidDataException("Display pre-state omitted persistent physical identity evidence.");

        var identity = path.Identity;
        var selector = new PersistentDisplaySelector(
            MonitorDevicePath: identity.MonitorDevicePath,
            EdidManufactureId: identity.EdidManufactureId,
            EdidProductCodeId: identity.EdidProductCodeId,
            ConnectorInstance: identity.ConnectorInstance,
            OutputTechnology: identity.OutputTechnology,
            AdapterLuidHint: identity.AdapterLuidHint,
            FriendlyMonitorName: identity.FriendlyMonitorName,
            AllowWeakFallback: false,
            PnpDeviceInstanceId: identity.PnpDeviceInstanceId);

        var state = new DurableDisplayModeState(
            selector,
            path.SourceResolution.Value,
            path.RefreshRate.Value,
            path.Rotation,
            DisplayTopologyIntent.PreserveActiveTopology);
        Validate(state);
        return state;
    }

    public static void Validate(DurableDisplayModeState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(state.Selector);
        if (state.TopologyIntent != DisplayTopologyIntent.PreserveActiveTopology)
            throw new InvalidDataException("Durable display actions currently support only PRESERVE_ACTIVE_TOPOLOGY.");
        if (!state.Resolution.IsValid)
            throw new InvalidDataException("Durable display resolution must be positive.");
        if (state.RefreshRate.Denominator == 0)
            throw new InvalidDataException("Durable display refresh denominator must be non-zero.");
        if (state.Rotation is < 1 or > 4)
            throw new InvalidDataException("Durable display rotation must be one of IDENTITY/90/180/270.");

        var selector = state.Selector;
        if (selector.EdidManufactureId.HasValue != selector.EdidProductCodeId.HasValue)
            throw new InvalidDataException("Durable display selector must carry both EDID manufacture and product ids or neither.");
        if (selector.AllowWeakFallback)
            throw new InvalidDataException("Durable display action refuses friendly-name-only weak fallback in this recovery-safe vertical.");

        var hasStrongIdentity = !string.IsNullOrWhiteSpace(selector.PnpDeviceInstanceId) ||
                                !string.IsNullOrWhiteSpace(selector.MonitorDevicePath);
        if (!hasStrongIdentity && !selector.HasEdidPair)
            throw new InvalidDataException("Durable display selector lacks persistent physical identity evidence required for rollback re-resolution.");
    }
}
