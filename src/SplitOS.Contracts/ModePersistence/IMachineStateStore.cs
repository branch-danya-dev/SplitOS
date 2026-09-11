namespace SplitOS.Persistence.Machine;

public interface IMachineStateStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<OperationalModeRecord> GetOperationalModeAsync(CancellationToken cancellationToken = default);
}
