using SplitOS.Contracts.Protocol;

namespace SplitOS.Runtime.Client;

public enum LauncherLifecycleState
{
    Stopped,
    Starting,
    Connecting,
    Preparing,
    ReadyPrecommit,
    Active,
    BackgroundGameRunning,
    Restoring,
    DegradedDisconnected,
    Stopping
}

public enum LauncherBindingDisposition
{
    Applied,
    NoOp,
    Rejected
}

public static class LauncherBindingReasonCodes
{
    public const string StateApplied = "STATE_APPLIED";
    public const string Idempotent = "IDEMPOTENT";
    public const string InvalidLifecycleTransition = "INVALID_LIFECYCLE_TRANSITION";
    public const string FreshSnapshotRequired = "FRESH_SNAPSHOT_REQUIRED";
    public const string RuntimeSnapshotInconsistent = "RUNTIME_SNAPSHOT_INCONSISTENT";
    public const string PresentationNotReady = "PRESENTATION_NOT_READY";
    public const string RuntimeNotReady = "RUNTIME_NOT_READY";
}

public sealed record LauncherBindingDecision(
    LauncherBindingDisposition Disposition,
    string ReasonCode,
    LauncherLifecycleState State,
    bool HasFreshRuntimeSnapshot,
    bool CanIssueMutatingRequests,
    bool CanReportGameModeReady,
    Guid? ExpectedGameModeOperationId,
    Guid? ExpectedGameModeCorrelationId,
    long? RuntimeSnapshotVersion);

public sealed class LauncherRuntimeBindingController
{
    private LauncherLifecycleState _state = LauncherLifecycleState.Stopped;
    private bool _transportConnected;
    private bool _presentationSubsystemReady;
    private bool _hasFreshSnapshot;
    private LauncherRuntimeSnapshotResult? _snapshot;
    private LauncherPresentationState _presentationState = LauncherPresentationState.Inactive;

    public LauncherLifecycleState State => _state;

    public LauncherBindingDecision BeginStart()
    {
        if (_state == LauncherLifecycleState.Starting)
            return Decision(LauncherBindingDisposition.NoOp, LauncherBindingReasonCodes.Idempotent);
        if (_state != LauncherLifecycleState.Stopped)
            return Decision(LauncherBindingDisposition.Rejected, LauncherBindingReasonCodes.InvalidLifecycleTransition);

        ResetBinding();
        _state = LauncherLifecycleState.Starting;
        return Decision(LauncherBindingDisposition.Applied, LauncherBindingReasonCodes.StateApplied);
    }

    public LauncherBindingDecision BeginConnecting()
    {
        if (_state == LauncherLifecycleState.Connecting)
            return Decision(LauncherBindingDisposition.NoOp, LauncherBindingReasonCodes.Idempotent);
        if (_state is not (LauncherLifecycleState.Starting or LauncherLifecycleState.DegradedDisconnected))
            return Decision(LauncherBindingDisposition.Rejected, LauncherBindingReasonCodes.InvalidLifecycleTransition);

        _transportConnected = false;
        _hasFreshSnapshot = false;
        _snapshot = null;
        _state = LauncherLifecycleState.Connecting;
        return Decision(LauncherBindingDisposition.Applied, LauncherBindingReasonCodes.StateApplied);
    }

    public LauncherBindingDecision ReportTransportConnected()
    {
        if (_transportConnected && _state == LauncherLifecycleState.Preparing)
            return Decision(LauncherBindingDisposition.NoOp, LauncherBindingReasonCodes.FreshSnapshotRequired);
        if (_state != LauncherLifecycleState.Connecting)
            return Decision(LauncherBindingDisposition.Rejected, LauncherBindingReasonCodes.InvalidLifecycleTransition);

        _transportConnected = true;
        _hasFreshSnapshot = false;
        _snapshot = null;
        _state = LauncherLifecycleState.Preparing;
        return Decision(LauncherBindingDisposition.Applied, LauncherBindingReasonCodes.FreshSnapshotRequired);
    }

    public LauncherBindingDecision ReportPresentationSubsystemReady()
    {
        if (_presentationSubsystemReady)
            return Decision(LauncherBindingDisposition.NoOp, LauncherBindingReasonCodes.Idempotent);
        if (_state is LauncherLifecycleState.Stopped or LauncherLifecycleState.Stopping)
            return Decision(LauncherBindingDisposition.Rejected, LauncherBindingReasonCodes.InvalidLifecycleTransition);

        _presentationSubsystemReady = true;
        if (!_transportConnected)
            return Decision(LauncherBindingDisposition.Applied, LauncherBindingReasonCodes.FreshSnapshotRequired);

        return RecomputeFromCurrentBinding();
    }

    public LauncherBindingDecision ApplyFreshRuntimeSnapshot(
        LauncherRuntimeSnapshotResult snapshot,
        LauncherPresentationState presentationState)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!_transportConnected || _state is LauncherLifecycleState.Stopped or LauncherLifecycleState.Stopping)
            return Decision(LauncherBindingDisposition.Rejected, LauncherBindingReasonCodes.InvalidLifecycleTransition);

        if (!ValidateSnapshot(snapshot, presentationState))
            return Decision(LauncherBindingDisposition.Rejected, LauncherBindingReasonCodes.RuntimeSnapshotInconsistent);

        _snapshot = snapshot;
        _presentationState = presentationState;
        _hasFreshSnapshot = true;
        return RecomputeFromCurrentBinding();
    }

    public LauncherBindingDecision ReportRuntimeDisconnected()
    {
        if (_state == LauncherLifecycleState.DegradedDisconnected)
            return Decision(LauncherBindingDisposition.NoOp, LauncherBindingReasonCodes.Idempotent);
        if (_state is LauncherLifecycleState.Stopped or LauncherLifecycleState.Stopping)
            return Decision(LauncherBindingDisposition.Rejected, LauncherBindingReasonCodes.InvalidLifecycleTransition);

        _transportConnected = false;
        _hasFreshSnapshot = false;
        _snapshot = null;
        _state = LauncherLifecycleState.DegradedDisconnected;
        return Decision(LauncherBindingDisposition.Applied, LauncherBindingReasonCodes.FreshSnapshotRequired);
    }

    public LauncherBindingDecision BeginStopping()
    {
        if (_state == LauncherLifecycleState.Stopping)
            return Decision(LauncherBindingDisposition.NoOp, LauncherBindingReasonCodes.Idempotent);
        if (_state == LauncherLifecycleState.Stopped)
            return Decision(LauncherBindingDisposition.NoOp, LauncherBindingReasonCodes.Idempotent);

        _state = LauncherLifecycleState.Stopping;
        return Decision(LauncherBindingDisposition.Applied, LauncherBindingReasonCodes.StateApplied);
    }

    public LauncherBindingDecision ReportStopped()
    {
        if (_state == LauncherLifecycleState.Stopped)
            return Decision(LauncherBindingDisposition.NoOp, LauncherBindingReasonCodes.Idempotent);
        if (_state != LauncherLifecycleState.Stopping)
            return Decision(LauncherBindingDisposition.Rejected, LauncherBindingReasonCodes.InvalidLifecycleTransition);

        ResetBinding();
        _presentationSubsystemReady = false;
        _presentationState = LauncherPresentationState.Inactive;
        _state = LauncherLifecycleState.Stopped;
        return Decision(LauncherBindingDisposition.Applied, LauncherBindingReasonCodes.StateApplied);
    }

    private LauncherBindingDecision RecomputeFromCurrentBinding()
    {
        if (!_transportConnected)
            return Decision(LauncherBindingDisposition.Applied, LauncherBindingReasonCodes.FreshSnapshotRequired);

        if (!_hasFreshSnapshot || _snapshot is null)
        {
            _state = LauncherLifecycleState.Preparing;
            return Decision(LauncherBindingDisposition.Applied, LauncherBindingReasonCodes.FreshSnapshotRequired);
        }

        if (!string.Equals(_snapshot.RuntimeStatus, "READY", StringComparison.Ordinal))
        {
            _state = LauncherLifecycleState.Preparing;
            return Decision(LauncherBindingDisposition.Applied, LauncherBindingReasonCodes.RuntimeNotReady);
        }

        if (string.Equals(_snapshot.CommittedMode, "GAME", StringComparison.Ordinal))
        {
            _state = _presentationState switch
            {
                LauncherPresentationState.Active => LauncherLifecycleState.Active,
                LauncherPresentationState.BackgroundGameRunning => LauncherLifecycleState.BackgroundGameRunning,
                LauncherPresentationState.Restoring => LauncherLifecycleState.Restoring,
                _ => LauncherLifecycleState.Preparing
            };

            var reason = _state == LauncherLifecycleState.Preparing
                ? LauncherBindingReasonCodes.RuntimeSnapshotInconsistent
                : LauncherBindingReasonCodes.StateApplied;
            var disposition = _state == LauncherLifecycleState.Preparing
                ? LauncherBindingDisposition.Rejected
                : LauncherBindingDisposition.Applied;
            return Decision(disposition, reason);
        }

        if (!_presentationSubsystemReady)
        {
            _state = LauncherLifecycleState.Preparing;
            return Decision(LauncherBindingDisposition.Applied, LauncherBindingReasonCodes.PresentationNotReady);
        }

        _state = LauncherLifecycleState.ReadyPrecommit;
        return Decision(LauncherBindingDisposition.Applied, LauncherBindingReasonCodes.StateApplied);
    }

    private static bool ValidateSnapshot(
        LauncherRuntimeSnapshotResult snapshot,
        LauncherPresentationState presentationState)
    {
        if (snapshot.SnapshotVersion <= 0 || snapshot.GameSessionRevision < 0 || snapshot.ReadinessRevision < 0)
            return false;

        var hasExpectedOperation = snapshot.ExpectedGameModeOperationId.HasValue;
        if (hasExpectedOperation != snapshot.ExpectedGameModeCorrelationId.HasValue)
            return false;
        if (snapshot.ExpectedGameModeOperationId == Guid.Empty || snapshot.ExpectedGameModeCorrelationId == Guid.Empty)
            return false;

        var gameCommitted = string.Equals(snapshot.CommittedMode, "GAME", StringComparison.Ordinal);
        var sessionInactive = string.Equals(snapshot.GameSessionState, "INACTIVE", StringComparison.Ordinal);
        if (gameCommitted && sessionInactive)
            return false;
        if (!gameCommitted && !sessionInactive)
            return false;
        if (gameCommitted && presentationState == LauncherPresentationState.Inactive)
            return false;
        if (!gameCommitted && presentationState != LauncherPresentationState.Inactive)
            return false;

        return true;
    }

    private void ResetBinding()
    {
        _transportConnected = false;
        _hasFreshSnapshot = false;
        _snapshot = null;
    }

    private LauncherBindingDecision Decision(
        LauncherBindingDisposition disposition,
        string reasonCode)
    {
        var canMutate = _transportConnected
            && _hasFreshSnapshot
            && _snapshot is not null
            && string.Equals(_snapshot.RuntimeStatus, "READY", StringComparison.Ordinal)
            && _state == LauncherLifecycleState.Active;

        var canReportReady = _transportConnected
            && _hasFreshSnapshot
            && _state == LauncherLifecycleState.ReadyPrecommit
            && _snapshot?.ExpectedGameModeOperationId is not null
            && _snapshot.ExpectedGameModeCorrelationId is not null;

        return new LauncherBindingDecision(
            disposition,
            reasonCode,
            _state,
            _hasFreshSnapshot,
            canMutate,
            canReportReady,
            _snapshot?.ExpectedGameModeOperationId,
            _snapshot?.ExpectedGameModeCorrelationId,
            _snapshot?.SnapshotVersion);
    }
}
