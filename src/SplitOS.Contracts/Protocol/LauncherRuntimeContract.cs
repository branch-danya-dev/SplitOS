namespace SplitOS.Contracts.Protocol;

/// <summary>
/// Requests one coherent Runtime-owned snapshot for rebuilding Launcher presentation after connect/reconnect.
/// The request intentionally contains no caller-supplied mode, session, game, or entitlement truth.
/// </summary>
public sealed record LauncherRuntimeSnapshotRequest;

public sealed record LauncherRuntimeSnapshotResult(
    string RuntimeStatus,
    string ManagedRuntimeAccess,
    string CommittedMode,
    string GameSessionState,
    long GameSessionRevision,
    string? ActiveLaunchOperationId,
    string? ActiveLaunchCorrelationId,
    string? ActiveGameId,
    long SnapshotVersion,
    DateTimeOffset ObservedAtUtc);

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
