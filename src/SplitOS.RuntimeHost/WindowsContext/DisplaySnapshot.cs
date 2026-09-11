namespace SplitOS.RuntimeHost.WindowsContext;

public readonly record struct DisplayRational
{
    public DisplayRational(uint numerator, uint denominator)
    {
        if (denominator == 0)
            throw new ArgumentOutOfRangeException(nameof(denominator), "Display refresh denominator must be non-zero.");

        Numerator = numerator;
        Denominator = denominator;
    }

    public uint Numerator { get; }
    public uint Denominator { get; }
    public double Hertz => (double)Numerator / Denominator;
}

public readonly record struct DisplayPixelSize(uint Width, uint Height)
{
    public bool IsValid => Width > 0 && Height > 0;
}

public readonly record struct DisplayDesktopPoint(int X, int Y);

public readonly record struct DisplayPathKey(long AdapterLuid, uint TargetId);

public sealed record DisplayTargetIdentityEvidence(
    string? MonitorDevicePath,
    string? FriendlyMonitorName,
    ushort? EdidManufactureId,
    ushort? EdidProductCodeId,
    uint ConnectorInstance,
    int OutputTechnology,
    long AdapterLuidHint,
    string? PnpDeviceInstanceId = null)
{
    public bool HasStrongDevicePath => !string.IsNullOrWhiteSpace(MonitorDevicePath);
    public bool HasPnpDeviceInstanceId => !string.IsNullOrWhiteSpace(PnpDeviceInstanceId);
    public bool HasEdidPair => EdidManufactureId.HasValue && EdidProductCodeId.HasValue;
}

public sealed record DisplayPathEvidence(
    long SourceAdapterLuid,
    uint SourceId,
    DisplayPathKey TargetKey,
    bool Active,
    bool TargetAvailable,
    int OutputTechnology,
    uint Rotation,
    uint Scaling,
    DisplayRational? RefreshRate,
    DisplayPixelSize? SourceResolution = null,
    DisplayDesktopPoint? SourcePosition = null,
    bool SupportsVirtualMode = false,
    bool BoostRefreshRate = false,
    DisplayTargetIdentityEvidence? Identity = null);

public sealed record DisplaySnapshot(
    long Generation,
    DateTimeOffset ObservedUtc,
    IReadOnlyList<DisplayPathEvidence> Paths);

public interface IDisplayGenerationTracker
{
    long CurrentGeneration { get; }
    long Invalidate(string reason);
}

public sealed class DisplayGenerationTracker : IDisplayGenerationTracker
{
    private long _generation = 1;

    public long CurrentGeneration => Interlocked.Read(ref _generation);

    public long Invalidate(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Display invalidation reason must not be empty.", nameof(reason));

        return Interlocked.Increment(ref _generation);
    }
}

public interface IDisplayConfigQuery
{
    IReadOnlyList<DisplayPathEvidence> QueryActivePaths();
}

public interface IDisplaySnapshotReader
{
    DisplaySnapshot Read();
}

public sealed class DisplaySnapshotReader(
    IDisplayConfigQuery displayConfigQuery,
    IDisplayGenerationTracker generationTracker,
    TimeProvider? timeProvider = null) : IDisplaySnapshotReader
{
    private const int MaxStableReadAttempts = 4;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public DisplaySnapshot Read()
    {
        for (var attempt = 0; attempt < MaxStableReadAttempts; attempt++)
        {
            var before = generationTracker.CurrentGeneration;
            var paths = displayConfigQuery.QueryActivePaths();
            var after = generationTracker.CurrentGeneration;

            if (before == after)
            {
                return new DisplaySnapshot(
                    after,
                    _timeProvider.GetUtcNow(),
                    paths.ToArray());
            }
        }

        throw new InvalidOperationException(
            "Display topology changed repeatedly while building a snapshot; stale evidence was not published.");
    }
}
