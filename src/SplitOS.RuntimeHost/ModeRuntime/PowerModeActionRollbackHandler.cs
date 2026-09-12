using System.Text.Json;
using SplitOS.Persistence.Machine;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.ModeRuntime;

/// <summary>
/// Restart-safe compensation for one durable current-user power action. Recovery never re-resolves
/// the semantic PowerPolicyId: it restores the exact Windows scheme GUID captured before mutation,
/// then proves the actual active scheme with a fresh read-back.
/// </summary>
public sealed class PowerModeActionRollbackHandler(
    IPowerSchemeQuery query,
    IPowerSchemeSetter setter,
    IControlSessionIdentity controlSessionIdentity) : IModeActionRollbackHandler
{
    public bool CanHandle(PersistedModeActionRecord action) => PowerActionSemantics.CanHandle(action);

    public Task<RuntimeModeRollbackStepOutcome> RollbackAsync(
        ModeActionRollbackCommand command,
        PersistedModeActionRecord action,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = Validate(action);
        if (validation is not null)
            return Task.FromResult(Reconciliation(validation.Value.ProductCode));
        if (!AuthorityMatches(command.ControlSessionKey))
            return Task.FromResult(Reconciliation("MODE_POWER_CONTROL_CONTEXT_STALE"));

        try
        {
            _ = PowerActionSemantics.ReadDesired(action);
            var preState = PowerActionSemantics.ReadPreState(action);
            var before = query.QueryActiveScheme();
            if (!AuthorityMatches(command.ControlSessionKey))
                return Task.FromResult(Reconciliation("MODE_POWER_CONTROL_CONTEXT_STALE"));

            if (before == preState.ActiveSchemeId)
            {
                return Task.FromResult(new RuntimeModeRollbackStepOutcome(
                    "VERIFIED",
                    "MODE_POWER_ROLLBACK_ALREADY_VERIFIED"));
            }

            if (!AuthorityMatches(command.ControlSessionKey))
                return Task.FromResult(Reconciliation("MODE_POWER_CONTROL_CONTEXT_STALE"));

            var set = setter.SetActiveScheme(preState.ActiveSchemeId);
            var after = query.QueryActiveScheme();
            if (!AuthorityMatches(command.ControlSessionKey))
                return Task.FromResult(Reconciliation("MODE_POWER_CONTROL_CONTEXT_STALE"));

            if (after == preState.ActiveSchemeId)
            {
                return Task.FromResult(new RuntimeModeRollbackStepOutcome(
                    "VERIFIED",
                    set.ErrorCode == 0
                        ? "MODE_POWER_ROLLBACK_VERIFIED"
                        : "MODE_POWER_ROLLBACK_VERIFIED_AFTER_SET_REJECTED"));
            }

            return Task.FromResult(Reconciliation(
                set.ErrorCode == 0
                    ? "MODE_POWER_ROLLBACK_MISMATCH"
                    : "MODE_POWER_ROLLBACK_SET_REJECTED"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidDataException or System.ComponentModel.Win32Exception)
        {
            return Task.FromResult(Reconciliation("MODE_POWER_ROLLBACK_EVIDENCE_UNAVAILABLE"));
        }
    }

    private static (string ProductCode, string Detail)? Validate(PersistedModeActionRecord action)
    {
        if (!PowerActionSemantics.CanHandle(action) || action.State != PersistedModeActionState.RollingBack)
        {
            return (
                "MODE_POWER_ROLLBACK_ACTION_INVALID",
                "Durable power rollback requires a supported ROLLING_BACK action.");
        }
        if (string.IsNullOrWhiteSpace(action.PreStateJson) || string.IsNullOrWhiteSpace(action.PreStateDigest))
        {
            return (
                "MODE_POWER_PRESTATE_MISSING",
                "Durable power rollback has no captured pre-state evidence.");
        }

        try
        {
            _ = PowerActionSemantics.ReadDesired(action);
            _ = PowerActionSemantics.ReadPreState(action);
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidDataException)
        {
            return ("MODE_POWER_ROLLBACK_EVIDENCE_INVALID", ex.Message);
        }
        return null;
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
