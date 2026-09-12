using SplitOS.Contracts.Protocol;
using SplitOS.Persistence.Machine;

namespace SplitOS.RuntimeHost.ModeRuntime;

public interface IManagedServiceSourceVerificationClient
{
    Task<MachineServiceSourceVerifyResult> VerifySourceAsync(Guid operationId, Guid correlationId,
        MachineServiceSourceVerifyRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Terminalizes only a verified, authorized source without changing canonical mode.</summary>
public sealed class RuntimeModeRollbackCompletionCoordinator
{
    private readonly IModeTransitionStore _transitions;
    private readonly IModeTransitionReconciliationStore _reconciliation;
    private readonly IModeSourceAuthority _authority;
    private readonly IModeSourceVerificationCoordinator _sourceVerification;

    public RuntimeModeRollbackCompletionCoordinator(
        IModeTransitionStore transitions,
        IModeTransitionReconciliationStore reconciliation,
        IModeSourceAuthority authority,
        IModeSourceVerificationCoordinator sourceVerification)
    {
        _transitions = transitions;
        _reconciliation = reconciliation;
        _authority = authority;
        _sourceVerification = sourceVerification;
    }

    // Compatibility boundary for existing direct unit-test composition. Production DI supplies the
    // plan-aware generic dispatcher above.
    public RuntimeModeRollbackCompletionCoordinator(
        IModeTransitionStore transitions,
        IModeTransitionReconciliationStore reconciliation,
        IModeSourceAuthority authority,
        IManagedServiceSourceVerificationClient broker)
        : this(transitions, reconciliation, authority, new LegacyManagedServiceSourceVerificationCoordinator(broker))
    {
    }

    public async Task<RuntimeModeRollbackStepOutcome> CompleteAsync(Guid transitionId, CancellationToken cancellationToken = default)
    {
        var transition = await _transitions.GetAsync(transitionId, cancellationToken).ConfigureAwait(false);
        if (transition is null || transition.CommitDurable || transition.TransitionState != PersistedModeTransitionState.RollingBack)
            return new("REJECTED", "MODE_ROLLBACK_COMPLETION_INVALID_LIFECYCLE");
        var authorization = await _authority.EvaluateAsync(transition, cancellationToken).ConfigureAwait(false);
        if (!authorization.MayRestoreSource) return new("RECONCILIATION_REQUIRED", authorization.ProductCode);
        var verified = await _sourceVerification.VerifySourceAsync(
            new ModeSourceVerificationCommand(
                transitionId,
                transition.LeaseId,
                transition.FenceToken,
                transition.OperationId,
                transition.CorrelationId,
                transition.ControlSessionKey,
                transition.Revision),
            cancellationToken).ConfigureAwait(false);
        if (verified.Disposition != "VERIFIED") return verified;
        authorization = await _authority.EvaluateAsync(transition, cancellationToken).ConfigureAwait(false);
        if (!authorization.MayRestoreSource) return new("RECONCILIATION_REQUIRED", authorization.ProductCode);
        var stage = await _transitions.AdvanceAsync(transitionId, transition.Revision, transition.LeaseId, transition.FenceToken,
            transition.OperationId, PersistedModeTransitionState.RollingBack, PersistedModeTransitionStage.RollbackVerify,
            transition.MandatoryVerified, null, cancellationToken).ConfigureAwait(false);
        if (stage.Disposition is not (ModeTransitionAdvanceDisposition.Advanced or ModeTransitionAdvanceDisposition.Unchanged) || stage.Transition is null)
            return new("REJECTED", stage.ProductCode);
        // Re-evaluate after the durable stage write: an account refresh can revoke source access during IPC.
        authorization = await _authority.EvaluateAsync(stage.Transition, cancellationToken).ConfigureAwait(false);
        if (!authorization.MayRestoreSource) return new("RECONCILIATION_REQUIRED", authorization.ProductCode);
        var terminal = await _transitions.AdvanceAsync(transitionId, stage.Transition.Revision, transition.LeaseId, transition.FenceToken,
            transition.OperationId, PersistedModeTransitionState.FailedWithSafeFallback, PersistedModeTransitionStage.Terminal,
            transition.MandatoryVerified, "FAILED_WITH_SAFE_FALLBACK", cancellationToken).ConfigureAwait(false);
        if (terminal.Disposition != ModeTransitionAdvanceDisposition.Advanced) return new("REJECTED", terminal.ProductCode);
        var cleanup = await _reconciliation.ReleaseTerminalLeaseAsync(cancellationToken).ConfigureAwait(false);
        return cleanup.Disposition is ModeTerminalLeaseCleanupDisposition.Released or ModeTerminalLeaseCleanupDisposition.AlreadyReleased
            ? new("FAILED_WITH_SAFE_FALLBACK", terminal.ProductCode) : new("RECONCILIATION_REQUIRED", cleanup.ProductCode);
    }

    private sealed class LegacyManagedServiceSourceVerificationCoordinator(IManagedServiceSourceVerificationClient broker)
        : IModeSourceVerificationCoordinator
    {
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
}
