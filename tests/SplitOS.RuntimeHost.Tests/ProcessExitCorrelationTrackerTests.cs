using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.GameRuntime;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class ProcessExitCorrelationTrackerTests
{
    private static readonly DateTimeOffset RunningObservedUtc =
        new(2026, 9, 13, 10, 0, 10, TimeSpan.Zero);

    private static readonly DateTimeOffset PrimaryCreationUtc =
        RunningObservedUtc.AddSeconds(-5);

    [TestMethod]
    public void RequiredPrimaryStillPresentKeepsSessionRunning()
    {
        var tracker = CreateTracker();
        var observed = RunningObservedUtc.AddSeconds(1);

        var result = tracker.Observe(Snapshot(
            observed,
            Process(100, 7, @"C:\Games\Title\Bootstrap.exe", PrimaryCreationUtc, observed)));

        Assert.AreEqual(ProcessExitCorrelationClassification.StillRunning, result.Classification);
        Assert.AreEqual(ProcessExitCorrelationReasonCodes.RequiredProcessStillPresent, result.ReasonCode);
        Assert.IsNull(result.ExitCandidateSinceUtc);
    }

    [TestMethod]
    public void MissingPrimaryStartsGraceThenConfirmsExitAfterBound()
    {
        var tracker = CreateTracker(exitGrace: TimeSpan.FromSeconds(5));
        var firstMissing = RunningObservedUtc.AddSeconds(1);

        var candidate = tracker.Observe(Snapshot(firstMissing));
        Assert.AreEqual(ProcessExitCorrelationClassification.ExitCandidate, candidate.Classification);
        Assert.AreEqual(ProcessExitCorrelationReasonCodes.ExitGraceStarted, candidate.ReasonCode);
        Assert.AreEqual(firstMissing, candidate.ExitCandidateSinceUtc);

        var pending = tracker.Observe(Snapshot(firstMissing.AddSeconds(4)));
        Assert.AreEqual(ProcessExitCorrelationClassification.ExitCandidate, pending.Classification);
        Assert.AreEqual(ProcessExitCorrelationReasonCodes.ExitGracePending, pending.ReasonCode);

        var exited = tracker.Observe(Snapshot(firstMissing.AddSeconds(5)));
        Assert.AreEqual(ProcessExitCorrelationClassification.ExitConfirmed, exited.Classification);
        Assert.AreEqual(ProcessExitCorrelationReasonCodes.ExitGraceSatisfied, exited.ReasonCode);
    }

    [TestMethod]
    public void KnownHelperRemainingAfterPrimaryExitDoesNotBlockExitConfirmation()
    {
        var tracker = CreateTracker(
            exitGrace: TimeSpan.FromSeconds(2),
            ignored: ["CrashReporter.exe"]);
        var firstMissing = RunningObservedUtc.AddSeconds(1);
        var helperCreation = RunningObservedUtc.AddMilliseconds(500);

        var candidate = tracker.Observe(Snapshot(
            firstMissing,
            Process(150, 7, @"C:\Games\Title\CrashReporter.exe", helperCreation, firstMissing)));
        Assert.AreEqual(ProcessExitCorrelationClassification.ExitCandidate, candidate.Classification);

        var exited = tracker.Observe(Snapshot(
            firstMissing.AddSeconds(2),
            Process(150, 7, @"C:\Games\Title\CrashReporter.exe", helperCreation, firstMissing.AddSeconds(2))));

        Assert.AreEqual(ProcessExitCorrelationClassification.ExitConfirmed, exited.Classification);
    }

    [TestMethod]
    public void ExternalClientRemainingAfterGameExitDoesNotBlockExitConfirmation()
    {
        var tracker = CreateTracker(exitGrace: TimeSpan.FromSeconds(1));
        var firstMissing = RunningObservedUtc.AddSeconds(1);
        var steamCreation = RunningObservedUtc.AddMinutes(-10);

        _ = tracker.Observe(Snapshot(
            firstMissing,
            Process(160, 7, @"C:\Program Files (x86)\Steam\steam.exe", steamCreation, firstMissing)));
        var exited = tracker.Observe(Snapshot(
            firstMissing.AddSeconds(1),
            Process(160, 7, @"C:\Program Files (x86)\Steam\steam.exe", steamCreation, firstMissing.AddSeconds(1))));

        Assert.AreEqual(ProcessExitCorrelationClassification.ExitConfirmed, exited.Classification);
    }

    [TestMethod]
    public void PermittedReplacementWithinWindowContinuesCorrelation()
    {
        var tracker = CreateTracker(
            replacements: [new ProcessReplacementRule("Bootstrap.exe", "Game.exe")]);
        var observed = RunningObservedUtc.AddSeconds(1);
        var replacementCreation = RunningObservedUtc.AddMilliseconds(500);

        var replacement = tracker.Observe(Snapshot(
            observed,
            Process(200, 7, @"C:\Games\Title\Game.exe", replacementCreation, observed)));

        Assert.AreEqual(ProcessExitCorrelationClassification.ReplacementProcessFound, replacement.Classification);
        Assert.AreEqual(ProcessExitCorrelationReasonCodes.PermittedReplacementFound, replacement.ReasonCode);
        Assert.IsNotNull(replacement.Replacement);
        Assert.AreEqual(200, replacement.TrackedPrimary.ProcessId);
        Assert.AreEqual(ProcessExitProofSetIds.PermittedReplacementV1, replacement.TrackedPrimary.ProofSetId);

        var next = tracker.Observe(Snapshot(
            observed.AddSeconds(1),
            Process(200, 7, @"C:\Games\Title\Game.exe", replacementCreation, observed.AddSeconds(1))));

        Assert.AreEqual(ProcessExitCorrelationClassification.StillRunning, next.Classification);
        Assert.AreEqual(200, next.TrackedPrimary.ProcessId);
    }

    [TestMethod]
    public void ReplacementOutsideValidatedRootIsNotAdopted()
    {
        var tracker = CreateTracker(
            replacements: [new ProcessReplacementRule("Bootstrap.exe", "Game.exe")]);
        var observed = RunningObservedUtc.AddSeconds(1);

        var result = tracker.Observe(Snapshot(
            observed,
            Process(201, 7, @"C:\Other\Game.exe", RunningObservedUtc.AddMilliseconds(500), observed)));

        Assert.AreEqual(ProcessExitCorrelationClassification.ExitCandidate, result.Classification);
        Assert.IsNull(result.Replacement);
    }

    [TestMethod]
    public void ReplacementInWrongSessionIsNotAdopted()
    {
        var tracker = CreateTracker(
            replacements: [new ProcessReplacementRule("Bootstrap.exe", "Game.exe")]);
        var observed = RunningObservedUtc.AddSeconds(1);

        var result = tracker.Observe(Snapshot(
            observed,
            Process(202, 8, @"C:\Games\Title\Game.exe", RunningObservedUtc.AddMilliseconds(500), observed)));

        Assert.AreEqual(ProcessExitCorrelationClassification.ExitCandidate, result.Classification);
    }

    [TestMethod]
    public void ReplacementWithoutCreationIdentityIsNotAdopted()
    {
        var tracker = CreateTracker(
            replacements: [new ProcessReplacementRule("Bootstrap.exe", "Game.exe")]);
        var observed = RunningObservedUtc.AddSeconds(1);

        var result = tracker.Observe(new ProcessEvidenceSnapshot(
            observed,
            [new ProcessEvidenceObservation(
                ProcessId: 203,
                SessionId: 7,
                ImagePath: @"C:\Games\Title\Game.exe",
                ProcessCreationTimeUtc: null,
                ObservedUtc: observed)]));

        Assert.AreEqual(ProcessExitCorrelationClassification.ExitCandidate, result.Classification);
        Assert.IsNull(result.Replacement);
    }

    [TestMethod]
    public void UnknownRecentGameRootProcessFailsClosedAsCorrelationLost()
    {
        var tracker = CreateTracker(
            replacements: [new ProcessReplacementRule("Bootstrap.exe", "Game.exe")]);
        var observed = RunningObservedUtc.AddSeconds(1);

        var result = tracker.Observe(Snapshot(
            observed,
            Process(204, 7, @"C:\Games\Title\Unexpected.exe", RunningObservedUtc.AddMilliseconds(500), observed)));

        Assert.AreEqual(ProcessExitCorrelationClassification.CorrelationLost, result.Classification);
        Assert.AreEqual(ProcessExitCorrelationReasonCodes.UnknownReplacementCandidate, result.ReasonCode);
    }

    [TestMethod]
    public void TwoPermittedReplacementsAreAmbiguousAndNotAutoAdopted()
    {
        var tracker = CreateTracker(
            replacements:
            [
                new ProcessReplacementRule("Bootstrap.exe", "Game.exe"),
                new ProcessReplacementRule("Bootstrap.exe", "GameShipping.exe")
            ]);
        var observed = RunningObservedUtc.AddSeconds(1);

        var result = tracker.Observe(Snapshot(
            observed,
            Process(205, 7, @"C:\Games\Title\Game.exe", RunningObservedUtc.AddMilliseconds(400), observed),
            Process(206, 7, @"C:\Games\Title\GameShipping.exe", RunningObservedUtc.AddMilliseconds(500), observed)));

        Assert.AreEqual(ProcessExitCorrelationClassification.CorrelationLost, result.Classification);
        Assert.AreEqual(ProcessExitCorrelationReasonCodes.AmbiguousPermittedReplacement, result.ReasonCode);
        Assert.AreEqual(100, result.TrackedPrimary.ProcessId);
    }

    [TestMethod]
    public void SamePrimaryPidWithoutCreationTimeStopsExitClockAndFailsClosed()
    {
        var tracker = CreateTracker(exitGrace: TimeSpan.FromSeconds(2));
        var firstMissing = RunningObservedUtc.AddSeconds(1);
        _ = tracker.Observe(Snapshot(firstMissing));
        var incompleteAt = firstMissing.AddSeconds(1);

        var lost = tracker.Observe(new ProcessEvidenceSnapshot(
            incompleteAt,
            [new ProcessEvidenceObservation(
                ProcessId: 100,
                SessionId: 7,
                ImagePath: @"C:\Games\Title\Bootstrap.exe",
                ProcessCreationTimeUtc: null,
                ObservedUtc: incompleteAt)]));

        Assert.AreEqual(ProcessExitCorrelationClassification.CorrelationLost, lost.Classification);
        Assert.AreEqual(ProcessExitCorrelationReasonCodes.RequiredIdentityIncomplete, lost.ReasonCode);
        Assert.IsNull(lost.ExitCandidateSinceUtc);

        var restartedGrace = tracker.Observe(Snapshot(incompleteAt.AddSeconds(1)));
        Assert.AreEqual(ProcessExitCorrelationClassification.ExitCandidate, restartedGrace.Classification);
        Assert.AreEqual(incompleteAt.AddSeconds(1), restartedGrace.ExitCandidateSinceUtc);
    }

    [TestMethod]
    public void StalePreexistingAllowedTargetCannotBecomeReplacement()
    {
        var tracker = CreateTracker(
            exitGrace: TimeSpan.FromSeconds(2),
            replacementWindow: TimeSpan.FromSeconds(2),
            replacements: [new ProcessReplacementRule("Bootstrap.exe", "Game.exe")]);
        var firstMissing = RunningObservedUtc.AddSeconds(1);
        var oldCreation = RunningObservedUtc.AddMinutes(-5);

        var candidate = tracker.Observe(Snapshot(
            firstMissing,
            Process(207, 7, @"C:\Games\Title\Game.exe", oldCreation, firstMissing)));
        Assert.AreEqual(ProcessExitCorrelationClassification.ExitCandidate, candidate.Classification);

        var exited = tracker.Observe(Snapshot(
            firstMissing.AddSeconds(2),
            Process(207, 7, @"C:\Games\Title\Game.exe", oldCreation, firstMissing.AddSeconds(2))));
        Assert.AreEqual(ProcessExitCorrelationClassification.ExitConfirmed, exited.Classification);
    }

    [TestMethod]
    public void EvidenceTimestampCannotMoveBackwards()
    {
        var tracker = CreateTracker();

        var exception = AssertThrows<InvalidDataException>(() =>
            tracker.Observe(Snapshot(RunningObservedUtc.AddMilliseconds(-1))));

        StringAssert.Contains(exception.Message, "moved backwards");
    }

    [TestMethod]
    public void ReplacementWindowCannotExceedExitGraceWindow()
    {
        var rules = new ProcessExitCorrelationRules(
            SessionId: 7,
            ValidatedInstallRoot: @"C:\Games\Title",
            ExitGraceWindow: TimeSpan.FromSeconds(2),
            ReplacementWindow: TimeSpan.FromSeconds(3),
            AllowedReplacementRules: [],
            KnownIgnoredExecutableNames: []);

        var exception = AssertThrows<InvalidDataException>(() => rules.Validate());

        StringAssert.Contains(exception.Message, "cannot exceed the exit grace window");
    }

    [TestMethod]
    public void TrackerRequiresRunningConfirmedInput()
    {
        var correlation = RunningCorrelation() with
        {
            Classification = ProcessCorrelationClassification.StartingConfirmed
        };

        var exception = AssertThrows<InvalidDataException>(() =>
            new ProcessExitCorrelationTracker(correlation, Rules()));

        StringAssert.Contains(exception.Message, "RUNNING_CONFIRMED");
    }

    private static ProcessExitCorrelationTracker CreateTracker(
        TimeSpan? exitGrace = null,
        TimeSpan? replacementWindow = null,
        IReadOnlyList<ProcessReplacementRule>? replacements = null,
        IReadOnlyList<string>? ignored = null)
        => new(
            RunningCorrelation(),
            Rules(exitGrace, replacementWindow, replacements, ignored));

    private static ProcessExitCorrelationRules Rules(
        TimeSpan? exitGrace = null,
        TimeSpan? replacementWindow = null,
        IReadOnlyList<ProcessReplacementRule>? replacements = null,
        IReadOnlyList<string>? ignored = null)
    {
        var grace = exitGrace ?? TimeSpan.FromSeconds(5);
        return new ProcessExitCorrelationRules(
            SessionId: 7,
            ValidatedInstallRoot: @"C:\Games\Title",
            ExitGraceWindow: grace,
            ReplacementWindow: replacementWindow ?? TimeSpan.FromSeconds(Math.Min(3, grace.TotalSeconds)),
            AllowedReplacementRules: replacements ?? [],
            KnownIgnoredExecutableNames: ignored ?? []);
    }

    private static ProcessCorrelationResult RunningCorrelation()
    {
        var primary = new CorrelatedProcessEvidence(
            ProcessId: 100,
            ProcessCreationTimeUtc: PrimaryCreationUtc,
            SessionId: 7,
            NormalizedImagePath: @"C:\Games\Title\Bootstrap.exe",
            Role: CorrelatedExecutableRole.GamePrimary,
            EvidenceLevel: ProcessCorrelationEvidenceLevel.Strong,
            ProofSetId: ProcessProofSetIds.CuratedExecutableV1,
            FirstObservedUtc: PrimaryCreationUtc,
            LastObservedUtc: RunningObservedUtc);

        return new ProcessCorrelationResult(
            Classification: ProcessCorrelationClassification.RunningConfirmed,
            EvidenceLevel: ProcessCorrelationEvidenceLevel.Strong,
            ProofSetId: ProcessProofSetIds.CuratedExecutableV1,
            ReasonCode: ProcessCorrelationReasonCodes.ProofSetSatisfied,
            ObservedUtc: RunningObservedUtc,
            CorrelatedProcesses: [primary]);
    }

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
