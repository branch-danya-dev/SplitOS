namespace SplitOS.RuntimeHost.WindowsContext;

public enum PowerPolicyResolutionKind
{
    Scheme,
    NoChange
}

public sealed record PowerPolicyCatalogEntry(
    string PowerPolicyId,
    PowerPolicyResolutionKind ResolutionKind,
    Guid? SchemeId);

public sealed record ResolvedPowerPolicyTarget(
    string PowerPolicyId,
    PowerPolicyResolutionKind ResolutionKind,
    Guid? SchemeId);

public interface IPowerPolicyCatalogResolver
{
    bool TryResolve(string powerPolicyId, out ResolvedPowerPolicyTarget? target);
}

/// <summary>
/// Release-owned semantic power-policy catalog. Callers choose only a PowerPolicyId; the Windows
/// scheme GUID is supplied by trusted release configuration rather than UI/profile input.
/// </summary>
public sealed class PowerPolicyCatalogResolver : IPowerPolicyCatalogResolver
{
    public const int MaxPolicyIdLength = 64;

    private readonly IReadOnlyDictionary<string, ResolvedPowerPolicyTarget> _targets;

    public PowerPolicyCatalogResolver(IReadOnlyCollection<PowerPolicyCatalogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var targets = new Dictionary<string, ResolvedPowerPolicyTarget>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            ValidatePolicyId(entry.PowerPolicyId);
            ValidateEntry(entry);
            var resolved = new ResolvedPowerPolicyTarget(
                entry.PowerPolicyId,
                entry.ResolutionKind,
                entry.SchemeId);
            if (!targets.TryAdd(entry.PowerPolicyId, resolved))
                throw new ArgumentException($"Power policy id {entry.PowerPolicyId} appears more than once.", nameof(entries));
        }

        _targets = targets;
    }

    public bool TryResolve(string powerPolicyId, out ResolvedPowerPolicyTarget? target)
    {
        ValidatePolicyId(powerPolicyId);
        return _targets.TryGetValue(powerPolicyId, out target);
    }

    public static void ValidatePolicyId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxPolicyIdLength)
            throw new ArgumentException("PowerPolicyId is missing or outside supported bounds.", nameof(value));

        foreach (var character in value)
        {
            var allowed = character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-' or '.';
            if (!allowed)
                throw new ArgumentException("PowerPolicyId must be an uppercase release-owned semantic identifier.", nameof(value));
        }
    }

    private static void ValidateEntry(PowerPolicyCatalogEntry entry)
    {
        switch (entry.ResolutionKind)
        {
            case PowerPolicyResolutionKind.Scheme:
                if (!entry.SchemeId.HasValue || entry.SchemeId.Value == Guid.Empty)
                    throw new ArgumentException("Scheme power policy requires a non-empty Windows scheme GUID.", nameof(entry));
                break;
            case PowerPolicyResolutionKind.NoChange:
                if (entry.SchemeId.HasValue)
                    throw new ArgumentException("NO_CHANGE power policy must not carry a Windows scheme GUID.", nameof(entry));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(entry), "Power policy resolution kind is not supported.");
        }
    }
}
