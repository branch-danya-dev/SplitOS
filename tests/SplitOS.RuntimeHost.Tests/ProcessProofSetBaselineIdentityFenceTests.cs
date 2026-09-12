using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.GameRuntime;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class ProcessProofSetBaselineIdentityFenceTests
{
    [TestMethod]
    public void BaselinePidWithoutCreationTimeCannotLaterBecomeStrongLaunchProof()
    {
        var baselineUtc = new DateTimeOffset(2026, 9, 13, 9, 0, 0, TimeSpan.Zero);
        var handoffUtc = baselineUtc.AddSeconds(1);
        var baseline = new ProcessEvidenceSnapshot(
            baselineUtc,
            [new ProcessEvidenceObservation(
                ProcessId: 700,
                SessionId: 7,
                ImagePath: @"C:\Games\Title\Game.exe",
                ProcessCreationTimeUtc: null,
                ObservedUtc: baselineUtc)]);
        var engine = new ProcessProofSetCorrelationEngine(
            baseline,
            new ProcessCorrelationRules(
                SessionId: 7,
                HandoffUtc: handoffUtc,
                ValidatedInstallRoot: @"C:\Games\Title",
                ExpectedExecutableNames: ["Game.exe"],
                KnownHelperExecutableNames: [],
                MinimumStabilityWindow: TimeSpan.Zero));
        var observedUtc = handoffUtc.AddSeconds(2);

        var result = engine.Observe(new ProcessEvidenceSnapshot(
            observedUtc,
            [new ProcessEvidenceObservation(
                ProcessId: 700,
                SessionId: 7,
                ImagePath: @"C:\Games\Title\Game.exe",
                ProcessCreationTimeUtc: handoffUtc.AddMilliseconds(100),
                ObservedUtc: observedUtc)]));

        Assert.AreEqual(ProcessCorrelationClassification.Candidate, result.Classification);
        Assert.AreEqual(ProcessCorrelationEvidenceLevel.Weak, result.EvidenceLevel);
        Assert.IsNull(result.ProofSetId);
        Assert.AreEqual(ProcessCorrelationReasonCodes.ProcessIdentityIncomplete, result.ReasonCode);
    }
}
