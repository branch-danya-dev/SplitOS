using SplitOS.Contracts.Protocol;
using SplitOS.Persistence.Machine;

namespace SplitOS.RuntimeHost.ModeRuntime;

public sealed record ModePreparedActionValidationOutcome(
    bool IsValid,
    string ProductCode,
    string? Detail = null)
{
    public static ModePreparedActionValidationOutcome Valid(string productCode)
        => new(true, productCode);

    public static ModePreparedActionValidationOutcome Invalid(string productCode, string detail)
        => new(false, productCode, detail);
}

public interface IModePreparedActionValidator
{
    bool CanHandle(PersistedModeActionDefinition action);

    ModePreparedActionValidationOutcome Validate(PersistedModeActionDefinition action);
}

/// <summary>
/// Fail-closed semantic validation for immutable prepared actions before the durable plan is created.
/// Exactly one domain validator must own every action. Validation never executes Windows mutation.
/// </summary>
public sealed class ModePreparedActionValidationDispatcher(
    IEnumerable<IModePreparedActionValidator> validators)
{
    private readonly IModePreparedActionValidator[] _validators = validators?.ToArray()
        ?? throw new ArgumentNullException(nameof(validators));

    public ModePreparedActionValidationOutcome Validate(PersistedModeActionDefinition action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var structural = ValidateStructural(action);
        if (structural is not null)
            return structural;

        var matches = _validators.Where(validator => validator.CanHandle(action)).Take(2).ToArray();
        if (matches.Length == 0)
        {
            return ModePreparedActionValidationOutcome.Invalid(
                "MODE_PREPARED_ACTION_VALIDATOR_UNAVAILABLE",
                $"No prepared-action validator owns {action.OwningModule}/{action.ActionType}.");
        }
        if (matches.Length > 1)
        {
            return ModePreparedActionValidationOutcome.Invalid(
                "MODE_PREPARED_ACTION_VALIDATOR_AMBIGUOUS",
                $"More than one prepared-action validator owns {action.OwningModule}/{action.ActionType}.");
        }

        try
        {
            return matches[0].Validate(action);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or System.Text.Json.JsonException)
        {
            return ModePreparedActionValidationOutcome.Invalid(
                "MODE_PREPARED_ACTION_SEMANTICS_INVALID",
                ex.Message);
        }
    }

    public ModePreparedActionValidationOutcome ValidateAll(
        IReadOnlyList<PersistedModeActionDefinition> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        if (actions.Count == 0)
        {
            return ModePreparedActionValidationOutcome.Invalid(
                "MODE_PREPARED_ACTIONS_EMPTY",
                "Executable mode target must contain at least one durable action.");
        }

        var ids = new HashSet<Guid>();
        var previousSequence = 0;
        foreach (var action in actions)
        {
            var result = Validate(action);
            if (!result.IsValid)
                return result;
            if (!ids.Add(action.ActionId))
            {
                return ModePreparedActionValidationOutcome.Invalid(
                    "MODE_PREPARED_ACTION_ID_DUPLICATE",
                    "Prepared action ids must be unique within one immutable plan.");
            }
            if (action.SequenceNo <= previousSequence)
            {
                return ModePreparedActionValidationOutcome.Invalid(
                    "MODE_PREPARED_ACTION_SEQUENCE_INVALID",
                    "Prepared actions must be supplied in strictly increasing sequence order.");
            }
            previousSequence = action.SequenceNo;
        }

        return ModePreparedActionValidationOutcome.Valid("MODE_PREPARED_ACTIONS_VALID");
    }

    private static ModePreparedActionValidationOutcome? ValidateStructural(PersistedModeActionDefinition action)
    {
        if (action.ActionId == Guid.Empty)
            return ModePreparedActionValidationOutcome.Invalid("MODE_PREPARED_ACTION_ID_INVALID", "Action id must not be empty.");
        if (action.SequenceNo < 1)
            return ModePreparedActionValidationOutcome.Invalid("MODE_PREPARED_ACTION_SEQUENCE_INVALID", "Action sequence must be positive.");
        if (string.IsNullOrWhiteSpace(action.OwningModule) || string.IsNullOrWhiteSpace(action.ActionType) ||
            string.IsNullOrWhiteSpace(action.TargetRef) || string.IsNullOrWhiteSpace(action.DesiredStateJson) ||
            string.IsNullOrWhiteSpace(action.RollbackClass) || string.IsNullOrWhiteSpace(action.VerificationClass))
        {
            return ModePreparedActionValidationOutcome.Invalid(
                "MODE_PREPARED_ACTION_METADATA_INVALID",
                "Prepared action semantic metadata is incomplete.");
        }
        if (action.DesiredSchemaVersion < 1)
            return ModePreparedActionValidationOutcome.Invalid("MODE_PREPARED_ACTION_SCHEMA_INVALID", "Desired-state schema version must be positive.");
        if (!IsDigest(action.DesiredStateDigest))
            return ModePreparedActionValidationOutcome.Invalid("MODE_PREPARED_ACTION_DIGEST_INVALID", "Desired-state digest is malformed.");
        return null;
    }

    private static bool IsDigest(string value)
        => value.Length == 64 && value.All(Uri.IsHexDigit);
}

/// <summary>
/// Release-owned registry of prepared-action contracts currently safe for durable mode execution.
/// This is an admission boundary only; it neither resolves policy nor performs Windows mutation.
/// </summary>
public static class ModePreparedActionValidation
{
    private static readonly ModePreparedActionValidationDispatcher BuiltInDispatcher = new(
        new IModePreparedActionValidator[]
        {
            new ManagedServicePreparedActionValidator(),
            new DisplayPreparedActionValidator(),
            new PowerPreparedActionValidator()
        });

    public static ModePreparedActionValidationOutcome ValidateBuiltIn(
        IReadOnlyList<PersistedModeActionDefinition> actions)
        => BuiltInDispatcher.ValidateAll(actions);
}

public sealed class ManagedServicePreparedActionValidator : IModePreparedActionValidator
{
    private const string RollbackClass = "restore_pre_state";
    private const string VerificationClass = "service.actual-state";

    public bool CanHandle(PersistedModeActionDefinition action)
        => string.Equals(action.OwningModule, ManagedServicePolicyActionContract.OwningModule, StringComparison.Ordinal) &&
           string.Equals(action.ActionType, ManagedServicePolicyActionContract.ActionType, StringComparison.Ordinal);

    public ModePreparedActionValidationOutcome Validate(PersistedModeActionDefinition action)
    {
        if (!string.Equals(action.TargetRef, ManagedServicePolicyActionContract.TargetRef, StringComparison.Ordinal) ||
            action.DesiredSchemaVersion != ManagedServicePolicyActionContract.DesiredSchemaVersion ||
            !string.Equals(action.RollbackClass, RollbackClass, StringComparison.Ordinal) ||
            !string.Equals(action.VerificationClass, VerificationClass, StringComparison.Ordinal))
        {
            return ModePreparedActionValidationOutcome.Invalid(
                "MODE_PREPARED_SERVICE_METADATA_INVALID",
                "Managed-service action metadata does not match the release-owned semantic contract.");
        }

        var desired = ManagedServicePolicyActionContract.DeserializeDesiredState(action.DesiredStateJson);
        var digest = ManagedServicePolicyActionContract.ComputeDesiredStateDigest(desired);
        if (!string.Equals(digest, action.DesiredStateDigest, StringComparison.OrdinalIgnoreCase))
        {
            return ModePreparedActionValidationOutcome.Invalid(
                "MODE_PREPARED_SERVICE_DIGEST_MISMATCH",
                "Managed-service desired-state digest does not match canonical durable intent.");
        }

        return ModePreparedActionValidationOutcome.Valid("MODE_PREPARED_SERVICE_ACTION_VALID");
    }
}

public sealed class DisplayPreparedActionValidator : IModePreparedActionValidator
{
    public bool CanHandle(PersistedModeActionDefinition action)
        => string.Equals(action.OwningModule, DisplayModeActionContract.OwningModule, StringComparison.Ordinal) &&
           action.ActionType is DisplayModeActionContract.TopologyExtendActionType or DisplayModeActionContract.TargetModeActionType;

    public ModePreparedActionValidationOutcome Validate(PersistedModeActionDefinition action)
    {
        if (!string.Equals(action.TargetRef, DisplayModeActionContract.TargetRef, StringComparison.Ordinal) ||
            action.DesiredSchemaVersion != DisplayModeActionContract.DesiredSchemaVersion ||
            !string.Equals(action.RollbackClass, DisplayModeActionContract.RollbackClass, StringComparison.Ordinal))
        {
            return ModePreparedActionValidationOutcome.Invalid(
                "MODE_PREPARED_DISPLAY_METADATA_INVALID",
                "Display action metadata does not match the durable display semantic contract.");
        }

        string digest;
        string expectedVerification;
        if (string.Equals(action.ActionType, DisplayModeActionContract.TopologyExtendActionType, StringComparison.Ordinal))
        {
            var desired = DisplayModeActionContract.DeserializeTopologyExtend(action.DesiredStateJson);
            digest = DisplayModeActionContract.ComputeTopologyExtendDigest(desired);
            expectedVerification = DisplayModeActionContract.TopologyVerificationClass;
        }
        else
        {
            var desired = DisplayModeActionContract.DeserializeTargetMode(action.DesiredStateJson);
            digest = DisplayModeActionContract.ComputeTargetModeDigest(desired);
            expectedVerification = DisplayModeActionContract.ModeVerificationClass;
        }

        if (!string.Equals(action.VerificationClass, expectedVerification, StringComparison.Ordinal))
        {
            return ModePreparedActionValidationOutcome.Invalid(
                "MODE_PREPARED_DISPLAY_VERIFICATION_INVALID",
                "Display action verification class does not match its action type.");
        }
        if (!string.Equals(digest, action.DesiredStateDigest, StringComparison.OrdinalIgnoreCase))
        {
            return ModePreparedActionValidationOutcome.Invalid(
                "MODE_PREPARED_DISPLAY_DIGEST_MISMATCH",
                "Display desired-state digest does not match canonical durable intent.");
        }

        return ModePreparedActionValidationOutcome.Valid("MODE_PREPARED_DISPLAY_ACTION_VALID");
    }
}

public sealed class PowerPreparedActionValidator : IModePreparedActionValidator
{
    public bool CanHandle(PersistedModeActionDefinition action)
        => string.Equals(action.OwningModule, PowerModeActionContract.OwningModule, StringComparison.Ordinal) &&
           string.Equals(action.ActionType, PowerModeActionContract.ActionType, StringComparison.Ordinal);

    public ModePreparedActionValidationOutcome Validate(PersistedModeActionDefinition action)
    {
        if (!string.Equals(action.TargetRef, PowerModeActionContract.TargetRef, StringComparison.Ordinal) ||
            action.DesiredSchemaVersion != PowerModeActionContract.DesiredSchemaVersion ||
            !string.Equals(action.RollbackClass, PowerModeActionContract.RollbackClass, StringComparison.Ordinal) ||
            !string.Equals(action.VerificationClass, PowerModeActionContract.VerificationClass, StringComparison.Ordinal))
        {
            return ModePreparedActionValidationOutcome.Invalid(
                "MODE_PREPARED_POWER_METADATA_INVALID",
                "Power action metadata does not match the durable power semantic contract.");
        }

        var desired = PowerModeActionContract.DeserializeDesired(action.DesiredStateJson);
        var digest = PowerModeActionContract.ComputeDesiredDigest(desired);
        if (!string.Equals(digest, action.DesiredStateDigest, StringComparison.OrdinalIgnoreCase))
        {
            return ModePreparedActionValidationOutcome.Invalid(
                "MODE_PREPARED_POWER_DIGEST_MISMATCH",
                "Power desired-state digest does not match canonical durable intent.");
        }

        return ModePreparedActionValidationOutcome.Valid("MODE_PREPARED_POWER_ACTION_VALID");
    }
}
