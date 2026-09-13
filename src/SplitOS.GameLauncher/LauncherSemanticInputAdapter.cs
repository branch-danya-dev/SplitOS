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
            case VirtualKey.W:
            case VirtualKey.GamepadDPadUp:
            case VirtualKey.GamepadLeftThumbstickUp:
                action = LauncherSemanticAction.NavUp;
                return true;

            case VirtualKey.Down:
            case VirtualKey.S:
            case VirtualKey.GamepadDPadDown:
            case VirtualKey.GamepadLeftThumbstickDown:
                action = LauncherSemanticAction.NavDown;
                return true;

            case VirtualKey.Left:
            case VirtualKey.A:
            case VirtualKey.GamepadDPadLeft:
            case VirtualKey.GamepadLeftThumbstickLeft:
                action = LauncherSemanticAction.NavLeft;
                return true;

            case VirtualKey.Right:
            case VirtualKey.D:
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

            case VirtualKey.Home:
                action = LauncherSemanticAction.FocusHome;
                return true;

            default:
                action = default;
                return false;
        }
    }
}

/// <summary>
/// Bridges the semantic focus owner to concrete WinUI controls. Programmatic semantic focus is
/// projected into WinUI, while pointer/keyboard focus changes are reflected back into the stable
/// semantic focus bookmark. The adapter never derives product state from the visual tree.
/// </summary>
internal sealed class LauncherSemanticFocusWindowAdapter(
    LauncherSemanticFocusController controller)
{
    private readonly Dictionary<string, Control> _targets = new(StringComparer.Ordinal);
    private readonly Dictionary<Control, string> _keysByTarget = [];

    public void RegisterTarget(string focusKey, Control target)
    {
        if (string.IsNullOrWhiteSpace(focusKey))
            throw new ArgumentException("Focus key is required.", nameof(focusKey));
        ArgumentNullException.ThrowIfNull(target);

        if (_targets.ContainsKey(focusKey))
            throw new InvalidOperationException($"Focus target '{focusKey}' is already registered.");
        if (_keysByTarget.ContainsKey(target))
            throw new InvalidOperationException("A WinUI focus target cannot own multiple semantic focus keys.");

        _targets.Add(focusKey, target);
        _keysByTarget.Add(target, focusKey);
        target.GotFocus += OnTargetGotFocus;
    }

    public bool UnregisterTarget(string focusKey)
    {
        if (!_targets.Remove(focusKey, out var target))
            return false;

        _keysByTarget.Remove(target);
        target.GotFocus -= OnTargetGotFocus;
        return true;
    }

    public void ClearTargets()
    {
        foreach (var target in _keysByTarget.Keys.ToArray())
            target.GotFocus -= OnTargetGotFocus;

        _targets.Clear();
        _keysByTarget.Clear();
    }

    public bool Apply(LauncherSemanticFocusDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (decision.Kind != LauncherSemanticDispatchKind.FocusMoved || decision.FocusKey is null)
            return true;

        return Focus(decision.FocusKey);
    }

    public bool FocusCurrent()
        => controller.CurrentFocusKey is not null && Focus(controller.CurrentFocusKey);

    private bool Focus(string focusKey)
        => _targets.TryGetValue(focusKey, out var target)
           && target.IsEnabled
           && target.Visibility == Visibility.Visible
           && target.Focus(FocusState.Programmatic);

    private void OnTargetGotFocus(object sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is Control target && _keysByTarget.TryGetValue(target, out var focusKey))
            _ = controller.SetLogicalFocus(focusKey);
    }
}
