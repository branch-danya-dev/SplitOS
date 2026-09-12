using System.Text.Json;
using SplitOS.Persistence.Machine;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.ModeRuntime;

public sealed class PowerModeActionApplyHandler(
    IModeTransitionActionJournalStore journal,
    IPowerSchemeQuery query,
    PowerSchemeApplyCoordinator coordinator,
    IControlSessionIdentity controlSessionIdentity) : IModeActionApplyHandler
{
    public bool CanHandle(PersistedModeActionRecord action) => PowerActionSemantics.CanHandle(action);

    public async Task<ModeActionApplyStageOutcome> ApplyAsync(
        ModeActionExecutionCommand command,
        PersistedModeActionRecord action,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = PowerActionSemantics.ValidateForApply(action);
        if (validation is not null)
            return new(false, validation.Value.ProductCode, action.Revision, validation.Value.Detail);
        if (!AuthorityMatches(command.ControlSessionKey, out var authorityDetail))
            return new(false, "MODE_POWER_CONTROL_CONTEXT_STALE", action.Revision, authorityDetail);

        PowerPolicyDesiredState desired;
        PowerSchemePreState preState;
        string preJson;
        string preDigest;
        try
        {
            desired = PowerActionSemantics.ReadDesired(action);
            preState = PowerModeActionContract.CapturePreState(query.QueryActiveScheme());
            preJson = PowerModeActionContract.SerializePreState(preState);
            preDigest = PowerModeActionContract.ComputePreStateDigest(preState);
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidDataException or System.ComponentModel.Win32Exception)
        {
            return new(false, "MODE_POWER_PRESTATE_UNAVAILABLE", action.Revision, ex.Message);
        }

        if (!AuthorityMatches(command.ControlSessionKey, out authorityDetail))
            return new(false, "MODE_POWER_CONTROL_CONTEXT_STALE", action.Revision, authorityDetail);

        await journal.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var begin = await journal.BeginApplyAsync(
            command.TransitionId,
            command.ActionId,
            command.ExpectedActionRevision,
            command.LeaseId,
            command.FenceToken,
            command.OperationId,
            preJson,
            preDigest,
            cancellationToken).ConfigureAwait(false);
        if (!IsJournalSuccess(begin))
            return JournalApplyRejected(begin);
        var applying = begin.Action
            ?? throw new InvalidDataException("Power BeginApply did not return APPLYING evidence.");

        if (!AuthorityMatches(command.ControlSessionKey, out authorityDetail))
        {
            var denied = await RecordApplyAsync(
                command,
                applying.Revision,
                PersistedModeApplyResult.Failed,
                cancellationToken).ConfigureAwait(false);
            return new(false, "MODE_POWER_CONTROL_CONTEXT_STALE", denied.Action?.Revision ?? applying.Revision, authorityDetail);
        }

        PowerSchemeApplyOutcome outcome;
        try
        {
            outcome = coordinator.Apply(desired.PowerPolicyId, preState.ActiveSchemeId);
        }
        catch (Exception ex)
        {
            var unknown = await RecordApplyAsync(
                command,
                applying.Revision,
                PersistedModeApplyResult.Unknown,
                cancellationToken).ConfigureAwait(false);
            return IsJournalSuccess(unknown)
                ? new(false, "MODE_POWER_APPLY_OUTCOME_UNKNOWN", unknown.Action?.Revision, ex.Message)
                : JournalApplyRejected(unknown, "Power mutation outcome became unknown and could not be persisted");
        }

        var stillAuthorized = AuthorityMatches(command.ControlSessionKey, out authorityDetail);
        var persisted = stillAuthorized && outcome.IsVerified
            ? PersistedModeApplyResult.Applied
            : !stillAuthorized || outcome.OperationAttempted
                ? PersistedModeApplyResult.Unknown
                : PersistedModeApplyResult.Failed;
        var recorded = await RecordApplyAsync(
            command,
            applying.Revision,
            persisted,
            cancellationToken).ConfigureAwait(false);
        if (!IsJournalSuccess(recorded))
            return JournalApplyRejected(recorded, "Power apply result could not be persisted");
        if (!stillAuthorized)
            return new(false, "MODE_POWER_CONTROL_CONTEXT_STALE", recorded.Action?.Revision, authorityDetail);

        return outcome.IsVerified
            ? new(true, outcome.ProductCode, recorded.Action?.Revision)
            : new(false, outcome.ProductCode, recorded.Action?.Revision);
    }

    private Task<ModeActionAdvanceOutcome> RecordApplyAsync(
        ModeActionExecutionCommand command,
        int revision,
        PersistedModeApplyResult result,
        CancellationToken cancellationToken)
        => journal.RecordApplyResultAsync(
            command.TransitionId,
            command.ActionId,
            revision,
            command.LeaseId,
            command.FenceToken,
            command.OperationId,
            result,
            cancellationToken);

    private bool AuthorityMatches(string expected, out string? detail)
    {
        try
        {
            detail = string.Equals(controlSessionIdentity.GetCurrentKey(), expected, StringComparison.Ordinal)
                ? null
                : "The durable power action no longer belongs to the active physical-console control session.";
            return detail is null;
        }
        catch (Exception ex)
        {
            detail = $"Physical-console identity could not be re-derived: {ex.Message}";
            return false;
        }
    }

    private static bool IsJournalSuccess(ModeActionAdvanceOutcome outcome)
        => outcome.Disposition is ModeActionAdvanceDisposition.Advanced or ModeActionAdvanceDisposition.Replayed;

    private static ModeActionApplyStageOutcome JournalApplyRejected(ModeActionAdvanceOutcome outcome, string? prefix = null)
        => new(
            false,
            outcome.ProductCode,
            outcome.Action?.Revision ?? outcome.ActualActionRevision,
            prefix is null ? outcome.Detail : $"{prefix}: {outcome.Detail ?? outcome.ProductCode}");
}

public sealed class PowerModeActionVerifyHandler(
    IModeTransitionActionJournalStore journal,
    IPowerSchemeQuery query,
    IPowerPolicyCatalogResolver policyResolver,
    IControlSessionIdentity controlSessionIdentity) : IModeActionVerifyHandler
{
    public bool CanHandle(PersistedModeActionRecord action) => PowerActionSemantics.CanHandle(action);

    public async Task<ModeActionVerifyStageOutcome> VerifyAsync(
        ModeActionExecutionCommand command,
        PersistedModeActionRecord action,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = PowerActionSemantics.ValidateForVerify(action);
        if (validation is not null)
            return new(false, validation.Value.ProductCode, action.Revision, validation.Value.Detail);
        if (!AuthorityMatches(command.ControlSessionKey, out var authorityDetail))
            return new(false, "MODE_POWER_CONTROL_CONTEXT_STALE", action.Revision, authorityDetail);

        await journal.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var begin = await journal.BeginVerifyAsync(
            command.TransitionId,
            command.ActionId,
            command.ExpectedActionRevision,
            command.LeaseId,
            command.FenceToken,
            command.OperationId,
            cancellationToken).ConfigureAwait(false);
        if (!IsJournalSuccess(begin))
            return JournalVerifyRejected(begin);
        var verifying = begin.Action
            ?? throw new InvalidDataException("Power BeginVerify did not return VERIFYING evidence.");

        PersistedModeVerifyResult persisted;
        string productCode;
        string? detail = null;
        try
        {
            var desired = PowerActionSemantics.ReadDesired(action);
            var preState = PowerActionSemantics.ReadPreState(action);
            if (!policyResolver.TryResolve(desired.PowerPolicyId, out var target) || target is null)
            {
                persisted = PersistedModeVerifyResult.Mismatch;
                productCode = "MODE_POWER_POLICY_TARGET_NOT_FOUND";
            }
            else
            {
                var expected = target.ResolutionKind switch
                {
                    PowerPolicyResolutionKind.Scheme => target.SchemeId
                        ?? throw new InvalidDataException("Resolved power scheme target omitted its Windows scheme GUID."),
                    PowerPolicyResolutionKind.NoChange => preState.ActiveSchemeId,
                    _ => throw new InvalidDataException("Resolved power policy kind is unsupported.")
                };
                var observed = query.QueryActiveScheme();
                if (observed == expected)
                {
                    persisted = PersistedModeVerifyResult.Verified;
                    productCode = "MODE_POWER_SCHEME_VERIFIED";
                }
                else
                {
                    persisted = PersistedModeVerifyResult.Mismatch;
                    productCode = "MODE_POWER_SCHEME_MISMATCH";
                    detail = $"Fresh active power scheme {observed:D} does not match durable expected scheme {expected:D}.";
                }
            }

            if (!AuthorityMatches(command.ControlSessionKey, out authorityDetail))
            {
                persisted = PersistedModeVerifyResult.Unknown;
                productCode = "MODE_POWER_CONTROL_CONTEXT_STALE";
                detail = authorityDetail;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidDataException or System.ComponentModel.Win32Exception)
        {
            persisted = PersistedModeVerifyResult.Unknown;
            productCode = "MODE_POWER_VERIFY_EVIDENCE_UNAVAILABLE";
            detail = ex.Message;
        }

        var recorded = await journal.RecordVerifyResultAsync(
            command.TransitionId,
            command.ActionId,
            verifying.Revision,
            command.LeaseId,
            command.FenceToken,
            command.OperationId,
            persisted,
            cancellationToken).ConfigureAwait(false);
        if (!IsJournalSuccess(recorded))
            return JournalVerifyRejected(recorded, "Power verification result could not be persisted");

        return persisted == PersistedModeVerifyResult.Verified
            ? new(true, productCode, recorded.Action?.Revision, detail)
            : new(false, productCode, recorded.Action?.Revision, detail);
    }

    private bool AuthorityMatches(string expected, out string? detail)
    {
        try
        {
            detail = string.Equals(controlSessionIdentity.GetCurrentKey(), expected, StringComparison.Ordinal)
                ? null
                : "The durable power action no longer belongs to the active physical-console control session.";
            return detail is null;
        }
        catch (Exception ex)
        {
            detail = $"Physical-console identity could not be re-derived: {ex.Message}";
            return false;
        }
    }

    private static bool IsJournalSuccess(ModeActionAdvanceOutcome outcome)
        => outcome.Disposition is ModeActionAdvanceDisposition.Advanced or ModeActionAdvanceDisposition.Replayed;

    private static ModeActionVerifyStageOutcome JournalVerifyRejected(ModeActionAdvanceOutcome outcome, string? prefix = null)
        => new(
            false,
            outcome.ProductCode,
            outcome.Action?.Revision ?? outcome.ActualActionRevision,
            prefix is null ? outcome.Detail : $"{prefix}: {outcome.Detail ?? outcome.ProductCode}");
}

internal static class PowerActionSemantics
{
    public static bool CanHandle(PersistedModeActionRecord action)
        => string.Equals(action.OwningModule, PowerModeActionContract.OwningModule, StringComparison.Ordinal) &&
           string.Equals(action.ActionType, PowerModeActionContract.ActionType, StringComparison.Ordinal);

    public static (string ProductCode, string Detail)? ValidateForApply(PersistedModeActionRecord action)
    {
        var header = ValidateHeader(action);
        if (header is not null) return header;
        if (action.State != PersistedModeActionState.Planned)
            return ("MODE_POWER_ACTION_NOT_PLANNED", $"Action state is {action.State}.");
        return ValidateDesired(action);
    }

    public static (string ProductCode, string Detail)? ValidateForVerify(PersistedModeActionRecord action)
    {
        var header = ValidateHeader(action);
        if (header is not null) return header;
        if (action.State != PersistedModeActionState.Applied ||
            !string.Equals(action.ApplyResultCode, "APPLIED", StringComparison.Ordinal))
        {
            return ("MODE_POWER_ACTION_NOT_APPLIED", $"Action must have durable APPLIED evidence before verification; actual state is {action.State}.");
        }

        var desired = ValidateDesired(action);
        if (desired is not null) return desired;
        if (string.IsNullOrWhiteSpace(action.PreStateJson) || string.IsNullOrWhiteSpace(action.PreStateDigest))
            return ("MODE_POWER_PRESTATE_MISSING", "Power action has no durable pre-mutation evidence.");
        try
        {
            _ = ReadPreState(action);
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidDataException)
        {
            return ("MODE_POWER_PRESTATE_INVALID", ex.Message);
        }

        return null;
    }

    public static PowerPolicyDesiredState ReadDesired(PersistedModeActionRecord action)
    {
        var desired = PowerModeActionContract.DeserializeDesired(action.DesiredStateJson!);
        if (!string.Equals(
                PowerModeActionContract.ComputeDesiredDigest(desired),
                action.DesiredStateDigest,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Power desired-state digest does not match its canonical payload.");
        }
        return desired;
    }

    public static PowerSchemePreState ReadPreState(PersistedModeActionRecord action)
    {
        var preState = PowerModeActionContract.DeserializePreState(action.PreStateJson!);
        if (!string.Equals(
                PowerModeActionContract.ComputePreStateDigest(preState),
                action.PreStateDigest,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Power pre-state digest does not match its canonical payload.");
        }
        return preState;
    }

    private static (string ProductCode, string Detail)? ValidateHeader(PersistedModeActionRecord action)
    {
        if (!CanHandle(action) ||
            !string.Equals(action.TargetRef, PowerModeActionContract.TargetRef, StringComparison.Ordinal) ||
            action.DesiredSchemaVersion != PowerModeActionContract.DesiredSchemaVersion ||
            !string.Equals(action.RollbackClass, PowerModeActionContract.RollbackClass, StringComparison.Ordinal) ||
            !string.Equals(action.VerificationClass, PowerModeActionContract.VerificationClass, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(action.DesiredStateJson))
        {
            return ("MODE_POWER_ACTION_SEMANTICS_INVALID", "Durable action is not a supported canonical power action.");
        }
        return null;
    }

    private static (string ProductCode, string Detail)? ValidateDesired(PersistedModeActionRecord action)
    {
        try
        {
            _ = ReadDesired(action);
            return null;
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidDataException)
        {
            return ("MODE_POWER_DESIRED_STATE_INVALID", ex.Message);
        }
    }
}
