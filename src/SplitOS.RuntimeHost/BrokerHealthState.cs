namespace SplitOS.RuntimeHost;

public sealed record BrokerHealthSnapshot(
    bool Healthy,
    string Status,
    string? Reason,
    int? ProcessId,
    int? SessionId,
    DateTimeOffset ObservedAtUtc);

public sealed class BrokerHealthState
{
    private BrokerHealthSnapshot _snapshot = new(
        Healthy: false,
        Status: "STARTING",
        Reason: null,
        ProcessId: null,
        SessionId: null,
        ObservedAtUtc: DateTimeOffset.UtcNow);

    public BrokerHealthSnapshot Snapshot => Volatile.Read(ref _snapshot);

    public void ReportHealthy(int processId, int sessionId)
        => Volatile.Write(ref _snapshot, new BrokerHealthSnapshot(
            Healthy: true,
            Status: "HEALTHY",
            Reason: null,
            ProcessId: processId,
            SessionId: sessionId,
            ObservedAtUtc: DateTimeOffset.UtcNow));

    public void ReportUnavailable(string reason)
        => Volatile.Write(ref _snapshot, new BrokerHealthSnapshot(
            Healthy: false,
            Status: "DEGRADED_BROKER_UNAVAILABLE",
            Reason: reason,
            ProcessId: null,
            SessionId: null,
            ObservedAtUtc: DateTimeOffset.UtcNow));
}
