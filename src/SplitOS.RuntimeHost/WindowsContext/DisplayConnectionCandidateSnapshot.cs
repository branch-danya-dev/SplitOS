namespace SplitOS.RuntimeHost.WindowsContext;

public sealed record DisplayConnectionCandidateSnapshot(
    long Generation,
    DateTimeOffset ObservedUtc,
    IReadOnlyList<DisplayConnectionCandidate> Candidates);

public interface IDisplayConnectionCandidateReader
{
    DisplayConnectionCandidateSnapshot Read();
}

public sealed class DisplayConnectionCandidateReader(
    IDisplayConnectionCandidateQuery candidateQuery,
    IDisplayGenerationTracker generationTracker,
    TimeProvider? timeProvider = null) : IDisplayConnectionCandidateReader
{
    private const int MaxStableReadAttempts = 4;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public DisplayConnectionCandidateSnapshot Read()
    {
        for (var attempt = 0; attempt < MaxStableReadAttempts; attempt++)
        {
            var before = generationTracker.CurrentGeneration;
            var candidates = candidateQuery.QueryAllCandidates();
            var after = generationTracker.CurrentGeneration;

            if (before == after)
            {
                return new DisplayConnectionCandidateSnapshot(
                    after,
                    _timeProvider.GetUtcNow(),
                    candidates.ToArray());
            }
        }

        throw new InvalidOperationException(
            "Display topology changed repeatedly while building the all-path connection catalog; stale candidates were not published.");
    }
}
