namespace SplitOS.Runtime.Client;

public enum LauncherRouteKind
{
    Home,
    Library,
    GameDetails
}

public sealed record LauncherRoute(
    LauncherRouteKind Kind,
    string? GameId = null)
{
    public static LauncherRoute Home { get; } = new(LauncherRouteKind.Home);
    public static LauncherRoute Library { get; } = new(LauncherRouteKind.Library);

    public static LauncherRoute GameDetails(string gameId)
        => new(LauncherRouteKind.GameDetails, NormalizeRequired(gameId, nameof(gameId)));

    public LauncherRoute Normalize()
        => Kind switch
        {
            LauncherRouteKind.Home when GameId is null => Home,
            LauncherRouteKind.Library when GameId is null => Library,
            LauncherRouteKind.GameDetails => GameDetails(
                GameId ?? throw new InvalidDataException("GAME_DETAILS requires a game ID.")),
            _ => throw new InvalidDataException($"Route {Kind} carries invalid parameters.")
        };

    public string RouteKey => Kind switch
    {
        LauncherRouteKind.Home => "HOME",
        LauncherRouteKind.Library => "LIBRARY",
        LauncherRouteKind.GameDetails => $"GAME_DETAILS:{GameId}",
        _ => throw new InvalidOperationException($"Unsupported Launcher route {Kind}.")
    };

    private static string NormalizeRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A non-empty semantic identifier is required.", parameterName);
        return value.Trim();
    }
}

public enum LauncherNavigationDisposition
{
    Applied,
    NoOp,
    Rejected
}

public enum LauncherNavigationTransitionKind
{
    Initialized,
    RootChanged,
    NestedOpened,
    BackRestored,
    BookmarkRestored,
    FallbackApplied,
    NoOp,
    Rejected
}

public static class LauncherNavigationReasonCodes
{
    public const string Initialized = "NAVIGATION_INITIALIZED";
    public const string RootChanged = "ROOT_ROUTE_CHANGED";
    public const string NestedOpened = "NESTED_ROUTE_OPENED";
    public const string BackRestored = "BACK_RESTORED";
    public const string BookmarkRestored = "BOOKMARK_RESTORED";
    public const string RouteFallback = "ROUTE_FALLBACK";
    public const string RootBackNoOp = "ROOT_BACK_NO_OP";
    public const string SameRouteNoOp = "SAME_ROUTE_NO_OP";
    public const string InvalidRoute = "INVALID_ROUTE";
    public const string InvalidBookmark = "INVALID_BOOKMARK";
}

public sealed record LauncherNavigationSnapshot(
    LauncherRoute CurrentRoute,
    string? PreferredFocusKey,
    int NestedDepth,
    long Revision);

public sealed record LauncherNavigationDecision(
    LauncherNavigationDisposition Disposition,
    LauncherNavigationTransitionKind TransitionKind,
    string ReasonCode,
    LauncherNavigationSnapshot Snapshot);

/// <summary>
/// Presentation-only route owner for the v1 HOME/LIBRARY/GAME_DETAILS skeleton. Root switches do not
/// create history. Nested GAME_DETAILS transitions capture the exact source route and semantic focus
/// key, so BACK restores the meaningful parent target. No route state is treated as game/library truth.
/// </summary>
public sealed class LauncherNavigationController
{
    private readonly Stack<ReturnFrame> _returns = new();
    private LauncherRoute _currentRoute = LauncherRoute.Home;
    private string? _preferredFocusKey;
    private long _revision;
    private bool _initialized;

    public LauncherNavigationSnapshot Snapshot
        => new(_currentRoute, _preferredFocusKey, _returns.Count, _revision);

    public LauncherNavigationDecision Initialize(
        LauncherRoute? initialRoute = null,
        string? preferredFocusKey = null)
    {
        if (_initialized)
            return NoOp(LauncherNavigationReasonCodes.SameRouteNoOp);

        LauncherRoute route;
        try
        {
            route = (initialRoute ?? LauncherRoute.Home).Normalize();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
        {
            return Rejected(LauncherNavigationReasonCodes.InvalidRoute);
        }

        if (route.Kind == LauncherRouteKind.GameDetails)
            return Rejected(LauncherNavigationReasonCodes.InvalidRoute);

        _currentRoute = route;
        _preferredFocusKey = NormalizeOptional(preferredFocusKey);
        _returns.Clear();
        _initialized = true;
        _revision = checked(_revision + 1);
        return Applied(LauncherNavigationTransitionKind.Initialized, LauncherNavigationReasonCodes.Initialized);
    }

    public LauncherNavigationDecision GoHome(string? preferredFocusKey = null)
        => GoRoot(LauncherRoute.Home, preferredFocusKey);

    public LauncherNavigationDecision GoLibrary(string? preferredFocusKey = null)
        => GoRoot(LauncherRoute.Library, preferredFocusKey);

    public LauncherNavigationDecision OpenGameDetails(
        string gameId,
        string? returnFocusKey)
    {
        EnsureInitialized();

        LauncherRoute target;
        try
        {
            target = LauncherRoute.GameDetails(gameId);
        }
        catch (ArgumentException)
        {
            return Rejected(LauncherNavigationReasonCodes.InvalidRoute);
        }

        if (_currentRoute == target)
            return NoOp(LauncherNavigationReasonCodes.SameRouteNoOp);

        _returns.Push(new ReturnFrame(_currentRoute, NormalizeOptional(returnFocusKey)));
        _currentRoute = target;
        _preferredFocusKey = null;
        _revision = checked(_revision + 1);
        return Applied(LauncherNavigationTransitionKind.NestedOpened, LauncherNavigationReasonCodes.NestedOpened);
    }

    public LauncherNavigationDecision Back()
    {
        EnsureInitialized();
        if (_returns.Count == 0)
            return NoOp(LauncherNavigationReasonCodes.RootBackNoOp);

        var parent = _returns.Pop();
        _currentRoute = parent.Route;
        _preferredFocusKey = parent.FocusKey;
        _revision = checked(_revision + 1);
        return Applied(LauncherNavigationTransitionKind.BackRestored, LauncherNavigationReasonCodes.BackRestored);
    }

    public LauncherNavigationDecision RestoreBookmark(
        LauncherPresentationBookmark bookmark,
        bool gameDetailsStillAvailable,
        bool libraryAvailable = true)
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(bookmark);

        LauncherPresentationBookmark normalized;
        try
        {
            normalized = bookmark.Normalize();
        }
        catch (InvalidDataException)
        {
            return Rejected(LauncherNavigationReasonCodes.InvalidBookmark);
        }

        var route = ParseRouteKey(normalized.Route);
        if (route is null)
            return Rejected(LauncherNavigationReasonCodes.InvalidBookmark);

        if (route.Kind == LauncherRouteKind.GameDetails && !gameDetailsStillAvailable)
        {
            _returns.Clear();
            _currentRoute = libraryAvailable ? LauncherRoute.Library : LauncherRoute.Home;
            _preferredFocusKey = null;
            _revision = checked(_revision + 1);
            return Applied(LauncherNavigationTransitionKind.FallbackApplied, LauncherNavigationReasonCodes.RouteFallback);
        }

        _returns.Clear();
        _currentRoute = route;
        _preferredFocusKey = NormalizeOptional(normalized.FocusKey);
        _revision = checked(_revision + 1);
        return Applied(LauncherNavigationTransitionKind.BookmarkRestored, LauncherNavigationReasonCodes.BookmarkRestored);
    }

    public LauncherPresentationBookmark CaptureBookmark(string? currentFocusKey)
    {
        EnsureInitialized();
        return new LauncherPresentationBookmark(
            _currentRoute.RouteKey,
            NormalizeOptional(currentFocusKey));
    }

    private LauncherNavigationDecision GoRoot(LauncherRoute target, string? preferredFocusKey)
    {
        EnsureInitialized();
        var focus = NormalizeOptional(preferredFocusKey);
        if (_currentRoute == target && string.Equals(_preferredFocusKey, focus, StringComparison.Ordinal))
            return NoOp(LauncherNavigationReasonCodes.SameRouteNoOp);

        _returns.Clear();
        _currentRoute = target;
        _preferredFocusKey = focus;
        _revision = checked(_revision + 1);
        return Applied(LauncherNavigationTransitionKind.RootChanged, LauncherNavigationReasonCodes.RootChanged);
    }

    private static LauncherRoute? ParseRouteKey(string routeKey)
    {
        if (string.Equals(routeKey, "HOME", StringComparison.Ordinal))
            return LauncherRoute.Home;
        if (string.Equals(routeKey, "LIBRARY", StringComparison.Ordinal))
            return LauncherRoute.Library;

        const string prefix = "GAME_DETAILS:";
        if (!routeKey.StartsWith(prefix, StringComparison.Ordinal))
            return null;

        var gameId = routeKey[prefix.Length..];
        try
        {
            return LauncherRoute.GameDetails(gameId);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
            throw new InvalidOperationException("Launcher navigation must be initialized before use.");
    }

    private LauncherNavigationDecision Applied(
        LauncherNavigationTransitionKind kind,
        string reasonCode)
        => new(LauncherNavigationDisposition.Applied, kind, reasonCode, Snapshot);

    private LauncherNavigationDecision NoOp(string reasonCode)
        => new(
            LauncherNavigationDisposition.NoOp,
            LauncherNavigationTransitionKind.NoOp,
            reasonCode,
            Snapshot);

    private LauncherNavigationDecision Rejected(string reasonCode)
        => new(
            LauncherNavigationDisposition.Rejected,
            LauncherNavigationTransitionKind.Rejected,
            reasonCode,
            Snapshot);

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ReturnFrame(LauncherRoute Route, string? FocusKey);
}
