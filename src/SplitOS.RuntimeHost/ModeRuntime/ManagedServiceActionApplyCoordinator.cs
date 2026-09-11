using System.Text.Json;
using SplitOS.Contracts.Protocol;
using SplitOS.Persistence.Machine;

namespace SplitOS.RuntimeHost.ModeRuntime;

public enum ManagedServiceActionApplyDisposition
{
    Applied,
    ActionRejected,
    SnapshotRejected,
    JournalRejected,
    BrokerRejected,
    BrokerUnavailable
}

public sealed record ManagedServiceActionApplyCommand(
    Guid TransitionId,
    Guid ActionId,
    int ExpectedActionRevision,
    Guid LeaseId,
    long FenceToken,
    Guid OperationId,
    Guid CorrelationId,
    string ControlSessionKey);

public sealed record ManagedServiceActionApplyOutcome(
    ManagedServiceActionApplyDisposition Disposition,
    string ProductCode,
    int? ActionRevision,
    MachineServicePolicySnapshotResult? Snapshot = null,
    MachineServicePolicyApplyResult? ApplyResult = null,
    string? Detail = null)
{
    public bool IsApplied => Disposition == ManagedServiceActionApplyDisposition.Applied;
}

/// <summary>
/// Runtime orchestration primitive for one immutable managed-service action during the APPLY phase.
/// It deliberately does not advance the transition into VERIFYING or COMMITTING because those are
/// plan-level decisions: a transition may contain multiple ordered actions.
/// </summary>
public sealed class ManagedServiceActionApplyCoordinator(
    IModeTransitionActionJournalStore actionJournalStore,
    IManagedServiceActionBrokerClient brokerClient)
{
    public async Task<ManagedServiceActionApplyOutcome> ApplyAsync(
        ManagedServiceActionApplyCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateCommand(command);
        await actionJournalStore.InitializeAsync(cancellationToken).ConfigureAwait(false);

        var action = await actionJournalStore.GetAsync(command.ActionId, cancellationToken).ConfigureAwait(false);
        var durableIntent = ValidateDurableIntent(command, action);
        if (durableIntent.Outcome is not null) return durableIntent.Outcome;
        var entries = durableIntent.Entries!;

        var snapshotRequest = new MachineServicePolicySnapshotRequest(
            command.TransitionId,
            command.ActionId,
            command.LeaseId,
            command.FenceToken,
            command.ControlSessionKey,
            command.ExpectedActionRevision,
            entries);

        MachineServicePolicySnapshotResult snapshot;
        try
        {
            snapshot = await brokerClient.SnapshotAsync(
                command.OperationId,
                command.CorrelationId,
                snapshotRequest,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsBrokerTransportException(ex))
        {
            return new ManagedServiceActionApplyOutcome(
                ManagedServiceActionApplyDisposition.BrokerUnavailable,
                "MODE_SERVICE_SNAPSHOT_UNAVAILABLE",
                action!.Revision,
                Detail: ex.Message);
        }

        if (!string.Equals(snapshot.Disposition, "CAPTURED", StringComparison.Ordinal))
        {
            return new ManagedServiceActionApplyOutcome(
                ManagedServiceActionApplyDisposition.SnapshotRejected,
                snapshot.ProductCode,
                action!.Revision,
                snapshot,
                Detail: "Broker did not return stable pre-mutation evidence; action remains PLANNED.");
        }

        var snapshotValidation = ValidateSnapshot(entries, snapshot);
        if (snapshotValidation is not null)
        {
            return new ManagedServiceActionApplyOutcome(
                ManagedServiceActionApplyDisposition.SnapshotRejected,
                "MODE_SERVICE_SNAPSHOT_EVIDENCE_INVALID",
                action!.Revision,
                snapshot,
                Detail: snapshotValidation);
        }

        var beginApply = await actionJournalStore.BeginApplyAsync(
            command.TransitionId,
            command.ActionId,
            command.ExpectedActionRevision,
            command.LeaseId,
            command.FenceToken,
            command.OperationId,
            snapshot.PreStateJson,
            snapshot.PreStateDigest,
            cancellationToken).ConfigureAwait(false);
        if (!IsJournalSuccess(beginApply))
        {
            return JournalRejected(beginApply, snapshot);
        }

        var applying = beginApply.Action
            ?? throw new InvalidDataException("Successful BeginApply did not return the APPLYING action.");
        var applyRequest = new MachineServicePolicyApplyRequest(
            command.TransitionId,
            command.ActionId,
            command.LeaseId,
            command.FenceToken,
            command.ControlSessionKey,
            applying.Revision,
            entries);

        MachineServicePolicyApplyResult brokerResult;
        try
        {
            brokerResult = await brokerClient.ApplyAsync(
                command.OperationId,
                command.CorrelationId,
                applyRequest,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsBrokerTransportException(ex))
        {
            var unknown = await actionJournalStore.RecordApplyResultAsync(
                command.TransitionId,
                command.ActionId,
                applying.Revision,
                command.LeaseId,
                command.FenceToken,
                command.OperationId,
                PersistedModeApplyResult.Unknown,
                cancellationToken).ConfigureAwait(false);
            if (!IsJournalSuccess(unknown))
            {
                return JournalRejected(
                    unknown,
                    snapshot,
                    detailPrefix: $"Broker apply response was unavailable ({ex.Message}) and UNKNOWN could not be persisted");
            }

            return new ManagedServiceActionApplyOutcome(
                ManagedServiceActionApplyDisposition.BrokerUnavailable,
                "MODE_SERVICE_APPLY_RESPONSE_UNKNOWN",
                unknown.Action?.Revision,
                snapshot,
                Detail: ex.Message);
        }

        var brokerVerified = IsVerifiedBrokerSuccess(entries, brokerResult);
        var persistedResult = brokerVerified
            ? PersistedModeApplyResult.Applied
            : string.Equals(brokerResult.Disposition, "SUCCEEDED", StringComparison.Ordinal)
                ? PersistedModeApplyResult.Unknown
                : PersistedModeApplyResult.Failed;

        var recorded = await actionJournalStore.RecordApplyResultAsync(
            command.TransitionId,
            command.ActionId,
            applying.Revision,
            command.LeaseId,
            command.FenceToken,
            command.OperationId,
            persistedResult,
            cancellationToken).ConfigureAwait(false);
        if (!IsJournalSuccess(recorded))
        {
            return JournalRejected(
                recorded,
                snapshot,
                brokerResult,
                "Broker returned an apply result but the durable action journal rejected it");
        }

        if (brokerVerified)
        {
            return new ManagedServiceActionApplyOutcome(
                ManagedServiceActionApplyDisposition.Applied,
                brokerResult.ProductCode,
                recorded.Action?.Revision,
                snapshot,
                brokerResult);
        }

        return new ManagedServiceActionApplyOutcome(
            ManagedServiceActionApplyDisposition.BrokerRejected,
            brokerResult.ProductCode,
            recorded.Action?.Revision,
            snapshot,
            brokerResult,
            string.Equals(brokerResult.Disposition, "SUCCEEDED", StringComparison.Ordinal)
                ? "Broker claimed SUCCEEDED but its entry-level read-back evidence was inconsistent; action was durably marked UNKNOWN."
                : "Broker did not complete the action successfully; action was durably marked FAILED.");
    }

    private static (IReadOnlyList<ManagedServicePolicyEntry>? Entries, ManagedServiceActionApplyOutcome? Outcome)
        ValidateDurableIntent(
            ManagedServiceActionApplyCommand command,
            PersistedModeActionRecord? action)
    {
        if (action is null)
        {
            return (null, ActionRejected("MODE_SERVICE_ACTION_MISSING", null, "Durable action does not exist."));
        }

        if (action.TransitionId != command.TransitionId)
        {
            return (null, ActionRejected("MODE_SERVICE_ACTION_TRANSITION_MISMATCH", action.Revision, "Action belongs to a different transition."));
        }

        if (action.Revision != command.ExpectedActionRevision)
        {
            return (null, ActionRejected("MODE_SERVICE_ACTION_REVISION_MISMATCH", action.Revision, "Caller action revision is stale."));
        }

        if (action.State != PersistedModeActionState.Planned)
        {
            return (null, ActionRejected("MODE_SERVICE_ACTION_NOT_PLANNED", action.Revision, $"Action state is {action.State}."));
        }

        if (!string.Equals(action.OwningModule, ManagedServicePolicyActionContract.OwningModule, StringComparison.Ordinal) ||
            !string.Equals(action.ActionType, ManagedServicePolicyActionContract.ActionType, StringComparison.Ordinal) ||
            !string.Equals(action.TargetRef, ManagedServicePolicyActionContract.TargetRef, StringComparison.Ordinal) ||
            action.DesiredSchemaVersion != ManagedServicePolicyActionContract.DesiredSchemaVersion ||
            string.IsNullOrWhiteSpace(action.DesiredStateJson))
        {
            return (null, ActionRejected(
                "MODE_SERVICE_ACTION_SEMANTICS_INVALID",
                action.Revision,
                "Durable action is not a supported managed-service policy action."));
        }

        IReadOnlyList<ManagedServicePolicyEntry> entries;
        try
        {
            entries = ManagedServicePolicyActionContract.DeserializeDesiredState(action.DesiredStateJson);
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException)
        {
            return (null, ActionRejected(
                "MODE_SERVICE_ACTION_DESIRED_STATE_INVALID",
                action.Revision,
                ex.Message));
        }

        var digest = ManagedServicePolicyActionContract.ComputeDesiredStateDigest(entries);
        if (!string.Equals(digest, action.DesiredStateDigest, StringComparison.OrdinalIgnoreCase))
        {
            return (null, ActionRejected(
                "MODE_SERVICE_ACTION_DESIRED_DIGEST_MISMATCH",
                action.Revision,
                "Durable desired-state digest does not match its canonical payload."));
        }

        return (entries, null);
    }

    private static ManagedServiceActionApplyOutcome ActionRejected(
        string productCode,
        int? revision,
        string detail)
        => new(
            ManagedServiceActionApplyDisposition.ActionRejected,
            productCode,
            revision,
            Detail: detail);

    private static string? ValidateSnapshot(
        IReadOnlyList<ManagedServicePolicyEntry> desired,
        MachineServicePolicySnapshotResult snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.PreStateJson) || string.IsNullOrWhiteSpace(snapshot.PreStateDigest))
        {
            return "Captured snapshot omitted pre-state JSON or digest.";
        }

        IReadOnlyList<ManagedServicePreStateEntry> preState;
        try
        {
            preState = ManagedServicePolicyActionContract.DeserializePreState(snapshot.PreStateJson);
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException)
        {
            return $"Captured snapshot pre-state is invalid: {ex.Message}";
        }

        var digest = ManagedServicePolicyActionContract.ComputePreStateDigest(preState);
        if (!string.Equals(digest, snapshot.PreStateDigest, StringComparison.OrdinalIgnoreCase))
        {
            return "Captured snapshot digest does not match its canonical pre-state payload.";
        }

        if (preState.Count != desired.Count || snapshot.Entries.Count != preState.Count)
        {
            return "Captured snapshot target count does not match the durable desired policy.";
        }

        for (var index = 0; index < desired.Count; index++)
        {
            if (!string.Equals(desired[index].ManagedServiceId, preState[index].ManagedServiceId, StringComparison.Ordinal) ||
                snapshot.Entries[index] != preState[index])
            {
                return "Captured snapshot targets/evidence do not match the durable desired policy.";
            }
        }

        return null;
    }

    private static bool IsVerifiedBrokerSuccess(
        IReadOnlyList<ManagedServicePolicyEntry> desired,
        MachineServicePolicyApplyResult result)
    {
        if (!string.Equals(result.Disposition, "SUCCEEDED", StringComparison.Ordinal) ||
            result.Entries.Count != desired.Count)
        {
            return false;
        }

        var ordered = result.Entries
            .OrderBy(static item => item.ManagedServiceId, StringComparer.Ordinal)
            .ToArray();
        for (var index = 0; index < desired.Count; index++)
        {
            if (!string.Equals(ordered[index].ManagedServiceId, desired[index].ManagedServiceId, StringComparison.Ordinal) ||
                !string.Equals(ordered[index].DesiredState, desired[index].DesiredState, StringComparison.Ordinal) ||
                !string.Equals(ordered[index].ActualStateObserved, desired[index].DesiredState, StringComparison.Ordinal) ||
                !string.Equals(ordered[index].VerificationStatus, "VERIFIED", StringComparison.Ordinal) ||
                ordered[index].ErrorCode is not null)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsJournalSuccess(ModeActionAdvanceOutcome outcome)
        => outcome.Disposition is ModeActionAdvanceDisposition.Advanced or ModeActionAdvanceDisposition.Replayed;

    private static ManagedServiceActionApplyOutcome JournalRejected(
        ModeActionAdvanceOutcome outcome,
        MachineServicePolicySnapshotResult? snapshot,
        MachineServicePolicyApplyResult? applyResult = null,
        string? detailPrefix = null)
    {
        var detail = detailPrefix is null
            ? outcome.Detail
            : $"{detailPrefix}: {outcome.ProductCode}: {outcome.Detail}";
        return new ManagedServiceActionApplyOutcome(
            ManagedServiceActionApplyDisposition.JournalRejected,
            outcome.ProductCode,
            outcome.Action?.Revision ?? outcome.ActualActionRevision,
            snapshot,
            applyResult,
            detail);
    }

    private static bool IsBrokerTransportException(Exception exception)
        => exception is IOException or InvalidDataException or UnauthorizedAccessException or TimeoutException;

    private static void ValidateCommand(ManagedServiceActionApplyCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.TransitionId == Guid.Empty) throw new ArgumentException("TransitionId must not be empty.", nameof(command));
        if (command.ActionId == Guid.Empty) throw new ArgumentException("ActionId must not be empty.", nameof(command));
        if (command.ExpectedActionRevision < 1) throw new ArgumentOutOfRangeException(nameof(command), "ExpectedActionRevision must be greater than zero.");
        if (command.LeaseId == Guid.Empty) throw new ArgumentException("LeaseId must not be empty.", nameof(command));
        if (command.FenceToken < 1) throw new ArgumentOutOfRangeException(nameof(command), "FenceToken must be greater than zero.");
        if (command.OperationId == Guid.Empty) throw new ArgumentException("OperationId must not be empty.", nameof(command));
        if (command.CorrelationId == Guid.Empty) throw new ArgumentException("CorrelationId must not be empty.", nameof(command));
        if (string.IsNullOrWhiteSpace(command.ControlSessionKey) ||
            command.ControlSessionKey.Length > 256 ||
            command.ControlSessionKey.Any(static character => char.IsControl(character)))
        {
            throw new ArgumentException("ControlSessionKey is outside supported bounds.", nameof(command));
        }
    }
}
