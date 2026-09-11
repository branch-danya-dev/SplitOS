using System.Text.Json;
using SplitOS.Contracts.Protocol;
using SplitOS.Persistence.Machine;

namespace SplitOS.RuntimeHost.ModeRuntime;

public enum ManagedServiceActionVerifyDisposition
{
    Verified,
    ActionRejected,
    JournalRejected,
    BrokerRejected,
    BrokerUnavailable
}

public sealed record ManagedServiceActionVerifyCommand(
    Guid TransitionId,
    Guid ActionId,
    int ExpectedActionRevision,
    Guid LeaseId,
    long FenceToken,
    Guid OperationId,
    Guid CorrelationId,
    string ControlSessionKey);

public sealed record ManagedServiceActionVerifyOutcome(
    ManagedServiceActionVerifyDisposition Disposition,
    string ProductCode,
    int? ActionRevision,
    MachineServicePolicyVerifyResult? VerificationResult = null,
    string? Detail = null)
{
    public bool IsVerified => Disposition == ManagedServiceActionVerifyDisposition.Verified;
}

/// <summary>
/// Runtime orchestration primitive for one immutable managed-service action during VERIFYING.
/// The plan-level engine must already have advanced the transition to VERIFYING/VERIFY_STARTED.
/// This coordinator never advances the transition and never commits WORK/GAME canonical truth.
/// </summary>
public sealed class ManagedServiceActionVerifyCoordinator(
    IModeTransitionActionJournalStore actionJournalStore,
    IManagedServiceActionVerificationBrokerClient brokerClient)
{
    public async Task<ManagedServiceActionVerifyOutcome> VerifyAsync(
        ManagedServiceActionVerifyCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateCommand(command);
        await actionJournalStore.InitializeAsync(cancellationToken).ConfigureAwait(false);

        var action = await actionJournalStore.GetAsync(command.ActionId, cancellationToken).ConfigureAwait(false);
        var durableIntent = ValidateDurableIntent(command, action);
        if (durableIntent.Outcome is not null) return durableIntent.Outcome;
        var entries = durableIntent.Entries!;

        var beginVerify = await actionJournalStore.BeginVerifyAsync(
            command.TransitionId,
            command.ActionId,
            command.ExpectedActionRevision,
            command.LeaseId,
            command.FenceToken,
            command.OperationId,
            cancellationToken).ConfigureAwait(false);
        if (!IsJournalSuccess(beginVerify))
        {
            return JournalRejected(beginVerify);
        }

        var verifying = beginVerify.Action
            ?? throw new InvalidDataException("Successful BeginVerify did not return the VERIFYING action.");
        var request = new MachineServicePolicyVerifyRequest(
            command.TransitionId,
            command.ActionId,
            command.LeaseId,
            command.FenceToken,
            command.ControlSessionKey,
            verifying.Revision,
            entries);

        MachineServicePolicyVerifyResult brokerResult;
        try
        {
            brokerResult = await brokerClient.VerifyAsync(
                command.OperationId,
                command.CorrelationId,
                request,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsBrokerTransportException(ex))
        {
            var unknown = await actionJournalStore.RecordVerifyResultAsync(
                command.TransitionId,
                command.ActionId,
                verifying.Revision,
                command.LeaseId,
                command.FenceToken,
                command.OperationId,
                PersistedModeVerifyResult.Unknown,
                cancellationToken).ConfigureAwait(false);
            if (!IsJournalSuccess(unknown))
            {
                return JournalRejected(
                    unknown,
                    $"Broker verify response was unavailable ({ex.Message}) and UNKNOWN could not be persisted");
            }

            return new ManagedServiceActionVerifyOutcome(
                ManagedServiceActionVerifyDisposition.BrokerUnavailable,
                "MODE_SERVICE_VERIFY_RESPONSE_UNKNOWN",
                unknown.Action?.Revision,
                Detail: ex.Message);
        }

        var classification = ClassifyBrokerResult(entries, brokerResult);
        var recorded = await actionJournalStore.RecordVerifyResultAsync(
            command.TransitionId,
            command.ActionId,
            verifying.Revision,
            command.LeaseId,
            command.FenceToken,
            command.OperationId,
            classification.PersistedResult,
            cancellationToken).ConfigureAwait(false);
        if (!IsJournalSuccess(recorded))
        {
            return JournalRejected(
                recorded,
                "Broker returned verification evidence but the durable action journal rejected it");
        }

        if (classification.IsConsistent && classification.PersistedResult == PersistedModeVerifyResult.Verified)
        {
            return new ManagedServiceActionVerifyOutcome(
                ManagedServiceActionVerifyDisposition.Verified,
                brokerResult.ProductCode,
                recorded.Action?.Revision,
                brokerResult);
        }

        return new ManagedServiceActionVerifyOutcome(
            ManagedServiceActionVerifyDisposition.BrokerRejected,
            classification.IsConsistent
                ? brokerResult.ProductCode
                : "MODE_SERVICE_VERIFY_EVIDENCE_INVALID",
            recorded.Action?.Revision,
            brokerResult,
            classification.Detail ?? "Managed-service target did not verify.");
    }

    private static (IReadOnlyList<ManagedServicePolicyEntry>? Entries, ManagedServiceActionVerifyOutcome? Outcome)
        ValidateDurableIntent(
            ManagedServiceActionVerifyCommand command,
            PersistedModeActionRecord? action)
    {
        if (action is null)
            return (null, ActionRejected("MODE_SERVICE_ACTION_MISSING", null, "Durable action does not exist."));
        if (action.TransitionId != command.TransitionId)
            return (null, ActionRejected("MODE_SERVICE_ACTION_TRANSITION_MISMATCH", action.Revision, "Action belongs to a different transition."));
        if (action.Revision != command.ExpectedActionRevision)
            return (null, ActionRejected("MODE_SERVICE_ACTION_REVISION_MISMATCH", action.Revision, "Caller action revision is stale."));
        if (action.State != PersistedModeActionState.Applied ||
            !string.Equals(action.ApplyResultCode, "APPLIED", StringComparison.Ordinal))
        {
            return (null, ActionRejected(
                "MODE_SERVICE_ACTION_NOT_APPLIED",
                action.Revision,
                $"Action must have durable APPLIED evidence before verification; actual state is {action.State}."));
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

    private static ManagedServiceActionVerifyOutcome ActionRejected(
        string productCode,
        int? revision,
        string detail)
        => new(
            ManagedServiceActionVerifyDisposition.ActionRejected,
            productCode,
            revision,
            Detail: detail);

    private static VerificationClassification ClassifyBrokerResult(
        IReadOnlyList<ManagedServicePolicyEntry> desired,
        MachineServicePolicyVerifyResult result)
    {
        if (result.Entries.Count != desired.Count)
        {
            return VerificationClassification.Invalid(
                "Broker verification target count does not match durable desired intent.");
        }

        var ordered = result.Entries
            .OrderBy(static item => item.ManagedServiceId, StringComparer.Ordinal)
            .ToArray();
        var hasMismatch = false;
        var hasUnknown = false;
        for (var index = 0; index < desired.Count; index++)
        {
            var expected = desired[index];
            var actual = ordered[index];
            if (!string.Equals(actual.ManagedServiceId, expected.ManagedServiceId, StringComparison.Ordinal) ||
                !string.Equals(actual.DesiredState, expected.DesiredState, StringComparison.Ordinal))
            {
                return VerificationClassification.Invalid(
                    "Broker verification entries do not match durable desired targets.");
            }

            switch (actual.VerificationStatus)
            {
                case "VERIFIED" when
                    string.Equals(actual.ActualStateObserved, expected.DesiredState, StringComparison.Ordinal) &&
                    actual.ErrorCode is null:
                    break;
                case "MISMATCH" when
                    actual.ActualStateObserved is "RUNNING" or "STOPPED" &&
                    !string.Equals(actual.ActualStateObserved, expected.DesiredState, StringComparison.Ordinal):
                    hasMismatch = true;
                    break;
                case "UNKNOWN" when string.Equals(actual.ActualStateObserved, "UNKNOWN", StringComparison.Ordinal):
                    hasUnknown = true;
                    break;
                default:
                    return VerificationClassification.Invalid(
                        $"Broker verification evidence for {expected.ManagedServiceId} is internally inconsistent.");
            }
        }

        var expectedDisposition = hasUnknown
            ? "UNKNOWN"
            : hasMismatch
                ? "MISMATCH"
                : "VERIFIED";
        if (!string.Equals(result.Disposition, expectedDisposition, StringComparison.Ordinal))
        {
            return VerificationClassification.Invalid(
                $"Broker aggregate disposition {result.Disposition} conflicts with entry-level evidence {expectedDisposition}.");
        }

        var persisted = expectedDisposition switch
        {
            "VERIFIED" => PersistedModeVerifyResult.Verified,
            "MISMATCH" => PersistedModeVerifyResult.Mismatch,
            _ => PersistedModeVerifyResult.Unknown
        };
        return new VerificationClassification(true, persisted, null);
    }

    private static bool IsJournalSuccess(ModeActionAdvanceOutcome outcome)
        => outcome.Disposition is ModeActionAdvanceDisposition.Advanced or ModeActionAdvanceDisposition.Replayed;

    private static ManagedServiceActionVerifyOutcome JournalRejected(
        ModeActionAdvanceOutcome outcome,
        string? detailPrefix = null)
        => new(
            ManagedServiceActionVerifyDisposition.JournalRejected,
            outcome.ProductCode,
            outcome.Action?.Revision ?? outcome.ActualActionRevision,
            Detail: detailPrefix is null
                ? outcome.Detail
                : $"{detailPrefix}: {outcome.Detail ?? outcome.ProductCode}");

    private static bool IsBrokerTransportException(Exception exception)
        => exception is IOException or InvalidDataException or UnauthorizedAccessException or TimeoutException;

    private static void ValidateCommand(ManagedServiceActionVerifyCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.TransitionId == Guid.Empty ||
            command.ActionId == Guid.Empty ||
            command.LeaseId == Guid.Empty ||
            command.OperationId == Guid.Empty ||
            command.CorrelationId == Guid.Empty)
        {
            throw new ArgumentException("Managed-service verify identifiers must not be empty.", nameof(command));
        }

        if (command.ExpectedActionRevision < 1)
            throw new ArgumentOutOfRangeException(nameof(command), "ExpectedActionRevision must be positive.");
        if (command.FenceToken < 1)
            throw new ArgumentOutOfRangeException(nameof(command), "FenceToken must be positive.");
        if (string.IsNullOrWhiteSpace(command.ControlSessionKey) ||
            command.ControlSessionKey.Length > 256 ||
            command.ControlSessionKey.Any(static character => char.IsControl(character)))
        {
            throw new ArgumentException("ControlSessionKey is outside supported bounds.", nameof(command));
        }
    }

    private sealed record VerificationClassification(
        bool IsConsistent,
        PersistedModeVerifyResult PersistedResult,
        string? Detail)
    {
        public static VerificationClassification Invalid(string detail)
            => new(false, PersistedModeVerifyResult.Unknown, detail);
    }
}
