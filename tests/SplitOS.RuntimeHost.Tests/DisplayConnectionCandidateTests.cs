using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class DisplayConnectionCandidateTests
{
    [TestMethod]
    public void AllPathQueryRetriesInsufficientBufferAndPreservesPriorityOrder()
    {
        var first = Candidate(0, sourceId: 1, targetId: 10, active: true);
        var second = Candidate(1, sourceId: 2, targetId: 11, active: false);
        var interop = new FakeAllPathInterop(
            new DisplayConnectionQueryAttempt(122, Array.Empty<DisplayConnectionCandidate>()),
            new DisplayConnectionQueryAttempt(0, new[] { first, second }));
        var query = new WindowsDisplayConnectionCandidateQuery(interop);

        var result = query.QueryAllCandidates();

        Assert.AreEqual(2, interop.SizingCalls);
        Assert.AreEqual(2, interop.QueryCalls);
        CollectionAssert.AreEqual(
            new[] { first.TargetKey, second.TargetKey },
            result.Select(candidate => candidate.TargetKey).ToArray());
        CollectionAssert.AreEqual(
            new[] { 0, 1 },
            result.Select(candidate => candidate.PriorityOrdinal).ToArray());
    }

    [TestMethod]
    public void CandidateReaderRetriesWhenGenerationChangesDuringAllPathsRead()
    {
        var tracker = new DisplayGenerationTracker();
        var query = new InvalidationCandidateQuery(tracker, invalidateEveryRead: false);
        var reader = new DisplayConnectionCandidateReader(query, tracker);

        var snapshot = reader.Read();

        Assert.AreEqual(2, query.Calls);
        Assert.AreEqual(2L, snapshot.Generation);
        Assert.AreEqual(1, snapshot.Candidates.Count);
        Assert.AreEqual(new DisplayPathKey(100, 10), snapshot.Candidates[0].TargetKey);
    }

    [TestMethod]
    public void CandidateReaderFailsClosedWhenTopologyNeverStabilizes()
    {
        var tracker = new DisplayGenerationTracker();
        var query = new InvalidationCandidateQuery(tracker, invalidateEveryRead: true);
        var reader = new DisplayConnectionCandidateReader(query, tracker);

        Assert.ThrowsExactly<InvalidOperationException>(() => reader.Read());
        Assert.AreEqual(4, query.Calls);
        Assert.AreEqual(5L, tracker.CurrentGeneration);
    }

    private static DisplayConnectionCandidate Candidate(
        int priority,
        uint sourceId,
        uint targetId,
        bool active) => new(
        PriorityOrdinal: priority,
        SourceAdapterLuid: 100,
        SourceId: sourceId,
        TargetKey: new DisplayPathKey(100, targetId),
        Active: active,
        Identity: new DisplayTargetIdentityEvidence(
            $"MONITOR#{targetId}",
            $"Monitor {targetId}",
            1,
            checked((ushort)targetId),
            targetId,
            5,
            100,
            $"DISPLAY\\MON{targetId}\\UID"));

    private sealed class FakeAllPathInterop(params DisplayConnectionQueryAttempt[] attempts) : IWindowsDisplayConnectionInterop
    {
        private readonly Queue<DisplayConnectionQueryAttempt> _attempts = new(attempts);

        public int SizingCalls { get; private set; }
        public int QueryCalls { get; private set; }

        public DisplayConfigBufferSizingResult GetAllPathBufferSizes()
        {
            SizingCalls++;
            return new DisplayConfigBufferSizingResult(0, 2, 1);
        }

        public DisplayConnectionQueryAttempt QueryAll(uint pathCapacity, uint modeCapacity)
        {
            QueryCalls++;
            Assert.AreEqual(2u, pathCapacity);
            Assert.AreEqual(1u, modeCapacity);
            return _attempts.Dequeue();
        }
    }

    private sealed class InvalidationCandidateQuery(
        IDisplayGenerationTracker tracker,
        bool invalidateEveryRead) : IDisplayConnectionCandidateQuery
    {
        public int Calls { get; private set; }

        public IReadOnlyList<DisplayConnectionCandidate> QueryAllCandidates()
        {
            Calls++;
            if (invalidateEveryRead || Calls == 1)
                tracker.Invalidate("test-all-path-topology-change");

            return new[] { Candidate(0, sourceId: 1, targetId: 10, active: false) };
        }
    }
}
