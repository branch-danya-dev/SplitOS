namespace SplitOS.RuntimeHost.GameRuntime;

public enum GameSessionState
{
    Inactive,
    Launcher,
    Preparing,
    ClientHandoff,
    GameStarting,
    GameRunning,
    GameExitDetected,
    ReturningToLauncher,
    Failed
}

public enum GameSessionSignal
{
    LauncherReady,
    LaunchRequested,
    ClientHandoffSubmitted,
    ClientHandoffAccepted,
    GameStartingConfirmed,
    GameRunningConfirmed,
    GameProcessReplaced,
    GameExitedConfirmed,
    CorrelationLost,
    StartTimeout,
    LaunchFailed,
    BeginLauncherReturn,
    LauncherRestored,
    ModeDeactivated
}

public enum GameSessionTransitionDisposition
{
    Applied,
    NoOp,
    Rejected
}

public static class GameSessionReasonCodes
{
    public const string TransitionApplied = "TRANSITION_APPLIED";
    public const string HandoffAccepted = "HANDOFF_ACCEPTED";
    public const string EvidenceDoesNotChangeState = "EVIDENCE_DOES_NOT_CHANGE_STATE";
    public const string IdempotentReplay = "IDEMPOTENT_REPLAY";
    public const string InvalidTransition = "INVALID_TRANSITION";
    public const string GameModeNotCommitted = "GAME_MODE_NOT_COMMITTED";
    public const string GameModeStillCommitted = "GAME_MODE_STILL_COMMITTED";
    public const string LaunchIdentityRequired = "LAUNCH_IDENTITY_REQUIRED";
    public const string NoActiveLaunch = "NO_ACTIVE_LAUNCH";
    public const string ActiveLaunchMismatch = "ACTIVE_LAUNCH_MISMATCH";
    public const string ActiveLaunchInProgress = "ACTIVE_LAUNCH_IN_PROGRESS";
    public const string HandoffNotAccepted = "HANDOFF_NOT_ACCEPTED";
    public const string FailureCodeRequired = "FAILURE_CODE_REQUIRED";
    public const string ModeDeactivated = "MODE_DEACTIVATED";
}

public static class GameSessionFailureCodes
{
    public const string CorrelationLost = "CORRELATION_LOST";
    public const string StartTimeout = "START_TIMEOUT";
}

public sealed record GameSessionLaunchIdentity(
    string LaunchOperationId,
    string CorrelationId,
    string GameId)
{
    public GameSessionLaunchIdentity Normalize()
    {
        static string Required(string value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidDataException($"{fieldName} cannot be empty.");

            return value.Trim();
        }

        return new GameSessionLaunchIdentity(
            Required(LaunchOperationId, "launch operation ID"),
            Required(CorrelationId, "launch correlation ID"),
            Required(GameId, "game ID"));
    }
}

public sealed record GameSessionTransitionRequest(
    GameSessionSignal Signal,
    bool IsGameModeCommitted,
    GameSessionLaunchIdentity? LaunchIdentity = null,
    string? FailureCode = null);

public sealed record GameSessionSnapshot(
    GameSessionState State,
    GameSessionLaunchIdentity? ActiveLaunch,
    bool ClientHandoffAccepted,
    string? FailureCode,
    long Revision);

public sealed record GameSessionTransitionResult(
    GameSessionTransitionDisposition Disposition,
    string ReasonCode,
    GameSessionState PreviousState,
    GameSessionSnapshot Snapshot);

/// <summary>
/// Canonical Runtime Host owner for one interactive session's managed GameSession state.
///
/// The state machine consumes normalized semantic signals only. It never enumerates processes,
/// scores candidates, interprets client lifetime, or infers game state from a PID. Those concerns
/// remain in the client/launch observation and IMP-075 correlation layers.
/// </summary>
public sealed class GameSessionStateMachine
{
    private GameSessionSnapshot _snapshot = new(
        GameSessionState.Inactive,
        ActiveLaunch: null,
        ClientHandoffAccepted: false,
        FailureCode: null,
        Revision: 0);

    public GameSessionSnapshot Snapshot => _snapshot;

    public GameSessionTransitionResult Apply(GameSessionTransitionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var previous = _snapshot.State;

        if (request.Signal == GameSessionSignal.ModeDeactivated)
            return ApplyModeDeactivated(request, previous);

        // Every non-INACTIVE GameSession state belongs to committed GAME. The state machine accepts
        // the committed-mode observation from its orchestration caller instead of reading mode state
        // itself, keeping ownership boundaries explicit and testable.
        if (!request.IsGameModeCommitted)
            return Reject(GameSessionReasonCodes.GameModeNotCommitted, previous);

        return request.Signal switch
        {
            GameSessionSignal.LauncherReady => ApplyLauncherReady(previous, request),
            GameSessionSignal.LaunchRequested => ApplyLaunchRequested(previous, request),
            GameSessionSignal.ClientHandoffSubmitted => ApplyClientHandoffSubmitted(previous, request),
            GameSessionSignal.ClientHandoffAccepted => ApplyClientHandoffAccepted(previous, request),
            GameSessionSignal.GameStartingConfirmed => ApplyGameStartingConfirmed(previous, request),
            GameSessionSignal.GameRunningConfirmed => ApplyGameRunningConfirmed(previous, request),
            GameSessionSignal.GameProcessReplaced => ApplyGameProcessReplaced(previous, request),
            GameSessionSignal.GameExitedConfirmed => ApplyGameExitedConfirmed(previous, request),
            GameSessionSignal.CorrelationLost => ApplyCorrelationLost(previous, request),
            GameSessionSignal.StartTimeout => ApplyStartTimeout(previous, request),
            GameSessionSignal.LaunchFailed => ApplyLaunchFailed(previous, request),
            GameSessionSignal.BeginLauncherReturn => ApplyBeginLauncherReturn(previous, request),
            GameSessionSignal.LauncherRestored => ApplyLauncherRestored(previous, request),
            _ => Reject(GameSessionReasonCodes.InvalidTransition, previous)
        };
    }

    private GameSessionTransitionResult ApplyModeDeactivated(
        GameSessionTransitionRequest request,
        GameSessionState previous)
    {
        if (request.IsGameModeCommitted)
            return Reject(GameSessionReasonCodes.GameModeStillCommitted, previous);

        if (_snapshot.State == GameSessionState.Inactive)
            return NoOp(GameSessionReasonCodes.IdempotentReplay, previous);

        _snapshot = new GameSessionSnapshot(
            GameSessionState.Inactive,
            ActiveLaunch: null,
            ClientHandoffAccepted: false,
            FailureCode: null,
            Revision: checked(_snapshot.Revision + 1));

        return Applied(GameSessionReasonCodes.ModeDeactivated, previous);
    }

    private GameSessionTransitionResult ApplyLauncherReady(
        GameSessionState previous,
        GameSessionTransitionRequest request)
    {
        if (request.LaunchIdentity is not null)
            return Reject(GameSessionReasonCodes.InvalidTransition, previous);

        if (_snapshot.State == GameSessionState.Launcher)
            return NoOp(GameSessionReasonCodes.IdempotentReplay, previous);

        if (_snapshot.State != GameSessionState.Inactive)
            return Reject(GameSessionReasonCodes.InvalidTransition, previous);

        return Move(
            GameSessionState.Launcher,
            previous,
            activeLaunch: null,
            handoffAccepted: false,
            failureCode: null);
    }

    private GameSessionTransitionResult ApplyLaunchRequested(
        GameSessionState previous,
        GameSessionTransitionRequest request)
    {
        if (!TryNormalizeLaunch(request.LaunchIdentity, out var launch))
            return Reject(GameSessionReasonCodes.LaunchIdentityRequired, previous);

        if (_snapshot.State == GameSessionState.Preparing)
        {
            if (_snapshot.ActiveLaunch == launch)
                return NoOp(GameSessionReasonCodes.IdempotentReplay, previous);

            return Reject(GameSessionReasonCodes.ActiveLaunchInProgress, previous);
        }

        if (_snapshot.State != GameSessionState.Launcher)
            return Reject(GameSessionReasonCodes.InvalidTransition, previous);

        _snapshot = new GameSessionSnapshot(
            GameSessionState.Preparing,
            launch,
            ClientHandoffAccepted: false,
            FailureCode: null,
            Revision: checked(_snapshot.Revision + 1));

        return Applied(GameSessionReasonCodes.TransitionApplied, previous);
    }

    private GameSessionTransitionResult ApplyClientHandoffSubmitted(
        GameSessionState previous,
        GameSessionTransitionRequest request)
    {
        var identityFailure = RequireActiveLaunch(request, previous);
        if (identityFailure is not null)
            return identityFailure;

        if (_snapshot.State == GameSessionState.ClientHandoff)
            return NoOp(GameSessionReasonCodes.IdempotentReplay, previous);

        if (_snapshot.State != GameSessionState.Preparing)
            return Reject(GameSessionReasonCodes.InvalidTransition, previous);

        return Move(
            GameSessionState.ClientHandoff,
            previous,
            _snapshot.ActiveLaunch,
            handoffAccepted: false,
            failureCode: null);
    }

    private GameSessionTransitionResult ApplyClientHandoffAccepted(
        GameSessionState previous,
        GameSessionTransitionRequest request)
    {
        var identityFailure = RequireActiveLaunch(request, previous);
        if (identityFailure is not null)
            return identityFailure;

        if (_snapshot.State != GameSessionState.ClientHandoff)
            return Reject(GameSessionReasonCodes.InvalidTransition, previous);

        // HANDOFF_ACCEPTED is meaningful evidence, but it is deliberately not GAME_STARTING or
        // GAME_RUNNING. Persist it as a state detail while keeping canonical state at CLIENT_HANDOFF.
        if (_snapshot.ClientHandoffAccepted)
            return NoOp(GameSessionReasonCodes.IdempotentReplay, previous);

        _snapshot = _snapshot with
        {
            ClientHandoffAccepted = true,
            Revision = checked(_snapshot.Revision + 1)
        };

        return Applied(GameSessionReasonCodes.HandoffAccepted, previous);
    }

    private GameSessionTransitionResult ApplyGameStartingConfirmed(
        GameSessionState previous,
        GameSessionTransitionRequest request)
    {
        var identityFailure = RequireActiveLaunch(request, previous);
        if (identityFailure is not null)
            return identityFailure;

        if (_snapshot.State == GameSessionState.GameStarting)
            return NoOp(GameSessionReasonCodes.IdempotentReplay, previous);

        if (_snapshot.State != GameSessionState.ClientHandoff)
            return Reject(GameSessionReasonCodes.InvalidTransition, previous);

        if (!_snapshot.ClientHandoffAccepted)
            return Reject(GameSessionReasonCodes.HandoffNotAccepted, previous);

        return Move(
            GameSessionState.GameStarting,
            previous,
            _snapshot.ActiveLaunch,
            handoffAccepted: true,
            failureCode: null);
    }

    private GameSessionTransitionResult ApplyGameRunningConfirmed(
        GameSessionState previous,
        GameSessionTransitionRequest request)
    {
        var identityFailure = RequireActiveLaunch(request, previous);
        if (identityFailure is not null)
            return identityFailure;

        if (_snapshot.State == GameSessionState.GameRunning)
            return NoOp(GameSessionReasonCodes.IdempotentReplay, previous);

        if (_snapshot.State != GameSessionState.GameStarting)
            return Reject(GameSessionReasonCodes.InvalidTransition, previous);

        return Move(
            GameSessionState.GameRunning,
            previous,
            _snapshot.ActiveLaunch,
            handoffAccepted: true,
            failureCode: null);
    }

    private GameSessionTransitionResult ApplyGameProcessReplaced(
        GameSessionState previous,
        GameSessionTransitionRequest request)
    {
        var identityFailure = RequireActiveLaunch(request, previous);
        if (identityFailure is not null)
            return identityFailure;

        if (_snapshot.State != GameSessionState.GameRunning)
            return Reject(GameSessionReasonCodes.InvalidTransition, previous);

        // Correlated process replacement belongs to evidence ownership. It does not create a new
        // canonical GameSession state and must not reset or advance the session lifecycle.
        return NoOp(GameSessionReasonCodes.EvidenceDoesNotChangeState, previous);
    }

    private GameSessionTransitionResult ApplyGameExitedConfirmed(
        GameSessionState previous,
        GameSessionTransitionRequest request)
    {
        var identityFailure = RequireActiveLaunch(request, previous);
        if (identityFailure is not null)
            return identityFailure;

        if (_snapshot.State == GameSessionState.GameExitDetected)
            return NoOp(GameSessionReasonCodes.IdempotentReplay, previous);

        if (_snapshot.State != GameSessionState.GameRunning)
            return Reject(GameSessionReasonCodes.InvalidTransition, previous);

        return Move(
            GameSessionState.GameExitDetected,
            previous,
            _snapshot.ActiveLaunch,
            handoffAccepted: true,
            failureCode: null);
    }

    private GameSessionTransitionResult ApplyCorrelationLost(
        GameSessionState previous,
        GameSessionTransitionRequest request)
    {
        var identityFailure = RequireActiveLaunch(request, previous);
        if (identityFailure is not null)
            return identityFailure;

        if (_snapshot.State == GameSessionState.Failed
            && string.Equals(_snapshot.FailureCode, GameSessionFailureCodes.CorrelationLost, StringComparison.Ordinal))
        {
            return NoOp(GameSessionReasonCodes.IdempotentReplay, previous);
        }

        if (_snapshot.State is not (GameSessionState.ClientHandoff
            or GameSessionState.GameStarting
            or GameSessionState.GameRunning))
        {
            return Reject(GameSessionReasonCodes.InvalidTransition, previous);
        }

        return Move(
            GameSessionState.Failed,
            previous,
            _snapshot.ActiveLaunch,
            _snapshot.ClientHandoffAccepted,
            GameSessionFailureCodes.CorrelationLost);
    }

    private GameSessionTransitionResult ApplyStartTimeout(
        GameSessionState previous,
        GameSessionTransitionRequest request)
    {
        var identityFailure = RequireActiveLaunch(request, previous);
        if (identityFailure is not null)
            return identityFailure;

        if (_snapshot.State == GameSessionState.Failed
            && string.Equals(_snapshot.FailureCode, GameSessionFailureCodes.StartTimeout, StringComparison.Ordinal))
        {
            return NoOp(GameSessionReasonCodes.IdempotentReplay, previous);
        }

        if (_snapshot.State is not (GameSessionState.ClientHandoff or GameSessionState.GameStarting))
            return Reject(GameSessionReasonCodes.InvalidTransition, previous);

        return Move(
            GameSessionState.Failed,
            previous,
            _snapshot.ActiveLaunch,
            _snapshot.ClientHandoffAccepted,
            GameSessionFailureCodes.StartTimeout);
    }

    private GameSessionTransitionResult ApplyLaunchFailed(
        GameSessionState previous,
        GameSessionTransitionRequest request)
    {
        var identityFailure = RequireActiveLaunch(request, previous);
        if (identityFailure is not null)
            return identityFailure;

        if (string.IsNullOrWhiteSpace(request.FailureCode))
            return Reject(GameSessionReasonCodes.FailureCodeRequired, previous);

        var failureCode = request.FailureCode.Trim();
        if (_snapshot.State == GameSessionState.Failed
            && string.Equals(_snapshot.FailureCode, failureCode, StringComparison.Ordinal))
        {
            return NoOp(GameSessionReasonCodes.IdempotentReplay, previous);
        }

        if (_snapshot.State is not (GameSessionState.Preparing
            or GameSessionState.ClientHandoff
            or GameSessionState.GameStarting))
        {
            return Reject(GameSessionReasonCodes.InvalidTransition, previous);
        }

        return Move(
            GameSessionState.Failed,
            previous,
            _snapshot.ActiveLaunch,
            _snapshot.ClientHandoffAccepted,
            failureCode);
    }

    private GameSessionTransitionResult ApplyBeginLauncherReturn(
        GameSessionState previous,
        GameSessionTransitionRequest request)
    {
        var identityFailure = RequireActiveLaunch(request, previous);
        if (identityFailure is not null)
            return identityFailure;

        if (_snapshot.State == GameSessionState.ReturningToLauncher)
            return NoOp(GameSessionReasonCodes.IdempotentReplay, previous);

        if (_snapshot.State is not (GameSessionState.GameExitDetected or GameSessionState.Failed))
            return Reject(GameSessionReasonCodes.InvalidTransition, previous);

        return Move(
            GameSessionState.ReturningToLauncher,
            previous,
            _snapshot.ActiveLaunch,
            _snapshot.ClientHandoffAccepted,
            _snapshot.FailureCode);
    }

    private GameSessionTransitionResult ApplyLauncherRestored(
        GameSessionState previous,
        GameSessionTransitionRequest request)
    {
        if (_snapshot.State == GameSessionState.Launcher && request.LaunchIdentity is null)
            return NoOp(GameSessionReasonCodes.IdempotentReplay, previous);

        var identityFailure = RequireActiveLaunch(request, previous);
        if (identityFailure is not null)
            return identityFailure;

        if (_snapshot.State != GameSessionState.ReturningToLauncher)
            return Reject(GameSessionReasonCodes.InvalidTransition, previous);

        return Move(
            GameSessionState.Launcher,
            previous,
            activeLaunch: null,
            handoffAccepted: false,
            failureCode: null);
    }

    private GameSessionTransitionResult? RequireActiveLaunch(
        GameSessionTransitionRequest request,
        GameSessionState previous)
    {
        if (_snapshot.ActiveLaunch is null)
            return Reject(GameSessionReasonCodes.NoActiveLaunch, previous);

        if (!TryNormalizeLaunch(request.LaunchIdentity, out var launch))
            return Reject(GameSessionReasonCodes.LaunchIdentityRequired, previous);

        if (_snapshot.ActiveLaunch != launch)
            return Reject(GameSessionReasonCodes.ActiveLaunchMismatch, previous);

        return null;
    }

    private static bool TryNormalizeLaunch(
        GameSessionLaunchIdentity? candidate,
        out GameSessionLaunchIdentity? launch)
    {
        launch = null;
        if (candidate is null)
            return false;

        try
        {
            launch = candidate.Normalize();
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private GameSessionTransitionResult Move(
        GameSessionState target,
        GameSessionState previous,
        GameSessionLaunchIdentity? activeLaunch,
        bool handoffAccepted,
        string? failureCode)
    {
        _snapshot = new GameSessionSnapshot(
            target,
            activeLaunch,
            handoffAccepted,
            failureCode,
            checked(_snapshot.Revision + 1));

        return Applied(GameSessionReasonCodes.TransitionApplied, previous);
    }

    private GameSessionTransitionResult Applied(string reasonCode, GameSessionState previous)
        => new(
            GameSessionTransitionDisposition.Applied,
            reasonCode,
            previous,
            _snapshot);

    private GameSessionTransitionResult NoOp(string reasonCode, GameSessionState previous)
        => new(
            GameSessionTransitionDisposition.NoOp,
            reasonCode,
            previous,
            _snapshot);

    private GameSessionTransitionResult Reject(string reasonCode, GameSessionState previous)
        => new(
            GameSessionTransitionDisposition.Rejected,
            reasonCode,
            previous,
            _snapshot);
}
