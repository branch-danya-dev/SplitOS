namespace SplitOS.RuntimeHost.GameRuntime;

public interface ILauncherGameModeReadinessCoordinator
{
    LauncherReadinessExpectation Arm(Guid operationId, Guid correlationId);
    bool IsReady(Guid operationId, Guid correlationId);
    void Clear(Guid operationId, Guid correlationId);
}

/// <summary>
/// Mode-runtime-facing semantic boundary for the UX_READINESS_HANDSHAKE verification class.
/// The mode operation owns expectation lifecycle; the Launcher itself can only acknowledge through IPC.
/// </summary>
public sealed class LauncherGameModeReadinessCoordinator(LauncherReadinessState state)
    : ILauncherGameModeReadinessCoordinator
{
    public LauncherReadinessExpectation Arm(Guid operationId, Guid correlationId)
        => state.Arm(operationId, correlationId);

    public bool IsReady(Guid operationId, Guid correlationId)
    {
        var snapshot = state.Snapshot;
        return snapshot.IsReady
            && snapshot.ExpectedOperation?.OperationId == operationId
            && snapshot.ExpectedOperation.CorrelationId == correlationId;
    }

    public void Clear(Guid operationId, Guid correlationId)
        => state.Clear(operationId, correlationId);
}
