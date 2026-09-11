using SplitOS.Persistence.Machine;

namespace SplitOS.RuntimeHost.ModeRuntime;

public sealed record ModeSourceAuthorityDecision(bool MayRestoreSource, string ProductCode);

public interface IModeSourceAuthority
{
    Task<ModeSourceAuthorityDecision> EvaluateAsync(ModeTransitionRecord transition, CancellationToken cancellationToken = default);
}

public interface ICurrentModeAccess
{
    ValueTask<RuntimeAccessEvaluation> EvaluateAsync(CancellationToken cancellationToken = default);
}

public sealed class CurrentModeAccess(AccountAssociationCoordinator association, IRuntimeAccessEvaluator evaluator) : ICurrentModeAccess
{
    public async ValueTask<RuntimeAccessEvaluation> EvaluateAsync(CancellationToken cancellationToken = default)
        => await evaluator.EvaluateAsync(await association.EvaluateAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
}

/// <summary>Checks current source ownership and fresh access; never reuses a forward-command assertion.</summary>
public sealed class RuntimeModeSourceAuthority(
    IMachineStateStore machine, IControlSessionIdentity session, ICurrentModeAccess access) : IModeSourceAuthority
{
    public async Task<ModeSourceAuthorityDecision> EvaluateAsync(ModeTransitionRecord transition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transition);
        if (transition.RecoveryContextId is not null)
            return new(false, "MODE_SOURCE_AUTHORITY_BASE_CONVERGENCE_REQUIRED");
        if (transition.CommitDurable || transition.SourceMode is not ("NONE" or "WORK" or "GAME"))
            return new(false, "MODE_SOURCE_AUTHORITY_INVALID_TRANSITION");
        if (session.GetCurrentKey() != transition.ControlSessionKey)
            return new(false, "MODE_SOURCE_AUTHORITY_SESSION_CHANGED");
        var canonical = await machine.GetOperationalModeAsync(cancellationToken).ConfigureAwait(false);
        if (canonical.CommittedMode != transition.SourceMode || canonical.Revision != transition.SourceModeRevision)
            return new(false, "MODE_SOURCE_AUTHORITY_CANONICAL_CHANGED");
        if (transition.SourceMode != "NONE")
        {
            var target = transition.SourceMode == "WORK" ? PersistedModePolicyTarget.Work : PersistedModePolicyTarget.Game;
            if (canonical.ControlSessionKey != transition.ControlSessionKey || canonical.ActivationEpochId is null ||
                canonical.PolicyIdentity is null || canonical.PolicyTarget != target || string.IsNullOrWhiteSpace(canonical.ResolvedPolicyDigest))
                return new(false, "MODE_SOURCE_AUTHORITY_POLICY_MISSING");
            var currentAccess = await access.EvaluateAsync(cancellationToken).ConfigureAwait(false);
            if (!currentAccess.IsEnabled) return new(false, "MODE_SOURCE_AUTHORITY_BASE_CONVERGENCE_REQUIRED");
        }
        // Access evaluation can cross asynchronous account/entitlement refresh boundaries.
        if (session.GetCurrentKey() != transition.ControlSessionKey ||
            canonical != await machine.GetOperationalModeAsync(cancellationToken).ConfigureAwait(false))
            return new(false, "MODE_SOURCE_AUTHORITY_CONTEXT_CHANGED");
        return new(true, "MODE_SOURCE_AUTHORITY_CONFIRMED");
    }
}
