using Microsoft.UI.Xaml;
using SplitOS.Runtime.Client;

namespace SplitOS.GameLauncher;

public sealed partial class MainWindow : Window
{
    private readonly LauncherPresentationController _presentationController = new();
    private readonly LauncherPresentationWindowAdapter _presentationWindow;

    public MainWindow()
    {
        InitializeComponent();
        _presentationWindow = new LauncherPresentationWindowAdapter(this);
    }

    internal bool IsNavigationInputEnabled => _presentationWindow.NavigationInputEnabled;

    /// <summary>
    /// Runtime-binding hook for coherent GameSession snapshots/events. The Launcher only projects
    /// authoritative Runtime truth; callers must requery a fresh snapshot when the returned decision
    /// says revision continuity could not be proven.
    /// </summary>
    internal LauncherPresentationDecision ObserveGameSession(
        LauncherGameSessionProjection projection,
        LauncherRuntimeUpdateKind updateKind)
    {
        var decision = _presentationController.Observe(projection, updateKind);
        if (decision.Disposition == LauncherPresentationDisposition.Applied)
            _presentationWindow.Apply(decision);

        return decision;
    }

    internal LauncherPresentationDecision CapturePresentationBookmark(
        LauncherPresentationBookmark bookmark)
        => _presentationController.CaptureBookmark(bookmark);

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var version = typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "0.0.0";
            var client = new RuntimeHealthClient("SplitOS.GameLauncher", version);
            var health = await client.ReadAsync();
            StatusText.Text = $"Runtime: {health.Status} · PID {health.ProcessId} · Session {health.SessionId}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Runtime unavailable: {ex.Message}";
        }
    }
}
