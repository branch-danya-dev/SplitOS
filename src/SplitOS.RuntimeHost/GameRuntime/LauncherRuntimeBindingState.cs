using SplitOS.Contracts.Protocol;

namespace SplitOS.RuntimeHost.GameRuntime;

public static class LauncherReadinessProductCodes
{
    public const string ReadyAccepted = "READY_FOR_GAME_MODE_PRESENTATION";
    public const string ReadyAlreadyRecorded = "READY_ALREADY_RECORDED";
    public const string NoExpectedOperation = "NO_EXPECTED_GAME_MODE_OPERATION";
    public const string OperationMismatch = "GAME_MODE_OPERATION_MISMATCH";
}

public sealed record LauncherReadinessExpectation(
    Guid OperationId,
    Guid CorrelationId,
    long Revision);

public sealed record LauncherReadinessSnapshot(
    LauncherReadinessExpectation? ExpectedOperation,
    bool IsReady,
    long Revision,
    DateTimeOffset ObservedAtUtc);

public sealed class LauncherReadinessState
{
    private readonly object _gate = new();
    private LauncherReadinessExpectation? _expected;
    private bool _isReady;
    private long _revision;
    private DateTimeOffset _observedAtUtc = DateTimeOffset.UtcNow;

    public LauncherReadinessSnapshot Snapshot
    {
        get
        {
            lock (_gate)
                return new LauncherReadinessSnapshot(_expected, _isReady, _revision, _observedAtUtc);
        }
    }

    public LauncherReadinessExpectation Arm(Guid operationId, Guid correlationId)
    {
        ValidateIdentity(operationId, correlationId);
        lock (_gate)
        {
            if (_expected is not null)
            {
                if (_expected.OperationId == operationId && _expected.CorrelationId == correlationId)
                    return _expected;
                throw new InvalidOperationException(
                    "A different GAME mode operation already owns the Launcher readiness expectation.");
            }

            _revision = checked(_revision + 1);
            _expected = new LauncherReadinessExpectation(operationId, correlationId, _revision);
            _isReady = false;
            _observedAtUtc = DateTimeOffset.UtcNow;
            return _expected;
        }
    }

    public void Clear(Guid operationId, Guid correlationId)
    {
        ValidateIdentity(operationId, correlationId);
        lock (_gate)
        {
            if (_expected is null)
                return;
            if (_expected.OperationId != operationId || _expected.CorrelationId != correlationId)
                throw new InvalidOperationException(
                    "Cannot clear Launcher readiness owned by a different GAME mode operation.");

            _revision = checked(_revision + 1);
            _expected = null;
            _isReady = false;
            _observedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    public LauncherReadyForGameModeResult ReportReady(Guid operationId, Guid correlationId)
    {
        ValidateIdentity(operationId, correlationId);
        lock (_gate)
        {
            if (_expected is null)
                return Result("REJECTED", LauncherReadinessProductCodes.NoExpectedOperation, operationId, correlationId);
            if (_expected.OperationId != operationId || _expected.CorrelationId != correlationId)
                return Result("REJECTED", LauncherReadinessProductCodes.OperationMismatch, operationId, correlationId);
            if (_isReady)
                return Result("NO_OP", LauncherReadinessProductCodes.ReadyAlreadyRecorded, operationId, correlationId);

            _revision = checked(_revision + 1);
            _isReady = true;
            _observedAtUtc = DateTimeOffset.UtcNow;
            return Result("ACCEPTED", LauncherReadinessProductCodes.ReadyAccepted, operationId, correlationId);
        }
    }

    private LauncherReadyForGameModeResult Result(
        string disposition,
        string productCode,
        Guid operationId,
        Guid correlationId)
        => new(disposition, productCode, operationId, correlationId, _revision, _observedAtUtc);

    private static void ValidateIdentity(Guid operationId, Guid correlationId)
    {
        if (operationId == Guid.Empty)
            throw new InvalidDataException("Launcher readiness operation ID cannot be empty.");
        if (correlationId == Guid.Empty)
            throw new InvalidDataException("Launcher readiness correlation ID cannot be empty.");
    }
}

public sealed class LauncherRuntimeSnapshotProvider(
    RuntimeStateState runtimeState,
    GameSessionStateMachine gameSession,
    LauncherReadinessState readinessState)
{
    private readonly object _gate = new();
    private LauncherRuntimeSnapshotResult? _lastSnapshot;
    private long _snapshotVersion;

    public LauncherRuntimeSnapshotResult Read()
    {
        lock (_gate)
        {
            var runtime = runtimeState.Snapshot;
            var session = gameSession.Snapshot;
            var readiness = readinessState.Snapshot;
            var active = session.ActiveLaunch;
            var expected = readiness.ExpectedOperation;
            var sessionState = ToWireState(session.State);

            if (_lastSnapshot is null || SemanticStateChanged(_lastSnapshot, runtime, session, readiness, sessionState))
                _snapshotVersion = checked(_snapshotVersion + 1);

            var snapshot = new LauncherRuntimeSnapshotResult(
                runtime.Status,
                runtime.ManagedRuntimeAccess,
                runtime.OperationalMode,
                sessionState,
                session.Revision,
                active?.LaunchOperationId,
                active?.CorrelationId,
                active?.GameId,
                expected?.OperationId,
                expected?.CorrelationId,
                readiness.Revision,
                _snapshotVersion,
                DateTimeOffset.UtcNow);

            _lastSnapshot = snapshot;
            return snapshot;
        }
    }

    private static bool SemanticStateChanged(
        LauncherRuntimeSnapshotResult previous,
        RuntimeStateReadResult runtime,
        GameSessionSnapshot session,
        LauncherReadinessSnapshot readiness,
        string sessionState)
    {
        var active = session.ActiveLaunch;
        var expected = readiness.ExpectedOperation;
        return !string.Equals(previous.RuntimeStatus, runtime.Status, StringComparison.Ordinal)
            || !string.Equals(previous.ManagedRuntimeAccess, runtime.ManagedRuntimeAccess, StringComparison.Ordinal)
            || !string.Equals(previous.CommittedMode, runtime.OperationalMode, StringComparison.Ordinal)
            || !string.Equals(previous.GameSessionState, sessionState, StringComparison.Ordinal)
            || previous.GameSessionRevision != session.Revision
            || !string.Equals(previous.ActiveLaunchOperationId, active?.LaunchOperationId, StringComparison.Ordinal)
            || !string.Equals(previous.ActiveLaunchCorrelationId, active?.CorrelationId, StringComparison.Ordinal)
            || !string.Equals(previous.ActiveGameId, active?.GameId, StringComparison.Ordinal)
            || previous.ExpectedGameModeOperationId != expected?.OperationId
            || previous.ExpectedGameModeCorrelationId != expected?.CorrelationId
            || previous.ReadinessRevision != readiness.Revision;
    }

    internal static string ToWireState(GameSessionState state)
        => state switch
        {
            GameSessionState.Inactive => "INACTIVE",
            GameSessionState.Launcher => "LAUNCHER",
            GameSessionState.Preparing => "PREPARING",
            GameSessionState.ClientHandoff => "CLIENT_HANDOFF",
            GameSessionState.GameStarting => "GAME_STARTING",
            GameSessionState.GameRunning => "GAME_RUNNING",
            GameSessionState.GameExitDetected => "GAME_EXIT_DETECTED",
            GameSessionState.ReturningToLauncher => "RETURNING_TO_LAUNCHER",
            GameSessionState.Failed => "FAILED",
            _ => throw new InvalidDataException($"Unsupported GameSession state {state}.")
        };
}
