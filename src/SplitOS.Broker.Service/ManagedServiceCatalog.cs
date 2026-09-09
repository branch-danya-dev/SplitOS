namespace SplitOS.Broker.Service;

public enum ManagedServiceDesiredState
{
    Running,
    Stopped
}

public enum ManagedServiceObservedState
{
    Stopped,
    StartPending,
    StopPending,
    Running,
    Paused,
    OtherTransitional,
    NotFound,
    AccessDenied,
    Unknown
}

public enum ManagedServiceApplyDisposition
{
    AlreadySatisfied,
    AppliedVerified,
    TargetNotFound,
    UnsupportedCapability,
    AccessDenied,
    OperationRejected,
    VerificationFailed,
    TechnicalFailure
}

public sealed record ManagedServiceCatalogEntry(
    string ManagedServiceId,
    string WindowsServiceName,
    IReadOnlySet<ManagedServiceDesiredState> AllowedStates,
    TimeSpan TransitionTimeout,
    int MinimumWindowsBuild,
    int? MaximumWindowsBuild = null);

public sealed record ManagedServiceObservation(
    ManagedServiceObservedState State,
    uint WaitHintMilliseconds = 0,
    int? NativeErrorCode = null);

public sealed record ManagedServiceTechnicalResult(
    ManagedServiceApplyDisposition Disposition,
    bool OperationAttempted,
    string ImmediateResult,
    ManagedServiceObservedState ActualState,
    string VerificationStatus,
    string? ErrorCode = null,
    int? NativeErrorCode = null)
{
    public bool Verified => Disposition is
        ManagedServiceApplyDisposition.AlreadySatisfied or
        ManagedServiceApplyDisposition.AppliedVerified;
}

public interface IManagedServiceCatalog
{
    bool TryResolve(string managedServiceId, out ManagedServiceCatalogEntry entry);
}

public interface IManagedServiceAdapter
{
    ValueTask<ManagedServiceObservation> QueryAsync(
        ManagedServiceCatalogEntry entry,
        CancellationToken cancellationToken = default);

    ValueTask<ManagedServiceTechnicalResult> ApplyAsync(
        ManagedServiceCatalogEntry entry,
        ManagedServiceDesiredState desiredState,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Release-owned v1 service target catalog. IPC callers see only ManagedServiceId values;
/// raw SCM key names remain inside the privileged Broker release.
/// </summary>
public sealed class ReleaseManagedServiceCatalog : IManagedServiceCatalog
{
    private static readonly IReadOnlyDictionary<string, ManagedServiceCatalogEntry> Entries =
        new Dictionary<string, ManagedServiceCatalogEntry>(StringComparer.Ordinal)
        {
            ["SEARCH_INDEXER"] = new(
                "SEARCH_INDEXER",
                "WSearch",
                new HashSet<ManagedServiceDesiredState>
                {
                    ManagedServiceDesiredState.Running,
                    ManagedServiceDesiredState.Stopped
                },
                TimeSpan.FromSeconds(20),
                19041)
        };

    public bool TryResolve(string managedServiceId, out ManagedServiceCatalogEntry entry)
    {
        if (managedServiceId is null)
        {
            entry = null!;
            return false;
        }

        return Entries.TryGetValue(managedServiceId, out entry!);
    }
}
