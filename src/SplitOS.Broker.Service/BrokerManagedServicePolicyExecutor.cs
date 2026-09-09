using SplitOS.Contracts.Protocol;
using SplitOS.Persistence.Machine;

namespace SplitOS.Broker.Service;

/// <summary>
/// Privileged semantic executor for Machine.ServicePolicy.Apply@1.
/// Every target is resolved through the release-owned catalog and every adapter invocation is
/// immediately preceded by canonical lease/fence/action-semantic validation.
/// </summary>
public sealed class BrokerManagedServicePolicyExecutor(
    BrokerModeMutationFenceBoundary mutationBoundary,
    IManagedServiceCatalog catalog,
    IManagedServiceAdapter adapter)
{
    public async ValueTask<MachineServicePolicyApplyResult> ExecuteAsync(
        Guid operationId,
        Guid correlationId,
        MachineServicePolicyApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(operationId, correlationId, request);
        var entries = ManagedServicePolicyActionContract.NormalizeEntries(request.Entries);
        var digest = ManagedServicePolicyActionContract.ComputeDesiredStateDigest(entries);

        var resolved = new List<ResolvedEntry>(entries.Count);
        var invalidResults = new List<ManagedServicePolicyEntryResult>();
        foreach (var requested in entries)
        {
            if (!catalog.TryResolve(requested.ManagedServiceId, out var target))
            {
                invalidResults.Add(NotAttempted(
                    requested,
                    "TARGET_NOT_FOUND",
                    "TARGET_NOT_FOUND"));
                continue;
            }

            var desired = ParseDesiredState(requested.DesiredState);
            if (!target.AllowedStates.Contains(desired))
            {
                invalidResults.Add(NotAttempted(
                    requested,
                    "DESIRED_STATE_NOT_ALLOWLISTED",
                    "OPERATION_REJECTED"));
                continue;
            }

            resolved.Add(new ResolvedEntry(requested, target, desired));
        }

        if (invalidResults.Count != 0)
        {
            foreach (var valid in resolved)
            {
                invalidResults.Add(NotAttempted(
                    valid.Requested,
                    "BATCH_VALIDATION_FAILED",
                    null));
            }

            return new MachineServicePolicyApplyResult(
                "FAILED",
                "SERVICE_POLICY_TARGET_INVALID",
                OrderResults(invalidResults));
        }

        var actionBinding = new ModeMutationActionBinding(
            ManagedServicePolicyActionContract.OwningModule,
            ManagedServicePolicyActionContract.ActionType,
            ManagedServicePolicyActionContract.TargetRef,
            ManagedServicePolicyActionContract.DesiredSchemaVersion,
            digest);
        var fenceContext = new ModeMutationFenceContext(
            request.TransitionId,
            request.ActionId,
            request.LeaseId,
            request.FenceToken,
            operationId,
            correlationId,
            request.ControlSessionKey,
            request.ExpectedActionRevision,
            actionBinding);

        var results = new List<ManagedServicePolicyEntryResult>(resolved.Count);
        for (var index = 0; index < resolved.Count; index++)
        {
            var entry = resolved[index];
            var execution = await mutationBoundary.ExecuteAsync(
                fenceContext,
                token => adapter.ApplyAsync(entry.Target, entry.DesiredState, token),
                cancellationToken).ConfigureAwait(false);

            if (!execution.Executed)
            {
                results.Add(NotAttempted(
                    entry.Requested,
                    "FENCE_REJECTED",
                    execution.ProductCode));
                for (var remaining = index + 1; remaining < resolved.Count; remaining++)
                {
                    results.Add(NotAttempted(
                        resolved[remaining].Requested,
                        "FENCE_REJECTED",
                        execution.ProductCode));
                }

                return new MachineServicePolicyApplyResult(
                    results.Any(static result => result.OperationAttempted) ? "PARTIAL" : "REJECTED",
                    execution.ProductCode,
                    OrderResults(results));
            }

            var technical = execution.Result
                ?? throw new InvalidDataException("Managed-service adapter execution returned no technical result.");
            results.Add(ToProtocolResult(entry.Requested, technical));
        }

        var verifiedCount = results.Count(static result =>
            string.Equals(result.VerificationStatus, "VERIFIED", StringComparison.Ordinal));
        if (verifiedCount == results.Count)
        {
            return new MachineServicePolicyApplyResult(
                "SUCCEEDED",
                "SERVICE_POLICY_APPLIED_VERIFIED",
                OrderResults(results));
        }

        return new MachineServicePolicyApplyResult(
            verifiedCount == 0 ? "FAILED" : "PARTIAL",
            verifiedCount == 0 ? "SERVICE_POLICY_FAILED" : "SERVICE_POLICY_PARTIAL",
            OrderResults(results));
    }

    private static ManagedServicePolicyEntryResult ToProtocolResult(
        ManagedServicePolicyEntry requested,
        ManagedServiceTechnicalResult technical)
        => new(
            requested.ManagedServiceId,
            requested.DesiredState,
            technical.OperationAttempted,
            technical.ImmediateResult,
            ToProtocolState(technical.ActualState),
            technical.VerificationStatus,
            technical.ErrorCode);

    private static ManagedServicePolicyEntryResult NotAttempted(
        ManagedServicePolicyEntry requested,
        string immediateResult,
        string? errorCode)
        => new(
            requested.ManagedServiceId,
            requested.DesiredState,
            false,
            immediateResult,
            "UNKNOWN",
            "NOT_VERIFIED",
            errorCode);

    private static IReadOnlyList<ManagedServicePolicyEntryResult> OrderResults(
        IEnumerable<ManagedServicePolicyEntryResult> results)
        => results
            .OrderBy(static result => result.ManagedServiceId, StringComparer.Ordinal)
            .ToArray();

    private static ManagedServiceDesiredState ParseDesiredState(string value)
        => value switch
        {
            "RUNNING" => ManagedServiceDesiredState.Running,
            "STOPPED" => ManagedServiceDesiredState.Stopped,
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

    private static string ToProtocolState(ManagedServiceObservedState state)
        => state switch
        {
            ManagedServiceObservedState.Stopped => "STOPPED",
            ManagedServiceObservedState.StartPending => "START_PENDING",
            ManagedServiceObservedState.StopPending => "STOP_PENDING",
            ManagedServiceObservedState.Running => "RUNNING",
            ManagedServiceObservedState.Paused => "PAUSED",
            ManagedServiceObservedState.OtherTransitional => "OTHER_TRANSITIONAL",
            ManagedServiceObservedState.NotFound => "NOT_FOUND",
            ManagedServiceObservedState.AccessDenied => "ACCESS_DENIED",
            _ => "UNKNOWN"
        };

    private static void ValidateRequest(
        Guid operationId,
        Guid correlationId,
        MachineServicePolicyApplyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (operationId == Guid.Empty) throw new ArgumentException("OperationId must not be empty.", nameof(operationId));
        if (correlationId == Guid.Empty) throw new ArgumentException("CorrelationId must not be empty.", nameof(correlationId));
        if (request.TransitionId == Guid.Empty) throw new ArgumentException("TransitionId must not be empty.", nameof(request));
        if (request.ActionId == Guid.Empty) throw new ArgumentException("ActionId must not be empty.", nameof(request));
        if (request.LeaseId == Guid.Empty) throw new ArgumentException("LeaseId must not be empty.", nameof(request));
        if (request.FenceToken < 1) throw new ArgumentOutOfRangeException(nameof(request), "FenceToken must be greater than zero.");
        if (request.ExpectedActionRevision < 1) throw new ArgumentOutOfRangeException(nameof(request), "ExpectedActionRevision must be greater than zero.");
        if (string.IsNullOrWhiteSpace(request.ControlSessionKey) ||
            request.ControlSessionKey.Length > 256 ||
            request.ControlSessionKey.Any(static character => char.IsControl(character)))
        {
            throw new ArgumentException("ControlSessionKey is outside supported bounds.", nameof(request));
        }
    }

    private sealed record ResolvedEntry(
        ManagedServicePolicyEntry Requested,
        ManagedServiceCatalogEntry Target,
        ManagedServiceDesiredState DesiredState);
}
