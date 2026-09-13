namespace SplitOS.Runtime.Client;

public enum LauncherSemanticAction
{
    NavUp,
    NavDown,
    NavLeft,
    NavRight,
    Activate,
    Back,
    OpenContext,
    OpenSystemMenu,
    PageNext,
    PagePrevious
}

public enum LauncherSemanticDispatchKind
{
    FocusMoved,
    InvocationIssued,
    CommandIssued,
    NoOp,
    Rejected
}

public static class LauncherSemanticFocusReasonCodes
{
    public const string FocusMoved = "FOCUS_MOVED";
    public const string FocusRestored = "FOCUS_RESTORED";
    public const string InvocationIssued = "INVOCATION_ISSUED";
    public const string CommandIssued = "COMMAND_ISSUED";
    public const string NavigationBoundary = "NAVIGATION_BOUNDARY";
    public const string TargetUnavailable = "TARGET_UNAVAILABLE";
    public const string NoActivation = "NO_ACTIVATION";
    public const string FocusNotReady = "FOCUS_NOT_READY";
    public const string FocusKeyUnavailable = "FOCUS_KEY_UNAVAILABLE";
    public const string RootScopeLoaded = "ROOT_SCOPE_LOADED";
    public const string ScopePushed = "SCOPE_PUSHED";
    public const string ScopePopped = "SCOPE_POPPED";
    public const string RootScopeCannotPop = "ROOT_SCOPE_CANNOT_POP";
}

public sealed record LauncherFocusNode(
    string FocusKey,
    string? Up = null,
    string? Down = null,
    string? Left = null,
    string? Right = null,
    string? ActivationId = null,
    bool IsAvailable = true);

public sealed record LauncherFocusScopeDefinition(
    string ScopeKey,
    string DefaultFocusKey,
    IReadOnlyList<LauncherFocusNode> Nodes);

public sealed record LauncherSemanticFocusDecision(
    LauncherSemanticDispatchKind Kind,
    string ReasonCode,
    LauncherSemanticAction? Action,
    string? ScopeKey,
    string? FocusKey,
    string? InvocationId,
    long Revision,
    bool IsReady);

/// <summary>
/// Device-agnostic semantic action/focus owner for the Game Launcher. Navigation follows only the
/// explicit focus graph supplied by the current surface; it never falls back to geometry, tab order,
/// pointer position, or control-tree heuristics. Modal/overlay scopes preserve the exact parent focus
/// key and restore it deterministically when the scope closes.
/// </summary>
public sealed class LauncherSemanticFocusController
{
    private readonly List<FocusFrame> _frames = [];
    private long _revision;

    public bool IsReady => CurrentFrame is { CurrentFocusKey: not null };
    public long Revision => _revision;
    public string? CurrentScopeKey => CurrentFrame?.Definition.ScopeKey;
    public string? CurrentFocusKey => CurrentFrame?.CurrentFocusKey;

    public LauncherSemanticFocusDecision LoadRootScope(
        LauncherFocusScopeDefinition definition,
        string? preferredFocusKey = null)
    {
        var normalized = Validate(definition);
        _frames.Clear();
        _frames.Add(new FocusFrame(normalized, ResolveInitialFocus(normalized, preferredFocusKey)));
        _revision = checked(_revision + 1);
        return Decision(
            LauncherSemanticDispatchKind.FocusMoved,
            LauncherSemanticFocusReasonCodes.RootScopeLoaded,
            action: null,
            invocationId: null);
    }

    public LauncherSemanticFocusDecision PushScope(
        LauncherFocusScopeDefinition definition,
        string? preferredFocusKey = null)
    {
        var normalized = Validate(definition);
        _frames.Add(new FocusFrame(normalized, ResolveInitialFocus(normalized, preferredFocusKey)));
        _revision = checked(_revision + 1);
        return Decision(
            LauncherSemanticDispatchKind.FocusMoved,
            LauncherSemanticFocusReasonCodes.ScopePushed,
            action: null,
            invocationId: null);
    }

    public LauncherSemanticFocusDecision PopScope()
    {
        if (_frames.Count == 0)
            return Rejected(LauncherSemanticFocusReasonCodes.FocusNotReady);
        if (_frames.Count == 1)
            return Decision(
                LauncherSemanticDispatchKind.NoOp,
                LauncherSemanticFocusReasonCodes.RootScopeCannotPop,
                action: null,
                invocationId: null);

        _frames.RemoveAt(_frames.Count - 1);
        _revision = checked(_revision + 1);
        return Decision(
            LauncherSemanticDispatchKind.FocusMoved,
            LauncherSemanticFocusReasonCodes.ScopePopped,
            action: null,
            invocationId: null);
    }

    public LauncherSemanticFocusDecision RestoreFocus(string focusKey)
    {
        if (string.IsNullOrWhiteSpace(focusKey))
            throw new ArgumentException("Focus key is required.", nameof(focusKey));

        var frame = CurrentFrame;
        if (frame is null)
            return Rejected(LauncherSemanticFocusReasonCodes.FocusNotReady);

        if (!frame.Nodes.TryGetValue(focusKey, out var target) || !target.IsAvailable)
        {
            return Decision(
                LauncherSemanticDispatchKind.NoOp,
                LauncherSemanticFocusReasonCodes.FocusKeyUnavailable,
                action: null,
                invocationId: null);
        }

        if (string.Equals(frame.CurrentFocusKey, focusKey, StringComparison.Ordinal))
        {
            return Decision(
                LauncherSemanticDispatchKind.NoOp,
                LauncherSemanticFocusReasonCodes.FocusRestored,
                action: null,
                invocationId: null);
        }

        frame.CurrentFocusKey = focusKey;
        _revision = checked(_revision + 1);
        return Decision(
            LauncherSemanticDispatchKind.FocusMoved,
            LauncherSemanticFocusReasonCodes.FocusRestored,
            action: null,
            invocationId: null);
    }

    public LauncherSemanticFocusDecision Dispatch(LauncherSemanticAction action)
    {
        var frame = CurrentFrame;
        if (frame is null || frame.CurrentFocusKey is null)
            return Rejected(LauncherSemanticFocusReasonCodes.FocusNotReady, action);

        if (!frame.Nodes.TryGetValue(frame.CurrentFocusKey, out var current) || !current.IsAvailable)
            return Rejected(LauncherSemanticFocusReasonCodes.FocusNotReady, action);

        return action switch
        {
            LauncherSemanticAction.NavUp => Move(action, current.Up),
            LauncherSemanticAction.NavDown => Move(action, current.Down),
            LauncherSemanticAction.NavLeft => Move(action, current.Left),
            LauncherSemanticAction.NavRight => Move(action, current.Right),
            LauncherSemanticAction.Activate => Activate(action, current),
            _ => Decision(
                LauncherSemanticDispatchKind.CommandIssued,
                LauncherSemanticFocusReasonCodes.CommandIssued,
                action,
                invocationId: null)
        };
    }

    private LauncherSemanticFocusDecision Move(LauncherSemanticAction action, string? targetKey)
    {
        if (targetKey is null)
        {
            return Decision(
                LauncherSemanticDispatchKind.NoOp,
                LauncherSemanticFocusReasonCodes.NavigationBoundary,
                action,
                invocationId: null);
        }

        var frame = CurrentFrame!;
        if (!frame.Nodes.TryGetValue(targetKey, out var target) || !target.IsAvailable)
        {
            return Decision(
                LauncherSemanticDispatchKind.NoOp,
                LauncherSemanticFocusReasonCodes.TargetUnavailable,
                action,
                invocationId: null);
        }

        if (string.Equals(frame.CurrentFocusKey, targetKey, StringComparison.Ordinal))
        {
            return Decision(
                LauncherSemanticDispatchKind.NoOp,
                LauncherSemanticFocusReasonCodes.NavigationBoundary,
                action,
                invocationId: null);
        }

        frame.CurrentFocusKey = targetKey;
        _revision = checked(_revision + 1);
        return Decision(
            LauncherSemanticDispatchKind.FocusMoved,
            LauncherSemanticFocusReasonCodes.FocusMoved,
            action,
            invocationId: null);
    }

    private LauncherSemanticFocusDecision Activate(
        LauncherSemanticAction action,
        LauncherFocusNode current)
    {
        if (string.IsNullOrWhiteSpace(current.ActivationId))
        {
            return Decision(
                LauncherSemanticDispatchKind.NoOp,
                LauncherSemanticFocusReasonCodes.NoActivation,
                action,
                invocationId: null);
        }

        return Decision(
            LauncherSemanticDispatchKind.InvocationIssued,
            LauncherSemanticFocusReasonCodes.InvocationIssued,
            action,
            current.ActivationId);
    }

    private LauncherSemanticFocusDecision Rejected(
        string reasonCode,
        LauncherSemanticAction? action = null)
        => Decision(
            LauncherSemanticDispatchKind.Rejected,
            reasonCode,
            action,
            invocationId: null);

    private LauncherSemanticFocusDecision Decision(
        LauncherSemanticDispatchKind kind,
        string reasonCode,
        LauncherSemanticAction? action,
        string? invocationId)
        => new(
            kind,
            reasonCode,
            action,
            CurrentScopeKey,
            CurrentFocusKey,
            invocationId,
            _revision,
            IsReady);

    private FocusFrame? CurrentFrame => _frames.Count == 0 ? null : _frames[^1];

    private static FocusScope Validate(LauncherFocusScopeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.ScopeKey))
            throw new InvalidDataException("Focus scope key is required.");
        if (string.IsNullOrWhiteSpace(definition.DefaultFocusKey))
            throw new InvalidDataException("Default focus key is required.");
        if (definition.Nodes is null || definition.Nodes.Count == 0)
            throw new InvalidDataException("A focus scope must contain at least one node.");

        var nodes = new Dictionary<string, LauncherFocusNode>(StringComparer.Ordinal);
        foreach (var node in definition.Nodes)
        {
            if (node is null || string.IsNullOrWhiteSpace(node.FocusKey))
                throw new InvalidDataException("Every focus node must have a non-empty focus key.");
            if (!nodes.TryAdd(node.FocusKey, node))
                throw new InvalidDataException($"Duplicate focus key '{node.FocusKey}'.");
        }

        if (!nodes.TryGetValue(definition.DefaultFocusKey, out var defaultNode) || !defaultNode.IsAvailable)
            throw new InvalidDataException("Default focus key must resolve to an available node.");

        foreach (var node in nodes.Values)
        {
            ValidateEdge(node.FocusKey, node.Up, nodes);
            ValidateEdge(node.FocusKey, node.Down, nodes);
            ValidateEdge(node.FocusKey, node.Left, nodes);
            ValidateEdge(node.FocusKey, node.Right, nodes);
        }

        return new FocusScope(definition.ScopeKey, definition.DefaultFocusKey, nodes);
    }

    private static void ValidateEdge(
        string sourceKey,
        string? targetKey,
        IReadOnlyDictionary<string, LauncherFocusNode> nodes)
    {
        if (targetKey is null)
            return;
        if (string.Equals(sourceKey, targetKey, StringComparison.Ordinal))
            throw new InvalidDataException($"Focus node '{sourceKey}' cannot navigate to itself.");
        if (!nodes.ContainsKey(targetKey))
            throw new InvalidDataException($"Focus edge '{sourceKey}' -> '{targetKey}' is unresolved.");
    }

    private static string ResolveInitialFocus(FocusScope scope, string? preferredFocusKey)
    {
        if (preferredFocusKey is not null
            && scope.Nodes.TryGetValue(preferredFocusKey, out var preferred)
            && preferred.IsAvailable)
            return preferredFocusKey;

        return scope.DefaultFocusKey;
    }

    private sealed class FocusFrame(FocusScope definition, string currentFocusKey)
    {
        public FocusScope Definition { get; } = definition;
        public IReadOnlyDictionary<string, LauncherFocusNode> Nodes => Definition.Nodes;
        public string? CurrentFocusKey { get; set; } = currentFocusKey;
    }

    private sealed record FocusScope(
        string ScopeKey,
        string DefaultFocusKey,
        IReadOnlyDictionary<string, LauncherFocusNode> Nodes);
}
