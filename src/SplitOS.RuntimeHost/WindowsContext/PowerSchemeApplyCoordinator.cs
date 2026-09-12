namespace SplitOS.RuntimeHost.WindowsContext;

public enum PowerSchemeApplyDisposition
{
    NoChangeVerified,
    AlreadySatisfied,
    AppliedVerified,
    TargetNotFound,
    SourceDrift,
    OperationRejected,
    VerificationFailed
}

public sealed record PowerSchemeApplyOutcome(
    PowerSchemeApplyDisposition Disposition,
    string ProductCode,
    string PowerPolicyId,
    Guid? SourceSchemeId,
    Guid? TargetSchemeId,
    Guid? ObservedSchemeId,
    bool OperationAttempted)
{
    public bool IsVerified => Disposition is
        PowerSchemeApplyDisposition.NoChangeVerified or
        PowerSchemeApplyDisposition.AlreadySatisfied or
        PowerSchemeApplyDisposition.AppliedVerified;
}

/// <summary>
/// Applies one release-owned semantic power target for the current user. A native set call is never
/// sufficient proof: every attempted mutation is followed by a fresh active-scheme read-back.
/// When durable rollback evidence already captured a source scheme, expectedSourceSchemeId fences
/// the mutation against drift between pre-state capture and the native write.
/// </summary>
public sealed class PowerSchemeApplyCoordinator(
    IPowerPolicyCatalogResolver policyResolver,
    IPowerSchemeQuery query,
    IPowerSchemeSetter setter)
{
    public PowerSchemeApplyOutcome Apply(
        string powerPolicyId,
        Guid? expectedSourceSchemeId = null)
    {
        PowerPolicyCatalogResolver.ValidatePolicyId(powerPolicyId);
        if (expectedSourceSchemeId == Guid.Empty)
            throw new ArgumentException("Expected source power scheme must not be empty.", nameof(expectedSourceSchemeId));

        if (!policyResolver.TryResolve(powerPolicyId, out var target) || target is null)
        {
            return new PowerSchemeApplyOutcome(
                PowerSchemeApplyDisposition.TargetNotFound,
                "POWER_POLICY_TARGET_NOT_FOUND",
                powerPolicyId,
                null,
                null,
                null,
                false);
        }

        var source = query.QueryActiveScheme();
        Guid? targetScheme = target.ResolutionKind == PowerPolicyResolutionKind.Scheme
            ? target.SchemeId ?? throw new InvalidDataException("Resolved scheme power policy omitted target scheme GUID.")
            : null;

        if (expectedSourceSchemeId.HasValue && source != expectedSourceSchemeId.Value)
        {
            return new PowerSchemeApplyOutcome(
                PowerSchemeApplyDisposition.SourceDrift,
                "POWER_SCHEME_SOURCE_DRIFT",
                target.PowerPolicyId,
                source,
                targetScheme,
                source,
                false);
        }

        if (target.ResolutionKind == PowerPolicyResolutionKind.NoChange)
        {
            return new PowerSchemeApplyOutcome(
                PowerSchemeApplyDisposition.NoChangeVerified,
                "POWER_POLICY_NO_CHANGE_VERIFIED",
                target.PowerPolicyId,
                source,
                null,
                source,
                false);
        }

        if (source == targetScheme)
        {
            return new PowerSchemeApplyOutcome(
                PowerSchemeApplyDisposition.AlreadySatisfied,
                "POWER_SCHEME_ALREADY_SATISFIED",
                target.PowerPolicyId,
                source,
                targetScheme,
                source,
                false);
        }

        var set = setter.SetActiveScheme(targetScheme!.Value);
        var observed = query.QueryActiveScheme();
        if (set.ErrorCode != 0)
        {
            return new PowerSchemeApplyOutcome(
                PowerSchemeApplyDisposition.OperationRejected,
                "POWER_SCHEME_SET_REJECTED",
                target.PowerPolicyId,
                source,
                targetScheme,
                observed,
                true);
        }

        if (observed != targetScheme)
        {
            return new PowerSchemeApplyOutcome(
                PowerSchemeApplyDisposition.VerificationFailed,
                "POWER_SCHEME_READBACK_MISMATCH",
                target.PowerPolicyId,
                source,
                targetScheme,
                observed,
                true);
        }

        return new PowerSchemeApplyOutcome(
            PowerSchemeApplyDisposition.AppliedVerified,
            "POWER_SCHEME_APPLIED_VERIFIED",
            target.PowerPolicyId,
            source,
            targetScheme,
            observed,
            true);
    }
}
