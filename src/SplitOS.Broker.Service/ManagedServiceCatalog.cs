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

[Flags]
public enum ManagedServiceScmAccess : uint
{
    None = 0,
    QueryStatus = 0x0004,
    Start = 0x0010,
    Stop = 0x0020
}

/// <summary>
/// Release-owned minimum SCM access required for observation and each supported mutation.
/// Runtime/UI callers never supply these masks.
/// </summary>
public sealed record ManagedServiceAccessPolicy(
    ManagedServiceScmAccess QueryAccess,
    ManagedServiceScmAccess StartAccess,
    ManagedServiceScmAccess StopAccess)
{
    public static ManagedServiceAccessPolicy QueryStartStop { get; } = new(
        ManagedServiceScmAccess.QueryStatus,
        ManagedServiceScmAccess.QueryStatus | ManagedServiceScmAccess.Start,
        ManagedServiceScmAccess.QueryStatus | ManagedServiceScmAccess.Stop);

    public ManagedServiceScmAccess Resolve(ManagedServiceDesiredState desiredState)
        => desiredState switch
        {
            ManagedServiceDesiredState.Running => StartAccess,
            ManagedServiceDesiredState.Stopped => StopAccess,
            _ => throw new ArgumentOutOfRangeException(nameof(desiredState), desiredState, null)
        };

    public void Validate()
    {
        if (QueryAccess != ManagedServiceScmAccess.QueryStatus)
            throw new InvalidOperationException("Managed service query access must be SERVICE_QUERY_STATUS only.");

        if (StartAccess != (ManagedServiceScmAccess.QueryStatus | ManagedServiceScmAccess.Start))
            throw new InvalidOperationException("Managed service start access must be QUERY_STATUS | START only.");

        if (StopAccess != (ManagedServiceScmAccess.QueryStatus | ManagedServiceScmAccess.Stop))
            throw new InvalidOperationException("Managed service stop access must be QUERY_STATUS | STOP only.");
    }
}

public enum ManagedServiceDependencyFallback
{
    RejectMutation,
    LeaveRunning
}

/// <summary>
/// Release-owned dependency semantics. The Broker never recursively discovers/stops arbitrary
/// dependent services; any planned dependency must itself be represented by a ManagedServiceId.
/// </summary>
public sealed record ManagedServiceDependencyPolicy(
    IReadOnlyList<string> RequiredManagedServiceIds,
    ManagedServiceDependencyFallback Fallback)
{
    public static ManagedServiceDependencyPolicy NoAutomaticFanOut { get; } = new(
        Array.Empty<string>(),
        ManagedServiceDependencyFallback.RejectMutation);
}

public sealed record ManagedServiceCatalogEntry(
    string ManagedServiceId,
    string WindowsServiceName,
    IReadOnlySet<ManagedServiceDesiredState> AllowedStates,
    TimeSpan TransitionTimeout,
    int MinimumWindowsBuild,
    int? MaximumWindowsBuild = null)
{
    public ManagedServiceAccessPolicy AccessPolicy { get; init; } = ManagedServiceAccessPolicy.QueryStartStop;

    public ManagedServiceDependencyPolicy DependencyPolicy { get; init; } =
        ManagedServiceDependencyPolicy.NoAutomaticFanOut;
}

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
/// raw SCM key names and access masks remain inside the privileged Broker release.
/// </summary>
public sealed class ReleaseManagedServiceCatalog : IManagedServiceCatalog
{
    private static readonly IReadOnlyDictionary<string, ManagedServiceCatalogEntry> Entries =
        CreateValidatedEntries();

    public bool TryResolve(string managedServiceId, out ManagedServiceCatalogEntry entry)
    {
        if (managedServiceId is null)
        {
            entry = null!;
            return false;
        }

        return Entries.TryGetValue(managedServiceId, out entry!);
    }

    private static IReadOnlyDictionary<string, ManagedServiceCatalogEntry> CreateValidatedEntries()
    {
        var entries = new Dictionary<string, ManagedServiceCatalogEntry>(StringComparer.Ordinal)
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
            {
                AccessPolicy = ManagedServiceAccessPolicy.QueryStartStop,
                DependencyPolicy = ManagedServiceDependencyPolicy.NoAutomaticFanOut
            }
        };

        foreach (var pair in entries)
            ValidateEntry(pair.Key, pair.Value, entries);

        return entries;
    }

    private static void ValidateEntry(
        string key,
        ManagedServiceCatalogEntry entry,
        IReadOnlyDictionary<string, ManagedServiceCatalogEntry> entries)
    {
        if (!string.Equals(key, entry.ManagedServiceId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(entry.ManagedServiceId) ||
            string.IsNullOrWhiteSpace(entry.WindowsServiceName))
        {
            throw new InvalidOperationException("Managed service catalog identity is invalid.");
        }

        if (entry.AllowedStates.Count == 0)
            throw new InvalidOperationException($"Managed service {key} has no allowed desired states.");

        if (entry.TransitionTimeout <= TimeSpan.Zero)
            throw new InvalidOperationException($"Managed service {key} has an invalid transition timeout.");

        if (entry.MinimumWindowsBuild < 0 ||
            (entry.MaximumWindowsBuild.HasValue && entry.MaximumWindowsBuild.Value < entry.MinimumWindowsBuild))
        {
            throw new InvalidOperationException($"Managed service {key} has invalid Windows build constraints.");
        }

        entry.AccessPolicy.Validate();

        foreach (var dependencyId in entry.DependencyPolicy.RequiredManagedServiceIds)
        {
            if (string.IsNullOrWhiteSpace(dependencyId) ||
                string.Equals(dependencyId, key, StringComparison.Ordinal) ||
                !entries.ContainsKey(dependencyId))
            {
                throw new InvalidOperationException(
                    $"Managed service {key} has an invalid managed dependency '{dependencyId}'.");
            }
        }
    }
}
