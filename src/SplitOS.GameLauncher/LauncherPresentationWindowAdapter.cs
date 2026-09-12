using Microsoft.UI.Xaml;
using SplitOS.Runtime.Client;

namespace SplitOS.GameLauncher;

/// <summary>
/// Applies already-authoritative Launcher presentation decisions to the physical WinUI window.
/// It does not inspect game processes or decide whether a game is running.
/// </summary>
internal sealed class LauncherPresentationWindowAdapter
{
    private readonly Window _window;
    private readonly Action<LauncherPresentationBookmark>? _restoreBookmark;

    public LauncherPresentationWindowAdapter(
        Window window,
        Action<LauncherPresentationBookmark>? restoreBookmark = null)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _restoreBookmark = restoreBookmark;
    }

    public bool NavigationInputEnabled { get; private set; } = true;

    public void Apply(LauncherPresentationDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        NavigationInputEnabled = decision.NavigationIntent == LauncherNavigationIntent.Enabled;

        switch (decision.WindowIntent)
        {
            case LauncherWindowIntent.None:
                break;

            case LauncherWindowIntent.KeepForeground:
                // Ensure the Launcher remains visible without stealing activation from another app.
                _window.AppWindow.Show(activateWindow: false);
                break;

            case LauncherWindowIntent.YieldForeground:
                // Hide the Launcher surface while keeping the process/window object resident for a
                // fast Runtime-driven restore after GAME_EXITED_CONFIRMED.
                _window.AppWindow.Hide();
                break;

            case LauncherWindowIntent.RestoreForeground:
                _window.AppWindow.Show(activateWindow: true);
                break;

            default:
                throw new InvalidDataException($"Unsupported launcher window intent: {decision.WindowIntent}.");
        }

        if (decision.BookmarkToRestore is not null)
            _restoreBookmark?.Invoke(decision.BookmarkToRestore);
    }
}
