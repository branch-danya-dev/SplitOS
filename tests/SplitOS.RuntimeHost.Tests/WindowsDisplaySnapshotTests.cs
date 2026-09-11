using System.ComponentModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class WindowsDisplaySnapshotTests
{
    [TestMethod]
    public void DisplayGenerationIsMonotonicAndRequiresReason()
    {
        var tracker = new DisplayGenerationTracker();

        Assert.AreEqual(1L, tracker.CurrentGeneration);
        Assert.AreEqual(2L, tracker.Invalidate("WM_DISPLAYCHANGE"));
        Assert.AreEqual(3L, tracker.Invalidate("device-interface-change"));
        Assert.AreEqual(3L, tracker.CurrentGeneration);
        Assert.ThrowsExactly<ArgumentException>(() => tracker.Invalidate(" "));
    }

    [TestMethod]
    public void SnapshotReaderRetriesWhenGenerationChangesDuringRead()
    {
        var tracker = new DisplayGenerationTracker();
        var query = new InvalidationQuery(tracker, invalidateEveryRead: false);
        var reader = new DisplaySnapshotReader(query, tracker);

        var snapshot = reader.Read();

        Assert.AreEqual(2L, snapshot.Generation);
        Assert.AreEqual(2, query.Calls);
        Assert.AreEqual(1, snapshot.Paths.Count);
        Assert.AreEqual(new DisplayPathKey(20, 7), snapshot.Paths[0].TargetKey);
        Assert.AreEqual(new DisplayRational(60000, 1001), snapshot.Paths[0].RefreshRate);
    }

    [TestMethod]
    public void SnapshotReaderFailsClosedWhenTopologyNeverStabilizes()
    {
        var tracker = new DisplayGenerationTracker();
        var query = new InvalidationQuery(tracker, invalidateEveryRead: true);
        var reader = new DisplaySnapshotReader(query, tracker);

        Assert.ThrowsExactly<InvalidOperationException>(() => reader.Read());
        Assert.AreEqual(4, query.Calls);
        Assert.AreEqual(5L, tracker.CurrentGeneration);
    }

    [TestMethod]
    public void DisplayConfigQueryRetriesInsufficientBuffer()
    {
        var expected = CreatePath();
        var interop = new FakeInterop(
            new DisplayConfigQueryAttempt(122, Array.Empty<DisplayPathEvidence>()),
            new DisplayConfigQueryAttempt(0, new[] { expected }));
        var query = new WindowsDisplayConfigQuery(interop);

        var paths = query.QueryActivePaths();

        Assert.AreEqual(2, interop.QueryCalls);
        Assert.AreEqual(2, interop.SizingCalls);
        Assert.AreEqual(1, paths.Count);
        Assert.AreEqual(expected, paths[0]);
    }

    [TestMethod]
    public void DisplayConfigQueryMapsUnexpectedWin32Failure()
    {
        var interop = new FakeInterop(new DisplayConfigQueryAttempt(5, Array.Empty<DisplayPathEvidence>()));
        var query = new WindowsDisplayConfigQuery(interop);

        var error = Assert.ThrowsExactly<Win32Exception>(() => query.QueryActivePaths());

        Assert.AreEqual(5, error.NativeErrorCode);
    }

    [TestMethod]
    public void RefreshRatePreservesRationalPrecision()
    {
        var refresh = new DisplayRational(60000, 1001);

        Assert.AreEqual(60000u, refresh.Numerator);
        Assert.AreEqual(1001u, refresh.Denominator);
        Assert.AreEqual(59.94005994005994, refresh.Hertz, 0.000000001);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new DisplayRational(60, 0));
    }

    private static DisplayPathEvidence CreatePath() => new(
        SourceAdapterLuid: 10,
        SourceId: 3,
        TargetKey: new DisplayPathKey(20, 7),
        Active: true,
        TargetAvailable: true,
        OutputTechnology: 5,
        Rotation: 1,
        Scaling: 1,
        RefreshRate: new DisplayRational(60000, 1001));

    private sealed class InvalidationQuery(
        IDisplayGenerationTracker tracker,
        bool invalidateEveryRead) : IDisplayConfigQuery
    {
        public int Calls { get; private set; }

        public IReadOnlyList<DisplayPathEvidence> QueryActivePaths()
        {
            Calls++;
            if (invalidateEveryRead || Calls == 1)
                tracker.Invalidate("test-topology-change");

            return new[] { CreatePath() };
        }
    }

    private sealed class FakeInterop(params DisplayConfigQueryAttempt[] attempts) : IWindowsDisplayConfigInterop
    {
        private readonly Queue<DisplayConfigQueryAttempt> _attempts = new(attempts);

        public int SizingCalls { get; private set; }
        public int QueryCalls { get; private set; }

        public DisplayConfigBufferSizingResult GetActiveBufferSizes()
        {
            SizingCalls++;
            return new DisplayConfigBufferSizingResult(0, 1, 1);
        }

        public DisplayConfigQueryAttempt QueryActive(uint pathCapacity, uint modeCapacity)
        {
            QueryCalls++;
            Assert.AreEqual(1u, pathCapacity);
            Assert.AreEqual(1u, modeCapacity);
            return _attempts.Dequeue();
        }
    }
}
