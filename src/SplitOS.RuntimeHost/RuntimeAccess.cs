using SplitOS.RuntimeHost.ProductIdentity;

namespace SplitOS.RuntimeHost;

public sealed record RuntimeAccessEvaluation(
    string ManagedRuntimeAccess,
    string Reason,
    long? EntitlementVersion)
{
    public bool IsEnabled => string.Equals(ManagedRuntimeAccess, "ENABLED", StringComparison.Ordinal);
}

public sealed record RuntimeAccessPolicy(
    TimeSpan MaximumOnlineEvidenceAge,
    TimeSpan AllowedClockSkew)
{
    public static RuntimeAccessPolicy Default { get; } = new(
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(5));

    public void Validate()
    {
        if (MaximumOnlineEvidenceAge <= TimeSpan.Zero || MaximumOnlineEvidenceAge > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumOnlineEvidenceAge),
                "Online entitlement evidence age must be greater than zero and no more than one hour.");
        }

        if (AllowedClockSkew < TimeSpan.Zero || AllowedClockSkew > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(
                nameof(AllowedClockSkew),
                "Clock skew must be between zero and five minutes.");
        }
    }
}

public interface IRuntimeAccessEvaluator
{
    ValueTask<RuntimeAccessEvaluation> EvaluateAsync(
        AccountAssociationEvaluation association,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Capability-first managed-runtime authorization from fresh authenticated online entitlement evidence.
/// This evaluator deliberately does not consume local plan/cache metadata or protected offline assertions yet.
/// </summary>
public sealed class OnlineEntitlementRuntimeAccessEvaluator : IRuntimeAccessEvaluator
{
    private const string ManagedRuntimeCapability = "runtime.managed_modes";

    private readonly OnlineEntitlementEvidenceState _evidenceState;
    private readonly RuntimeAccessPolicy _policy;
    private readonly TimeProvider _timeProvider;

    public OnlineEntitlementRuntimeAccessEvaluator(
        OnlineEntitlementEvidenceState evidenceState,
        RuntimeAccessPolicy? policy = null,
        TimeProvider? timeProvider = null)
    {
        _evidenceState = evidenceState ?? throw new ArgumentNullException(nameof(evidenceState));
        _policy = policy ?? RuntimeAccessPolicy.Default;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _policy.Validate();
    }

    public ValueTask<RuntimeAccessEvaluation> EvaluateAsync(
        AccountAssociationEvaluation association,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(association);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(association.AssociationState, "ACTIVE", StringComparison.Ordinal))
        {
            return Disabled("ACCOUNT_NOT_ACTIVE");
        }

        if (string.IsNullOrWhiteSpace(association.AccountId) ||
            string.IsNullOrWhiteSpace(association.AssociationId))
        {
            return Disabled("ACCOUNT_ASSOCIATION_INCOMPLETE");
        }

        var evidence = _evidenceState.Read();
        if (evidence is null)
        {
            return Disabled("ONLINE_ENTITLEMENT_MISSING");
        }

        if (!string.Equals(evidence.AssociationId, association.AssociationId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(evidence.Entitlement.AccountId, association.AccountId, StringComparison.Ordinal))
        {
            return Disabled("ONLINE_ENTITLEMENT_CONTEXT_MISMATCH", evidence.Entitlement.EntitlementVersion);
        }

        var now = _timeProvider.GetUtcNow();
        if (evidence.ObservedLocalUtc > now.Add(_policy.AllowedClockSkew))
        {
            return Disabled("ONLINE_ENTITLEMENT_CLOCK_INVALID", evidence.Entitlement.EntitlementVersion);
        }

        if (now - evidence.ObservedLocalUtc > _policy.MaximumOnlineEvidenceAge)
        {
            return Disabled("ONLINE_ENTITLEMENT_STALE", evidence.Entitlement.EntitlementVersion);
        }

        var entitlement = evidence.Entitlement;
        if (entitlement.ServerUtc > now.Add(_policy.AllowedClockSkew))
        {
            return Disabled("ONLINE_ENTITLEMENT_SERVER_CLOCK_INVALID", entitlement.EntitlementVersion);
        }

        if (entitlement.ValidFromUtc is DateTimeOffset validFrom &&
            validFrom > now.Add(_policy.AllowedClockSkew))
        {
            return Disabled("ENTITLEMENT_NOT_YET_VALID", entitlement.EntitlementVersion);
        }

        if (entitlement.ValidUntilUtc is DateTimeOffset validUntil &&
            now > validUntil.Add(_policy.AllowedClockSkew))
        {
            return Disabled("ENTITLEMENT_EXPIRED", entitlement.EntitlementVersion);
        }

        if (string.Equals(entitlement.Plan, "FREE", StringComparison.Ordinal))
        {
            return Disabled("FREE_ENTITLEMENT", entitlement.EntitlementVersion);
        }

        var statusAllowsCurrentAccess = string.Equals(entitlement.Status, "ACTIVE", StringComparison.Ordinal) ||
                                        string.Equals(entitlement.Status, "CANCELLED_AT_PERIOD_END", StringComparison.Ordinal);
        if (!statusAllowsCurrentAccess)
        {
            return Disabled("ENTITLEMENT_NOT_ACTIVE", entitlement.EntitlementVersion);
        }

        if (!entitlement.HasCapability(ManagedRuntimeCapability))
        {
            return Disabled("MANAGED_RUNTIME_CAPABILITY_MISSING", entitlement.EntitlementVersion);
        }

        return ValueTask.FromResult(new RuntimeAccessEvaluation(
            "ENABLED",
            "PRO_ONLINE_CONFIRMED",
            entitlement.EntitlementVersion));
    }

    private static ValueTask<RuntimeAccessEvaluation> Disabled(string reason, long? entitlementVersion = null)
        => ValueTask.FromResult(new RuntimeAccessEvaluation("DISABLED", reason, entitlementVersion));
}
