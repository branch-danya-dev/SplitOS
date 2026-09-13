using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SplitOS.Runtime.Client;
using Windows.System;

namespace SplitOS.GameLauncher;

internal static class LauncherSemanticInputMapper
{
    public static bool TryMap(VirtualKey key, out LauncherSemanticAction action)
    {
        switch (key)
        {
            case VirtualKey.Up:
            case VirtualKey.GamepadDPadUp:
            case VirtualKey.GamepadLeftThumbstickUp:
                action = LauncherSemanticAction.NavUp;
                return true;

            case VirtualKey.Down:
            case VirtualKey.GamepadDPadDown:
            case VirtualKey.GamepadLeftThumbstickDown:
                action = LauncherSemanticAction.NavDown;
                return true;

            case VirtualKey.Left:
            case VirtualKey.GamepadDPadLeft:
            case VirtualKey.GamepadLeftThumbstickLeft:
                action = LauncherSemanticAction.NavLeft;
                return true;

            case VirtualKey.Right:
            case VirtualKey.GamepadDPadRight:
            case VirtualKey.GamepadLeftThumbstickRight:
                action = LauncherSemanticAction.NavRight;
                return true;

            case VirtualKey.Enter:
            case VirtualKey.Space:
            case VirtualKey.GamepadA:
                action = LauncherSemanticAction.Activate;
                return true;

            case VirtualKey.Escape:
            case VirtualKey.Back:
            case VirtualKey.GamepadB:
                action = LauncherSemanticAction.Back;
                return true;

            case VirtualKey.Application:
            case VirtualKey.GamepadView:
            case VirtualKey.GamepadX:
                action = LauncherSemanticAction.OpenContext;
                return true;

            case VirtualKey.F10:
            case VirtualKey.GamepadMenu:
                action = LauncherSemanticAction.OpenSystemMenu;
                return true;

            case VirtualKey.PageDown:
            case VirtualKey.GamepadRightShoulder:
                action = LauncherSemanticAction.PageNext;
                return true;

            case VirtualKey.PageUp:
            case VirtualKey.GamepadLeftShoulder:
                action = LauncherSemanticAction.PagePrevious;
                return true;

            default:
                action = default;
                return false;
        }
    }
}

/// <summary>
/// Bridges the semantic focus owner to concrete WinUI controls. The semantic controller decides
/// which focus key is authoritative; this adapter only resolves that key to a trusted control
/// registered by the Launcher surface and asks WinUI to apply programmatic focus.
/// </summary>
internal sealed class LauncherSemanticFocusWindowAdapter
{
    private readonly Dictionary<string, Control> _targets = new(StringComparer.Ordinal);

    public void RegisterTarget(string focusKey, Control target)
    {
        if (string.IsNullOrWhiteSpace(focusKey))
            throw new ArgumentException("Focus key is required.", nameof(focusKey));
        ArgumentNullException.ThrowIfNull(target);

        if (!_targets.TryAdd(focusKey, target))
            throw new InvalidOperationException($"Focus target '{focusKey}' is already registered.");
    }

    public bool Apply(LauncherSemanticFocusDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (decision.Kind != LauncherSemanticDispatchKind.FocusMoved || decision.FocusKey is null)
            return true;

        return Focus(decision.FocusKey);
    }

    public bool FocusCurrent(LauncherSemanticFocusController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return controller.CurrentFocusKey is not null && Focus(controller.CurrentFocusKey);
    }

    private bool Focus(string focusKey)
        => _targets.TryGetValue(focusKey, out var target)
           && target.IsEnabled
           && target.Visibility == Visibility.Visible
           && target.Focus(FocusState.Programmatic);
}
