using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.GameRuntime;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class GameSessionStateMachineTests
{
    private static readonly GameSessionLaunchIdentity LaunchA =
        new("launch-a", "corr-a", "game-a");

    private static readonly GameSessionLaunchIdentity LaunchB =
        new("launch-b", "corr-b", "game-b");

    [TestMethod]
    public void NormalManagedLaunchExitAndReturnFollowsCanonicalStatePath()
    {
        var machine = new GameSessionStateMachine();

        AssertApplied(machine, GameSessionSignal.LauncherReady, expected: GameSessionState.Launcher);
        AssertApplied(machine, GameSessionSignal.LaunchRequested, GameSessionState.Preparing, LaunchA);
        AssertApplied(machine, GameSessionSignal.ClientHandoffSubmitted, GameSessionState.ClientHandoff, LaunchA);

        var handoff = machine.Apply(Request(GameSessionSignal.ClientHandoffAccepted, LaunchA));
        Assert.AreEqual(GameSessionTransitionDisposition.Applied, handoff.Disposition);
        Assert.AreEqual(GameSessionReasonCodes.HandoffAccepted, handoff.ReasonCode);
        Assert.AreEqual(GameSessionState.ClientHandoff, handoff.Snapshot.State);
        Assert.IsTrue(handoff.Snapshot.ClientHandoffAccepted);

        AssertApplied(machine, GameSessionSignal.GameStartingConfirmed, GameSessionState.GameStarting, LaunchA);
        AssertApplied(machine, GameSessionSignal.GameRunningConfirmed, GameSessionState.GameRunning, LaunchA);

        var replaced = machine.Apply(Request(GameSessionSignal.GameProcessReplaced, LaunchA));
        Assert.AreEqual(GameSessionTransitionDisposition.NoOp, replaced.Disposition);
        Assert.AreEqual(GameSessionReasonCodes.EvidenceDoesNotChangeState, replaced.ReasonCode);
        Assert.AreEqual(GameSessionState.GameRunning, replaced.Snapshot.State);

        AssertApplied(machine, GameSessionSignal.GameExitedConfirmed, GameSessionState.GameExitDetected, LaunchA);
        AssertApplied(machine, GameSessionSignal.BeginLauncherReturn, GameSessionState.ReturningToLauncher, LaunchA);
        AssertApplied(machine, GameSessionSignal.LauncherRestored, GameSessionState.Launcher, LaunchA);

        Assert.IsNull(machine.Snapshot.ActiveLaunch);
        Assert.IsFalse(machine.Snapshot.ClientHandoffAccepted);
        Assert.IsNull(machine.Snapshot.FailureCode);
        Assert.AreEqual(9L, machine.Snapshot.Revision);
    }

    [TestMethod]
    public void HandoffAcceptedDoesNotMeanGameStartingOrRunning()
    {
        var machine = CreateAtClientHandoff();

        var accepted = machine.Apply(Request(GameSessionSignal.ClientHandoffAccepted, LaunchA));

        Assert.AreEqual(GameSessionTransitionDisposition.Applied, accepted.Disposition);
        Assert.AreEqual(GameSessionState.ClientHandoff, accepted.Snapshot.State);
        Assert.IsTrue(accepted.Snapshot.ClientHandoffAccepted);
    }

    [TestMethod]
    public void StartingConfirmationRequiresAcceptedHandoff()
    {
        var machine = CreateAtClientHandoff();

        var result = machine.Apply(Request(GameSessionSignal.GameStartingConfirmed, LaunchA));

        Assert.AreEqual(GameSessionTransitionDisposition.Rejected, result.Disposition);
        Assert.AreEqual(GameSessionReasonCodes.HandoffNotAccepted, result.ReasonCode);
        Assert.AreEqual(GameSessionState.ClientHandoff, machine.Snapshot.State);
    }

    [TestMethod]
    public void RunningConfirmationCannotSkipGameStarting()
    {
        var machine = CreateAtClientHandoff(accepted: true);

        var result = machine.Apply(Request(GameSessionSignal.GameRunningConfirmed, LaunchA));

        Assert.AreEqual(GameSessionTransitionDisposition.Rejected, result.Disposition);
        Assert.AreEqual(GameSessionReasonCodes.InvalidTransition, result.ReasonCode);
        Assert.AreEqual(GameSessionState.ClientHandoff, machine.Snapshot.State);
    }

    [TestMethod]
    public void RunningConfirmationRequiresCommittedGameMode()
    {
        var machine = CreateAtGameStarting();

        var result = machine.Apply(new GameSessionTransitionRequest(
            GameSessionSignal.GameRunningConfirmed,
            IsGameModeCommitted: false,
            LaunchA));

        Assert.AreEqual(GameSessionTransitionDisposition.Rejected, result.Disposition);
        Assert.AreEqual(GameSessionReasonCodes.GameModeNotCommitted, result.ReasonCode);
        Assert.AreEqual(GameSessionState.GameStarting, machine.Snapshot.State);
    }

    [TestMethod]
    public void StaleLaunchOperationCannotMutateActiveSession()
    {
        var machine = CreateAtClientHandoff(accepted: true);
        var revision = machine.Snapshot.Revision;

        var stale = machine.Apply(Request(GameSessionSignal.GameStartingConfirmed, LaunchB));

        Assert.AreEqual(GameSessionTransitionDisposition.Rejected, stale.Disposition);
        Assert.AreEqual(GameSessionReasonCodes.ActiveLaunchMismatch, stale.ReasonCode);
        Assert.AreEqual(GameSessionState.ClientHandoff, machine.Snapshot.State);
        Assert.AreEqual(LaunchA, machine.Snapshot.ActiveLaunch);
        Assert.AreEqual(revision, machine.Snapshot.Revision);
    }

    [TestMethod]
    public void DifferentLaunchCannotReplacePreparingOperation()
    {
        var machine = new GameSessionStateMachine();
        AssertApplied(machine, GameSessionSignal.LauncherReady, GameSessionState.Launcher);
        AssertApplied(machine, GameSessionSignal.LaunchRequested, GameSessionState.Preparing, LaunchA);

        var second = machine.Apply(Request(GameSessionSignal.LaunchRequested, LaunchB));

        Assert.AreEqual(GameSessionTransitionDisposition.Rejected, second.Disposition);
        Assert.AreEqual(GameSessionReasonCodes.ActiveLaunchInProgress, second.ReasonCode);
        Assert.AreEqual(LaunchA, machine.Snapshot.ActiveLaunch);
    }

    [TestMethod]
    public void DuplicateSemanticSignalsAreIdempotent()
    {
        var machine = new GameSessionStateMachine();
        AssertApplied(machine, GameSessionSignal.LauncherReady, GameSessionState.Launcher);
        AssertApplied(machine, GameSessionSignal.LaunchRequested, GameSessionState.Preparing, LaunchA);

        var launchRevision = machine.Snapshot.Revision;
        var duplicateLaunch = machine.Apply(Request(GameSessionSignal.LaunchRequested, LaunchA));
        Assert.AreEqual(GameSessionTransitionDisposition.NoOp, duplicateLaunch.Disposition);
        Assert.AreEqual(launchRevision, machine.Snapshot.Revision);

        AssertApplied(machine, GameSessionSignal.ClientHandoffSubmitted, GameSessionState.ClientHandoff, LaunchA);
        _ = machine.Apply(Request(GameSessionSignal.ClientHandoffAccepted, LaunchA));
        var acceptedRevision = machine.Snapshot.Revision;

        var duplicateAccepted = machine.Apply(Request(GameSessionSignal.ClientHandoffAccepted, LaunchA));
        Assert.AreEqual(GameSessionTransitionDisposition.NoOp, duplicateAccepted.Disposition);
        Assert.AreEqual(acceptedRevision, machine.Snapshot.Revision);

        AssertApplied(machine, GameSessionSignal.GameStartingConfirmed, GameSessionState.GameStarting, LaunchA);
        AssertApplied(machine, GameSessionSignal.GameRunningConfirmed, GameSessionState.GameRunning, LaunchA);
        var runningRevision = machine.Snapshot.Revision;

        var duplicateRunning = machine.Apply(Request(GameSessionSignal.GameRunningConfirmed, LaunchA));
        Assert.AreEqual(GameSessionTransitionDisposition.NoOp, duplicateRunning.Disposition);
        Assert.AreEqual(runningRevision, machine.Snapshot.Revision);
    }

    [TestMethod]
    public void CorrelationLostFailsClosedAndReturnsThroughLauncherRecovery()
    {
        var machine = CreateAtGameRunning();

        var lost = machine.Apply(Request(GameSessionSignal.CorrelationLost, LaunchA));
        Assert.AreEqual(GameSessionTransitionDisposition.Applied, lost.Disposition);
        Assert.AreEqual(GameSessionState.Failed, lost.Snapshot.State);
        Assert.AreEqual(GameSessionFailureCodes.CorrelationLost, lost.Snapshot.FailureCode);

        AssertApplied(machine, GameSessionSignal.BeginLauncherReturn, GameSessionState.ReturningToLauncher, LaunchA);
        AssertApplied(machine, GameSessionSignal.LauncherRestored, GameSessionState.Launcher, LaunchA);
        Assert.IsNull(machine.Snapshot.ActiveLaunch);
        Assert.IsNull(machine.Snapshot.FailureCode);
    }

    [TestMethod]
    public void StartTimeoutFailsOnlyBeforeRunning()
    {
        var starting = CreateAtGameStarting();

        var timeout = starting.Apply(Request(GameSessionSignal.StartTimeout, LaunchA));
        Assert.AreEqual(GameSessionTransitionDisposition.Applied, timeout.Disposition);
        Assert.AreEqual(GameSessionState.Failed, timeout.Snapshot.State);
        Assert.AreEqual(GameSessionFailureCodes.StartTimeout, timeout.Snapshot.FailureCode);

        var running = CreateAtGameRunning();
        var invalid = running.Apply(Request(GameSessionSignal.StartTimeout, LaunchA));
        Assert.AreEqual(GameSessionTransitionDisposition.Rejected, invalid.Disposition);
        Assert.AreEqual(GameSessionState.GameRunning, running.Snapshot.State);
    }

    [TestMethod]
    public void TypedSessionFailureRequiresFailureCode()
    {
        var machine = new GameSessionStateMachine();
        AssertApplied(machine, GameSessionSignal.LauncherReady, GameSessionState.Launcher);
        AssertApplied(machine, GameSessionSignal.LaunchRequested, GameSessionState.Preparing, LaunchA);

        var missing = machine.Apply(Request(GameSessionSignal.SessionFailed, LaunchA));
        Assert.AreEqual(GameSessionTransitionDisposition.Rejected, missing.Disposition);
        Assert.AreEqual(GameSessionReasonCodes.FailureCodeRequired, missing.ReasonCode);

        var failed = machine.Apply(new GameSessionTransitionRequest(
            GameSessionSignal.SessionFailed,
            IsGameModeCommitted: true,
            LaunchA,
            FailureCode: "CLIENT_NOT_AVAILABLE"));

        Assert.AreEqual(GameSessionTransitionDisposition.Applied, failed.Disposition);
        Assert.AreEqual(GameSessionState.Failed, failed.Snapshot.State);
        Assert.AreEqual("CLIENT_NOT_AVAILABLE", failed.Snapshot.FailureCode);
    }

    [TestMethod]
    public void LauncherReturnFailureCanBeAcknowledgedBackToLauncher()
    {
        var machine = CreateAtGameRunning();
        AssertApplied(machine, GameSessionSignal.GameExitedConfirmed, GameSessionState.GameExitDetected, LaunchA);
        AssertApplied(machine, GameSessionSignal.BeginLauncherReturn, GameSessionState.ReturningToLauncher, LaunchA);

        var failed = machine.Apply(new GameSessionTransitionRequest(
            GameSessionSignal.SessionFailed,
            IsGameModeCommitted: true,
            LaunchA,
            FailureCode: "LAUNCHER_RETURN_FAILED"));
        Assert.AreEqual(GameSessionTransitionDisposition.Applied, failed.Disposition);
        Assert.AreEqual(GameSessionState.Failed, failed.Snapshot.State);
        Assert.AreEqual("LAUNCHER_RETURN_FAILED", failed.Snapshot.FailureCode);
        Assert.AreEqual(LaunchA, failed.Snapshot.ActiveLaunch);

        AssertApplied(machine, GameSessionSignal.FailureAcknowledged, GameSessionState.Launcher, LaunchA);
        Assert.IsNull(machine.Snapshot.ActiveLaunch);
        Assert.IsNull(machine.Snapshot.FailureCode);
    }

    [TestMethod]
    public void ModeDeactivationClearsRunningSessionAndBlocksNonGameTransitions()
    {
        var machine = CreateAtGameRunning();

        var deactivate = machine.Apply(new GameSessionTransitionRequest(
            GameSessionSignal.ModeDeactivated,
            IsGameModeCommitted: false));

        Assert.AreEqual(GameSessionTransitionDisposition.Applied, deactivate.Disposition);
        Assert.AreEqual(GameSessionReasonCodes.ModeDeactivated, deactivate.ReasonCode);
        Assert.AreEqual(GameSessionState.Inactive, machine.Snapshot.State);
        Assert.IsNull(machine.Snapshot.ActiveLaunch);

        var launcherWhileBase = machine.Apply(new GameSessionTransitionRequest(
            GameSessionSignal.LauncherReady,
            IsGameModeCommitted: false));
        Assert.AreEqual(GameSessionTransitionDisposition.Rejected, launcherWhileBase.Disposition);
        Assert.AreEqual(GameSessionReasonCodes.GameModeNotCommitted, launcherWhileBase.ReasonCode);
    }

    [TestMethod]
    public void ModeDeactivationSignalIsRejectedWhileGameRemainsCommitted()
    {
        var machine = CreateAtGameRunning();

        var result = machine.Apply(new GameSessionTransitionRequest(
            GameSessionSignal.ModeDeactivated,
            IsGameModeCommitted: true));

        Assert.AreEqual(GameSessionTransitionDisposition.Rejected, result.Disposition);
        Assert.AreEqual(GameSessionReasonCodes.GameModeStillCommitted, result.ReasonCode);
        Assert.AreEqual(GameSessionState.GameRunning, machine.Snapshot.State);
    }

    [TestMethod]
    public void StaleEvidenceAfterLauncherRestoreCannotResurrectOldSession()
    {
        var machine = CreateAtGameRunning();
        AssertApplied(machine, GameSessionSignal.GameExitedConfirmed, GameSessionState.GameExitDetected, LaunchA);
        AssertApplied(machine, GameSessionSignal.BeginLauncherReturn, GameSessionState.ReturningToLauncher, LaunchA);
        AssertApplied(machine, GameSessionSignal.LauncherRestored, GameSessionState.Launcher, LaunchA);

        var stale = machine.Apply(Request(GameSessionSignal.GameRunningConfirmed, LaunchA));

        Assert.AreEqual(GameSessionTransitionDisposition.Rejected, stale.Disposition);
        Assert.AreEqual(GameSessionReasonCodes.NoActiveLaunch, stale.ReasonCode);
        Assert.AreEqual(GameSessionState.Launcher, machine.Snapshot.State);
    }

    [TestMethod]
    public void NewLaunchCanStartAfterPreviousSessionReturnsToLauncher()
    {
        var machine = CreateAtGameRunning();
        AssertApplied(machine, GameSessionSignal.GameExitedConfirmed, GameSessionState.GameExitDetected, LaunchA);
        AssertApplied(machine, GameSessionSignal.BeginLauncherReturn, GameSessionState.ReturningToLauncher, LaunchA);
        AssertApplied(machine, GameSessionSignal.LauncherRestored, GameSessionState.Launcher, LaunchA);

        AssertApplied(machine, GameSessionSignal.LaunchRequested, GameSessionState.Preparing, LaunchB);
        Assert.AreEqual(LaunchB, machine.Snapshot.ActiveLaunch);
    }

    [TestMethod]
    public void InvalidLaunchIdentityFailsClosedWithoutMutation()
    {
        var machine = new GameSessionStateMachine();
        AssertApplied(machine, GameSessionSignal.LauncherReady, GameSessionState.Launcher);
        var revision = machine.Snapshot.Revision;

        var invalid = machine.Apply(Request(
            GameSessionSignal.LaunchRequested,
            new GameSessionLaunchIdentity(" ", "corr", "game")));

        Assert.AreEqual(GameSessionTransitionDisposition.Rejected, invalid.Disposition);
        Assert.AreEqual(GameSessionReasonCodes.LaunchIdentityRequired, invalid.ReasonCode);
        Assert.AreEqual(GameSessionState.Launcher, machine.Snapshot.State);
        Assert.AreEqual(revision, machine.Snapshot.Revision);
    }

    private static GameSessionStateMachine CreateAtClientHandoff(bool accepted = false)
    {
        var machine = new GameSessionStateMachine();
        AssertApplied(machine, GameSessionSignal.LauncherReady, GameSessionState.Launcher);
        AssertApplied(machine, GameSessionSignal.LaunchRequested, GameSessionState.Preparing, LaunchA);
        AssertApplied(machine, GameSessionSignal.ClientHandoffSubmitted, GameSessionState.ClientHandoff, LaunchA);
        if (accepted)
        {
            var result = machine.Apply(Request(GameSessionSignal.ClientHandoffAccepted, LaunchA));
            Assert.AreEqual(GameSessionTransitionDisposition.Applied, result.Disposition);
        }

        return machine;
    }

    private static GameSessionStateMachine CreateAtGameStarting()
    {
        var machine = CreateAtClientHandoff(accepted: true);
        AssertApplied(machine, GameSessionSignal.GameStartingConfirmed, GameSessionState.GameStarting, LaunchA);
        return machine;
    }

    private static GameSessionStateMachine CreateAtGameRunning()
    {
        var machine = CreateAtGameStarting();
        AssertApplied(machine, GameSessionSignal.GameRunningConfirmed, GameSessionState.GameRunning, LaunchA);
        return machine;
    }

    private static GameSessionTransitionRequest Request(
        GameSessionSignal signal,
        GameSessionLaunchIdentity? launch = null)
        => new(
            signal,
            IsGameModeCommitted: true,
            launch);

    private static void AssertApplied(
        GameSessionStateMachine machine,
        GameSessionSignal signal,
        GameSessionState expected,
        GameSessionLaunchIdentity? launch = null)
    {
        var result = machine.Apply(Request(signal, launch));
        Assert.AreEqual(GameSessionTransitionDisposition.Applied, result.Disposition, result.ReasonCode);
        Assert.AreEqual(expected, result.Snapshot.State);
    }
}
