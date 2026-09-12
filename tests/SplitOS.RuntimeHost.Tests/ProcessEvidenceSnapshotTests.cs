using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class ProcessEvidenceSnapshotTests
{
    [TestMethod]
    public void SnapshotNormalizesOrdersAndDeduplicatesProcessEvidence()
    {
        var observed = new DateTimeOffset(2026, 9, 12, 7, 30, 0, TimeSpan.Zero);
        var creation = new DateTimeOffset(2026, 9, 12, 7, 0, 0, TimeSpan.Zero);
        var interop = new FakeInterop(
            new[] { 42, 7, 42, 0 },
            new Dictionary<int, WindowsProcessProbeAttempt>
            {
                [7] = new(7, null, null, null),
                [42] = new(42, 3, @" C:/Apps/SplitOS/Game.exe ", creation.UtcDateTime.ToFileTimeUtc())
            });
        var reader = new ProcessEvidenceSnapshotReader(interop, new FixedTimeProvider(observed));

        var snapshot = reader.Read();

        Assert.AreEqual(observed, snapshot.ObservedUtc);
        Assert.AreEqual(2, snapshot.Processes.Count);
        Assert.AreEqual(7, snapshot.Processes[0].ProcessId);
        Assert.AreEqual(42, snapshot.Processes[1].ProcessId);

        var process = snapshot.Processes[1];
        Assert.AreEqual(3, process.SessionId);
        Assert.AreEqual(@"C:\Apps\SplitOS\Game.exe", process.ImagePath);
        Assert.AreEqual(creation, process.ProcessCreationTimeUtc);
        Assert.AreEqual(observed, process.ObservedUtc);
        Assert.AreEqual(
            new ProcessInstanceIdentity(42, creation),
            process.ReuseProtectedIdentity);
    }

    [TestMethod]
    public void MissingPerProcessFieldsRemainUnknownInsteadOfBeingInvented()
    {
        var observed = new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.Zero);
        var reader = new ProcessEvidenceSnapshotReader(
            new FakeInterop(
                new[] { 91 },
                new Dictionary<int, WindowsProcessProbeAttempt>
                {
                    [91] = new(91, null, "   ", null)
                }),
            new FixedTimeProvider(observed));

        var process = reader.Read().Processes.Single();

        Assert.IsNull(process.SessionId);
        Assert.IsNull(process.ImagePath);
        Assert.IsNull(process.ProcessCreationTimeUtc);
        Assert.IsNull(process.ReuseProtectedIdentity);
        Assert.AreEqual(observed, process.ObservedUtc);
    }

    [TestMethod]
    public void PidReuseProtectionRequiresCreationTime()
    {
        var creationA = new DateTimeOffset(2026, 9, 12, 8, 10, 0, TimeSpan.Zero);
        var creationB = creationA.AddSeconds(1);

        var identityA = ReadIdentity(500, creationA);
        var identityB = ReadIdentity(500, creationB);

        Assert.IsNotNull(identityA);
        Assert.IsNotNull(identityB);
        Assert.AreNotEqual(identityA, identityB);
    }

    [TestMethod]
    public void ProbeIdentityMismatchFailsClosed()
    {
        var reader = new ProcessEvidenceSnapshotReader(
            new FakeInterop(
                new[] { 100 },
                new Dictionary<int, WindowsProcessProbeAttempt>
                {
                    [100] = new(101, 1, @"C:\Apps\Wrong.exe", null)
                }));

        var exception = AssertThrows<InvalidDataException>(() => reader.Read());

        StringAssert.Contains(exception.Message, "Requested PID 100");
        StringAssert.Contains(exception.Message, "received PID 101");
    }

    private static ProcessInstanceIdentity? ReadIdentity(int processId, DateTimeOffset creationTime)
    {
        var reader = new ProcessEvidenceSnapshotReader(
            new FakeInterop(
                new[] { processId },
                new Dictionary<int, WindowsProcessProbeAttempt>
                {
                    [processId] = new(
                        processId,
                        1,
                        @"C:\Apps\Game.exe",
                        creationTime.UtcDateTime.ToFileTimeUtc())
                }));

        return reader.Read().Processes.Single().ReuseProtectedIdentity;
    }

    private sealed class FakeInterop(
        IReadOnlyList<int> processIds,
        IReadOnlyDictionary<int, WindowsProcessProbeAttempt> probes) : IWindowsProcessEvidenceInterop
    {
        public IReadOnlyList<int> EnumerateProcessIds() => processIds;

        public WindowsProcessProbeAttempt Probe(int processId)
            => probes.TryGetValue(processId, out var attempt)
                ? attempt
                : throw new InvalidOperationException($"Missing fake process probe for PID {processId}.");
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private static T AssertThrows<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T exception)
        {
            return exception;
        }
        catch (Exception exception)
        {
            Assert.Fail($"Expected {typeof(T).Name}, got {exception.GetType().Name}: {exception.Message}");
            throw;
        }

        Assert.Fail($"Expected {typeof(T).Name}.");
        throw new InvalidOperationException();
    }
}
