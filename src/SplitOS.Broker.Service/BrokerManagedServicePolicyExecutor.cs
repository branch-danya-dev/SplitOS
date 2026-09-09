using System.Text.Json;
using SplitOS.Contracts.Protocol;
using SplitOS.Persistence.Machine;

namespace SplitOS.Broker.Service;

/// <summary>
/// Privileged semantic executor for Machine.ServicePolicy.Apply@1.
/// Every target is resolved through the release-owned catalog and every adapter invocation is
/// immediately preceded by canonical lease/fence/action-semantic validation.
/// The durable pre-state captured before BeginApply is then re-read and compared with SCM state
/// immediately before the privileged mutation so stale rollback evidence cannot be applied forward.
/// </summary>
public sealed class BrokerManagedServicePolicyExecutor(
    BrokerModeMutationFenceBoundary mutationBoundary,
    ModeTransitionActionJournalStore actionJournalStore,
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
                token => GuardAndApplyAsync(request, entries, entry, token),
                cancellationToken).ConfigureAwait(false);

            if (!execution.Executed)
            {
                results.Add(NotAttempted(
                    entry.Requested,
                    "FENCE_REJECTED",
                    execution.ProductCode));
                AddRemainingNotAttempted(
                    results,
                    resolved,
                    index + 1,
                    "FENCE_REJECTED",
                    execution.ProductCode);

                return new MachineServicePolicyApplyResult(
                    HasPartialProgress(results) ? "PARTIAL" : "REJECTED",
                    execution.ProductCode,
                    OrderResults(results));
            }

            var guarded = execution.Result
                ?? throw new InvalidDataException("Managed-service guarded execution returned no result.");
            if (guarded.Disposition != GuardedServiceExecutionDisposition.Applied)
            {
                results.Add(NotAttempted(
                    entry.Requested,
                    guarded.ImmediateResult,
                    guarded.ErrorCode,
                    guarded.ActualStateObserved));
                AddRemainingNotAttempted(
                    results,
                    resolved,
                    index + 1,
                    "BATCH_PRESTATE_GUARD_ABORTED",
                    guarded.ErrorCode);

                return new MachineServicePolicyApplyResult(
                    HasPartialProgress(results) ? "PARTIAL" : "REJECTED",
                    guarded.ProductCode,
                    OrderResults(results));
            }

            var technical = guarded.TechnicalResult
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

    private async ValueTask<GuardedServiceExecution> GuardAndApplyAsync(
        MachineServicePolicyApplyRequest request,
        IReadOnlyList<ManagedServicePolicyEntry> normalizedEntries,
        ResolvedEntry entry,
        CancellationToken cancellationToken)
    {
        var action = await actionJournalStore.GetAsync(request.ActionId, cancellationToken).ConfigureAwait(false);
        if (action is null ||
            action.TransitionId != request.TransitionId ||
            action.Revision != request.ExpectedActionRevision ||
            action.State != PersistedModeActionState.Applying ||
            string.IsNullOrWhiteSpace(action.PreStateJson) ||
            string.IsNullOrWhiteSpace(action.PreStateDigest))
        {
            return EvidenceInvalid(
                "Durable APPLYING action no longer exposes the expected immutable pre-state evidence.");
        }

        IReadOnlyList<ManagedServicePreStateEntry> preState;
        try
        {
            preState = ManagedServicePolicyActionContract.DeserializePreState(action.PreStateJson);
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException)
        {
            return EvidenceInvalid($"Durable managed-service pre-state is invalid: {ex.Message}");
        }

        var computedDigest = ManagedServicePolicyActionContract.ComputePreStateDigest(preState);
        if (!string.Equals(computedDigest, action.PreStateDigest, StringComparison.OrdinalIgnoreCase))
        {
            return EvidenceInvalid("Durable managed-service pre-state digest does not match its canonical payload.");
        }

        if (!PreStateTargetSetMatches(normalizedEntries, preState))
        {
            return EvidenceInvalid(
                "Durable managed-service pre-state target set does not match the immutable requested policy.");
        }

        var expected = preState.Single(item =>
            string.Equals(item.ManagedServiceId, entry.Requested.ManagedServiceId, StringComparison.Ordinal));
        var observation = await adapter.QueryAsync(entry.Target, cancellationToken).ConfigureAwait(false);
        var actualState = ToProtocolState(observation.State);
        if (!string.Equals(actualState, expected.ActualState, StringComparison.Ordinal))
        {
            return new GuardedServiceExecution(
                GuardedServiceExecutionDisposition.DriftDetected,
                "SERVICE_PRESTATE_DRIFT_DETECTED",
                "PRESTATE_DRIFT_DETECTED",
                actualState,
                "PRESTATE_DRIFT_DETECTED",
                null,
                $"Managed service {entry.Requested.ManagedServiceId} changed from durable pre-state {expected.ActualState} to {actualState} before mutation.");
        }

        var technical = await adapter.ApplyAsync(
            entry.Target,
            entry.DesiredState,
            cancellationToken).ConfigureAwait(false);
        return new GuardedServiceExecution(
            GuardedServiceExecutionDisposition.Applied,
            "SERVICE_PRESTATE_MATCHED",
            "PRESTATE_MATCHED",
            actualState,
            null,
            technical,
            null);
    }

    private static GuardedServiceExecution EvidenceInvalid(string detail)
        => new(
            GuardedServiceExecutionDisposition.EvidenceInvalid,
            "SERVICE_PRESTATE_EVIDENCE_INVALID",
            "PRESTATE_EVIDENCE_INVALID",
            "UNKNOWN",
            "PRESTATE_EVIDENCE_INVALID",
            null,
            detail);

    private static bool PreStateTargetSetMatches(
        IReadOnlyList<ManagedServicePolicyEntry> requested,
        IReadOnlyList<ManagedServicePreStateEntry> preState)
    {
        if (requested.Count != preState.Count) return false;
        for (var index = 0; index < requested.Count; index++)
        {
            if (!string.Equals(
                    requested[index].ManagedServiceId,
                    preState[index].ManagedServiceId,
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static void AddRemainingNotAttempted(
        ICollection<ManagedServicePolicyEntryResult> results,
        IReadOnlyList<ResolvedEntry> resolved,
        int startIndex,
        string immediateResult,
        string? errorCode)
    {
        for (var remaining = startIndex; remaining < resolved.Count; remaining++)
        {
            results.Add(NotAttempted(
                resolved[remaining].Requested,
                immediateResult,
                errorCode));
        }
    }

    private static bool HasPartialProgress(IEnumerable<ManagedServicePolicyEntryResult> results)
        => results.Any(static result =>
            result.OperationAttempted ||
            string.Equals(result.VerificationStatus, "VERIFIED", StringComparison.Ordinal));

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
        string? errorCode,
        string actualStateObserved = "UNKNOWN")
        => new(
            requested.ManagedServiceId,
            requested.DesiredState,
            false,
            immediateResult,
            actualStateObserved,
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

    private enum GuardedServiceExecutionDisposition
    {
        Applied,
        DriftDetected,
        EvidenceInvalid
    }

    private sealed record GuardedServiceExecution(
        GuardedServiceExecutionDisposition Disposition,
        string ProductCode,
        string ImmediateResult,
        string ActualStateObserved,
        string? ErrorCode,
        ManagedServiceTechnicalResult? TechnicalResult,
        string? Detail);
}
