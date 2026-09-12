using SplitOS.Contracts.Protocol;
using SplitOS.Persistence.Machine;

namespace SplitOS.RuntimeHost.ModeRuntime;

public sealed record ModeSourceVerificationCommand(
    Guid TransitionId,
    Guid LeaseId,
    long FenceToken,
    Guid OperationId,
    Guid CorrelationId,
    string ControlSessionKey,
    int TransitionRevision);

public interface IModeActionPlanReader
{
    Task<ModeTransitionActionPlan?> GetAsync(Guid transitionId, CancellationToken cancellationToken = default);
}

public sealed class ModeActionPlanReader(IModeTransitionActionPlanStore inner) : IModeActionPlanReader
{
    public Task<ModeTransitionActionPlan?> GetAsync(Guid transitionId, CancellationToken cancellationToken = default)
        => inner.GetAsync(transitionId, cancellationToken);
}

public interface IModeSourceVerificationHandler
{
    bool CanHandle(PersistedModeActionRecord action);

    Task<RuntimeModeRollbackStepOutcome> VerifySourceAsync(
        ModeSourceVerificationCommand command,
        CancellationToken cancellationToken = default);
}

public interface IModeSourceVerificationCoordinator
{
    Task<RuntimeModeRollbackStepOutcome> VerifySourceAsync(
        ModeSourceVerificationCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Verifies the restored canonical source across every durable action domain represented by the
/// interrupted plan. Handler coverage is resolved completely before any verifier is invoked, so a
/// missing or ambiguous domain can never produce a partially accepted source verification.
/// </summary>
public sealed class ModeSourceVerificationDispatcher(
    IModeActionPlanReader plans,
    IEnumerable<IModeSourceVerificationHandler> handlers) : IModeSourceVerificationCoordinator
{
    private readonly IModeSourceVerificationHandler[] _handlers = handlers?.ToArray()
        ?? throw new ArgumentNullException(nameof(handlers));

    public async Task<RuntimeModeRollbackStepOutcome> VerifySourceAsync(
        ModeSourceVerificationCommand command,
        CancellationToken cancellationToken = default)
    {
        Validate(command);
        var plan = await plans.GetAsync(command.TransitionId, cancellationToken).ConfigureAwait(false);
        if (plan is null || plan.TransitionId != command.TransitionId || plan.Actions.Count == 0 ||
            plan.ActionCount != plan.Actions.Count)
        {
            return new("UNAVAILABLE", "MODE_SOURCE_PLAN_UNAVAILABLE");
        }

        var selected = new List<IModeSourceVerificationHandler>();
        var seen = new HashSet<IModeSourceVerificationHandler>(ReferenceEqualityComparer.Instance);
        foreach (var action in plan.Actions)
        {
            var matches = _handlers.Where(handler => handler.CanHandle(action)).Take(2).ToArray();
            if (matches.Length == 0)
                return new("RECONCILIATION_REQUIRED", "MODE_SOURCE_VERIFY_HANDLER_UNAVAILABLE");
            if (matches.Length > 1)
                return new("RECONCILIATION_REQUIRED", "MODE_SOURCE_VERIFY_HANDLER_AMBIGUOUS");
            if (seen.Add(matches[0])) selected.Add(matches[0]);
        }

        RuntimeModeRollbackStepOutcome? only = null;
        foreach (var handler in selected)
        {
            var result = await handler.VerifySourceAsync(command, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(result.Disposition, "VERIFIED", StringComparison.Ordinal)) return result;
            only = result;
        }

        return selected.Count == 1 && only is not null
            ? only
            : new("VERIFIED", "MODE_SOURCE_ALL_DOMAINS_VERIFIED");
    }

    private static void Validate(ModeSourceVerificationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.TransitionId == Guid.Empty || command.LeaseId == Guid.Empty || command.OperationId == Guid.Empty ||
            command.CorrelationId == Guid.Empty)
            throw new ArgumentException("Mode source verification identifiers must not be empty.", nameof(command));
        if (command.FenceToken < 1) throw new ArgumentOutOfRangeException(nameof(command));
        if (command.TransitionRevision < 1) throw new ArgumentOutOfRangeException(nameof(command));
        if (string.IsNullOrWhiteSpace(command.ControlSessionKey) || command.ControlSessionKey.Length > 256 ||
            command.ControlSessionKey.Any(static character => char.IsControl(character)))
            throw new ArgumentException("ControlSessionKey is outside supported bounds.", nameof(command));
    }
}

public sealed class ManagedServiceModeSourceVerificationHandler(IManagedServiceSourceVerificationClient broker)
    : IModeSourceVerificationHandler
{
    public bool CanHandle(PersistedModeActionRecord action) =>
        string.Equals(action.OwningModule, ManagedServicePolicyActionContract.OwningModule, StringComparison.Ordinal) &&
        string.Equals(action.ActionType, ManagedServicePolicyActionContract.ActionType, StringComparison.Ordinal);

    public async Task<RuntimeModeRollbackStepOutcome> VerifySourceAsync(
        ModeSourceVerificationCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await broker.VerifySourceAsync(
            command.OperationId,
            command.CorrelationId,
            new MachineServiceSourceVerifyRequest(
                command.TransitionId,
                command.LeaseId,
                command.FenceToken,
                command.ControlSessionKey,
                command.TransitionRevision),
            cancellationToken).ConfigureAwait(false);
        return new(result.Disposition, result.ProductCode);
    }
}
