using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Contracts.Protocol;
using SplitOS.Runtime.Client;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class LauncherLaunchPresentationContractTests
{
    [TestMethod]
    public void ActiveLaunchStateWithoutLaunchProjectionIsRejected()
    {
        var controller = new LauncherLaunchPresentationController();
        var snapshot = Snapshot("PREPARING", presentation: null, activeLaunch: true);

        var decision = controller.Observe(snapshot);

        Assert.AreEqual(LauncherLaunchPresentationDisposition.Rejected, decision.Disposition);
        Assert.AreEqual(LauncherLaunchPresentationReasonCodes.SessionContradiction, decision.ReasonCode);
        Assert.AreEqual(LauncherLaunchPresentationMode.Hidden, decision.View.Mode);
    }

    [TestMethod]
    public void IdleLauncherSnapshotWithoutLaunchProjectionRemainsHidden()
    {
        var controller = new LauncherLaunchPresentationController();
        var snapshot = Snapshot("LAUNCHER", presentation: null, activeLaunch: false);

        var decision = controller.Observe(snapshot);

        Assert.AreEqual(LauncherLaunchPresentationDisposition.NoOp, decision.Disposition);
        Assert.AreEqual(LauncherLaunchPresentationMode.Hidden, decision.View.Mode);
    }

    [TestMethod]
    public void RunningPhaseBeforeGameRunningSessionIsRejected()
    {
        var controller = new LauncherLaunchPresentationController();
        var presentation = Presentation("GAME_RUNNING_CONFIRMED");
        var snapshot = Snapshot("GAME_STARTING", presentation, activeLaunch: true);

        var decision = controller.Observe(snapshot);

        Assert.AreEqual(LauncherLaunchPresentationDisposition.Rejected, decision.Disposition);
        Assert.AreEqual(LauncherLaunchPresentationReasonCodes.SessionContradiction, decision.ReasonCode);
    }

    private static LauncherRuntimeSnapshotResult Snapshot(
        string gameSessionState,
        LauncherLaunchPresentationResult? presentation,
        bool activeLaunch)
        => new(
            "READY",
            "ENABLED",
            "GAME",
            gameSessionState,
            GameSessionRevision: 1,
            ActiveLaunchOperationId: activeLaunch ? "launch-1" : null,
            ActiveLaunchCorrelationId: activeLaunch ? "corr-1" : null,
            ActiveGameId: activeLaunch ? "game-1" : null,
            ExpectedGameModeOperationId: null,
            ExpectedGameModeCorrelationId: null,
            ReadinessRevision: 0,
            SnapshotVersion: 1,
            ObservedAtUtc: DateTimeOffset.UtcNow,
            LaunchPresentation: presentation);

    private static LauncherLaunchPresentationResult Presentation(string phase)
        => new(
            "launch-1",
            "corr-1",
            "game-1",
            phase,
            FailureClass: null,
            ExternalClientOutcome: null,
            AllowedActions: Array.Empty<string>(),
            RuntimePresentationRevision: 0,
            ObservedAtUtc: DateTimeOffset.UtcNow);
}
