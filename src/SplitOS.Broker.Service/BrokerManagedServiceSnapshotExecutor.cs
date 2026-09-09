using SplitOS.Contracts.Protocol;
using SplitOS.Persistence.Machine;

namespace SplitOS.Broker.Service;

/// <summary>
/// Read-only pre-mutation evidence executor. It binds the requested service policy to the
/// immutable PLANNED action and current MODE lease before observing release-owned SCM targets.
/// No Windows mutation is performed here.
/// </summary>
public sealed class BrokerManagedServiceSnapshotExecutor(
    ModePreMutationEvidenceStore evidenceStore,
    IManagedServiceCatalog catalog,
    IManagedServiceAdapter adapter)
{
    public async ValueTask<MachineServicePolicySnapshotResult> ExecuteAsync(
        Guid operationId,
        Guid correlationId,
        MachineServicePolicySnapshotRequest request,
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
                return Failure(
                    "SERVICE_PRESTATE_TARGET_INVALID",
                    [new ManagedServicePreStateEntry(requested.ManagedServiceId, "UNKNOWN")]);
            }

            var desired = requested.DesiredState switch
            {
                "RUNNING" => ManagedServiceDesiredState.Running,
                "STOPPED" => ManagedServiceDesiredState.Stopped,
                _ => throw new ArgumentOutOfRangeException(nameof(requested.DesiredState))
            };
            if (!target.AllowedStates.Contains(desired))
            {
                return Failure(
                    "SERVICE_PRESTATE_TARGET_INVALID",
                    [new ManagedServicePreStateEntry(requested.ManagedServiceId, "UNKNOWN")]);
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
            return new MachineServicePolicySnapshotResult(
                "REJECTED",
                authorization.ProductCode,
                [],
                null,
                null);
        }

        var observed = new List<ManagedServicePreStateEntry>(resolved.Count);
        foreach (var item in resolved)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = await adapter.QueryAsync(item.Target, cancellationToken).ConfigureAwait(false);
            var stable = state.State switch
            {
                ManagedServiceObservedState.Running => "RUNNING",
                ManagedServiceObservedState.Stopped => "STOPPED",
                _ => null
            };
            if (stable is null)
            {
                observed.Add(new ManagedServicePreStateEntry(item.Requested.ManagedServiceId, "UNKNOWN"));
                return Failure("SERVICE_PRESTATE_NOT_STABLE", observed);
            }
            observed.Add(new ManagedServicePreStateEntry(item.Requested.ManagedServiceId, stable));
        }

        var normalized = ManagedServicePolicyActionContract.NormalizePreState(observed);
        var json = ManagedServicePolicyActionContract.SerializePreState(normalized);
        var digest = ManagedServicePolicyActionContract.ComputePreStateDigest(normalized);
        return new MachineServicePolicySnapshotResult(
            "CAPTURED",
            "SERVICE_PRESTATE_CAPTURED",
            normalized,
            json,
            digest);
    }

    private static MachineServicePolicySnapshotResult Failure(
        string productCode,
        IReadOnlyList<ManagedServicePreStateEntry> entries)
        => new("FAILED", productCode, entries, null, null);

    private static void ValidateRequest(
        Guid operationId,
        Guid correlationId,
        MachineServicePolicySnapshotRequest request)
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
