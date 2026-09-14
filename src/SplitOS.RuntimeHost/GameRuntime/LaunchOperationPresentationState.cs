using SplitOS.Contracts.Protocol;

namespace SplitOS.RuntimeHost.GameRuntime;

public enum LaunchPresentationPhase
{
    Requested,
    ResolvingProfile,
    PreparingWindowsContext,
    PreparingGameConfiguration,
    ClientHandoff,
    WaitingForGame,
    GameRunningConfirmed
}

public enum LaunchPresentationFailureClass
{
    ProfileUnavailable,
    GameNotInstalled,
    ClientNotAvailable,
    AuthRequired,
    ClientUpdateRequired,
    GameConfigApplyFailed,
    WindowsContextFailed,
    HandoffFailed,
    GameStartNotConfirmed,
    CompatibilityBlocked,
    Cancelled,
    UnknownFailure
}

public enum LaunchPresentationExternalClientOutcome
{
    AuthRequired,
    UpdateRequired,
    ClientUiRequired,
    LicenseOrPlatformActionRequired
}

public enum LaunchPresentationAllowedAction
{
    Retry,
    Cancel,
    OpenClient,
    KeepWaiting,
    ReconnectDevice,
    ChooseAnotherProfile,
    EditProfile
}

public enum LaunchPresentationUpdateDisposition
{
    Applied,
    NoOp,
    Rejected
}

public static class LaunchPresentationReasonCodes
{
    public const string Applied = "LAUNCH_PRESENTATION_APPLIED";
    public const string Idempotent = "LAUNCH_PRESENTATION_IDEMPOTENT";
    public const string NoActiveLaunch = "LAUNCH_PRESENTATION_NO_ACTIVE_LAUNCH";
    public const string ActiveLaunchMismatch = "LAUNCH_PRESENTATION_ACTIVE_LAUNCH_MISMATCH";
    public const string SessionStateMismatch = "LAUNCH_PRESENTATION_SESSION_STATE_MISMATCH";
    public const string FailureClassMismatch = "LAUNCH_PRESENTATION_FAILURE_CLASS_MISMATCH";
    public const string InvalidIdentity = "LAUNCH_PRESENTATION_INVALID_IDENTITY";
    public const string Cleared = "LAUNCH_PRESENTATION_CLEARED";
}

public sealed record LaunchPresentationUpdateDecision(
    LaunchPresentationUpdateDisposition Disposition,
    string ReasonCode,
    long Revision);

/// <summary>
/// Runtime-owned launch-operation presentation metadata.
///
/// GameSession remains canonical for whether a launch exists and whether the game is running.
/// This owner can only enrich that canonical truth with Runtime-confirmed micro-phases,
/// external-client outcomes, normalized failures, and the exact actions Runtime currently permits.
/// Every override is fenced to the active launch identity and the GameSession revision on which it
/// was issued so stale UI actions disappear automatically when canonical session truth advances.
/// </summary>
public sealed class LaunchOperationPresentationState(GameSessionStateMachine gameSession)
{
    private readonly object _gate = new();
    private OverrideState? _override;
    private long _revision;

    public long Revision
    {
        get
        {
            lock (_gate)
                return _revision;
        }
    }

    public LaunchPresentationUpdateDecision ReportProgress(
        GameSessionLaunchIdentity identity,
        LaunchPresentationPhase phase,
        IReadOnlyCollection<LaunchPresentationAllowedAction>? allowedActions = null)
    {
        if (!TryNormalizeIdentity(identity, out var normalized))
            return Decision(LaunchPresentationUpdateDisposition.Rejected, LaunchPresentationReasonCodes.InvalidIdentity);

        var session = gameSession.Snapshot;
        var identityFailure = ValidateActiveIdentity(session, normalized!);
        if (identityFailure is not null)
            return identityFailure;
        if (!PhaseMatchesSession(phase, session))
            return Decision(LaunchPresentationUpdateDisposition.Rejected, LaunchPresentationReasonCodes.SessionStateMismatch);

        var next = new OverrideState(
            normalized!,
            session.Revision,
            Phase: phase,
            FailureClass: null,
            ExternalClientOutcome: null,
            AllowedActions: NormalizeActions(allowedActions),
            ObservedAtUtc: DateTimeOffset.UtcNow);
        return ApplyOverride(next);
    }

    public LaunchPresentationUpdateDecision ReportExternalClientOutcome(
        GameSessionLaunchIdentity identity,
        LaunchPresentationExternalClientOutcome outcome,
        IReadOnlyCollection<LaunchPresentationAllowedAction>? allowedActions = null)
    {
        if (!TryNormalizeIdentity(identity, out var normalized))
            return Decision(LaunchPresentationUpdateDisposition.Rejected, LaunchPresentationReasonCodes.InvalidIdentity);

        var session = gameSession.Snapshot;
        var identityFailure = ValidateActiveIdentity(session, normalized!);
        if (identityFailure is not null)
            return identityFailure;
        if (session.State is not (GameSessionState.Preparing or GameSessionState.ClientHandoff or GameSessionState.GameStarting))
            return Decision(LaunchPresentationUpdateDisposition.Rejected, LaunchPresentationReasonCodes.SessionStateMismatch);

        var next = new OverrideState(
            normalized!,
            session.Revision,
            Phase: null,
            FailureClass: null,
            ExternalClientOutcome: outcome,
            AllowedActions: NormalizeActions(allowedActions),
            ObservedAtUtc: DateTimeOffset.UtcNow);
        return ApplyOverride(next);
    }

    public LaunchPresentationUpdateDecision ReportFailure(
        GameSessionLaunchIdentity identity,
        LaunchPresentationFailureClass failureClass,
        IReadOnlyCollection<LaunchPresentationAllowedAction>? allowedActions = null)
    {
        if (!TryNormalizeIdentity(identity, out var normalized))
            return Decision(LaunchPresentationUpdateDisposition.Rejected, LaunchPresentationReasonCodes.InvalidIdentity);

        var session = gameSession.Snapshot;
        var identityFailure = ValidateActiveIdentity(session, normalized!);
        if (identityFailure is not null)
            return identityFailure;
        if (session.State != GameSessionState.Failed)
            return Decision(LaunchPresentationUpdateDisposition.Rejected, LaunchPresentationReasonCodes.SessionStateMismatch);

        var canonicalFailure = MapFailureCode(session.FailureCode);
        if (canonicalFailure != failureClass)
            return Decision(LaunchPresentationUpdateDisposition.Rejected, LaunchPresentationReasonCodes.FailureClassMismatch);

        var next = new OverrideState(
            normalized!,
            session.Revision,
            Phase: null,
            FailureClass: failureClass,
            ExternalClientOutcome: null,
            AllowedActions: NormalizeActions(allowedActions),
            ObservedAtUtc: DateTimeOffset.UtcNow);
        return ApplyOverride(next);
    }

    public LaunchPresentationUpdateDecision Clear(GameSessionLaunchIdentity identity)
    {
        if (!TryNormalizeIdentity(identity, out var normalized))
            return Decision(LaunchPresentationUpdateDisposition.Rejected, LaunchPresentationReasonCodes.InvalidIdentity);

        lock (_gate)
        {
            if (_override is null)
                return DecisionUnsafe(LaunchPresentationUpdateDisposition.NoOp, LaunchPresentationReasonCodes.Idempotent);
            if (_override.Identity != normalized)
                return DecisionUnsafe(LaunchPresentationUpdateDisposition.Rejected, LaunchPresentationReasonCodes.ActiveLaunchMismatch);

            _override = null;
            _revision = checked(_revision + 1);
            return DecisionUnsafe(LaunchPresentationUpdateDisposition.Applied, LaunchPresentationReasonCodes.Cleared);
        }
    }

    public LauncherLaunchPresentationResult? Project(
        GameSessionSnapshot session,
        bool isGameModeCommitted)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!isGameModeCommitted || session.ActiveLaunch is null)
            return null;
        if (session.State is GameSessionState.Inactive
            or GameSessionState.Launcher
            or GameSessionState.GameExitDetected
            or GameSessionState.ReturningToLauncher)
        {
            return null;
        }

        OverrideState? current;
        long ownerRevision;
        lock (_gate)
        {
            current = _override;
            ownerRevision = _revision;
        }

        var active = session.ActiveLaunch;
        var overrideCurrent = current is not null
            && current.Identity == active
            && current.GameSessionRevision == session.Revision
            ? current
            : null;

        var canonicalPhase = CanonicalPhase(session);
        var phase = canonicalPhase;
        if (session.State == GameSessionState.Preparing
            && overrideCurrent?.Phase is LaunchPresentationPhase overridePhase
            && PhaseMatchesSession(overridePhase, session))
        {
            phase = overridePhase;
        }

        var failure = session.State == GameSessionState.Failed
            ? MapFailureCode(session.FailureCode)
            : null;
        if (failure is not null && overrideCurrent?.FailureClass is LaunchPresentationFailureClass overrideFailure)
            failure = overrideFailure;

        var externalOutcome = session.State == GameSessionState.Failed
            ? null
            : overrideCurrent?.ExternalClientOutcome;
        var allowedActions = session.State == GameSessionState.GameRunning
            ? Array.Empty<string>()
            : (overrideCurrent?.AllowedActions ?? Array.Empty<LaunchPresentationAllowedAction>())
                .Select(ToWireAction)
                .ToArray();

        return new LauncherLaunchPresentationResult(
            active.LaunchOperationId,
            active.CorrelationId,
            active.GameId,
            phase is null ? null : ToWirePhase(phase.Value),
            failure is null ? null : ToWireFailure(failure.Value),
            externalOutcome is null ? null : ToWireExternalOutcome(externalOutcome.Value),
            allowedActions,
            ownerRevision,
            overrideCurrent?.ObservedAtUtc ?? DateTimeOffset.UtcNow);
    }

    private LaunchPresentationUpdateDecision ApplyOverride(OverrideState next)
    {
        lock (_gate)
        {
            if (_override is not null && SemanticEquals(_override, next))
                return DecisionUnsafe(LaunchPresentationUpdateDisposition.NoOp, LaunchPresentationReasonCodes.Idempotent);

            _override = next;
            _revision = checked(_revision + 1);
            return DecisionUnsafe(LaunchPresentationUpdateDisposition.Applied, LaunchPresentationReasonCodes.Applied);
        }
    }

    private LaunchPresentationUpdateDecision? ValidateActiveIdentity(
        GameSessionSnapshot session,
        GameSessionLaunchIdentity identity)
    {
        if (session.ActiveLaunch is null)
            return Decision(LaunchPresentationUpdateDisposition.Rejected, LaunchPresentationReasonCodes.NoActiveLaunch);
        if (session.ActiveLaunch != identity)
            return Decision(LaunchPresentationUpdateDisposition.Rejected, LaunchPresentationReasonCodes.ActiveLaunchMismatch);
        return null;
    }

    private LaunchPresentationUpdateDecision Decision(
        LaunchPresentationUpdateDisposition disposition,
        string reasonCode)
    {
        lock (_gate)
            return DecisionUnsafe(disposition, reasonCode);
    }

    private LaunchPresentationUpdateDecision DecisionUnsafe(
        LaunchPresentationUpdateDisposition disposition,
        string reasonCode)
        => new(disposition, reasonCode, _revision);

    private static bool TryNormalizeIdentity(
        GameSessionLaunchIdentity? identity,
        out GameSessionLaunchIdentity? normalized)
    {
        normalized = null;
        if (identity is null)
            return false;
        try
        {
            normalized = identity.Normalize();
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static IReadOnlyList<LaunchPresentationAllowedAction> NormalizeActions(
        IReadOnlyCollection<LaunchPresentationAllowedAction>? actions)
        => (actions ?? Array.Empty<LaunchPresentationAllowedAction>())
            .Distinct()
            .OrderBy(action => action)
            .ToArray();

    private static bool PhaseMatchesSession(LaunchPresentationPhase phase, GameSessionSnapshot session)
        => phase switch
        {
            LaunchPresentationPhase.Requested
                or LaunchPresentationPhase.ResolvingProfile
                or LaunchPresentationPhase.PreparingWindowsContext
                or LaunchPresentationPhase.PreparingGameConfiguration
                => session.State == GameSessionState.Preparing,
            LaunchPresentationPhase.ClientHandoff
                => session.State == GameSessionState.ClientHandoff && !session.ClientHandoffAccepted,
            LaunchPresentationPhase.WaitingForGame
                => (session.State == GameSessionState.ClientHandoff && session.ClientHandoffAccepted)
                    || session.State == GameSessionState.GameStarting,
            LaunchPresentationPhase.GameRunningConfirmed
                => session.State == GameSessionState.GameRunning,
            _ => false
        };

    private static LaunchPresentationPhase? CanonicalPhase(GameSessionSnapshot session)
        => session.State switch
        {
            GameSessionState.Preparing => LaunchPresentationPhase.Requested,
            GameSessionState.ClientHandoff when !session.ClientHandoffAccepted => LaunchPresentationPhase.ClientHandoff,
            GameSessionState.ClientHandoff => LaunchPresentationPhase.WaitingForGame,
            GameSessionState.GameStarting => LaunchPresentationPhase.WaitingForGame,
            GameSessionState.GameRunning => LaunchPresentationPhase.GameRunningConfirmed,
            _ => null
        };

    internal static LaunchPresentationFailureClass MapFailureCode(string? failureCode)
        => failureCode?.Trim().ToUpperInvariant() switch
        {
            "PROFILE_UNAVAILABLE" => LaunchPresentationFailureClass.ProfileUnavailable,
            "GAME_NOT_INSTALLED" => LaunchPresentationFailureClass.GameNotInstalled,
            "CLIENT_NOT_AVAILABLE" => LaunchPresentationFailureClass.ClientNotAvailable,
            "AUTH_REQUIRED" => LaunchPresentationFailureClass.AuthRequired,
            "CLIENT_UPDATE_REQUIRED" or "UPDATE_REQUIRED" => LaunchPresentationFailureClass.ClientUpdateRequired,
            "GAME_CONFIG_APPLY_FAILED" => LaunchPresentationFailureClass.GameConfigApplyFailed,
            "WINDOWS_CONTEXT_FAILED" => LaunchPresentationFailureClass.WindowsContextFailed,
            "HANDOFF_FAILED" => LaunchPresentationFailureClass.HandoffFailed,
            GameSessionFailureCodes.StartTimeout => LaunchPresentationFailureClass.GameStartNotConfirmed,
            "GAME_START_NOT_CONFIRMED" => LaunchPresentationFailureClass.GameStartNotConfirmed,
            "COMPATIBILITY_BLOCKED" => LaunchPresentationFailureClass.CompatibilityBlocked,
            "CANCELLED" => LaunchPresentationFailureClass.Cancelled,
            _ => LaunchPresentationFailureClass.UnknownFailure
        };

    internal static string ToWirePhase(LaunchPresentationPhase phase)
        => phase switch
        {
            LaunchPresentationPhase.Requested => "REQUESTED",
            LaunchPresentationPhase.ResolvingProfile => "RESOLVING_PROFILE",
            LaunchPresentationPhase.PreparingWindowsContext => "PREPARING_WINDOWS_CONTEXT",
            LaunchPresentationPhase.PreparingGameConfiguration => "PREPARING_GAME_CONFIGURATION",
            LaunchPresentationPhase.ClientHandoff => "CLIENT_HANDOFF",
            LaunchPresentationPhase.WaitingForGame => "WAITING_FOR_GAME",
            LaunchPresentationPhase.GameRunningConfirmed => "GAME_RUNNING_CONFIRMED",
            _ => throw new InvalidDataException($"Unsupported launch presentation phase {phase}.")
        };

    internal static string ToWireFailure(LaunchPresentationFailureClass failure)
        => failure switch
        {
            LaunchPresentationFailureClass.ProfileUnavailable => "PROFILE_UNAVAILABLE",
            LaunchPresentationFailureClass.GameNotInstalled => "GAME_NOT_INSTALLED",
            LaunchPresentationFailureClass.ClientNotAvailable => "CLIENT_NOT_AVAILABLE",
            LaunchPresentationFailureClass.AuthRequired => "AUTH_REQUIRED",
            LaunchPresentationFailureClass.ClientUpdateRequired => "CLIENT_UPDATE_REQUIRED",
            LaunchPresentationFailureClass.GameConfigApplyFailed => "GAME_CONFIG_APPLY_FAILED",
            LaunchPresentationFailureClass.WindowsContextFailed => "WINDOWS_CONTEXT_FAILED",
            LaunchPresentationFailureClass.HandoffFailed => "HANDOFF_FAILED",
            LaunchPresentationFailureClass.GameStartNotConfirmed => "GAME_START_NOT_CONFIRMED",
            LaunchPresentationFailureClass.CompatibilityBlocked => "COMPATIBILITY_BLOCKED",
            LaunchPresentationFailureClass.Cancelled => "CANCELLED",
            LaunchPresentationFailureClass.UnknownFailure => "UNKNOWN_FAILURE",
            _ => throw new InvalidDataException($"Unsupported launch failure class {failure}.")
        };

    internal static string ToWireExternalOutcome(LaunchPresentationExternalClientOutcome outcome)
        => outcome switch
        {
            LaunchPresentationExternalClientOutcome.AuthRequired => "AUTH_REQUIRED",
            LaunchPresentationExternalClientOutcome.UpdateRequired => "UPDATE_REQUIRED",
            LaunchPresentationExternalClientOutcome.ClientUiRequired => "CLIENT_UI_REQUIRED",
            LaunchPresentationExternalClientOutcome.LicenseOrPlatformActionRequired => "LICENSE_OR_PLATFORM_ACTION_REQUIRED",
            _ => throw new InvalidDataException($"Unsupported external client outcome {outcome}.")
        };

    internal static string ToWireAction(LaunchPresentationAllowedAction action)
        => action switch
        {
            LaunchPresentationAllowedAction.Retry => "RETRY",
            LaunchPresentationAllowedAction.Cancel => "CANCEL",
            LaunchPresentationAllowedAction.OpenClient => "OPEN_CLIENT",
            LaunchPresentationAllowedAction.KeepWaiting => "KEEP_WAITING",
            LaunchPresentationAllowedAction.ReconnectDevice => "RECONNECT_DEVICE",
            LaunchPresentationAllowedAction.ChooseAnotherProfile => "CHOOSE_ANOTHER_PROFILE",
            LaunchPresentationAllowedAction.EditProfile => "EDIT_PROFILE",
            _ => throw new InvalidDataException($"Unsupported launch action {action}.")
        };

    private static bool SemanticEquals(OverrideState left, OverrideState right)
        => left.Identity == right.Identity
            && left.GameSessionRevision == right.GameSessionRevision
            && left.Phase == right.Phase
            && left.FailureClass == right.FailureClass
            && left.ExternalClientOutcome == right.ExternalClientOutcome
            && left.AllowedActions.SequenceEqual(right.AllowedActions);

    private sealed record OverrideState(
        GameSessionLaunchIdentity Identity,
        long GameSessionRevision,
        LaunchPresentationPhase? Phase,
        LaunchPresentationFailureClass? FailureClass,
        LaunchPresentationExternalClientOutcome? ExternalClientOutcome,
        IReadOnlyList<LaunchPresentationAllowedAction> AllowedActions,
        DateTimeOffset ObservedAtUtc);
}
