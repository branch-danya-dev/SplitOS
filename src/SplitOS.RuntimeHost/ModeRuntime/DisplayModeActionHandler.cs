using SplitOS.Persistence.Machine;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.ModeRuntime;

public interface IDisplayModeActionJournal
{
    Task<ModeActionAdvanceOutcome> BeginApplyAsync(
        Guid transitionId, Guid actionId, int expectedActionRevision, Guid leaseId, long fenceToken,
        Guid ownerOperationId, string? preStateJson, string? preStateDigest,
        CancellationToken cancellationToken = default);

    Task<ModeActionAdvanceOutcome> RecordApplyResultAsync(
        Guid transitionId, Guid actionId, int expectedActionRevision, Guid leaseId, long fenceToken,
        Guid ownerOperationId, PersistedModeApplyResult result,
        CancellationToken cancellationToken = default);

    Task<ModeActionAdvanceOutcome> BeginVerifyAsync(
        Guid transitionId, Guid actionId, int expectedActionRevision, Guid leaseId, long fenceToken,
        Guid ownerOperationId, CancellationToken cancellationToken = default);

    Task<ModeActionAdvanceOutcome> RecordVerifyResultAsync(
        Guid transitionId, Guid actionId, int expectedActionRevision, Guid leaseId, long fenceToken,
        Guid ownerOperationId, PersistedModeVerifyResult result,
        CancellationToken cancellationToken = default);
}

public sealed class DisplayModeActionJournal(IModeTransitionActionJournalStore inner) : IDisplayModeActionJournal
{
    public Task<ModeActionAdvanceOutcome> BeginApplyAsync(
        Guid transitionId, Guid actionId, int expectedActionRevision, Guid leaseId, long fenceToken,
        Guid ownerOperationId, string? preStateJson, string? preStateDigest,
        CancellationToken cancellationToken = default)
        => inner.BeginApplyAsync(transitionId, actionId, expectedActionRevision, leaseId, fenceToken,
            ownerOperationId, preStateJson, preStateDigest, cancellationToken);

    public Task<ModeActionAdvanceOutcome> RecordApplyResultAsync(
        Guid transitionId, Guid actionId, int expectedActionRevision, Guid leaseId, long fenceToken,
        Guid ownerOperationId, PersistedModeApplyResult result,
        CancellationToken cancellationToken = default)
        => inner.RecordApplyResultAsync(transitionId, actionId, expectedActionRevision, leaseId, fenceToken,
            ownerOperationId, result, cancellationToken);

    public Task<ModeActionAdvanceOutcome> BeginVerifyAsync(
        Guid transitionId, Guid actionId, int expectedActionRevision, Guid leaseId, long fenceToken,
        Guid ownerOperationId, CancellationToken cancellationToken = default)
        => inner.BeginVerifyAsync(transitionId, actionId, expectedActionRevision, leaseId, fenceToken,
            ownerOperationId, cancellationToken);

    public Task<ModeActionAdvanceOutcome> RecordVerifyResultAsync(
        Guid transitionId, Guid actionId, int expectedActionRevision, Guid leaseId, long fenceToken,
        Guid ownerOperationId, PersistedModeVerifyResult result,
        CancellationToken cancellationToken = default)
        => inner.RecordVerifyResultAsync(transitionId, actionId, expectedActionRevision, leaseId, fenceToken,
            ownerOperationId, result, cancellationToken);
}

/// <summary>
/// First crash-safe display action owner for SPEC-05. It journals immutable desired state and bounded
/// pre-state before mutation, uses persistent selector re-resolution on every phase, and delegates the
/// actual CCD validate/apply/read-back boundary to DisplayTargetApplyCoordinator.
/// </summary>
public sealed class DisplayModeActionHandler(
    IDisplayModeActionJournal journal,
    IDisplaySnapshotReader snapshots,
    PersistentDisplaySelectorResolver selectorResolver,
    DisplayTargetApplyCoordinator display)
    : IModeActionApplyHandler, IModeActionVerifyHandler, IModeActionRollbackHandler
{
    public bool CanHandle(PersistedModeActionRecord action) =>
        string.Equals(action.OwningModule, DisplayModeActionContract.OwningModule, StringComparison.Ordinal) &&
        string.Equals(action.ActionType, DisplayModeActionContract.ActionType, StringComparison.Ordinal);

    public async Task<ModeActionApplyStageOutcome> ApplyAsync(
        ModeActionExecutionCommand command,
        PersistedModeActionRecord action,
        CancellationToken cancellationToken = default)
    {
        DurableDisplayModeState desired;
        try
        {
            desired = ReadDesired(action);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
        {
            return new(false, "MODE_DISPLAY_ACTION_DESIRED_STATE_INVALID", action.Revision, ex.Message);
        }

        DisplaySnapshot snapshot;
        DisplaySelectorResolution resolved;
        DurableDisplayModeState preState;
        try
        {
            snapshot = snapshots.Read();
            resolved = selectorResolver.Resolve(desired.Selector, snapshot);
            if (!resolved.IsResolved || resolved.Path is null)
                return new(false, resolved.ProductCode, action.Revision, resolved.Detail);
            if (!resolved.Path.Active || !resolved.Path.TargetAvailable)
                return new(false, "DISPLAY_TARGET_UNAVAILABLE", action.Revision,
                    "Durable display target is not active and available in the fresh pre-apply snapshot.");
            preState = DisplayModeActionContract.CapturePreState(resolved.Path);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or InvalidOperationException)
        {
            return new(false, "MODE_DISPLAY_PRE_STATE_UNAVAILABLE", action.Revision, ex.Message);
        }

        var preStateJson = DisplayModeActionContract.Serialize(preState);
        var preStateDigest = DisplayModeActionContract.ComputeDigest(preStateJson);
        var begun = await journal.BeginApplyAsync(
            command.TransitionId,
            command.ActionId,
            command.ExpectedActionRevision,
            command.LeaseId,
            command.FenceToken,
            command.OperationId,
            preStateJson,
            preStateDigest,
            cancellationToken).ConfigureAwait(false);
        if (!IsJournalSuccess(begun) || begun.Action is null)
            return JournalApplyRejected(begun);

        var applying = begun.Action;
        DisplayTargetApplyOutcome? outcome = null;
        PersistedModeApplyResult persisted;
        string productCode;
        string? detail;
        try
        {
            outcome = display.Apply(new ResolvedDisplayTarget(
                resolved.Path.TargetKey,
                snapshot.Generation,
                desired.Resolution,
                desired.RefreshRate,
                desired.Rotation,
                DisplayTopologyIntent.PreserveActiveTopology));
            persisted = ClassifyApply(outcome);
            productCode = outcome.ProductCode;
            detail = outcome.Detail;
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            // Once APPLYING is durable, an exception cannot prove that SetDisplayConfig did not execute.
            persisted = PersistedModeApplyResult.Unknown;
            productCode = "MODE_DISPLAY_APPLY_OUTCOME_UNKNOWN";
            detail = ex.Message;
        }

        var recorded = await journal.RecordApplyResultAsync(
            command.TransitionId,
            command.ActionId,
            applying.Revision,
            command.LeaseId,
            command.FenceToken,
            command.OperationId,
            persisted,
            cancellationToken).ConfigureAwait(false);
        if (!IsJournalSuccess(recorded))
            return JournalApplyRejected(recorded, $"Display apply result {productCode} could not be durably recorded");

        return persisted == PersistedModeApplyResult.Applied && outcome?.IsVerified == true
            ? new(true, productCode, recorded.Action?.Revision, detail)
            : new(false, productCode, recorded.Action?.Revision, detail);
    }

    public async Task<ModeActionVerifyStageOutcome> VerifyAsync(
        ModeActionExecutionCommand command,
        PersistedModeActionRecord action,
        CancellationToken cancellationToken = default)
    {
        DurableDisplayModeState desired;
        try
        {
            desired = ReadDesired(action);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
        {
            return new(false, "MODE_DISPLAY_ACTION_DESIRED_STATE_INVALID", action.Revision, ex.Message);
        }

        if (action.State != PersistedModeActionState.Applied ||
            !string.Equals(action.ApplyResultCode, "APPLIED", StringComparison.Ordinal))
        {
            return new(false, "MODE_DISPLAY_ACTION_NOT_APPLIED", action.Revision,
                "Display verification requires durable APPLIED evidence.");
        }

        var begun = await journal.BeginVerifyAsync(
            command.TransitionId,
            command.ActionId,
            command.ExpectedActionRevision,
            command.LeaseId,
            command.FenceToken,
            command.OperationId,
            cancellationToken).ConfigureAwait(false);
        if (!IsJournalSuccess(begun) || begun.Action is null)
            return JournalVerifyRejected(begun);

        var verifying = begun.Action;
        PersistedModeVerifyResult persisted;
        string productCode;
        string? detail = null;
        try
        {
            var snapshot = snapshots.Read();
            var resolution = selectorResolver.Resolve(desired.Selector, snapshot);
            if (!resolution.IsResolved || resolution.Path is null)
            {
                persisted = resolution.Disposition == DisplaySelectorResolutionDisposition.Unavailable
                    ? PersistedModeVerifyResult.Mismatch
                    : PersistedModeVerifyResult.Unknown;
                productCode = resolution.ProductCode;
                detail = resolution.Detail;
            }
            else if (MatchesDesired(resolution.Path, desired))
            {
                persisted = PersistedModeVerifyResult.Verified;
                productCode = "DISPLAY_DURABLE_ACTION_VERIFIED";
            }
            else
            {
                persisted = PersistedModeVerifyResult.Mismatch;
                productCode = "DISPLAY_DURABLE_ACTION_MISMATCH";
                detail = "Fresh display evidence does not match the durable desired resolution/refresh/rotation target.";
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            persisted = PersistedModeVerifyResult.Unknown;
            productCode = "MODE_DISPLAY_VERIFY_OUTCOME_UNKNOWN";
            detail = ex.Message;
        }

        var recorded = await journal.RecordVerifyResultAsync(
            command.TransitionId,
            command.ActionId,
            verifying.Revision,
            command.LeaseId,
            command.FenceToken,
            command.OperationId,
            persisted,
            cancellationToken).ConfigureAwait(false);
        if (!IsJournalSuccess(recorded))
            return JournalVerifyRejected(recorded, $"Display verification result {productCode} could not be durably recorded");

        return persisted == PersistedModeVerifyResult.Verified
            ? new(true, productCode, recorded.Action?.Revision, detail)
            : new(false, productCode, recorded.Action?.Revision, detail);
    }

    public Task<RuntimeModeRollbackStepOutcome> RollbackAsync(
        ModeActionRollbackCommand command,
        PersistedModeActionRecord action,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(action.PreStateJson) || string.IsNullOrWhiteSpace(action.PreStateDigest))
            return Task.FromResult(new RuntimeModeRollbackStepOutcome(
                "RECONCILIATION_REQUIRED",
                "MODE_DISPLAY_ROLLBACK_PRE_STATE_MISSING"));

        try
        {
            DisplayModeActionContract.ValidateDigest(action.PreStateJson, action.PreStateDigest);
            var source = DisplayModeActionContract.Deserialize(action.PreStateJson);
            var snapshot = snapshots.Read();
            var resolution = selectorResolver.Resolve(source.Selector, snapshot);
            if (!resolution.IsResolved || resolution.Path is null)
                return Task.FromResult(new RuntimeModeRollbackStepOutcome("RECONCILIATION_REQUIRED", resolution.ProductCode));

            var outcome = display.Apply(new ResolvedDisplayTarget(
                resolution.Path.TargetKey,
                snapshot.Generation,
                source.Resolution,
                source.RefreshRate,
                source.Rotation,
                DisplayTopologyIntent.PreserveActiveTopology));
            return Task.FromResult(outcome.IsVerified
                ? new RuntimeModeRollbackStepOutcome("VERIFIED", "DISPLAY_DURABLE_ROLLBACK_VERIFIED")
                : new RuntimeModeRollbackStepOutcome("RECONCILIATION_REQUIRED", outcome.ProductCode));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(new RuntimeModeRollbackStepOutcome(
                "RECONCILIATION_REQUIRED",
                "MODE_DISPLAY_ROLLBACK_OUTCOME_UNKNOWN"));
        }
    }

    private static DurableDisplayModeState ReadDesired(PersistedModeActionRecord action)
    {
        if (!string.Equals(action.TargetRef, DisplayModeActionContract.TargetRef, StringComparison.Ordinal) ||
            action.DesiredSchemaVersion != DisplayModeActionContract.DesiredSchemaVersion ||
            !string.Equals(action.RollbackClass, DisplayModeActionContract.RollbackClass, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(action.VerificationClass, DisplayModeActionContract.VerificationClass, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(action.DesiredStateJson))
        {
            throw new InvalidDataException("Durable action does not match the supported display action contract.");
        }

        DisplayModeActionContract.ValidateDigest(action.DesiredStateJson, action.DesiredStateDigest);
        return DisplayModeActionContract.Deserialize(action.DesiredStateJson);
    }

    private static PersistedModeApplyResult ClassifyApply(DisplayTargetApplyOutcome outcome)
    {
        if (outcome.IsVerified) return PersistedModeApplyResult.Applied;

        return outcome.Disposition switch
        {
            DisplayTargetApplyDisposition.TargetNotFound or
            DisplayTargetApplyDisposition.TargetUnavailable or
            DisplayTargetApplyDisposition.UnsupportedCapability or
            DisplayTargetApplyDisposition.OperationRejected => PersistedModeApplyResult.Failed,
            DisplayTargetApplyDisposition.StaleSnapshot when outcome.After is not null => PersistedModeApplyResult.Unknown,
            DisplayTargetApplyDisposition.TechnicalFailure or
            DisplayTargetApplyDisposition.VerificationFailed => PersistedModeApplyResult.Unknown,
            _ => PersistedModeApplyResult.Failed
        };
    }

    private static bool MatchesDesired(DisplayPathEvidence path, DurableDisplayModeState desired)
        => path.Active && path.TargetAvailable &&
           path.SourceResolution == desired.Resolution &&
           path.Rotation == desired.Rotation &&
           RationalEquals(path.RefreshRate, desired.RefreshRate);

    private static bool RationalEquals(DisplayRational? actual, DisplayRational expected)
    {
        if (!actual.HasValue) return false;
        return (ulong)actual.Value.Numerator * expected.Denominator ==
               (ulong)expected.Numerator * actual.Value.Denominator;
    }

    private static bool IsJournalSuccess(ModeActionAdvanceOutcome outcome)
        => outcome.Disposition is ModeActionAdvanceDisposition.Advanced or ModeActionAdvanceDisposition.Replayed;

    private static ModeActionApplyStageOutcome JournalApplyRejected(ModeActionAdvanceOutcome outcome, string? prefix = null)
        => new(
            false,
            outcome.ProductCode,
            outcome.Action?.Revision ?? outcome.ActualActionRevision,
            prefix is null ? outcome.Detail : $"{prefix}: {outcome.Detail ?? outcome.ProductCode}");

    private static ModeActionVerifyStageOutcome JournalVerifyRejected(ModeActionAdvanceOutcome outcome, string? prefix = null)
        => new(
            false,
            outcome.ProductCode,
            outcome.Action?.Revision ?? outcome.ActualActionRevision,
            prefix is null ? outcome.Detail : $"{prefix}: {outcome.Detail ?? outcome.ProductCode}");
}
