using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SplitOS.Contracts.Protocol;

public sealed record ManagedServicePolicyEntry(
    string ManagedServiceId,
    string DesiredState);

public sealed record MachineServicePolicyApplyRequest(
    Guid TransitionId,
    Guid ActionId,
    Guid LeaseId,
    long FenceToken,
    string ControlSessionKey,
    int ExpectedActionRevision,
    IReadOnlyList<ManagedServicePolicyEntry> Entries);

public sealed record ManagedServicePolicyEntryResult(
    string ManagedServiceId,
    string DesiredState,
    bool OperationAttempted,
    string ImmediateResult,
    string ActualStateObserved,
    string VerificationStatus,
    string? ErrorCode);

public sealed record MachineServicePolicyApplyResult(
    string Disposition,
    string ProductCode,
    IReadOnlyList<ManagedServicePolicyEntryResult> Entries);

public sealed record MachineServicePolicySnapshotRequest(
    Guid TransitionId,
    Guid ActionId,
    Guid LeaseId,
    long FenceToken,
    string ControlSessionKey,
    int ExpectedActionRevision,
    IReadOnlyList<ManagedServicePolicyEntry> Entries);

public sealed record ManagedServicePreStateEntry(
    string ManagedServiceId,
    string ActualState);

public sealed record MachineServicePolicySnapshotResult(
    string Disposition,
    string ProductCode,
    IReadOnlyList<ManagedServicePreStateEntry> Entries,
    string? PreStateJson,
    string? PreStateDigest);

/// <summary>
/// Canonical semantic action identity for the privileged managed-service capability.
/// Runtime persists this exact desired-state document/digest in the immutable action plan;
/// Broker recomputes it from the IPC request before the privileged adapter may run.
/// </summary>
public static class ManagedServicePolicyActionContract
{
    public const int DesiredSchemaVersion = 1;
    public const int PreStateSchemaVersion = 1;
    public const int MaxEntries = 32;
    public const string OwningModule = "windows-service";
    public const string ActionType = "service-policy.apply";
    public const string TargetRef = "managed-service-policy";

    public static IReadOnlyList<ManagedServicePolicyEntry> NormalizeEntries(
        IReadOnlyCollection<ManagedServicePolicyEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count is < 1 or > MaxEntries)
        {
            throw new ArgumentOutOfRangeException(
                nameof(entries),
                $"Managed-service policy must contain between 1 and {MaxEntries} entries.");
        }

        var normalized = new List<ManagedServicePolicyEntry>(entries.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            ValidateManagedServiceId(entry.ManagedServiceId);
            if (entry.DesiredState is not ("RUNNING" or "STOPPED"))
            {
                throw new ArgumentException(
                    "Managed-service desired state must be RUNNING or STOPPED.",
                    nameof(entries));
            }

            if (!ids.Add(entry.ManagedServiceId))
            {
                throw new ArgumentException(
                    $"Managed-service id {entry.ManagedServiceId} appears more than once.",
                    nameof(entries));
            }

            normalized.Add(new ManagedServicePolicyEntry(entry.ManagedServiceId, entry.DesiredState));
        }

        normalized.Sort(static (left, right) =>
            StringComparer.Ordinal.Compare(left.ManagedServiceId, right.ManagedServiceId));
        return normalized;
    }

    public static string SerializeDesiredState(IReadOnlyCollection<ManagedServicePolicyEntry> entries)
    {
        var normalized = NormalizeEntries(entries);
        return JsonSerializer.Serialize(
            new DesiredStateDocument(DesiredSchemaVersion, normalized),
            ProtocolJson.Options);
    }

    public static string ComputeDesiredStateDigest(IReadOnlyCollection<ManagedServicePolicyEntry> entries)
    {
        var json = SerializeDesiredState(entries);
        return Sha256(json);
    }

    public static IReadOnlyList<ManagedServicePreStateEntry> NormalizePreState(
        IReadOnlyCollection<ManagedServicePreStateEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count is < 1 or > MaxEntries)
        {
            throw new ArgumentOutOfRangeException(
                nameof(entries),
                $"Managed-service pre-state must contain between 1 and {MaxEntries} entries.");
        }

        var normalized = new List<ManagedServicePreStateEntry>(entries.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            ValidateManagedServiceId(entry.ManagedServiceId);
            if (entry.ActualState is not ("RUNNING" or "STOPPED"))
            {
                throw new ArgumentException(
                    "Managed-service rollback pre-state must be stable RUNNING or STOPPED evidence.",
                    nameof(entries));
            }

            if (!ids.Add(entry.ManagedServiceId))
            {
                throw new ArgumentException(
                    $"Managed-service id {entry.ManagedServiceId} appears more than once in pre-state.",
                    nameof(entries));
            }

            normalized.Add(new ManagedServicePreStateEntry(entry.ManagedServiceId, entry.ActualState));
        }

        normalized.Sort(static (left, right) =>
            StringComparer.Ordinal.Compare(left.ManagedServiceId, right.ManagedServiceId));
        return normalized;
    }

    public static string SerializePreState(IReadOnlyCollection<ManagedServicePreStateEntry> entries)
    {
        var normalized = NormalizePreState(entries);
        return JsonSerializer.Serialize(
            new PreStateDocument(PreStateSchemaVersion, normalized),
            ProtocolJson.Options);
    }

    public static string ComputePreStateDigest(IReadOnlyCollection<ManagedServicePreStateEntry> entries)
        => Sha256(SerializePreState(entries));

    /// <summary>
    /// Rehydrates only the canonical pre-state document emitted by <see cref="SerializePreState"/>.
    /// This deliberately rejects semantically-equivalent but differently serialized JSON so the
    /// durable digest has one byte-exact representation at the privileged mutation boundary.
    /// </summary>
    public static IReadOnlyList<ManagedServicePreStateEntry> DeserializePreState(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("Managed-service pre-state JSON is missing.", nameof(json));
        }

        var document = JsonSerializer.Deserialize<PreStateDocument>(json, ProtocolJson.Options)
            ?? throw new JsonException("Managed-service pre-state document is null.");
        if (document.SchemaVersion != PreStateSchemaVersion)
        {
            throw new ArgumentException(
                $"Managed-service pre-state schema {document.SchemaVersion} is not supported.",
                nameof(json));
        }

        if (document.Entries is null)
        {
            throw new ArgumentException("Managed-service pre-state entries are missing.", nameof(json));
        }

        var normalized = NormalizePreState(document.Entries);
        var canonical = SerializePreState(normalized);
        if (!string.Equals(json, canonical, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Managed-service pre-state JSON is not in canonical serialization form.",
                nameof(json));
        }

        return normalized;
    }

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static void ValidateManagedServiceId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64)
        {
            throw new ArgumentException(
                "Managed-service id is missing or outside supported bounds.",
                nameof(value));
        }

        foreach (var character in value)
        {
            var allowed = character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-' or '.';
            if (!allowed)
            {
                throw new ArgumentException(
                    "Managed-service id must be an uppercase release-owned semantic identifier.",
                    nameof(value));
            }
        }
    }

    private sealed record DesiredStateDocument(
        int SchemaVersion,
        IReadOnlyList<ManagedServicePolicyEntry> Entries);

    private sealed record PreStateDocument(
        int SchemaVersion,
        IReadOnlyList<ManagedServicePreStateEntry> Entries);
}
