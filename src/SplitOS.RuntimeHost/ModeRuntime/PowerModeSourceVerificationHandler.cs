using System.Text.Json;
using SplitOS.Persistence.Machine;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.ModeRuntime;

/// <summary>
/// Independently proves the original current-user power scheme after reverse-order compensation.
/// The verifier derives the source only from durable pre-state evidence and never trusts rollback
/// return codes or the release power-policy mapping as proof of restoration.
/// </summary>
public sealed class PowerModeSourceVerificationHandler(
    IModeActionPlanReader plans,
    IModeActionRecordReader actions,
    IPowerSchemeQuery query,
    IControlSessionIdentity controlSessionIdentity) : IModeSourceVerificationHandler
{
    public bool CanHandle(PersistedModeActionRecord action) => PowerActionSemantics.CanHandle(action);

    public async Task<RuntimeModeRollbackStepOutcome> VerifySourceAsync(
        ModeSourceVerificationCommand command,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!AuthorityMatches(command.ControlSessionKey))
            return Reconciliation("MODE_SOURCE_POWER_CONTROL_CONTEXT_STALE");

        var plan = await plans.GetAsync(command.TransitionId, cancellationToken).ConfigureAwait(false);
        if (plan is null || plan.TransitionId != command.TransitionId || plan.ActionCount != plan.Actions.Count)
            return new("UNAVAILABLE", "MODE_SOURCE_POWER_PLAN_UNAVAILABLE");

        var powerActions = plan.Actions
            .Where(PowerActionSemantics.CanHandle)
            .OrderBy(static action => action.SequenceNo)
            .ToArray();
        if (powerActions.Length == 0)
            return new("UNAVAILABLE", "MODE_SOURCE_POWER_PLAN_UNAVAILABLE");

        await actions.InitializeAsync(cancellationToken).ConfigureAwait(false);
        Guid? sourceSchemeId = null;
        var observedPreState = false;
        foreach (var planned in powerActions)
        {
            var record = await actions.GetAsync(planned.ActionId, cancellationToken).ConfigureAwait(false);
            if (record is null || record.TransitionId != command.TransitionId ||
                record.ActionId != planned.ActionId || record.SequenceNo != planned.SequenceNo ||
                !PowerActionSemantics.CanHandle(record) ||
                !string.Equals(record.ActionType, planned.ActionType, StringComparison.Ordinal))
            {
                return Reconciliation("MODE_SOURCE_POWER_JOURNAL_MISMATCH");
            }

            if (record.State is PersistedModeActionState.Applying
                or PersistedModeActionState.RollingBack
                or PersistedModeActionState.RollbackFailed)
            {
                return Reconciliation("MODE_SOURCE_POWER_ACTION_UNSETTLED");
            }

            try
            {
                _ = PowerActionSemantics.ReadDesired(record);
            }
            catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidDataException)
            {
                return Reconciliation("MODE_SOURCE_POWER_DESIRED_STATE_INVALID");
            }

            var mutationEvidence = record.ApplyResultCode is "APPLIED" or "UNKNOWN";
            if (mutationEvidence &&
                (record.State != PersistedModeActionState.RolledBack ||
                 !string.Equals(record.RollbackResultCode, "ROLLED_BACK", StringComparison.Ordinal)))
            {
                return Reconciliation("MODE_SOURCE_POWER_ROLLBACK_EVIDENCE_REQUIRED");
            }
            if (mutationEvidence &&
                (string.IsNullOrWhiteSpace(record.PreStateJson) || string.IsNullOrWhiteSpace(record.PreStateDigest)))
            {
                return Reconciliation("MODE_SOURCE_POWER_PRESTATE_REQUIRED");
            }

            if (string.IsNullOrWhiteSpace(record.PreStateJson) && string.IsNullOrWhiteSpace(record.PreStateDigest))
                continue;
            if (string.IsNullOrWhiteSpace(record.PreStateJson) || string.IsNullOrWhiteSpace(record.PreStateDigest))
                return Reconciliation("MODE_SOURCE_POWER_PRESTATE_INVALID");

            try
            {
                var preState = PowerActionSemantics.ReadPreState(record);
                sourceSchemeId ??= preState.ActiveSchemeId;
                observedPreState = true;
            }
            catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidDataException)
            {
                return Reconciliation("MODE_SOURCE_POWER_PRESTATE_INVALID");
            }
        }

        if (!observedPreState || !sourceSchemeId.HasValue)
        {
            return AuthorityMatches(command.ControlSessionKey)
                ? new RuntimeModeRollbackStepOutcome("VERIFIED", "MODE_SOURCE_POWER_NO_MUTATION")
                : Reconciliation("MODE_SOURCE_POWER_CONTROL_CONTEXT_STALE");
        }

        Guid observed;
        try
        {
            observed = query.QueryActiveScheme();
        }
        catch
        {
            return Reconciliation("MODE_SOURCE_POWER_EVIDENCE_UNAVAILABLE");
        }

        if (!AuthorityMatches(command.ControlSessionKey))
            return Reconciliation("MODE_SOURCE_POWER_CONTROL_CONTEXT_STALE");
        if (observed != sourceSchemeId.Value)
            return new("MISMATCH", "MODE_SOURCE_POWER_SCHEME_MISMATCH");

        return new("VERIFIED", "MODE_SOURCE_POWER_VERIFIED");
    }

    private bool AuthorityMatches(string expected)
    {
        try
        {
            return string.Equals(controlSessionIdentity.GetCurrentKey(), expected, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static RuntimeModeRollbackStepOutcome Reconciliation(string productCode)
        => new("RECONCILIATION_REQUIRED", productCode);
}
