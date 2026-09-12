using System.Text.Json;
using SplitOS.Persistence.Machine;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.ModeRuntime;

public sealed class DisplayModeActionApplyHandler(
    IModeTransitionActionJournalStore journal,
    IDisplaySnapshotReader snapshots,
    PersistentDisplaySelectorResolver selectorResolver,
    DisplayExtendApplyCoordinator extend,
    DisplayTargetApplyCoordinator targetApply,
    IControlSessionIdentity controlSessionIdentity) : IModeActionApplyHandler
{
    public bool CanHandle(PersistedModeActionRecord action) => DisplayActionSemantics.CanHandle(action);

    public async Task<ModeActionApplyStageOutcome> ApplyAsync(
        ModeActionExecutionCommand command,
        PersistedModeActionRecord action,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = DisplayActionSemantics.ValidateForApply(action);
        if (validation is not null)
            return new(false, validation.Value.ProductCode, action.Revision, validation.Value.Detail);
        if (!AuthorityMatches(command.ControlSessionKey, out var authorityDetail))
            return new(false, "MODE_DISPLAY_CONTROL_CONTEXT_STALE", action.Revision, authorityDetail);

        await journal.InitializeAsync(cancellationToken).ConfigureAwait(false);
        return action.ActionType switch
        {
            DisplayModeActionContract.TopologyExtendActionType =>
                await ApplyTopologyAsync(command, action, cancellationToken).ConfigureAwait(false),
            DisplayModeActionContract.TargetModeActionType =>
                await ApplyTargetModeAsync(command, action, cancellationToken).ConfigureAwait(false),
            _ => new(false, "MODE_DISPLAY_ACTION_SEMANTICS_INVALID", action.Revision)
        };
    }

    private async Task<ModeActionApplyStageOutcome> ApplyTopologyAsync(
        ModeActionExecutionCommand command,
        PersistedModeActionRecord action,
        CancellationToken cancellationToken)
    {
        DisplayTopologyExtendDesiredState desired;
        try
        {
            desired = DisplayActionSemantics.ReadTopologyDesired(action);
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidDataException)
        {
            return new(false, "MODE_DISPLAY_DESIRED_STATE_INVALID", action.Revision, ex.Message);
        }

        DisplaySnapshot before;
        DisplayTopologyPreState preState;
        string preJson;
        string preDigest;
        try
        {
            before = snapshots.Read();
            preState = DisplayModeActionPreStateContract.CaptureTopology(before);
            preJson = DisplayModeActionPreStateContract.SerializeTopology(preState);
            preDigest = DisplayModeActionPreStateContract.ComputeTopologyDigest(preState);
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidDataException or InvalidOperationException)
        {
            return new(false, "MODE_DISPLAY_PRESTATE_UNAVAILABLE", action.Revision, ex.Message);
        }

        if (!AuthorityMatches(command.ControlSessionKey, out var authorityDetail))
            return new(false, "MODE_DISPLAY_CONTROL_CONTEXT_STALE", action.Revision, authorityDetail);

        var begin = await journal.BeginApplyAsync(
            command.TransitionId, command.ActionId, command.ExpectedActionRevision,
            command.LeaseId, command.FenceToken, command.OperationId,
            preJson, preDigest, cancellationToken).ConfigureAwait(false);
        if (!IsJournalSuccess(begin)) return JournalApplyRejected(begin);
        var applying = begin.Action ?? throw new InvalidDataException("Display BeginApply did not return APPLYING evidence.");

        if (!AuthorityMatches(command.ControlSessionKey, out authorityDetail))
        {
            var denied = await RecordApplyAsync(command, applying.Revision, PersistedModeApplyResult.Failed, cancellationToken).ConfigureAwait(false);
            return new(false, "MODE_DISPLAY_CONTROL_CONTEXT_STALE", denied.Action?.Revision ?? applying.Revision, authorityDetail);
        }

        DisplayExtendApplyOutcome outcome;
        try
        {
            outcome = extend.Apply(new DisplayExtendRequest(desired.Selector, before.Generation));
        }
        catch (Exception ex)
        {
            var unknown = await RecordApplyAsync(command, applying.Revision, PersistedModeApplyResult.Unknown, cancellationToken).ConfigureAwait(false);
            return IsJournalSuccess(unknown)
                ? new(false, "MODE_DISPLAY_APPLY_OUTCOME_UNKNOWN", unknown.Action?.Revision, ex.Message)
                : JournalApplyRejected(unknown, "Display mutation threw and UNKNOWN could not be persisted");
        }

        var stillAuthorized = AuthorityMatches(command.ControlSessionKey, out authorityDetail);
        var persisted = stillAuthorized && outcome.IsVerified
            ? PersistedModeApplyResult.Applied
            : !stillAuthorized || outcome.After is not null
                ? PersistedModeApplyResult.Unknown
                : PersistedModeApplyResult.Failed;
        var recorded = await RecordApplyAsync(command, applying.Revision, persisted, cancellationToken).ConfigureAwait(false);
        if (!IsJournalSuccess(recorded)) return JournalApplyRejected(recorded, "Display EXTEND result could not be persisted");
        if (!stillAuthorized)
            return new(false, "MODE_DISPLAY_CONTROL_CONTEXT_STALE", recorded.Action?.Revision, authorityDetail);
        return outcome.IsVerified
            ? new(true, outcome.ProductCode, recorded.Action?.Revision, outcome.Detail)
            : new(false, outcome.ProductCode, recorded.Action?.Revision, outcome.Detail);
    }

    private async Task<ModeActionApplyStageOutcome> ApplyTargetModeAsync(
        ModeActionExecutionCommand command,
        PersistedModeActionRecord action,
        CancellationToken cancellationToken)
    {
        DisplayTargetModeDesiredState desired;
        try
        {
            desired = DisplayActionSemantics.ReadTargetModeDesired(action);
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidDataException)
        {
            return new(false, "MODE_DISPLAY_DESIRED_STATE_INVALID", action.Revision, ex.Message);
        }

        DisplaySnapshot before;
        DisplayPathEvidence path;
        DisplayTargetModePreState preState;
        string preJson;
        string preDigest;
        try
        {
            before = snapshots.Read();
            var resolved = selectorResolver.Resolve(desired.Selector, before);
            if (!resolved.IsResolved || resolved.Path is null)
                return new(false, resolved.ProductCode, action.Revision, resolved.Detail);
            path = resolved.Path;
            preState = DisplayModeActionPreStateContract.CaptureTargetMode(before, path);
            preJson = DisplayModeActionPreStateContract.SerializeTargetMode(preState);
            preDigest = DisplayModeActionPreStateContract.ComputeTargetModeDigest(preState);
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidDataException or InvalidOperationException)
        {
            return new(false, "MODE_DISPLAY_PRESTATE_UNAVAILABLE", action.Revision, ex.Message);
        }

        if (!AuthorityMatches(command.ControlSessionKey, out var authorityDetail))
            return new(false, "MODE_DISPLAY_CONTROL_CONTEXT_STALE", action.Revision, authorityDetail);

        var begin = await journal.BeginApplyAsync(
            command.TransitionId, command.ActionId, command.ExpectedActionRevision,
            command.LeaseId, command.FenceToken, command.OperationId,
            preJson, preDigest, cancellationToken).ConfigureAwait(false);
        if (!IsJournalSuccess(begin)) return JournalApplyRejected(begin);
        var applying = begin.Action ?? throw new InvalidDataException("Display BeginApply did not return APPLYING evidence.");

        if (!AuthorityMatches(command.ControlSessionKey, out authorityDetail))
        {
            var denied = await RecordApplyAsync(command, applying.Revision, PersistedModeApplyResult.Failed, cancellationToken).ConfigureAwait(false);
            return new(false, "MODE_DISPLAY_CONTROL_CONTEXT_STALE", denied.Action?.Revision ?? applying.Revision, authorityDetail);
        }

        DisplayTargetApplyOutcome outcome;
        try
        {
            outcome = targetApply.Apply(new ResolvedDisplayTarget(
                path.TargetKey,
                before.Generation,
                new DisplayPixelSize(desired.Width, desired.Height),
                new DisplayRational(desired.RefreshNumerator, desired.RefreshDenominator),
                desired.Rotation,
                DisplayTopologyIntent.PreserveActiveTopology));
        }
        catch (Exception ex)
        {
            var unknown = await RecordApplyAsync(command, applying.Revision, PersistedModeApplyResult.Unknown, cancellationToken).ConfigureAwait(false);
            return IsJournalSuccess(unknown)
                ? new(false, "MODE_DISPLAY_APPLY_OUTCOME_UNKNOWN", unknown.Action?.Revision, ex.Message)
                : JournalApplyRejected(unknown, "Display target-mode mutation threw and UNKNOWN could not be persisted");
        }

        var stillAuthorized = AuthorityMatches(command.ControlSessionKey, out authorityDetail);
        var persisted = stillAuthorized && outcome.IsVerified
            ? PersistedModeApplyResult.Applied
            : !stillAuthorized || outcome.After is not null
                ? PersistedModeApplyResult.Unknown
                : PersistedModeApplyResult.Failed;
        var recorded = await RecordApplyAsync(command, applying.Revision, persisted, cancellationToken).ConfigureAwait(false);
        if (!IsJournalSuccess(recorded)) return JournalApplyRejected(recorded, "Display target-mode result could not be persisted");
        if (!stillAuthorized)
            return new(false, "MODE_DISPLAY_CONTROL_CONTEXT_STALE", recorded.Action?.Revision, authorityDetail);
        return outcome.IsVerified
            ? new(true, outcome.ProductCode, recorded.Action?.Revision, outcome.Detail)
            : new(false, outcome.ProductCode, recorded.Action?.Revision, outcome.Detail);
    }

    private Task<ModeActionAdvanceOutcome> RecordApplyAsync(
        ModeActionExecutionCommand command,
        int revision,
        PersistedModeApplyResult result,
        CancellationToken cancellationToken)
        => journal.RecordApplyResultAsync(
            command.TransitionId, command.ActionId, revision,
            command.LeaseId, command.FenceToken, command.OperationId,
            result, cancellationToken);

    private bool AuthorityMatches(string expected, out string? detail)
    {
        try
        {
            var current = controlSessionIdentity.GetCurrentKey();
            detail = string.Equals(current, expected, StringComparison.Ordinal)
                ? null
                : "The durable display action no longer belongs to the active physical-console control session.";
            return detail is null;
        }
        catch (Exception ex)
        {
            detail = $"Physical-console identity could not be re-derived: {ex.Message}";
            return false;
        }
    }

    private static bool IsJournalSuccess(ModeActionAdvanceOutcome outcome)
        => outcome.Disposition is ModeActionAdvanceDisposition.Advanced or ModeActionAdvanceDisposition.Replayed;

    private static ModeActionApplyStageOutcome JournalApplyRejected(ModeActionAdvanceOutcome outcome, string? prefix = null)
        => new(false, outcome.ProductCode, outcome.Action?.Revision ?? outcome.ActualActionRevision,
            prefix is null ? outcome.Detail : $"{prefix}: {outcome.Detail ?? outcome.ProductCode}");
}

public sealed class DisplayModeActionVerifyHandler(
    IModeTransitionActionJournalStore journal,
    IDisplaySnapshotReader snapshots,
    PersistentDisplaySelectorResolver selectorResolver,
    IControlSessionIdentity controlSessionIdentity) : IModeActionVerifyHandler
{
    public bool CanHandle(PersistedModeActionRecord action) => DisplayActionSemantics.CanHandle(action);

    public async Task<ModeActionVerifyStageOutcome> VerifyAsync(
        ModeActionExecutionCommand command,
        PersistedModeActionRecord action,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = DisplayActionSemantics.ValidateForVerify(action);
        if (validation is not null)
            return new(false, validation.Value.ProductCode, action.Revision, validation.Value.Detail);
        if (!AuthorityMatches(command.ControlSessionKey, out var authorityDetail))
            return new(false, "MODE_DISPLAY_CONTROL_CONTEXT_STALE", action.Revision, authorityDetail);

        await journal.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var begin = await journal.BeginVerifyAsync(
            command.TransitionId, command.ActionId, command.ExpectedActionRevision,
            command.LeaseId, command.FenceToken, command.OperationId,
            cancellationToken).ConfigureAwait(false);
        if (!IsJournalSuccess(begin)) return JournalVerifyRejected(begin);
        var verifying = begin.Action ?? throw new InvalidDataException("Display BeginVerify did not return VERIFYING evidence.");

        PersistedModeVerifyResult persisted;
        string productCode;
        string? detail;
        try
        {
            if (!AuthorityMatches(command.ControlSessionKey, out authorityDetail))
            {
                persisted = PersistedModeVerifyResult.Unknown;
                productCode = "MODE_DISPLAY_CONTROL_CONTEXT_STALE";
                detail = authorityDetail;
            }
            else
            {
                var observation = action.ActionType switch
                {
                    DisplayModeActionContract.TopologyExtendActionType => VerifyTopology(action),
                    DisplayModeActionContract.TargetModeActionType => VerifyTargetMode(action),
                    _ => (PersistedModeVerifyResult.Unknown, "MODE_DISPLAY_ACTION_SEMANTICS_INVALID", (string?)null)
                };
                persisted = observation.Item1;
                productCode = observation.Item2;
                detail = observation.Item3;
                if (!AuthorityMatches(command.ControlSessionKey, out authorityDetail))
                {
                    persisted = PersistedModeVerifyResult.Unknown;
                    productCode = "MODE_DISPLAY_CONTROL_CONTEXT_STALE";
                    detail = authorityDetail;
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidDataException or InvalidOperationException)
        {
            persisted = PersistedModeVerifyResult.Unknown;
            productCode = "MODE_DISPLAY_VERIFY_EVIDENCE_UNAVAILABLE";
            detail = ex.Message;
        }

        var recorded = await journal.RecordVerifyResultAsync(
            command.TransitionId, command.ActionId, verifying.Revision,
            command.LeaseId, command.FenceToken, command.OperationId,
            persisted, cancellationToken).ConfigureAwait(false);
        if (!IsJournalSuccess(recorded)) return JournalVerifyRejected(recorded, "Display verification result could not be persisted");
        return persisted == PersistedModeVerifyResult.Verified
            ? new(true, productCode, recorded.Action?.Revision, detail)
            : new(false, productCode, recorded.Action?.Revision, detail);
    }

    private (PersistedModeVerifyResult Result, string ProductCode, string? Detail) VerifyTopology(
        PersistedModeActionRecord action)
    {
        var desired = DisplayActionSemantics.ReadTopologyDesired(action);
        var baseline = DisplayActionSemantics.ReadTopologyPreState(action);
        var current = snapshots.Read();
        var resolved = selectorResolver.Resolve(desired.Selector, current);
        if (!resolved.IsResolved || resolved.Path is null || !resolved.Path.Active || !resolved.Path.TargetAvailable || resolved.Path.Identity is null)
            return (PersistedModeVerifyResult.Mismatch, resolved.ProductCode, resolved.Detail);

        var active = DisplayModeActionPreStateContract.CaptureActiveSelectors(current);
        var added = DisplayModeActionPreStateContract.SelectorFromIdentity(resolved.Path.Identity);
        IReadOnlyList<PersistentDisplaySelector> expected;
        try
        {
            expected = baseline.ActiveTargets.Append(added).ToArray();
            expected = DisplayModeActionPreStateContract.Normalize(new DisplayTopologyPreState(expected)).ActiveTargets;
        }
        catch (InvalidDataException ex)
        {
            return (PersistedModeVerifyResult.Mismatch, "MODE_DISPLAY_TOPOLOGY_VERIFY_DUPLICATE", ex.Message);
        }

        return DisplayModeActionPreStateContract.TargetSetsEqual(expected, active)
            ? (PersistedModeVerifyResult.Verified, "MODE_DISPLAY_TOPOLOGY_VERIFIED", null)
            : (PersistedModeVerifyResult.Mismatch, "MODE_DISPLAY_TOPOLOGY_MISMATCH", "Fresh physical target-set is not the exact baseline plus the durable EXTEND target.");
    }

    private (PersistedModeVerifyResult Result, string ProductCode, string? Detail) VerifyTargetMode(
        PersistedModeActionRecord action)
    {
        var desired = DisplayActionSemantics.ReadTargetModeDesired(action);
        var preState = DisplayActionSemantics.ReadTargetModePreState(action);
        var current = snapshots.Read();
        var resolved = selectorResolver.Resolve(desired.Selector, current);
        if (!resolved.IsResolved || resolved.Path is null || !resolved.Path.Active || !resolved.Path.TargetAvailable)
            return (PersistedModeVerifyResult.Mismatch, resolved.ProductCode, resolved.Detail);

        var path = resolved.Path;
        var active = DisplayModeActionPreStateContract.CaptureActiveSelectors(current);
        if (!DisplayModeActionPreStateContract.TargetSetsEqual(preState.ActiveTargets, active))
            return (PersistedModeVerifyResult.Mismatch, "MODE_DISPLAY_MODE_TOPOLOGY_MISMATCH", "Target-mode verification observed an active physical target-set different from the pre-mutation topology.");
        if (path.SourceResolution != new DisplayPixelSize(desired.Width, desired.Height) ||
            path.Rotation != desired.Rotation ||
            !RationalEquals(path.RefreshRate, new DisplayRational(desired.RefreshNumerator, desired.RefreshDenominator)))
        {
            return (PersistedModeVerifyResult.Mismatch, "MODE_DISPLAY_MODE_MISMATCH", "Fresh display read-back does not match the durable target-mode intent.");
        }
        return (PersistedModeVerifyResult.Verified, "MODE_DISPLAY_MODE_VERIFIED", null);
    }

    private bool AuthorityMatches(string expected, out string? detail)
    {
        try
        {
            var current = controlSessionIdentity.GetCurrentKey();
            detail = string.Equals(current, expected, StringComparison.Ordinal)
                ? null
                : "The durable display action no longer belongs to the active physical-console control session.";
            return detail is null;
        }
        catch (Exception ex)
        {
            detail = $"Physical-console identity could not be re-derived: {ex.Message}";
            return false;
        }
    }

    private static bool RationalEquals(DisplayRational? actual, DisplayRational expected)
        => actual.HasValue &&
           (ulong)actual.Value.Numerator * expected.Denominator ==
           (ulong)expected.Numerator * actual.Value.Denominator;

    private static bool IsJournalSuccess(ModeActionAdvanceOutcome outcome)
        => outcome.Disposition is ModeActionAdvanceDisposition.Advanced or ModeActionAdvanceDisposition.Replayed;

    private static ModeActionVerifyStageOutcome JournalVerifyRejected(ModeActionAdvanceOutcome outcome, string? prefix = null)
        => new(false, outcome.ProductCode, outcome.Action?.Revision ?? outcome.ActualActionRevision,
            prefix is null ? outcome.Detail : $"{prefix}: {outcome.Detail ?? outcome.ProductCode}");
}

internal static class DisplayActionSemantics
{
    public static bool CanHandle(PersistedModeActionRecord action)
        => string.Equals(action.OwningModule, DisplayModeActionContract.OwningModule, StringComparison.Ordinal) &&
           action.ActionType is DisplayModeActionContract.TopologyExtendActionType or DisplayModeActionContract.TargetModeActionType;

    public static (string ProductCode, string Detail)? ValidateForApply(PersistedModeActionRecord action)
    {
        var header = ValidateHeader(action);
        if (header is not null) return header;
        if (action.State != PersistedModeActionState.Planned)
            return ("MODE_DISPLAY_ACTION_NOT_PLANNED", $"Action state is {action.State}.");
        return ValidateDesired(action);
    }

    public static (string ProductCode, string Detail)? ValidateForVerify(PersistedModeActionRecord action)
    {
        var header = ValidateHeader(action);
        if (header is not null) return header;
        if (action.State != PersistedModeActionState.Applied || !string.Equals(action.ApplyResultCode, "APPLIED", StringComparison.Ordinal))
            return ("MODE_DISPLAY_ACTION_NOT_APPLIED", $"Action must have durable APPLIED evidence before verification; actual state is {action.State}.");
        var desired = ValidateDesired(action);
        if (desired is not null) return desired;
        if (string.IsNullOrWhiteSpace(action.PreStateJson) || string.IsNullOrWhiteSpace(action.PreStateDigest))
            return ("MODE_DISPLAY_PRESTATE_MISSING", "Display action has no durable pre-mutation evidence.");
        try
        {
            if (action.ActionType == DisplayModeActionContract.TopologyExtendActionType)
                _ = ReadTopologyPreState(action);
            else
                _ = ReadTargetModePreState(action);
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidDataException)
        {
            return ("MODE_DISPLAY_PRESTATE_INVALID", ex.Message);
        }
        return null;
    }

    public static DisplayTopologyExtendDesiredState ReadTopologyDesired(PersistedModeActionRecord action)
    {
        var desired = DisplayModeActionContract.DeserializeTopologyExtend(action.DesiredStateJson!);
        if (!string.Equals(DisplayModeActionContract.ComputeTopologyExtendDigest(desired), action.DesiredStateDigest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Display topology desired-state digest does not match its canonical payload.");
        return desired;
    }

    public static DisplayTargetModeDesiredState ReadTargetModeDesired(PersistedModeActionRecord action)
    {
        var desired = DisplayModeActionContract.DeserializeTargetMode(action.DesiredStateJson!);
        if (!string.Equals(DisplayModeActionContract.ComputeTargetModeDigest(desired), action.DesiredStateDigest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Display target-mode desired-state digest does not match its canonical payload.");
        return desired;
    }

    public static DisplayTopologyPreState ReadTopologyPreState(PersistedModeActionRecord action)
    {
        var preState = DisplayModeActionPreStateContract.DeserializeTopology(action.PreStateJson!);
        if (!string.Equals(DisplayModeActionPreStateContract.ComputeTopologyDigest(preState), action.PreStateDigest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Display topology pre-state digest does not match its canonical payload.");
        return preState;
    }

    public static DisplayTargetModePreState ReadTargetModePreState(PersistedModeActionRecord action)
    {
        var preState = DisplayModeActionPreStateContract.DeserializeTargetMode(action.PreStateJson!);
        if (!string.Equals(DisplayModeActionPreStateContract.ComputeTargetModeDigest(preState), action.PreStateDigest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Display target-mode pre-state digest does not match its canonical payload.");
        return preState;
    }

    private static (string ProductCode, string Detail)? ValidateHeader(PersistedModeActionRecord action)
    {
        if (!CanHandle(action) ||
            !string.Equals(action.TargetRef, DisplayModeActionContract.TargetRef, StringComparison.Ordinal) ||
            action.DesiredSchemaVersion != DisplayModeActionContract.DesiredSchemaVersion ||
            !string.Equals(action.RollbackClass, DisplayModeActionContract.RollbackClass, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(action.DesiredStateJson))
        {
            return ("MODE_DISPLAY_ACTION_SEMANTICS_INVALID", "Durable action is not a supported canonical display action.");
        }
        var expectedVerification = action.ActionType == DisplayModeActionContract.TopologyExtendActionType
            ? DisplayModeActionContract.TopologyVerificationClass
            : DisplayModeActionContract.ModeVerificationClass;
        return string.Equals(action.VerificationClass, expectedVerification, StringComparison.Ordinal)
            ? null
            : ("MODE_DISPLAY_ACTION_SEMANTICS_INVALID", "Display verification class does not match the action type.");
    }

    private static (string ProductCode, string Detail)? ValidateDesired(PersistedModeActionRecord action)
    {
        try
        {
            if (action.ActionType == DisplayModeActionContract.TopologyExtendActionType)
                _ = ReadTopologyDesired(action);
            else
                _ = ReadTargetModeDesired(action);
            return null;
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidDataException)
        {
            return ("MODE_DISPLAY_DESIRED_STATE_INVALID", ex.Message);
        }
    }
}
