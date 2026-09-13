using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using SplitOS.Runtime.Client;

namespace SplitOS.GameLauncher;

public sealed partial class MainWindow : Window
{
    private const string PrecommitScopeKey = "PRECOMMIT";
    private const string PrecommitFocusKey = "PRECOMMIT_STATUS";
    private const string HomeFocusKey = "nav.home";
    private const string LibraryFocusKey = "nav.library";
    private const string DetailsBackFocusKey = "details.back";
    private const string NavigateHomeInvocation = "NAV_HOME";
    private const string NavigateLibraryInvocation = "NAV_LIBRARY";
    private const string NavigateBackInvocation = "NAV_BACK";

    private readonly LauncherPresentationController _presentationController = new();
    private readonly LauncherRuntimeBindingController _runtimeBindingController = new();
    private readonly LauncherSemanticFocusController _semanticFocusController = new();
    private readonly LauncherNavigationController _navigationController = new();
    private readonly LauncherRuntimeBindingClient _runtimeBindingClient;
    private readonly LauncherPresentationWindowAdapter _presentationWindow;
    private readonly LauncherSemanticFocusWindowAdapter _semanticFocusWindow;
    private long _renderedNavigationRevision = -1;

    public MainWindow()
    {
        InitializeComponent();
        _presentationWindow = new LauncherPresentationWindowAdapter(this);
        _semanticFocusWindow = new LauncherSemanticFocusWindowAdapter(_semanticFocusController);
        _semanticFocusWindow.RegisterTarget(PrecommitFocusKey, PreparingFocusAnchor);
        _ = _navigationController.Initialize();

        RootGrid.AddHandler(
            UIElement.KeyDownEvent,
            new KeyEventHandler(OnRootKeyDown),
            handledEventsToo: true);

        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        _runtimeBindingClient = new LauncherRuntimeBindingClient("SplitOS.GameLauncher", version);
    }

    internal bool IsNavigationInputEnabled => _presentationWindow.NavigationInputEnabled;
    internal LauncherLifecycleState LifecycleState => _runtimeBindingController.State;
    internal bool IsSemanticFocusReady => _semanticFocusController.IsReady;
    internal string? CurrentSemanticFocusKey => _semanticFocusController.CurrentFocusKey;
    internal LauncherNavigationSnapshot NavigationSnapshot => _navigationController.Snapshot;

    internal event Action<LauncherSemanticFocusDecision>? SemanticActionIssued;

    internal LauncherPresentationDecision ObserveGameSession(
        LauncherGameSessionProjection projection,
        LauncherRuntimeUpdateKind updateKind)
    {
        var decision = _presentationController.Observe(projection, updateKind);
        if (decision.Disposition == LauncherPresentationDisposition.Applied)
            _presentationWindow.Apply(decision);

        if (decision.BookmarkToRestore is not null)
        {
            // IMP-073 does not exist yet, so Game Details availability is intentionally UNKNOWN.
            // Structural route restoration is safe because the surface renders no installed/
            // launchable/profile truth until Runtime supplies the normalized library projection.
            _ = _navigationController.RestoreBookmark(
                decision.BookmarkToRestore,
                LauncherRouteAvailability.Unknown);
        }

        return decision;
    }

    internal LauncherPresentationDecision CapturePresentationBookmark(LauncherPresentationBookmark bookmark)
        => _presentationController.CaptureBookmark(bookmark);

    internal LauncherPresentationDecision CaptureCurrentNavigationBookmark()
        => _presentationController.CaptureBookmark(
            _navigationController.CaptureBookmark(_semanticFocusController.CurrentFocusKey));

    internal LauncherNavigationDecision NavigateToGameDetails(
        string gameId,
        string? returnFocusKey = null)
    {
        var decision = _navigationController.OpenGameDetails(
            gameId,
            returnFocusKey ?? _semanticFocusController.CurrentFocusKey);
        ApplyNavigationDecision(decision);
        return decision;
    }

    internal LauncherNavigationDecision RestoreNavigationBookmark(
        LauncherPresentationBookmark bookmark,
        LauncherRouteAvailability gameDetailsAvailability,
        bool libraryAvailable = true)
    {
        var decision = _navigationController.RestoreBookmark(
            bookmark,
            gameDetailsAvailability,
            libraryAvailable);
        ApplyNavigationDecision(decision);
        return decision;
    }

    internal async Task<LauncherBindingDecision> RefreshRuntimeBindingAsync(CancellationToken cancellationToken = default)
    {
        if (_runtimeBindingController.State == LauncherLifecycleState.Stopped)
            _ = _runtimeBindingController.BeginStart();
        if (_runtimeBindingController.State is LauncherLifecycleState.Starting or LauncherLifecycleState.DegradedDisconnected)
            _ = _runtimeBindingController.BeginConnecting();

        try
        {
            var snapshot = await _runtimeBindingClient.ReadSnapshotAsync(cancellationToken);
            if (_runtimeBindingController.State == LauncherLifecycleState.Connecting)
                _ = _runtimeBindingController.ReportTransportConnected();

            var presentation = ObserveGameSession(
                new LauncherGameSessionProjection(
                    snapshot.GameSessionRevision,
                    ParseSessionState(snapshot.GameSessionState),
                    string.Equals(snapshot.CommittedMode, "GAME", StringComparison.Ordinal),
                    snapshot.ActiveLaunchOperationId),
                LauncherRuntimeUpdateKind.Snapshot);

            if (presentation.Disposition == LauncherPresentationDisposition.Rejected)
                throw new InvalidDataException(presentation.ReasonCode);

            var decision = _runtimeBindingController.ApplyFreshRuntimeSnapshot(
                snapshot,
                presentation.PresentationState);
            UpdateLifecycleSurface(decision.State);
            return decision;
        }
        catch
        {
            if (_runtimeBindingController.State is not (LauncherLifecycleState.Stopped or LauncherLifecycleState.Stopping))
                _ = _runtimeBindingController.ReportRuntimeDisconnected();
            UpdateLifecycleSurface(_runtimeBindingController.State);
            throw;
        }
    }

    internal async Task<LauncherBindingDecision> ReportPresentationSubsystemReadyAsync(CancellationToken cancellationToken = default)
    {
        var decision = _runtimeBindingController.ReportPresentationSubsystemReady();
        if (!decision.CanReportGameModeReady
            || decision.ExpectedGameModeOperationId is null
            || decision.ExpectedGameModeCorrelationId is null)
            return decision;

        var readiness = await _runtimeBindingClient.ReportReadyForGameModeAsync(
            decision.ExpectedGameModeOperationId.Value,
            decision.ExpectedGameModeCorrelationId.Value,
            cancellationToken);
        if (readiness.Disposition is not ("ACCEPTED" or "NO_OP"))
            throw new InvalidOperationException(readiness.ProductCode);

        return await RefreshRuntimeBindingAsync(cancellationToken);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var decision = await RefreshRuntimeBindingAsync();
            if (decision.State is not (LauncherLifecycleState.Active
                    or LauncherLifecycleState.BackgroundGameRunning
                    or LauncherLifecycleState.Restoring)
                && !EnsurePrecommitSemanticFocus())
            {
                throw new InvalidOperationException("SEMANTIC_FOCUS_NOT_READY");
            }

            decision = await ReportPresentationSubsystemReadyAsync();
            UpdateLifecycleSurface(decision.State);
            StatusText.Text = $"Runtime binding: {decision.State} · snapshot {decision.RuntimeSnapshotVersion}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Runtime unavailable: {ex.Message}";
        }
    }

    private bool EnsurePrecommitSemanticFocus()
    {
        if (string.Equals(
                _semanticFocusController.CurrentScopeKey,
                PrecommitScopeKey,
                StringComparison.Ordinal))
        {
            return _semanticFocusWindow.FocusCurrent();
        }

        _semanticFocusWindow.ClearTargets();
        _semanticFocusWindow.RegisterTarget(PrecommitFocusKey, PreparingFocusAnchor);
        _renderedNavigationRevision = -1;

        var decision = _semanticFocusController.LoadRootScope(
            new LauncherFocusScopeDefinition(
                PrecommitScopeKey,
                PrecommitFocusKey,
                [new LauncherFocusNode(PrecommitFocusKey)]));
        return _semanticFocusWindow.Apply(decision);
    }

    private void UpdateLifecycleSurface(LauncherLifecycleState state)
    {
        PreparingFocusAnchor.Content = state switch
        {
            LauncherLifecycleState.Active => "Game Mode",
            LauncherLifecycleState.BackgroundGameRunning => "Game running",
            LauncherLifecycleState.Restoring => "Returning to Game Mode…",
            LauncherLifecycleState.DegradedDisconnected => "Reconnecting…",
            _ => "Preparing Game Mode…"
        };

        var committedGameSurface = state is LauncherLifecycleState.Active
            or LauncherLifecycleState.BackgroundGameRunning
            or LauncherLifecycleState.Restoring;
        PrecommitSurface.Visibility = committedGameSurface ? Visibility.Collapsed : Visibility.Visible;
        LauncherSurface.Visibility = committedGameSurface ? Visibility.Visible : Visibility.Collapsed;

        if (!committedGameSurface)
        {
            _ = EnsurePrecommitSemanticFocus();
            return;
        }

        if (state == LauncherLifecycleState.Active)
        {
            var navigation = _navigationController.Snapshot;
            var routeScopeActive = _semanticFocusController.CurrentScopeKey?.StartsWith(
                "ROUTE:",
                StringComparison.Ordinal) == true;
            if (_renderedNavigationRevision != navigation.Revision || !routeScopeActive)
                RenderNavigation(navigation);
        }
    }

    private void RenderNavigation(LauncherNavigationSnapshot snapshot)
    {
        HomeSurface.Visibility = snapshot.CurrentRoute.Kind == LauncherRouteKind.Home
            ? Visibility.Visible
            : Visibility.Collapsed;
        LibrarySurface.Visibility = snapshot.CurrentRoute.Kind == LauncherRouteKind.Library
            ? Visibility.Visible
            : Visibility.Collapsed;
        GameDetailsSurface.Visibility = snapshot.CurrentRoute.Kind == LauncherRouteKind.GameDetails
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (snapshot.CurrentRoute.Kind == LauncherRouteKind.GameDetails)
        {
            GameDetailsIdentityText.Text =
                $"Game identity: {snapshot.CurrentRoute.GameId} · availability/readiness not inferred locally.";
        }
        else
        {
            GameDetailsIdentityText.Text = string.Empty;
        }

        _semanticFocusWindow.ClearTargets();
        _semanticFocusWindow.RegisterTarget(HomeFocusKey, HomeNavButton);
        _semanticFocusWindow.RegisterTarget(LibraryFocusKey, LibraryNavButton);

        LauncherFocusScopeDefinition focusScope;
        switch (snapshot.CurrentRoute.Kind)
        {
            case LauncherRouteKind.Home:
                focusScope = new LauncherFocusScopeDefinition(
                    "ROUTE:HOME",
                    HomeFocusKey,
                    [
                        new LauncherFocusNode(
                            HomeFocusKey,
                            Right: LibraryFocusKey,
                            ActivationId: NavigateHomeInvocation),
                        new LauncherFocusNode(
                            LibraryFocusKey,
                            Left: HomeFocusKey,
                            ActivationId: NavigateLibraryInvocation)
                    ]);
                break;

            case LauncherRouteKind.Library:
                focusScope = new LauncherFocusScopeDefinition(
                    "ROUTE:LIBRARY",
                    LibraryFocusKey,
                    [
                        new LauncherFocusNode(
                            HomeFocusKey,
                            Right: LibraryFocusKey,
                            ActivationId: NavigateHomeInvocation),
                        new LauncherFocusNode(
                            LibraryFocusKey,
                            Left: HomeFocusKey,
                            ActivationId: NavigateLibraryInvocation)
                    ]);
                break;

            case LauncherRouteKind.GameDetails:
                _semanticFocusWindow.RegisterTarget(DetailsBackFocusKey, DetailsBackButton);
                focusScope = new LauncherFocusScopeDefinition(
                    $"ROUTE:{snapshot.CurrentRoute.RouteKey}",
                    DetailsBackFocusKey,
                    [
                        new LauncherFocusNode(
                            DetailsBackFocusKey,
                            Right: HomeFocusKey,
                            ActivationId: NavigateBackInvocation),
                        new LauncherFocusNode(
                            HomeFocusKey,
                            Left: DetailsBackFocusKey,
                            Right: LibraryFocusKey,
                            ActivationId: NavigateHomeInvocation),
                        new LauncherFocusNode(
                            LibraryFocusKey,
                            Left: HomeFocusKey,
                            ActivationId: NavigateLibraryInvocation)
                    ]);
                break;

            default:
                throw new InvalidOperationException(
                    $"Unsupported Launcher route {snapshot.CurrentRoute.Kind}.");
        }

        var focusDecision = _semanticFocusController.LoadRootScope(
            focusScope,
            snapshot.PreferredFocusKey);
        if (_semanticFocusWindow.Apply(focusDecision))
            _renderedNavigationRevision = snapshot.Revision;
        else
            StatusText.Text = $"Focus unavailable: {focusDecision.FocusKey}";
    }

    private void ApplyNavigationDecision(LauncherNavigationDecision decision)
    {
        if (decision.Disposition == LauncherNavigationDisposition.Rejected)
        {
            StatusText.Text = $"Navigation rejected: {decision.ReasonCode}";
            return;
        }

        if (decision.Disposition == LauncherNavigationDisposition.Applied
            && _runtimeBindingController.State == LauncherLifecycleState.Active)
        {
            RenderNavigation(decision.Snapshot);
        }
    }

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!IsNavigationInputEnabled)
            return;
        if (!LauncherSemanticInputMapper.TryMap(e.Key, out var action))
            return;

        e.Handled = true;
        var decision = _semanticFocusController.Dispatch(action);

        if (decision.Kind == LauncherSemanticDispatchKind.FocusMoved)
        {
            if (!_semanticFocusWindow.Apply(decision))
                StatusText.Text = $"Focus unavailable: {decision.FocusKey}";
            return;
        }

        if (decision.Kind == LauncherSemanticDispatchKind.InvocationIssued
            && HandleNavigationInvocation(decision.InvocationId))
            return;

        if (decision.Kind == LauncherSemanticDispatchKind.CommandIssued
            && decision.Action == LauncherSemanticAction.Back)
        {
            ApplyNavigationDecision(_navigationController.Back());
            return;
        }

        if (decision.Kind is LauncherSemanticDispatchKind.InvocationIssued
            or LauncherSemanticDispatchKind.CommandIssued)
        {
            SemanticActionIssued?.Invoke(decision);
        }
    }

    private bool HandleNavigationInvocation(string? invocationId)
    {
        switch (invocationId)
        {
            case NavigateHomeInvocation:
                ApplyNavigationDecision(_navigationController.GoHome(HomeFocusKey));
                return true;
            case NavigateLibraryInvocation:
                ApplyNavigationDecision(_navigationController.GoLibrary(LibraryFocusKey));
                return true;
            case NavigateBackInvocation:
                ApplyNavigationDecision(_navigationController.Back());
                return true;
            default:
                return false;
        }
    }

    private void OnHomeNavigationClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ApplyNavigationDecision(_navigationController.GoHome(HomeFocusKey));
    }

    private void OnLibraryNavigationClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ApplyNavigationDecision(_navigationController.GoLibrary(LibraryFocusKey));
    }

    private void OnDetailsBackClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ApplyNavigationDecision(_navigationController.Back());
    }

    private static LauncherRuntimeGameSessionState ParseSessionState(string value)
        => value switch
        {
            "INACTIVE" => LauncherRuntimeGameSessionState.Inactive,
            "LAUNCHER" => LauncherRuntimeGameSessionState.Launcher,
            "PREPARING" => LauncherRuntimeGameSessionState.Preparing,
            "CLIENT_HANDOFF" => LauncherRuntimeGameSessionState.ClientHandoff,
            "GAME_STARTING" => LauncherRuntimeGameSessionState.GameStarting,
            "GAME_RUNNING" => LauncherRuntimeGameSessionState.GameRunning,
            "GAME_EXIT_DETECTED" => LauncherRuntimeGameSessionState.GameExitDetected,
            "RETURNING_TO_LAUNCHER" => LauncherRuntimeGameSessionState.ReturningToLauncher,
            "FAILED" => LauncherRuntimeGameSessionState.Failed,
            _ => throw new InvalidDataException($"Unknown Runtime GameSession state '{value}'.")
        };
}
