namespace SplitOS.Runtime.Client;

/// <summary>
/// Client-side mirror of the Runtime-owned GameSession state used only for presentation projection.
/// The Launcher does not own or infer this state; the value must come from a coherent Runtime snapshot
/// or typed Runtime event.
/// </summary>
public enum LauncherRuntimeGameSessionState
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

public enum LauncherRuntimeUpdateKind
{
    Snapshot,
    IncrementalEvent
}

public enum LauncherPresentationState
{
    Inactive,
    Active,
    BackgroundGameRunning,
    Restoring
}

public enum LauncherWindowIntent
{
    None,
    KeepForeground,
    YieldForeground,
    RestoreForeground
}

public enum LauncherNavigationIntent
{
    Enabled,
    Suppressed
}

public enum LauncherPresentationDisposition
{
    Applied,
    NoOp,
    FreshSnapshotRequired,
    Rejected
}

public static class LauncherPresentationReasonCodes
{
    public const string ProjectionApplied = "PROJECTION_APPLIED";
    public const string IdempotentProjection = "IDEMPOTENT_PROJECTION";
    public const string StaleProjectionIgnored = "STALE_PROJECTION_IGNORED";
    public const string EventBaselineRequired = "EVENT_BASELINE_REQUIRED";
    public const string EventRevisionGap = "EVENT_REVISION_GAP";
    public const string ConflictingSameRevision = "CONFLICTING_SAME_REVISION";
    public const string RuntimeProjectionInconsistent = "RUNTIME_PROJECTION_INCONSISTENT";
    public const string BookmarkCaptured = "BOOKMARK_CAPTURED";
    public const string BookmarkCaptureNotAllowed = "BOOKMARK_CAPTURE_NOT_ALLOWED";
}

public sealed record LauncherGameSessionProjection(
    long Revision,
    LauncherRuntimeGameSessionState SessionState,
    bool IsGameModeCommitted,
    string? LaunchOperationId = null)
{
    public LauncherGameSessionProjection Normalize()
    {
        if (Revision < 0)
            throw new InvalidDataException("Launcher Runtime projection revision cannot be negative.");

        var operationId = string.IsNullOrWhiteSpace(LaunchOperationId)
            ? null
            : LaunchOperationId.Trim();

        if (SessionState != LauncherRuntimeGameSessionState.Inactive && !IsGameModeCommitted)
        {
            throw new InvalidDataException(
                "A non-INACTIVE GameSession projection requires committed GAME mode.");
        }

        return this with { LaunchOperationId = operationId };
    }
}

public sealed record LauncherPresentationBookmark(
    string Route,
    string? FocusKey = null)
{
    public LauncherPresentationBookmark Normalize()
    {
        if (string.IsNullOrWhiteSpace(Route))
            throw new InvalidDataException("Launcher presentation bookmark route cannot be empty.");

        return new LauncherPresentationBookmark(
            Route.Trim(),
            string.IsNullOrWhiteSpace(FocusKey) ? null : FocusKey.Trim());
    }
}

public sealed record LauncherPresentationDecision(
    LauncherPresentationDisposition Disposition,
    string ReasonCode,
    LauncherPresentationState PresentationState,
    LauncherWindowIntent WindowIntent,
    LauncherNavigationIntent NavigationIntent,
    LauncherPresentationBookmark? BookmarkToRestore,
    long LastRuntimeRevision,
    bool HasPendingBookmark);

/// <summary>
/// Projects Runtime-owned GameSession truth into Launcher-only window/input behavior.
///
/// This controller intentionally has no process, client, PID, or foreground-window heuristics. A game
/// becomes background-worthy only when Runtime says GameSession=GAME_RUNNING. Likewise restoration is
/// driven only by Runtime GameSession transitions. Revision continuity is enforced for incremental
/// events so presentation never knowingly advances from a missed authoritative state change.
/// </summary>
public sealed class LauncherPresentationController
{
    private LauncherGameSessionProjection? _lastProjection;
    private LauncherPresentationState _presentationState = LauncherPresentationState.Inactive;
    private LauncherPresentationBookmark? _pendingBookmark;

    public LauncherPresentationState PresentationState => _presentationState;
    public long LastRuntimeRevision => _lastProjection?.Revision ?? -1;
    public bool HasPendingBookmark => _pendingBookmark is not null;

    public LauncherPresentationDecision CaptureBookmark(LauncherPresentationBookmark bookmark)
    {
        ArgumentNullException.ThrowIfNull(bookmark);
        LauncherPresentationBookmark normalized;
        try
        {
            normalized = bookmark.Normalize();
        }
        catch (InvalidDataException)
        {
            return Decision(
                LauncherPresentationDisposition.Rejected,
                LauncherPresentationReasonCodes.BookmarkCaptureNotAllowed,
                LauncherWindowIntent.None,
                CurrentNavigationIntent(),
                bookmarkToRestore: null);
        }

        if (_presentationState != LauncherPresentationState.Active)
        {
            return Decision(
                LauncherPresentationDisposition.Rejected,
                LauncherPresentationReasonCodes.BookmarkCaptureNotAllowed,
                LauncherWindowIntent.None,
                CurrentNavigationIntent(),
                bookmarkToRestore: null);
        }

        _pendingBookmark = normalized;
        return Decision(
            LauncherPresentationDisposition.Applied,
            LauncherPresentationReasonCodes.BookmarkCaptured,
            LauncherWindowIntent.None,
            LauncherNavigationIntent.Enabled,
            bookmarkToRestore: null);
    }

    public LauncherPresentationDecision Observe(
        LauncherGameSessionProjection projection,
        LauncherRuntimeUpdateKind updateKind)
    {
        ArgumentNullException.ThrowIfNull(projection);

        LauncherGameSessionProjection normalized;
        try
        {
            normalized = projection.Normalize();
        }
        catch (InvalidDataException)
        {
            return Decision(
                LauncherPresentationDisposition.Rejected,
                LauncherPresentationReasonCodes.RuntimeProjectionInconsistent,
                LauncherWindowIntent.None,
                CurrentNavigationIntent(),
                bookmarkToRestore: null);
        }

        var revisionDecision = ValidateRevision(normalized, updateKind);
        if (revisionDecision is not null)
            return revisionDecision;

        var previousPresentation = _presentationState;
        var previousProjection = _lastProjection;
        _lastProjection = normalized;

        var target = MapPresentationState(normalized);
        _presentationState = target;

        var enteringBackground = target == LauncherPresentationState.BackgroundGameRunning
            && previousPresentation != LauncherPresentationState.BackgroundGameRunning;
        var enteringRestoring = target == LauncherPresentationState.Restoring
            && previousPresentation != LauncherPresentationState.Restoring;
        var enteringActive = target == LauncherPresentationState.Active
            && previousPresentation != LauncherPresentationState.Active;
        var leavingFailedForLauncher = normalized.SessionState == LauncherRuntimeGameSessionState.Launcher
            && previousProjection?.SessionState == LauncherRuntimeGameSessionState.Failed;

        LauncherWindowIntent windowIntent;
        LauncherPresentationBookmark? bookmarkToRestore = null;

        if (enteringBackground)
        {
            windowIntent = LauncherWindowIntent.YieldForeground;
        }
        else if (enteringRestoring)
        {
            // GAME_EXIT_DETECTED is Runtime-confirmed exit evidence. The Launcher may become visible,
            // but controller navigation stays suppressed until Runtime reaches canonical LAUNCHER.
            windowIntent = LauncherWindowIntent.RestoreForeground;
        }
        else if (enteringActive)
        {
            windowIntent = previousPresentation is LauncherPresentationState.Inactive
                or LauncherPresentationState.BackgroundGameRunning
                or LauncherPresentationState.Restoring
                ? LauncherWindowIntent.RestoreForeground
                : LauncherWindowIntent.KeepForeground;
        }
        else if (target == LauncherPresentationState.Inactive
            && previousPresentation != LauncherPresentationState.Inactive)
        {
            windowIntent = LauncherWindowIntent.YieldForeground;
        }
        else
        {
            windowIntent = LauncherWindowIntent.None;
        }

        // A bookmark is restored exactly once after the authoritative GameSession returns to LAUNCHER.
        // FAILED may surface its own error UX first, so the pending bookmark is intentionally preserved
        // until recovery/acknowledgement has completed and Runtime reports LAUNCHER.
        if (normalized.SessionState == LauncherRuntimeGameSessionState.Launcher
            && _pendingBookmark is not null
            && (previousPresentation is LauncherPresentationState.BackgroundGameRunning
                or LauncherPresentationState.Restoring
                || leavingFailedForLauncher))
        {
            bookmarkToRestore = _pendingBookmark;
            _pendingBookmark = null;
            windowIntent = LauncherWindowIntent.RestoreForeground;
        }

        var navigation = target is LauncherPresentationState.BackgroundGameRunning
            or LauncherPresentationState.Restoring
            or LauncherPresentationState.Inactive
            ? LauncherNavigationIntent.Suppressed
            : LauncherNavigationIntent.Enabled;

        return Decision(
            LauncherPresentationDisposition.Applied,
            LauncherPresentationReasonCodes.ProjectionApplied,
            windowIntent,
            navigation,
            bookmarkToRestore);
    }

    private LauncherPresentationDecision? ValidateRevision(
        LauncherGameSessionProjection projection,
        LauncherRuntimeUpdateKind updateKind)
    {
        if (_lastProjection is null)
        {
            if (updateKind == LauncherRuntimeUpdateKind.IncrementalEvent)
            {
                return Decision(
                    LauncherPresentationDisposition.FreshSnapshotRequired,
                    LauncherPresentationReasonCodes.EventBaselineRequired,
                    LauncherWindowIntent.None,
                    CurrentNavigationIntent(),
                    bookmarkToRestore: null);
            }

            return null;
        }

        if (projection.Revision < _lastProjection.Revision)
        {
            return Decision(
                LauncherPresentationDisposition.NoOp,
                LauncherPresentationReasonCodes.StaleProjectionIgnored,
                LauncherWindowIntent.None,
                CurrentNavigationIntent(),
                bookmarkToRestore: null);
        }

        if (projection.Revision == _lastProjection.Revision)
        {
            if (projection == _lastProjection)
            {
                return Decision(
                    LauncherPresentationDisposition.NoOp,
                    LauncherPresentationReasonCodes.IdempotentProjection,
                    LauncherWindowIntent.None,
                    CurrentNavigationIntent(),
                    bookmarkToRestore: null);
            }

            return Decision(
                LauncherPresentationDisposition.FreshSnapshotRequired,
                LauncherPresentationReasonCodes.ConflictingSameRevision,
                LauncherWindowIntent.None,
                CurrentNavigationIntent(),
                bookmarkToRestore: null);
        }

        if (updateKind == LauncherRuntimeUpdateKind.IncrementalEvent
            && projection.Revision != checked(_lastProjection.Revision + 1))
        {
            return Decision(
                LauncherPresentationDisposition.FreshSnapshotRequired,
                LauncherPresentationReasonCodes.EventRevisionGap,
                LauncherWindowIntent.None,
                CurrentNavigationIntent(),
                bookmarkToRestore: null);
        }

        return null;
    }

    private static LauncherPresentationState MapPresentationState(
        LauncherGameSessionProjection projection)
        => projection.SessionState switch
        {
            LauncherRuntimeGameSessionState.Inactive => LauncherPresentationState.Inactive,
            LauncherRuntimeGameSessionState.GameRunning => LauncherPresentationState.BackgroundGameRunning,
            LauncherRuntimeGameSessionState.GameExitDetected
                or LauncherRuntimeGameSessionState.ReturningToLauncher => LauncherPresentationState.Restoring,
            _ => LauncherPresentationState.Active
        };

    private LauncherNavigationIntent CurrentNavigationIntent()
        => _presentationState is LauncherPresentationState.BackgroundGameRunning
            or LauncherPresentationState.Restoring
            or LauncherPresentationState.Inactive
            ? LauncherNavigationIntent.Suppressed
            : LauncherNavigationIntent.Enabled;

    private LauncherPresentationDecision Decision(
        LauncherPresentationDisposition disposition,
        string reasonCode,
        LauncherWindowIntent windowIntent,
        LauncherNavigationIntent navigationIntent,
        LauncherPresentationBookmark? bookmarkToRestore)
        => new(
            disposition,
            reasonCode,
            _presentationState,
            windowIntent,
            navigationIntent,
            bookmarkToRestore,
            LastRuntimeRevision,
            HasPendingBookmark);
}
