using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.GameRuntime;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class ProcessProofSetCorrelationEngineTests
{
    private static readonly DateTimeOffset BaselineUtc =
        new(2026, 9, 13, 8, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset HandoffUtc = BaselineUtc.AddSeconds(1);

    [TestMethod]
    public void CuratedExecutableRequiresStabilityBeforeRunningConfirmation()
    {
        var engine = CreateEngine(Baseline(), expected: ["Game.exe"]);
        var creation = HandoffUtc.AddMilliseconds(100);

        var starting = engine.Observe(Snapshot(
            HandoffUtc.AddSeconds(1),
            Process(200, 7, @"C:\Games\Title\Game.exe", creation, HandoffUtc.AddSeconds(1))));

        Assert.AreEqual(ProcessCorrelationClassification.StartingConfirmed, starting.Classification);
        Assert.AreEqual(ProcessCorrelationEvidenceLevel.Strong, starting.EvidenceLevel);
        Assert.AreEqual(ProcessProofSetIds.CuratedExecutableV1, starting.ProofSetId);
        Assert.AreEqual(ProcessCorrelationReasonCodes.CandidateNotStable, starting.ReasonCode);

        var running = engine.Observe(Snapshot(
            HandoffUtc.AddSeconds(4),
            Process(200, 7, @"C:\Games\Title\Game.exe", creation, HandoffUtc.AddSeconds(4))));

        Assert.AreEqual(ProcessCorrelationClassification.RunningConfirmed, running.Classification);
        Assert.AreEqual(ProcessProofSetIds.CuratedExecutableV1, running.ProofSetId);
        Assert.AreEqual(ProcessCorrelationReasonCodes.ProofSetSatisfied, running.ReasonCode);
        Assert.AreEqual(1, running.CorrelatedProcesses.Count);
        Assert.AreEqual(CorrelatedExecutableRole.GamePrimary, running.CorrelatedProcesses[0].Role);
    }

    [TestMethod]
    public void SameExecutableNameOutsideValidatedRootIsRejected()
    {
        var engine = CreateEngine(Baseline(), expected: ["Game.exe"]);

        var result = engine.Observe(Snapshot(
            HandoffUtc.AddSeconds(2),
            Process(
                201,
                7,
                @"C:\Other\Game.exe",
                HandoffUtc.AddMilliseconds(100),
                HandoffUtc.AddSeconds(2))));

        Assert.AreEqual(ProcessCorrelationClassification.NoMatch, result.Classification);
        Assert.AreEqual(ProcessCorrelationReasonCodes.NoSupportedProof, result.ReasonCode);
    }

    [TestMethod]
    public void InstallRootPrefixTrapIsRejected()
    {
        var engine = CreateEngine(Baseline(), expected: ["Game.exe"]);

        var result = engine.Observe(Snapshot(
            HandoffUtc.AddSeconds(2),
            Process(
                202,
                7,
                @"C:\Games\Title-Evil\Game.exe",
                HandoffUtc.AddMilliseconds(100),
                HandoffUtc.AddSeconds(2))));

        Assert.AreEqual(ProcessCorrelationClassification.NoMatch, result.Classification);
    }

    [TestMethod]
    public void DifferentWindowsSessionCannotSatisfyProofSet()
    {
        var engine = CreateEngine(Baseline(), expected: ["Game.exe"]);

        var result = engine.Observe(Snapshot(
            HandoffUtc.AddSeconds(2),
            Process(
                203,
                8,
                @"C:\Games\Title\Game.exe",
                HandoffUtc.AddMilliseconds(100),
                HandoffUtc.AddSeconds(2))));

        Assert.AreEqual(ProcessCorrelationClassification.NoMatch, result.Classification);
    }

    [TestMethod]
    public void PidReuseWithDifferentCreationTimeIsTreatedAsNewProcessIdentity()
    {
        var oldCreation = BaselineUtc.AddMinutes(-5);
        var baseline = Baseline(Process(
            500,
            7,
            @"C:\Games\Title\Game.exe",
            oldCreation,
            BaselineUtc));
        var engine = CreateEngine(baseline, expected: ["Game.exe"], stability: TimeSpan.Zero);
        var newCreation = HandoffUtc.AddMilliseconds(10);

        var result = engine.Observe(Snapshot(
            HandoffUtc.AddSeconds(1),
            Process(500, 7, @"C:\Games\Title\Game.exe", newCreation, HandoffUtc.AddSeconds(1))));

        Assert.AreEqual(ProcessCorrelationClassification.RunningConfirmed, result.Classification);
        Assert.AreEqual(newCreation, result.CorrelatedProcesses.Single().ProcessCreationTimeUtc);
    }

    [TestMethod]
    public void ExistingBaselineProcessDoesNotBecomeLaunchProof()
    {
        var creation = BaselineUtc.AddMinutes(-2);
        var baselineProcess = Process(
            501,
            7,
            @"C:\Games\Title\Game.exe",
            creation,
            BaselineUtc);
        var engine = CreateEngine(Baseline(baselineProcess), expected: ["Game.exe"], stability: TimeSpan.Zero);

        var result = engine.Observe(Snapshot(
            HandoffUtc.AddSeconds(2),
            baselineProcess with { ObservedUtc = HandoffUtc.AddSeconds(2) }));

        Assert.AreEqual(ProcessCorrelationClassification.NoMatch, result.Classification);
    }

    [TestMethod]
    public void KnownHelperUnderInstallRootIsNotGenericGameProof()
    {
        var engine = CreateEngine(
            Baseline(),
            expected: [],
            helpers: ["CrashReporter.exe"],
            stability: TimeSpan.Zero);

        var result = engine.Observe(Snapshot(
            HandoffUtc.AddSeconds(2),
            Process(
                204,
                7,
                @"C:\Games\Title\CrashReporter.exe",
                HandoffUtc.AddMilliseconds(100),
                HandoffUtc.AddSeconds(2))));

        Assert.AreEqual(ProcessCorrelationClassification.NoMatch, result.Classification);
    }

    [TestMethod]
    public void GenericInstallRootProofIsMediumAndExplainable()
    {
        var engine = CreateEngine(Baseline(), expected: [], stability: TimeSpan.Zero);

        var result = engine.Observe(Snapshot(
            HandoffUtc.AddSeconds(2),
            Process(
                205,
                7,
                @"C:\Games\Title\Shipping\Title-Win64.exe",
                HandoffUtc.AddMilliseconds(100),
                HandoffUtc.AddSeconds(2))));

        Assert.AreEqual(ProcessCorrelationClassification.RunningConfirmed, result.Classification);
        Assert.AreEqual(ProcessCorrelationEvidenceLevel.Medium, result.EvidenceLevel);
        Assert.AreEqual(ProcessProofSetIds.InstallRootGenericV1, result.ProofSetId);
        Assert.AreEqual(CorrelatedExecutableRole.UnknownCandidate, result.CorrelatedProcesses.Single().Role);
    }

    [TestMethod]
    public void MissingCreationTimeCanOnlyProduceWeakCandidate()
    {
        var engine = CreateEngine(Baseline(), expected: ["Game.exe"], stability: TimeSpan.Zero);

        var result = engine.Observe(Snapshot(
            HandoffUtc.AddSeconds(2),
            new ProcessEvidenceObservation(
                206,
                7,
                @"C:\Games\Title\Game.exe",
                ProcessCreationTimeUtc: null,
                HandoffUtc.AddSeconds(2))));

        Assert.AreEqual(ProcessCorrelationClassification.Candidate, result.Classification);
        Assert.AreEqual(ProcessCorrelationEvidenceLevel.Weak, result.EvidenceLevel);
        Assert.IsNull(result.ProofSetId);
        Assert.AreEqual(ProcessCorrelationReasonCodes.ProcessIdentityIncomplete, result.ReasonCode);
    }

    [TestMethod]
    public void TwoEquivalentStrongCandidatesRemainAmbiguous()
    {
        var engine = CreateEngine(Baseline(), expected: ["Game.exe"], stability: TimeSpan.Zero);
        var observed = HandoffUtc.AddSeconds(2);

        var result = engine.Observe(Snapshot(
            observed,
            Process(207, 7, @"C:\Games\Title\Game.exe", HandoffUtc.AddMilliseconds(10), observed),
            Process(208, 7, @"C:\Games\Title\bin\Game.exe", HandoffUtc.AddMilliseconds(20), observed)));

        Assert.AreEqual(ProcessCorrelationClassification.Ambiguous, result.Classification);
        Assert.AreEqual(ProcessCorrelationEvidenceLevel.Strong, result.EvidenceLevel);
        Assert.IsNull(result.ProofSetId);
        Assert.AreEqual(ProcessCorrelationReasonCodes.MultipleEquivalentCandidates, result.ReasonCode);
        Assert.AreEqual(2, result.CorrelatedProcesses.Count);
    }

    [TestMethod]
    public void StrongCuratedProofWinsOverGenericInstallRootCandidate()
    {
        var engine = CreateEngine(Baseline(), expected: ["Game.exe"], stability: TimeSpan.Zero);
        var observed = HandoffUtc.AddSeconds(2);

        var result = engine.Observe(Snapshot(
            observed,
            Process(209, 7, @"C:\Games\Title\Game.exe", HandoffUtc.AddMilliseconds(10), observed),
            Process(210, 7, @"C:\Games\Title\Telemetry.exe", HandoffUtc.AddMilliseconds(20), observed)));

        Assert.AreEqual(ProcessCorrelationClassification.RunningConfirmed, result.Classification);
        Assert.AreEqual(ProcessCorrelationEvidenceLevel.Strong, result.EvidenceLevel);
        Assert.AreEqual(ProcessProofSetIds.CuratedExecutableV1, result.ProofSetId);
        Assert.AreEqual(2, result.CorrelatedProcesses.Count);
    }

    [TestMethod]
    public void RulesRejectExecutableThatIsAlsoClassifiedAsHelper()
    {
        var rules = new ProcessCorrelationRules(
            7,
            HandoffUtc,
            @"C:\Games\Title",
            ["Game.exe"],
            ["GAME.EXE"],
            TimeSpan.Zero);

        var exception = AssertThrows<InvalidDataException>(() => rules.Validate());

        StringAssert.Contains(exception.Message, "both expected game evidence and a known helper");
    }

    private static ProcessProofSetCorrelationEngine CreateEngine(
        ProcessEvidenceSnapshot baseline,
        IReadOnlyList<string> expected,
        IReadOnlyList<string>? helpers = null,
        TimeSpan? stability = null)
        => new(
            baseline,
            new ProcessCorrelationRules(
                SessionId: 7,
                HandoffUtc,
                ValidatedInstallRoot: @"C:\Games\Title",
                ExpectedExecutableNames: expected,
                KnownHelperExecutableNames: helpers ?? [],
                MinimumStabilityWindow: stability ?? TimeSpan.FromSeconds(2)));

    private static ProcessEvidenceSnapshot Baseline(params ProcessEvidenceObservation[] processes)
        => Snapshot(BaselineUtc, processes);

    private static ProcessEvidenceSnapshot Snapshot(
        DateTimeOffset observedUtc,
        params ProcessEvidenceObservation[] processes)
        => new(observedUtc, processes);

    private static ProcessEvidenceObservation Process(
        int processId,
        int sessionId,
        string imagePath,
        DateTimeOffset creationUtc,
        DateTimeOffset observedUtc)
        => new(
            processId,
            sessionId,
            imagePath,
            creationUtc,
            observedUtc);

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
