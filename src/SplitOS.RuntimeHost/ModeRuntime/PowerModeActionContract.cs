using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SplitOS.Contracts.Protocol;
using SplitOS.Persistence.Machine;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.ModeRuntime;

public sealed record PowerPolicyDesiredState(string PowerPolicyId);

public sealed record PowerSchemePreState(Guid ActiveSchemeId);

/// <summary>
/// Canonical durable semantics for current-user power-policy actions. Desired intent stores only the
/// release-owned semantic PowerPolicyId. Exact source Windows scheme identity is captured separately
/// as rollback evidence before mutation.
/// </summary>
public static class PowerModeActionContract
{
    public const int DesiredSchemaVersion = 1;
    public const int PreStateSchemaVersion = 1;
    public const string OwningModule = "windows-power";
    public const string ActionType = "power-policy.apply";
    public const string TargetRef = "current-user-power-policy";
    public const string RollbackClass = "restore_pre_state";
    public const string VerificationClass = "power.active-scheme.read-back";

    public static PersistedModeActionDefinition CreateDefinition(
        Guid actionId,
        int sequenceNo,
        string powerPolicyId,
        bool mandatory = true)
    {
        if (actionId == Guid.Empty)
            throw new ArgumentException("Power action id must not be empty.", nameof(actionId));
        if (sequenceNo < 1)
            throw new ArgumentOutOfRangeException(nameof(sequenceNo));

        var desired = NormalizeDesired(new PowerPolicyDesiredState(powerPolicyId));
        var json = SerializeDesired(desired);
        return new PersistedModeActionDefinition(
            actionId,
            sequenceNo,
            OwningModule,
            ActionType,
            TargetRef,
            DesiredSchemaVersion,
            json,
            Sha256(json),
            mandatory,
            RollbackClass,
            VerificationClass);
    }

    public static PowerPolicyDesiredState NormalizeDesired(PowerPolicyDesiredState desired)
    {
        ArgumentNullException.ThrowIfNull(desired);
        PowerPolicyCatalogResolver.ValidatePolicyId(desired.PowerPolicyId);
        return desired;
    }

    public static string SerializeDesired(PowerPolicyDesiredState desired)
    {
        var normalized = NormalizeDesired(desired);
        return JsonSerializer.Serialize(
            new DesiredDocument(DesiredSchemaVersion, normalized.PowerPolicyId),
            ProtocolJson.Options);
    }

    public static PowerPolicyDesiredState DeserializeDesired(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Power desired-state JSON is missing.", nameof(json));

        var document = JsonSerializer.Deserialize<DesiredDocument>(json, ProtocolJson.Options)
            ?? throw new JsonException("Power desired-state document is null.");
        if (document.SchemaVersion != DesiredSchemaVersion)
            throw new ArgumentException($"Power desired-state schema {document.SchemaVersion} is not supported.", nameof(json));

        var normalized = NormalizeDesired(new PowerPolicyDesiredState(
            document.PowerPolicyId ?? throw new ArgumentException("PowerPolicyId is missing.", nameof(json))));
        if (!string.Equals(json, SerializeDesired(normalized), StringComparison.Ordinal))
            throw new ArgumentException("Power desired-state JSON is not in canonical serialization form.", nameof(json));
        return normalized;
    }

    public static string ComputeDesiredDigest(PowerPolicyDesiredState desired)
        => Sha256(SerializeDesired(desired));

    public static PowerSchemePreState CapturePreState(Guid activeSchemeId)
    {
        if (activeSchemeId == Guid.Empty)
            throw new ArgumentException("Power rollback pre-state requires a non-empty active scheme GUID.", nameof(activeSchemeId));
        return new PowerSchemePreState(activeSchemeId);
    }

    public static string SerializePreState(PowerSchemePreState preState)
    {
        var normalized = NormalizePreState(preState);
        return JsonSerializer.Serialize(
            new PreStateDocument(PreStateSchemaVersion, normalized.ActiveSchemeId),
            ProtocolJson.Options);
    }

    public static PowerSchemePreState DeserializePreState(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Power pre-state JSON is missing.", nameof(json));

        var document = JsonSerializer.Deserialize<PreStateDocument>(json, ProtocolJson.Options)
            ?? throw new JsonException("Power pre-state document is null.");
        if (document.SchemaVersion != PreStateSchemaVersion)
            throw new ArgumentException($"Power pre-state schema {document.SchemaVersion} is not supported.", nameof(json));

        var normalized = NormalizePreState(new PowerSchemePreState(document.ActiveSchemeId));
        if (!string.Equals(json, SerializePreState(normalized), StringComparison.Ordinal))
            throw new ArgumentException("Power pre-state JSON is not in canonical serialization form.", nameof(json));
        return normalized;
    }

    public static string ComputePreStateDigest(PowerSchemePreState preState)
        => Sha256(SerializePreState(preState));

    private static PowerSchemePreState NormalizePreState(PowerSchemePreState preState)
    {
        ArgumentNullException.ThrowIfNull(preState);
        if (preState.ActiveSchemeId == Guid.Empty)
            throw new ArgumentException("Power rollback pre-state requires a non-empty active scheme GUID.", nameof(preState));
        return preState;
    }

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record DesiredDocument(int SchemaVersion, string? PowerPolicyId);
    private sealed record PreStateDocument(int SchemaVersion, Guid ActiveSchemeId);
}
