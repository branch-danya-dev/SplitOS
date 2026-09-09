using SplitOS.Contracts.Protocol;
using SplitOS.Persistence.Machine;

namespace SplitOS.Broker.Service;

/// <summary>
/// Read-only semantic verification executor for a durable managed-service action. It validates the
/// current MODE lease/fence and VERIFYING action identity before observing release-owned SCM targets.
/// No Windows mutation is performed here.
/// </summary>
public sealed class BrokerManagedServiceVerificationExecutor(
    ModeVerificationEvidenceStore evidenceStore,
    IManagedServiceCatalog catalog,
    IManagedServiceAdapter adapter)
{
    public async ValueTask<MachineServicePolicyVerifyResult> ExecuteAsync(
        Guid operationId,
        Guid correlationId,
        MachineServicePolicyVerifyRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(operationId, correlationId, request);
        var entries = ManagedServicePolicyActionContract.NormalizeEntries(request.Entries);
        var desiredDigest = ManagedServicePolicyActionContract.ComputeDesiredStateDigest(entries);

        var resolved = new List<(ManagedServicePolicyEntry Requested, ManagedServiceCatalogEntry Target)>(entries.Count);
        foreach (var requested in entries)
        {
            if (!catalog.TryResolve(requested.ManagedServiceId, out var target))
            {
                return new MachineServicePolicyVerifyResult(
                    "UNKNOWN",
                    "SERVICE_POLICY_VERIFY_TARGET_INVALID",
                    [new ManagedServicePolicyVerificationEntryResult(
                        requested.ManagedServiceId,
                        requested.DesiredState,
                        "UNKNOWN",
                        "UNKNOWN",
                        "TARGET_NOT_FOUND")]);
            }

            var desired = requested.DesiredState switch
            {
                "RUNNING" => ManagedServiceDesiredState.Running,
                "STOPPED" => ManagedServiceDesiredState.Stopped,
                _ => throw new ArgumentOutOfRangeException(nameof(requested.DesiredState))
            };
            if (!target.AllowedStates.Contains(desired))
            {
                return new MachineServicePolicyVerifyResult(
                    "UNKNOWN",
                    "SERVICE_POLICY_VERIFY_TARGET_INVALID",
                    [new ManagedServicePolicyVerificationEntryResult(
                        requested.ManagedServiceId,
                        requested.DesiredState,
                        "UNKNOWN",
                        "UNKNOWN",
                        "TARGET_STATE_NOT_ALLOWED")]);
            }

            resolved.Add((requested, target));
        }

        var binding = new ModeMutationActionBinding(
            ManagedServicePolicyActionContract.OwningModule,
            ManagedServicePolicyActionContract.ActionType,
            ManagedServicePolicyActionContract.TargetRef,
            ManagedServicePolicyActionContract.DesiredSchemaVersion,
            desiredDigest);
        var context = new ModeMutationFenceContext(
            request.TransitionId,
            request.ActionId,
            request.LeaseId,
            request.FenceToken,
            operationId,
            correlationId,
            request.ControlSessionKey,
            request.ExpectedActionRevision,
            binding);

        var authorization = await evidenceStore.ValidateAsync(context, cancellationToken).ConfigureAwait(false);
        if (!authorization.IsAuthorized)
        {
            return new MachineServicePolicyVerifyResult(
                "REJECTED",
                authorization.ProductCode,
                []);
        }

        var results = new List<ManagedServicePolicyVerificationEntryResult>(resolved.Count);
        var hasMismatch = false;
        var hasUnknown = false;
        foreach (var item in resolved)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var observation = await adapter.QueryAsync(item.Target, cancellationToken).ConfigureAwait(false);
            var actual = observation.State switch
            {
                ManagedServiceObservedState.Running => "RUNNING",
                ManagedServiceObservedState.Stopped => "STOPPED",
                _ => "UNKNOWN"
            };

            if (string.Equals(actual, "UNKNOWN", StringComparison.Ordinal))
            {
                hasUnknown = true;
                results.Add(new ManagedServicePolicyVerificationEntryResult(
                    item.Requested.ManagedServiceId,
                    item.Requested.DesiredState,
                    actual,
                    "UNKNOWN",
                    "STATE_NOT_STABLE"));
                continue;
            }

            if (!string.Equals(actual, item.Requested.DesiredState, StringComparison.Ordinal))
            {
                hasMismatch = true;
                results.Add(new ManagedServicePolicyVerificationEntryResult(
                    item.Requested.ManagedServiceId,
                    item.Requested.DesiredState,
                    actual,
                    "MISMATCH",
                    "STATE_MISMATCH"));
                continue;
            }

            results.Add(new ManagedServicePolicyVerificationEntryResult(
                item.Requested.ManagedServiceId,
                item.Requested.DesiredState,
                actual,
                "VERIFIED",
                null));
        }

        if (hasUnknown)
        {
            return new MachineServicePolicyVerifyResult(
                "UNKNOWN",
                "SERVICE_POLICY_VERIFY_UNKNOWN",
                results);
        }

        if (hasMismatch)
        {
            return new MachineServicePolicyVerifyResult(
                "MISMATCH",
                "SERVICE_POLICY_VERIFY_MISMATCH",
                results);
        }

        return new MachineServicePolicyVerifyResult(
            "VERIFIED",
            "SERVICE_POLICY_VERIFIED",
            results);
    }

    private static void ValidateRequest(
        Guid operationId,
        Guid correlationId,
        MachineServicePolicyVerifyRequest request)
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
}
