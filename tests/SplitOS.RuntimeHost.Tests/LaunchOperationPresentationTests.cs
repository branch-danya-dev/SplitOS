using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Contracts.Protocol;
using SplitOS.Runtime.Client;
using SplitOS.RuntimeHost;
using SplitOS.RuntimeHost.GameRuntime;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class LaunchOperationPresentationTests
{
    private static readonly GameSessionLaunchIdentity Launch = new(
        "launch-001",
        "corr-001",
        "game.example");

    [TestMethod]
    public void PreparingSessionProjectsRequestedWithoutInventedActions()
    {
        var session = PreparingSession();
        var owner = new LaunchOperationPresentationState(session);

        var projection = owner.Project(session.Snapshot, isGameModeCommitted: true);

        Assert.IsNotNull(projection);
        Assert.AreEqual("REQUESTED", projection.Phase);
        Assert.IsNull(projection.FailureClass);
        Assert.IsNull(projection.ExternalClientOutcome);
        Assert.AreEqual(0, projection.AllowedActions.Count);
    }

    [TestMethod]
    public void RuntimeCanExposePrecisePreparingPhaseAndOnlyExplicitActions()
    {
        var session = PreparingSession();
        var owner = new LaunchOperationPresentationState(session);

        var update = owner.ReportProgress(
            Launch,
            LaunchPresentationPhase.PreparingWindowsContext,
            [LaunchPresentationAllowedAction.Cancel]);
        var projection = owner.Project(session.Snapshot, isGameModeCommitted: true);

        Assert.AreEqual(LaunchPresentationUpdateDisposition.Applied, update.Disposition);
        Assert.IsNotNull(projection);
        Assert.AreEqual("PREPARING_WINDOWS_CONTEXT", projection.Phase);
        CollectionAssert.AreEqual(new[] { "CANCEL" }, projection.AllowedActions.ToArray());
    }

    [TestMethod]
    public void SessionRevisionAdvanceInvalidatesPreviouslyAllowedActions()
    {
        var session = PreparingSession();
        var owner = new LaunchOperationPresentationState(session);
        _ = owner.ReportProgress(
            Launch,
            LaunchPresentationPhase.PreparingGameConfiguration,
            [LaunchPresentationAllowedAction.Cancel]);

        _ = session.Apply(new GameSessionTransitionRequest(
            GameSessionSignal.ClientHandoffSubmitted,
            IsGameModeCommitted: true,
            Launch));
        var projection = owner.Project(session.Snapshot, isGameModeCommitted: true);

        Assert.IsNotNull(projection);
        Assert.AreEqual("CLIENT_HANDOFF", projection.Phase);
        Assert.AreEqual(0, projection.AllowedActions.Count);
    }

    [TestMethod]
    public void AcceptedClientHandoffProjectsWaitingNotRunning()
    {
        var session = PreparingSession();
        _ = session.Apply(new GameSessionTransitionRequest(
            GameSessionSignal.ClientHandoffSubmitted,
            true,
            Launch));
        _ = session.Apply(new GameSessionTransitionRequest(
            GameSessionSignal.ClientHandoffAccepted,
            true,
            Launch));
        var owner = new LaunchOperationPresentationState(session);

        var projection = owner.Project(session.Snapshot, isGameModeCommitted: true);

        Assert.IsNotNull(projection);
        Assert.AreEqual("WAITING_FOR_GAME", projection.Phase);
        Assert.AreNotEqual("GAME_RUNNING_CONFIRMED", projection.Phase);
    }

    [TestMethod]
    public void GameStartingStillProjectsWaitingUntilRunningEvidenceArrives()
    {
        var session = PreparingSession();
        MoveToGameStarting(session);
        var owner = new LaunchOperationPresentationState(session);

        var waiting = owner.Project(session.Snapshot, true);
        Assert.IsNotNull(waiting);
        Assert.AreEqual("WAITING_FOR_GAME", waiting.Phase);

        _ = session.Apply(new GameSessionTransitionRequest(
            GameSessionSignal.GameRunningConfirmed,
            true,
            Launch));
        var running = owner.Project(session.Snapshot, true);
        Assert.IsNotNull(running);
        Assert.AreEqual("GAME_RUNNING_CONFIRMED", running.Phase);
        Assert.AreEqual(0, running.AllowedActions.Count);
    }

    [TestMethod]
    public void StartTimeoutMapsToTypedFailureWithoutInferringRetry()
    {
        var session = PreparingSession();
        _ = session.Apply(new GameSessionTransitionRequest(
            GameSessionSignal.ClientHandoffSubmitted,
            true,
            Launch));
        _ = session.Apply(new GameSessionTransitionRequest(
            GameSessionSignal.ClientHandoffAccepted,
            true,
            Launch));
        _ = session.Apply(new GameSessionTransitionRequest(
            GameSessionSignal.StartTimeout,
            true,
            Launch));
        var owner = new LaunchOperationPresentationState(session);

        var projection = owner.Project(session.Snapshot, true);

        Assert.IsNotNull(projection);
        Assert.AreEqual("GAME_START_NOT_CONFIRMED", projection.FailureClass);
        Assert.AreEqual(0, projection.AllowedActions.Count);
    }

    [TestMethod]
    public void RuntimeMayExposeRetryOnlyAfterTypedFailureAndExactCurrentRevision()
    {
        var session = PreparingSession();
        _ = session.Apply(new GameSessionTransitionRequest(
            GameSessionSignal.SessionFailed,
            true,
            Launch,
            FailureCode: "AUTH_REQUIRED"));
        var owner = new LaunchOperationPresentationState(session);

        var update = owner.ReportFailure(
            Launch,
            LaunchPresentationFailureClass.AuthRequired,
            [LaunchPresentationAllowedAction.OpenClient, LaunchPresentationAllowedAction.Retry]);
        var projection = owner.Project(session.Snapshot, true);

        Assert.AreEqual(LaunchPresentationUpdateDisposition.Applied, update.Disposition);
        Assert.IsNotNull(projection);
        Assert.AreEqual("AUTH_REQUIRED", projection.FailureClass);
        CollectionAssert.AreEqual(
            new[] { "RETRY", "OPEN_CLIENT" },
            projection.AllowedActions.ToArray());
    }

    [TestMethod]
    public void ExternalAuthOutcomeDoesNotRequireLauncherToOwnCredentials()
    {
        var session = PreparingSession();
        var owner = new LaunchOperationPresentationState(session);

        var update = owner.ReportExternalClientOutcome(
            Launch,
            LaunchPresentationExternalClientOutcome.AuthRequired,
            [LaunchPresentationAllowedAction.OpenClient]);
        var projection = owner.Project(session.Snapshot, true);

        Assert.AreEqual(LaunchPresentationUpdateDisposition.Applied, update.Disposition);
        Assert.IsNotNull(projection);
        Assert.AreEqual("AUTH_REQUIRED", projection.ExternalClientOutcome);
        Assert.AreEqual("REQUESTED", projection.Phase);
        CollectionAssert.AreEqual(new[] { "OPEN_CLIENT" }, projection.AllowedActions.ToArray());
    }

    [TestMethod]
    public void PresentationUpdateForDifferentLaunchIdentityIsRejected()
    {
        var session = PreparingSession();
        var owner = new LaunchOperationPresentationState(session);
        var other = new GameSessionLaunchIdentity("launch-other", "corr-other", "game.other");

        var decision = owner.ReportProgress(other, LaunchPresentationPhase.ResolvingProfile);

        Assert.AreEqual(LaunchPresentationUpdateDisposition.Rejected, decision.Disposition);
        Assert.AreEqual(LaunchPresentationReasonCodes.ActiveLaunchMismatch, decision.ReasonCode);
        Assert.AreEqual(0L, owner.Revision);
    }

    [TestMethod]
    public void FailureClassMustMatchCanonicalGameSessionFailure()
    {
        var session = PreparingSession();
        _ = session.Apply(new GameSessionTransitionRequest(
            GameSessionSignal.SessionFailed,
            true,
            Launch,
            FailureCode: "AUTH_REQUIRED"));
        var owner = new LaunchOperationPresentationState(session);

        var decision = owner.ReportFailure(
            Launch,
            LaunchPresentationFailureClass.GameNotInstalled,
            [LaunchPresentationAllowedAction.Retry]);

        Assert.AreEqual(LaunchPresentationUpdateDisposition.Rejected, decision.Disposition);
        Assert.AreEqual(LaunchPresentationReasonCodes.FailureClassMismatch, decision.ReasonCode);
    }

    [TestMethod]
    public void CoherentLauncherSnapshotChangesVersionWhenOnlyRuntimeUxOutcomeChanges()
    {
        var runtime = ReadyRuntimeState("GAME");
        var session = PreparingSession();
        var readiness = new LauncherReadinessState();
        var launchPresentation = new LaunchOperationPresentationState(session);
        var provider = new LauncherRuntimeSnapshotProvider(
            runtime,
            session,
            readiness,
            launchPresentation);

        var first = provider.Read();
        Assert.IsNotNull(first.LaunchPresentation);
        Assert.AreEqual("REQUESTED", first.LaunchPresentation.Phase);

        _ = launchPresentation.ReportExternalClientOutcome(
            Launch,
            LaunchPresentationExternalClientOutcome.AuthRequired,
            [LaunchPresentationAllowedAction.OpenClient]);
        var second = provider.Read();

        Assert.IsTrue(second.SnapshotVersion > first.SnapshotVersion);
        Assert.IsNotNull(second.LaunchPresentation);
        Assert.AreEqual("AUTH_REQUIRED", second.LaunchPresentation.ExternalClientOutcome);
        CollectionAssert.AreEqual(new[] { "OPEN_CLIENT" }, second.LaunchPresentation.AllowedActions.ToArray());

        var reread = provider.Read();
        Assert.AreEqual(second.SnapshotVersion, reread.SnapshotVersion);
    }

    [TestMethod]
    public void LauncherControllerRejectsLaunchProjectionForDifferentOperation()
    {
        var controller = new LauncherLaunchPresentationController();
        var projection = Presentation(
            operationId: "other-operation",
            phase: "REQUESTED");
        var snapshot = Snapshot("PREPARING", projection);

        var decision = controller.Observe(snapshot);

        Assert.AreEqual(LauncherLaunchPresentationDisposition.Rejected, decision.Disposition);
        Assert.AreEqual(LauncherLaunchPresentationReasonCodes.IdentityMismatch, decision.ReasonCode);
        Assert.AreEqual(LauncherLaunchPresentationMode.Hidden, decision.View.Mode);
    }

    [TestMethod]
    public void LauncherControllerRendersWaitingWithoutCallingItRunning()
    {
        var controller = new LauncherLaunchPresentationController();
        var decision = controller.Observe(Snapshot(
            "CLIENT_HANDOFF",
            Presentation(phase: "WAITING_FOR_GAME")));

        Assert.AreEqual(LauncherLaunchPresentationDisposition.Applied, decision.Disposition);
        Assert.AreEqual(LauncherLaunchPresentationMode.Progress, decision.View.Mode);
        Assert.AreEqual("Waiting for game…", decision.View.Title);
        Assert.AreEqual(0, decision.View.AllowedActions.Count);
    }

    [TestMethod]
    public void LauncherControllerShowsOnlyRuntimeAllowedFailureActions()
    {
        var controller = new LauncherLaunchPresentationController();
        var presentation = Presentation(
            phase: null,
            failureClass: "GAME_START_NOT_CONFIRMED",
            actions: ["KEEP_WAITING", "OPEN_CLIENT"]);

        var decision = controller.Observe(Snapshot("FAILED", presentation));

        Assert.AreEqual(LauncherLaunchPresentationMode.Failure, decision.View.Mode);
        Assert.IsTrue(decision.View.Allows(LauncherLaunchAction.KeepWaiting));
        Assert.IsTrue(decision.View.Allows(LauncherLaunchAction.OpenClient));
        Assert.IsFalse(decision.View.Allows(LauncherLaunchAction.Retry));
        Assert.IsFalse(decision.View.Allows(LauncherLaunchAction.Cancel));
    }

    [TestMethod]
    public void LauncherControllerShowsExternalAuthAsClientOwnedActionRequirement()
    {
        var controller = new LauncherLaunchPresentationController();
        var presentation = Presentation(
            phase: "REQUESTED",
            externalOutcome: "AUTH_REQUIRED",
            actions: ["OPEN_CLIENT"]);

        var decision = controller.Observe(Snapshot("PREPARING", presentation));

        Assert.AreEqual(LauncherLaunchPresentationMode.ExternalActionRequired, decision.View.Mode);
        Assert.AreEqual("Sign-in required in game client", decision.View.Title);
        Assert.IsTrue(decision.View.Allows(LauncherLaunchAction.OpenClient));
        Assert.IsFalse(decision.View.Allows(LauncherLaunchAction.Retry));
    }

    [TestMethod]
    public void LauncherControllerRejectsUnknownRuntimeActionFailClosed()
    {
        var controller = new LauncherLaunchPresentationController();
        var presentation = Presentation(
            phase: "REQUESTED",
            actions: ["RETRY_WITH_FORCE"]);

        var decision = controller.Observe(Snapshot("PREPARING", presentation));

        Assert.AreEqual(LauncherLaunchPresentationDisposition.Rejected, decision.Disposition);
        Assert.AreEqual(LauncherLaunchPresentationReasonCodes.InvalidProjection, decision.ReasonCode);
    }

    [TestMethod]
    public void LauncherControllerClearsPresentationWhenFreshSnapshotHasNoLaunchProjection()
    {
        var controller = new LauncherLaunchPresentationController();
        _ = controller.Observe(Snapshot("PREPARING", Presentation(phase: "REQUESTED")));

        var cleared = controller.Observe(Snapshot("LAUNCHER", null, activeLaunch: false));

        Assert.AreEqual(LauncherLaunchPresentationDisposition.Applied, cleared.Disposition);
        Assert.AreEqual(LauncherLaunchPresentationMode.Hidden, cleared.View.Mode);
    }

    private static GameSessionStateMachine PreparingSession()
    {
        var session = new GameSessionStateMachine();
        _ = session.Apply(new GameSessionTransitionRequest(
            GameSessionSignal.LauncherReady,
            IsGameModeCommitted: true));
        _ = session.Apply(new GameSessionTransitionRequest(
            GameSessionSignal.LaunchRequested,
            IsGameModeCommitted: true,
            Launch));
        return session;
    }

    private static void MoveToGameStarting(GameSessionStateMachine session)
    {
        _ = session.Apply(new GameSessionTransitionRequest(
            GameSessionSignal.ClientHandoffSubmitted,
            true,
            Launch));
        _ = session.Apply(new GameSessionTransitionRequest(
            GameSessionSignal.ClientHandoffAccepted,
            true,
            Launch));
        _ = session.Apply(new GameSessionTransitionRequest(
            GameSessionSignal.GameStartingConfirmed,
            true,
            Launch));
    }

    private static RuntimeStateState ReadyRuntimeState(string committedMode)
    {
        var state = new RuntimeStateState();
        state.Report(new RuntimeStateReadResult(
            "READY",
            "ENABLED",
            committedMode,
            "ASSOCIATED",
            1,
            1,
            1,
            DateTimeOffset.UtcNow));
        return state;
    }

    private static LauncherRuntimeSnapshotResult Snapshot(
        string gameSessionState,
        LauncherLaunchPresentationResult? presentation,
        bool activeLaunch = true)
        => new(
            "READY",
            "ENABLED",
            "GAME",
            gameSessionState,
            GameSessionRevision: 3,
            ActiveLaunchOperationId: activeLaunch ? Launch.LaunchOperationId : null,
            ActiveLaunchCorrelationId: activeLaunch ? Launch.CorrelationId : null,
            ActiveGameId: activeLaunch ? Launch.GameId : null,
            ExpectedGameModeOperationId: null,
            ExpectedGameModeCorrelationId: null,
            ReadinessRevision: 0,
            SnapshotVersion: 1,
            ObservedAtUtc: DateTimeOffset.UtcNow,
            LaunchPresentation: presentation);

    private static LauncherLaunchPresentationResult Presentation(
        string operationId = "launch-001",
        string? phase = "REQUESTED",
        string? failureClass = null,
        string? externalOutcome = null,
        IReadOnlyList<string>? actions = null)
        => new(
            operationId,
            Launch.CorrelationId,
            Launch.GameId,
            phase,
            failureClass,
            externalOutcome,
            actions ?? Array.Empty<string>(),
            RuntimePresentationRevision: 1,
            ObservedAtUtc: DateTimeOffset.UtcNow);
}
