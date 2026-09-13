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
    PagePrevious,
    FocusHome
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
    public const string GeometricFallback = "GEOMETRIC_FALLBACK";
    public const string RouteFallback = "ROUTE_FALLBACK";
    public const string FocusHome = "FOCUS_HOME";
    public const string FocusRestored = "FOCUS_RESTORED";
    public const string LogicalFocusSet = "LOGICAL_FOCUS_SET";
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
    public const string ScopeRefreshedFocusPreserved = "SCOPE_REFRESHED_FOCUS_PRESERVED";
    public const string ScopeRefreshedFocusFallback = "SCOPE_REFRESHED_FOCUS_FALLBACK";
    public const string RootScopeCannotPop = "ROOT_SCOPE_CANNOT_POP";
}

public sealed record LauncherFocusRect(
    double X,
    double Y,
    double Width,
    double Height)
{
    public double CenterX => X + (Width / 2d);
    public double CenterY => Y + (Height / 2d);

    public LauncherFocusRect Validate()
    {
        if (!double.IsFinite(X)
            || !double.IsFinite(Y)
            || !double.IsFinite(Width)
            || !double.IsFinite(Height)
            || Width <= 0d
            || Height <= 0d)
            throw new InvalidDataException("Focus bounds must be finite with positive width and height.");

        return this;
    }
}

public sealed record LauncherDirectionalFocusFallbacks(
    string? Up = null,
    string? Down = null,
    string? Left = null,
    string? Right = null);

public sealed record LauncherFocusNode(
    string FocusKey,
    string? Up = null,
    string? Down = null,
    string? Left = null,
    string? Right = null,
    string? ActivationId = null,
    bool IsAvailable = true,
    LauncherFocusRect? Bounds = null);

public sealed record LauncherFocusScopeDefinition(
    string ScopeKey,
    string DefaultFocusKey,
    IReadOnlyList<LauncherFocusNode> Nodes,
    LauncherDirectionalFocusFallbacks? Fallbacks = null);

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
/// Device-agnostic semantic action/focus owner for the Game Launcher. Directional navigation follows
/// the SPEC-09 priority: explicit edge, then geometric nearest available target, then an optional
/// route-level fallback. Stable semantic focus keys survive data reordering and modal scopes preserve
/// the exact parent focus key. Product/runtime authority remains outside this presentation owner.
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

    public LauncherSemanticFocusDecision ReplaceCurrentScope(
        LauncherFocusScopeDefinition definition,
        string? fallbackFocusKey = null)
    {
        var frame = CurrentFrame;
        if (frame is null)
            return Rejected(LauncherSemanticFocusReasonCodes.FocusNotReady);

        var normalized = Validate(definition);
        if (!string.Equals(frame.Definition.ScopeKey, normalized.ScopeKey, StringComparison.Ordinal))
            throw new InvalidDataException("A scope refresh cannot change the semantic scope key.");

        var previousFocus = frame.CurrentFocusKey;
        var nextFocus = previousFocus is not null
            && normalized.Nodes.TryGetValue(previousFocus, out var preserved)
            && preserved.IsAvailable
                ? previousFocus
                : ResolveInitialFocus(normalized, fallbackFocusKey);

        frame.ReplaceDefinition(normalized, nextFocus);
        _revision = checked(_revision + 1);

        var preservedFocus = string.Equals(previousFocus, nextFocus, StringComparison.Ordinal);
        return Decision(
            preservedFocus ? LauncherSemanticDispatchKind.NoOp : LauncherSemanticDispatchKind.FocusMoved,
            preservedFocus
                ? LauncherSemanticFocusReasonCodes.ScopeRefreshedFocusPreserved
                : LauncherSemanticFocusReasonCodes.ScopeRefreshedFocusFallback,
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
        => SetLogicalFocusCore(focusKey, LauncherSemanticFocusReasonCodes.FocusRestored);

    public LauncherSemanticFocusDecision SetLogicalFocus(string focusKey)
        => SetLogicalFocusCore(focusKey, LauncherSemanticFocusReasonCodes.LogicalFocusSet);

    public LauncherSemanticFocusDecision Dispatch(LauncherSemanticAction action)
    {
        var frame = CurrentFrame;
        if (frame is null || frame.CurrentFocusKey is null)
            return Rejected(LauncherSemanticFocusReasonCodes.FocusNotReady, action);

        if (!frame.Nodes.TryGetValue(frame.CurrentFocusKey, out var current) || !current.IsAvailable)
            return Rejected(LauncherSemanticFocusReasonCodes.FocusNotReady, action);

        return action switch
        {
            LauncherSemanticAction.NavUp
                or LauncherSemanticAction.NavDown
                or LauncherSemanticAction.NavLeft
                or LauncherSemanticAction.NavRight => MoveDirectional(action, current),
            LauncherSemanticAction.FocusHome => MoveTo(
                action,
                frame.Definition.DefaultFocusKey,
                LauncherSemanticFocusReasonCodes.FocusHome),
            LauncherSemanticAction.Activate => Activate(action, current),
            _ => Decision(
                LauncherSemanticDispatchKind.CommandIssued,
                LauncherSemanticFocusReasonCodes.CommandIssued,
                action,
                invocationId: null)
        };
    }

    private LauncherSemanticFocusDecision SetLogicalFocusCore(string focusKey, string reasonCode)
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
                reasonCode,
                action: null,
                invocationId: null);
        }

        frame.CurrentFocusKey = focusKey;
        _revision = checked(_revision + 1);
        return Decision(
            LauncherSemanticDispatchKind.FocusMoved,
            reasonCode,
            action: null,
            invocationId: null);
    }

    private LauncherSemanticFocusDecision MoveDirectional(
        LauncherSemanticAction action,
        LauncherFocusNode current)
    {
        var frame = CurrentFrame!;
        var explicitTargetKey = ExplicitTarget(current, action);
        if (explicitTargetKey is not null
            && frame.Nodes.TryGetValue(explicitTargetKey, out var explicitTarget)
            && explicitTarget.IsAvailable)
        {
            return MoveTo(action, explicitTargetKey, LauncherSemanticFocusReasonCodes.FocusMoved);
        }

        var geometricTargetKey = FindGeometricTarget(frame, current, action);
        if (geometricTargetKey is not null)
            return MoveTo(action, geometricTargetKey, LauncherSemanticFocusReasonCodes.GeometricFallback);

        var routeFallbackKey = RouteFallback(frame.Definition, action);
        if (routeFallbackKey is not null
            && frame.Nodes.TryGetValue(routeFallbackKey, out var routeFallback)
            && routeFallback.IsAvailable)
        {
            return MoveTo(action, routeFallbackKey, LauncherSemanticFocusReasonCodes.RouteFallback);
        }

        return Decision(
            LauncherSemanticDispatchKind.NoOp,
            explicitTargetKey is null
                ? LauncherSemanticFocusReasonCodes.NavigationBoundary
                : LauncherSemanticFocusReasonCodes.TargetUnavailable,
            action,
            invocationId: null);
    }

    private LauncherSemanticFocusDecision MoveTo(
        LauncherSemanticAction action,
        string targetKey,
        string reasonCode)
    {
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
            reasonCode,
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

    private static string? ExplicitTarget(LauncherFocusNode node, LauncherSemanticAction action)
        => action switch
        {
            LauncherSemanticAction.NavUp => node.Up,
            LauncherSemanticAction.NavDown => node.Down,
            LauncherSemanticAction.NavLeft => node.Left,
            LauncherSemanticAction.NavRight => node.Right,
            _ => null
        };

    private static string? RouteFallback(FocusScope scope, LauncherSemanticAction action)
        => action switch
        {
            LauncherSemanticAction.NavUp => scope.Fallbacks.Up,
            LauncherSemanticAction.NavDown => scope.Fallbacks.Down,
            LauncherSemanticAction.NavLeft => scope.Fallbacks.Left,
            LauncherSemanticAction.NavRight => scope.Fallbacks.Right,
            _ => null
        };

    private static string? FindGeometricTarget(
        FocusFrame frame,
        LauncherFocusNode current,
        LauncherSemanticAction action)
    {
        if (current.Bounds is null)
            return null;

        var currentBounds = current.Bounds;
        var candidates = frame.Nodes.Values
            .Where(node => node.IsAvailable
                && node.Bounds is not null
                && !string.Equals(node.FocusKey, current.FocusKey, StringComparison.Ordinal))
            .Select(node => new
            {
                Node = node,
                Distance = SquaredDistance(currentBounds, node.Bounds!),
                InDirection = IsInDirection(currentBounds, node.Bounds!, action)
            })
            .Where(candidate => candidate.InDirection)
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Node.FocusKey, StringComparer.Ordinal)
            .FirstOrDefault();

        return candidates?.Node.FocusKey;
    }

    private static bool IsInDirection(
        LauncherFocusRect current,
        LauncherFocusRect candidate,
        LauncherSemanticAction action)
        => action switch
        {
            LauncherSemanticAction.NavUp => candidate.CenterY < current.CenterY,
            LauncherSemanticAction.NavDown => candidate.CenterY > current.CenterY,
            LauncherSemanticAction.NavLeft => candidate.CenterX < current.CenterX,
            LauncherSemanticAction.NavRight => candidate.CenterX > current.CenterX,
            _ => false
        };

    private static double SquaredDistance(LauncherFocusRect first, LauncherFocusRect second)
    {
        var dx = first.CenterX - second.CenterX;
        var dy = first.CenterY - second.CenterY;
        return (dx * dx) + (dy * dy);
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
            if (!nodes.TryAdd(node.FocusKey, node with { Bounds = node.Bounds?.Validate() }))
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

        var fallbacks = definition.Fallbacks ?? new LauncherDirectionalFocusFallbacks();
        ValidateFallback(fallbacks.Up, nodes);
        ValidateFallback(fallbacks.Down, nodes);
        ValidateFallback(fallbacks.Left, nodes);
        ValidateFallback(fallbacks.Right, nodes);

        return new FocusScope(definition.ScopeKey, definition.DefaultFocusKey, nodes, fallbacks);
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

    private static void ValidateFallback(
        string? targetKey,
        IReadOnlyDictionary<string, LauncherFocusNode> nodes)
    {
        if (targetKey is not null && !nodes.ContainsKey(targetKey))
            throw new InvalidDataException($"Route focus fallback '{targetKey}' is unresolved.");
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
        public FocusScope Definition { get; private set; } = definition;
        public IReadOnlyDictionary<string, LauncherFocusNode> Nodes => Definition.Nodes;
        public string? CurrentFocusKey { get; set; } = currentFocusKey;

        public void ReplaceDefinition(FocusScope definition, string currentFocusKey)
        {
            Definition = definition;
            CurrentFocusKey = currentFocusKey;
        }
    }

    private sealed record FocusScope(
        string ScopeKey,
        string DefaultFocusKey,
        IReadOnlyDictionary<string, LauncherFocusNode> Nodes,
        LauncherDirectionalFocusFallbacks Fallbacks);
}
