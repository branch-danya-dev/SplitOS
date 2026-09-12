using Microsoft.UI.Xaml;
using SplitOS.Runtime.Client;

namespace SplitOS.GameLauncher;

public sealed partial class MainWindow : Window
{
    private readonly LauncherPresentationController _presentationController = new();
    private readonly LauncherRuntimeBindingController _runtimeBindingController = new();
    private readonly LauncherRuntimeBindingClient _runtimeBindingClient;
    private readonly LauncherPresentationWindowAdapter _presentationWindow;

    public MainWindow()
    {
        InitializeComponent();
        _presentationWindow = new LauncherPresentationWindowAdapter(this);
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        _runtimeBindingClient = new LauncherRuntimeBindingClient("SplitOS.GameLauncher", version);
    }

    internal bool IsNavigationInputEnabled => _presentationWindow.NavigationInputEnabled;
    internal LauncherLifecycleState LifecycleState => _runtimeBindingController.State;

    internal LauncherPresentationDecision ObserveGameSession(
        LauncherGameSessionProjection projection,
        LauncherRuntimeUpdateKind updateKind)
    {
        var decision = _presentationController.Observe(projection, updateKind);
        if (decision.Disposition == LauncherPresentationDisposition.Applied)
            _presentationWindow.Apply(decision);
        return decision;
    }

    internal LauncherPresentationDecision CapturePresentationBookmark(LauncherPresentationBookmark bookmark)
        => _presentationController.CaptureBookmark(bookmark);

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

            return _runtimeBindingController.ApplyFreshRuntimeSnapshot(snapshot, presentation.PresentationState);
        }
        catch
        {
            if (_runtimeBindingController.State is not (LauncherLifecycleState.Stopped or LauncherLifecycleState.Stopping))
                _ = _runtimeBindingController.ReportRuntimeDisconnected();
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
            StatusText.Text = $"Runtime binding: {decision.State} · snapshot {decision.RuntimeSnapshotVersion}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Runtime unavailable: {ex.Message}";
        }
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
