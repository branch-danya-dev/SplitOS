using SplitOS.Contracts.Protocol;

namespace SplitOS.RuntimeHost;

public sealed class RuntimeStateState
{
    private RuntimeStateReadResult _snapshot = new(
        "STARTING", "DISABLED", "NONE", "UNASSOCIATED", 0, 0, 0, DateTimeOffset.UtcNow);

    public RuntimeStateReadResult Snapshot => Volatile.Read(ref _snapshot);

    public void Report(RuntimeStateReadResult snapshot) => Volatile.Write(ref _snapshot, snapshot);

    public void ReportUnavailable()
    {
        var current = Snapshot;
        Report(current with { Status = "DEGRADED_PERSISTENCE_UNAVAILABLE", ObservedAtUtc = DateTimeOffset.UtcNow });
    }
}
