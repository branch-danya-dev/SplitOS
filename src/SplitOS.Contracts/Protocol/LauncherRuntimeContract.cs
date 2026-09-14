namespace SplitOS.Contracts.Protocol;

/// <summary>
/// Requests one coherent Runtime-owned snapshot for rebuilding Launcher presentation after connect/reconnect.
/// The request intentionally contains no caller-supplied mode, session, game, or entitlement truth.
/// </summary>
public sealed record LauncherRuntimeSnapshotRequest;

/// <summary>
/// Runtime-owned launch-operation presentation projected from canonical GameSession truth plus explicit
/// Runtime subsystem outcomes. AllowedActions is authoritative: Launcher must never infer a retry,
/// cancellation, client-open, or keep-waiting action that is not present here.
/// </summary>
public sealed record LauncherLaunchPresentationResult(
    string LaunchOperationId,
    string CorrelationId,
    string GameId,
    string? Phase,
    string? FailureClass,
    string? ExternalClientOutcome,
    IReadOnlyList<string> AllowedActions,
    long RuntimePresentationRevision,
    DateTimeOffset ObservedAtUtc);

public sealed record LauncherRuntimeSnapshotResult(
    string RuntimeStatus,
    string ManagedRuntimeAccess,
    string CommittedMode,
    string GameSessionState,
    long GameSessionRevision,
    string? ActiveLaunchOperationId,
    string? ActiveLaunchCorrelationId,
    string? ActiveGameId,
    Guid? ExpectedGameModeOperationId,
    Guid? ExpectedGameModeCorrelationId,
    long ReadinessRevision,
    long SnapshotVersion,
    DateTimeOffset ObservedAtUtc,
    LauncherLaunchPresentationResult? LaunchPresentation = null);

/// <summary>
/// Semantic readiness acknowledgement for the exact GAME mode operation currently expected by Runtime.
/// Caller identity is derived from the authenticated Runtime UI pipe; no process identity is accepted here.
/// </summary>
public sealed record LauncherReadyForGameModeRequest(
    Guid OperationId,
    Guid CorrelationId);

public sealed record LauncherReadyForGameModeResult(
    string Disposition,
    string ProductCode,
    Guid OperationId,
    Guid CorrelationId,
    long ReadinessRevision,
    DateTimeOffset ObservedAtUtc);
