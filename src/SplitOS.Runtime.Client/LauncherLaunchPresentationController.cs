using SplitOS.Contracts.Protocol;

namespace SplitOS.Runtime.Client;

public enum LauncherLaunchPresentationMode
{
    Hidden,
    Progress,
    ExternalActionRequired,
    Failure,
    RunningConfirmed
}

public enum LauncherLaunchAction
{
    Retry,
    Cancel,
    OpenClient,
    KeepWaiting,
    ReconnectDevice,
    ChooseAnotherProfile,
    EditProfile
}

public enum LauncherLaunchPresentationDisposition
{
    Applied,
    NoOp,
    Rejected
}

public static class LauncherLaunchPresentationReasonCodes
{
    public const string Applied = "LAUNCH_UX_APPLIED";
    public const string Idempotent = "LAUNCH_UX_IDEMPOTENT";
    public const string InvalidProjection = "LAUNCH_UX_INVALID_PROJECTION";
    public const string IdentityMismatch = "LAUNCH_UX_IDENTITY_MISMATCH";
    public const string SessionContradiction = "LAUNCH_UX_SESSION_CONTRADICTION";
}

public sealed record LauncherLaunchPresentationView(
    LauncherLaunchPresentationMode Mode,
    string? LaunchOperationId,
    string? CorrelationId,
    string? GameId,
    string? Phase,
    string? FailureClass,
    string? ExternalClientOutcome,
    string Title,
    string Message,
    string? TechnicalReference,
    IReadOnlyList<LauncherLaunchAction> AllowedActions,
    long RuntimePresentationRevision)
{
    public static LauncherLaunchPresentationView Hidden { get; } = new(
        LauncherLaunchPresentationMode.Hidden,
        null,
        null,
        null,
        null,
        null,
        null,
        string.Empty,
        string.Empty,
        null,
        Array.Empty<LauncherLaunchAction>(),
        0);

    public bool Allows(LauncherLaunchAction action)
        => AllowedActions.Contains(action);
}

public sealed record LauncherLaunchPresentationDecision(
    LauncherLaunchPresentationDisposition Disposition,
    string ReasonCode,
    LauncherLaunchPresentationView View);

/// <summary>
/// Launcher-side projection of Runtime-owned launch UX truth. This controller formats typed Runtime
/// outcomes for presentation but never infers authority: action buttons come only from AllowedActions
/// supplied by the coherent Runtime snapshot.
/// </summary>
public sealed class LauncherLaunchPresentationController
{
    private LauncherLaunchPresentationView _view = LauncherLaunchPresentationView.Hidden;

    public LauncherLaunchPresentationView View => _view;

    public LauncherLaunchPresentationDecision Observe(LauncherRuntimeSnapshotResult snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.LaunchPresentation is null)
            return Apply(LauncherLaunchPresentationView.Hidden);

        var projection = snapshot.LaunchPresentation;
        if (!ValidateIdentity(snapshot, projection))
            return Reject(LauncherLaunchPresentationReasonCodes.IdentityMismatch);
        if (projection.RuntimePresentationRevision < 0
            || projection.AllowedActions is null
            || string.IsNullOrWhiteSpace(projection.LaunchOperationId)
            || string.IsNullOrWhiteSpace(projection.CorrelationId)
            || string.IsNullOrWhiteSpace(projection.GameId))
        {
            return Reject(LauncherLaunchPresentationReasonCodes.InvalidProjection);
        }

        if (!TryParseActions(projection.AllowedActions, out var actions))
            return Reject(LauncherLaunchPresentationReasonCodes.InvalidProjection);

        if (!TryBuildView(snapshot.GameSessionState, projection, actions!, out var next))
            return Reject(LauncherLaunchPresentationReasonCodes.SessionContradiction);

        return Apply(next!);
    }

    private LauncherLaunchPresentationDecision Apply(LauncherLaunchPresentationView next)
    {
        if (SemanticallyEqual(_view, next))
        {
            return new LauncherLaunchPresentationDecision(
                LauncherLaunchPresentationDisposition.NoOp,
                LauncherLaunchPresentationReasonCodes.Idempotent,
                _view);
        }

        _view = next;
        return new LauncherLaunchPresentationDecision(
            LauncherLaunchPresentationDisposition.Applied,
            LauncherLaunchPresentationReasonCodes.Applied,
            _view);
    }

    private LauncherLaunchPresentationDecision Reject(string reasonCode)
        => new(
            LauncherLaunchPresentationDisposition.Rejected,
            reasonCode,
            _view);

    private static bool ValidateIdentity(
        LauncherRuntimeSnapshotResult snapshot,
        LauncherLaunchPresentationResult projection)
        => string.Equals(snapshot.ActiveLaunchOperationId, projection.LaunchOperationId, StringComparison.Ordinal)
            && string.Equals(snapshot.ActiveLaunchCorrelationId, projection.CorrelationId, StringComparison.Ordinal)
            && string.Equals(snapshot.ActiveGameId, projection.GameId, StringComparison.Ordinal);

    private static bool TryBuildView(
        string gameSessionState,
        LauncherLaunchPresentationResult projection,
        IReadOnlyList<LauncherLaunchAction> actions,
        out LauncherLaunchPresentationView? view)
    {
        view = null;

        if (projection.FailureClass is not null)
        {
            if (!string.Equals(gameSessionState, "FAILED", StringComparison.Ordinal)
                || !TryFailureCopy(projection.FailureClass, out var title, out var message))
                return false;

            view = Build(
                LauncherLaunchPresentationMode.Failure,
                projection,
                title!,
                message!,
                actions);
            return true;
        }

        if (projection.ExternalClientOutcome is not null)
        {
            if (gameSessionState is not ("PREPARING" or "CLIENT_HANDOFF" or "GAME_STARTING")
                || !TryExternalOutcomeCopy(projection.ExternalClientOutcome, out var title, out var message))
                return false;

            view = Build(
                LauncherLaunchPresentationMode.ExternalActionRequired,
                projection,
                title!,
                message!,
                actions);
            return true;
        }

        if (projection.Phase is null
            || !TryPhaseCopy(projection.Phase, out var expectedSessionStates, out var title, out var message))
        {
            return false;
        }

        if (!expectedSessionStates.Contains(gameSessionState, StringComparer.Ordinal))
            return false;

        var mode = string.Equals(projection.Phase, "GAME_RUNNING_CONFIRMED", StringComparison.Ordinal)
            ? LauncherLaunchPresentationMode.RunningConfirmed
            : LauncherLaunchPresentationMode.Progress;
        view = Build(mode, projection, title!, message!, actions);
        return true;
    }

    private static LauncherLaunchPresentationView Build(
        LauncherLaunchPresentationMode mode,
        LauncherLaunchPresentationResult projection,
        string title,
        string message,
        IReadOnlyList<LauncherLaunchAction> actions)
        => new(
            mode,
            projection.LaunchOperationId,
            projection.CorrelationId,
            projection.GameId,
            projection.Phase,
            projection.FailureClass,
            projection.ExternalClientOutcome,
            title,
            message,
            $"Correlation: {projection.CorrelationId}",
            actions.ToArray(),
            projection.RuntimePresentationRevision);

    private static bool TryParseActions(
        IReadOnlyList<string> values,
        out IReadOnlyList<LauncherLaunchAction>? actions)
    {
        var parsed = new List<LauncherLaunchAction>(values.Count);
        foreach (var value in values)
        {
            var action = value switch
            {
                "RETRY" => LauncherLaunchAction.Retry,
                "CANCEL" => LauncherLaunchAction.Cancel,
                "OPEN_CLIENT" => LauncherLaunchAction.OpenClient,
                "KEEP_WAITING" => LauncherLaunchAction.KeepWaiting,
                "RECONNECT_DEVICE" => LauncherLaunchAction.ReconnectDevice,
                "CHOOSE_ANOTHER_PROFILE" => LauncherLaunchAction.ChooseAnotherProfile,
                "EDIT_PROFILE" => LauncherLaunchAction.EditProfile,
                _ => (LauncherLaunchAction?)null
            };
            if (action is null)
            {
                actions = null;
                return false;
            }
            if (!parsed.Contains(action.Value))
                parsed.Add(action.Value);
        }

        actions = parsed.OrderBy(action => action).ToArray();
        return true;
    }

    private static bool TryPhaseCopy(
        string phase,
        out IReadOnlyList<string> expectedSessionStates,
        out string? title,
        out string? message)
    {
        (expectedSessionStates, title, message) = phase switch
        {
            "REQUESTED" => (new[] { "PREPARING" }, "Starting game…", "Runtime accepted the managed launch request."),
            "RESOLVING_PROFILE" => (new[] { "PREPARING" }, "Resolving profile…", "Runtime is resolving the launch profile and current device context."),
            "PREPARING_WINDOWS_CONTEXT" => (new[] { "PREPARING" }, "Preparing Windows context…", "Runtime is preparing the verified Windows state required for this launch."),
            "PREPARING_GAME_CONFIGURATION" => (new[] { "PREPARING" }, "Preparing game configuration…", "Runtime is applying the managed game configuration."),
            "CLIENT_HANDOFF" => (new[] { "CLIENT_HANDOFF" }, "Opening game client…", "Runtime handed the launch request to the external game client."),
            "WAITING_FOR_GAME" => (new[] { "CLIENT_HANDOFF", "GAME_STARTING" }, "Waiting for game…", "Client handoff is not game-running confirmation. Runtime is still waiting for correlated game evidence."),
            "GAME_RUNNING_CONFIRMED" => (new[] { "GAME_RUNNING" }, "Game running", "Runtime confirmed the managed game session is running."),
            _ => (Array.Empty<string>(), null, null)
        };
        return title is not null;
    }

    private static bool TryFailureCopy(string failureClass, out string? title, out string? message)
    {
        (title, message) = failureClass switch
        {
            "PROFILE_UNAVAILABLE" => ("Profile unavailable", "The selected profile cannot currently resolve its required launch context."),
            "GAME_NOT_INSTALLED" => ("Game not installed", "Runtime has verified evidence that the game is not currently installed."),
            "CLIENT_NOT_AVAILABLE" => ("Game client unavailable", "The external game client required for this launch is not currently available."),
            "AUTH_REQUIRED" => ("Sign-in required", "The external game client requires sign-in before launch can continue."),
            "CLIENT_UPDATE_REQUIRED" => ("Game client update required", "The external game client requires an update before launch can continue."),
            "GAME_CONFIG_APPLY_FAILED" => ("Game configuration failed", "Runtime could not apply the managed game configuration."),
            "WINDOWS_CONTEXT_FAILED" => ("Windows preparation failed", "Runtime could not establish the required Windows launch context."),
            "HANDOFF_FAILED" => ("Game client handoff failed", "Runtime could not complete the managed handoff to the external game client."),
            "GAME_START_NOT_CONFIRMED" => ("Game start not confirmed", "Client handoff completed, but Runtime did not confirm the game within the policy window."),
            "COMPATIBILITY_BLOCKED" => ("Launch blocked", "Runtime detected a compatibility condition that blocks the managed launch."),
            "CANCELLED" => ("Launch cancelled", "The managed launch flow was cancelled."),
            "UNKNOWN_FAILURE" => ("Launch failed", "Runtime reported a launch failure that does not have a more specific presentation class."),
            _ => (null, null)
        };
        return title is not null;
    }

    private static bool TryExternalOutcomeCopy(string outcome, out string? title, out string? message)
    {
        (title, message) = outcome switch
        {
            "AUTH_REQUIRED" => ("Sign-in required in game client", "Complete sign-in in the external client, then use only the actions Runtime currently permits."),
            "UPDATE_REQUIRED" => ("Game client update required", "Complete the client update outside SplitOS before continuing the managed launch."),
            "CLIENT_UI_REQUIRED" => ("Game client action required", "The external client requires user interaction before the managed launch can continue."),
            "LICENSE_OR_PLATFORM_ACTION_REQUIRED" => ("Platform action required", "The external platform requires a license or account action before launch can continue."),
            _ => (null, null)
        };
        return title is not null;
    }

    private static bool SemanticallyEqual(
        LauncherLaunchPresentationView left,
        LauncherLaunchPresentationView right)
        => left.Mode == right.Mode
            && string.Equals(left.LaunchOperationId, right.LaunchOperationId, StringComparison.Ordinal)
            && string.Equals(left.CorrelationId, right.CorrelationId, StringComparison.Ordinal)
            && string.Equals(left.GameId, right.GameId, StringComparison.Ordinal)
            && string.Equals(left.Phase, right.Phase, StringComparison.Ordinal)
            && string.Equals(left.FailureClass, right.FailureClass, StringComparison.Ordinal)
            && string.Equals(left.ExternalClientOutcome, right.ExternalClientOutcome, StringComparison.Ordinal)
            && left.AllowedActions.SequenceEqual(right.AllowedActions)
            && left.RuntimePresentationRevision == right.RuntimePresentationRevision;
}
