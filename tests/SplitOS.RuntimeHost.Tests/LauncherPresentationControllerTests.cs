using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Runtime.Client;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class LauncherPresentationControllerTests
{
    [TestMethod]
    public void RunningTruthYieldsForegroundAndSuppressesNavigation()
    {
        var controller = ActiveController();
        var bookmark = new LauncherPresentationBookmark("GAME_DETAILS(game-a)", "Launch");
        var captured = controller.CaptureBookmark(bookmark);
        Assert.AreEqual(LauncherPresentationDisposition.Applied, captured.Disposition);

        var result = controller.Observe(
            Projection(2, LauncherRuntimeGameSessionState.GameRunning, launchOperationId: "launch-a"),
            LauncherRuntimeUpdateKind.IncrementalEvent);

        Assert.AreEqual(LauncherPresentationDisposition.Applied, result.Disposition);
        Assert.AreEqual(LauncherPresentationState.BackgroundGameRunning, result.PresentationState);
        Assert.AreEqual(LauncherWindowIntent.YieldForeground, result.WindowIntent);
        Assert.AreEqual(LauncherNavigationIntent.Suppressed, result.NavigationIntent);
        Assert.IsTrue(result.HasPendingBookmark);
        Assert.IsNull(result.BookmarkToRestore);
    }

    [TestMethod]
    public void ExitDetectedRestoresWindowButKeepsNavigationSuppressedUntilLauncherTruth()
    {
        var controller = BackgroundController();

        var exiting = controller.Observe(
            Projection(3, LauncherRuntimeGameSessionState.GameExitDetected, "launch-a"),
            LauncherRuntimeUpdateKind.IncrementalEvent);

        Assert.AreEqual(LauncherPresentationState.Restoring, exiting.PresentationState);
        Assert.AreEqual(LauncherWindowIntent.RestoreForeground, exiting.WindowIntent);
        Assert.AreEqual(LauncherNavigationIntent.Suppressed, exiting.NavigationIntent);
        Assert.IsNull(exiting.BookmarkToRestore);

        var returning = controller.Observe(
            Projection(4, LauncherRuntimeGameSessionState.ReturningToLauncher, "launch-a"),
            LauncherRuntimeUpdateKind.IncrementalEvent);
        Assert.AreEqual(LauncherPresentationState.Restoring, returning.PresentationState);
        Assert.AreEqual(LauncherWindowIntent.None, returning.WindowIntent);
        Assert.AreEqual(LauncherNavigationIntent.Suppressed, returning.NavigationIntent);
    }

    [TestMethod]
    public void LauncherTruthRestoresBookmarkExactlyOnceAndReenablesNavigation()
    {
        var controller = BackgroundController();
        _ = controller.Observe(
            Projection(3, LauncherRuntimeGameSessionState.GameExitDetected, "launch-a"),
            LauncherRuntimeUpdateKind.IncrementalEvent);
        _ = controller.Observe(
            Projection(4, LauncherRuntimeGameSessionState.ReturningToLauncher, "launch-a"),
            LauncherRuntimeUpdateKind.IncrementalEvent);

        var restored = controller.Observe(
            Projection(5, LauncherRuntimeGameSessionState.Launcher),
            LauncherRuntimeUpdateKind.IncrementalEvent);

        Assert.AreEqual(LauncherPresentationState.Active, restored.PresentationState);
        Assert.AreEqual(LauncherWindowIntent.RestoreForeground, restored.WindowIntent);
        Assert.AreEqual(LauncherNavigationIntent.Enabled, restored.NavigationIntent);
        Assert.AreEqual("GAME_DETAILS(game-a)", restored.BookmarkToRestore?.Route);
        Assert.AreEqual("Launch", restored.BookmarkToRestore?.FocusKey);
        Assert.IsFalse(restored.HasPendingBookmark);

        var later = controller.Observe(
            Projection(6, LauncherRuntimeGameSessionState.Launcher),
            LauncherRuntimeUpdateKind.IncrementalEvent);
        Assert.IsNull(later.BookmarkToRestore);
        Assert.AreEqual(LauncherWindowIntent.None, later.WindowIntent);
    }

    [TestMethod]
    public void GameRunningCannotBeInferredFromPreparingOrHandoffStates()
    {
        var controller = ActiveController();

        var preparing = controller.Observe(
            Projection(2, LauncherRuntimeGameSessionState.Preparing, "launch-a"),
            LauncherRuntimeUpdateKind.IncrementalEvent);
        Assert.AreEqual(LauncherPresentationState.Active, preparing.PresentationState);
        Assert.AreNotEqual(LauncherWindowIntent.YieldForeground, preparing.WindowIntent);
        Assert.AreEqual(LauncherNavigationIntent.Enabled, preparing.NavigationIntent);

        var handoff = controller.Observe(
            Projection(3, LauncherRuntimeGameSessionState.ClientHandoff, "launch-a"),
            LauncherRuntimeUpdateKind.IncrementalEvent);
        Assert.AreEqual(LauncherPresentationState.Active, handoff.PresentationState);
        Assert.AreNotEqual(LauncherWindowIntent.YieldForeground, handoff.WindowIntent);

        var starting = controller.Observe(
            Projection(4, LauncherRuntimeGameSessionState.GameStarting, "launch-a"),
            LauncherRuntimeUpdateKind.IncrementalEvent);
        Assert.AreEqual(LauncherPresentationState.Active, starting.PresentationState);
        Assert.AreNotEqual(LauncherWindowIntent.YieldForeground, starting.WindowIntent);
    }

    [TestMethod]
    public void FailedWhileGameWasBackgroundedRestoresFailureUxButPreservesBookmark()
    {
        var controller = BackgroundController();

        var failed = controller.Observe(
            Projection(3, LauncherRuntimeGameSessionState.Failed, "launch-a"),
            LauncherRuntimeUpdateKind.IncrementalEvent);

        Assert.AreEqual(LauncherPresentationState.Active, failed.PresentationState);
        Assert.AreEqual(LauncherWindowIntent.RestoreForeground, failed.WindowIntent);
        Assert.AreEqual(LauncherNavigationIntent.Enabled, failed.NavigationIntent);
        Assert.IsTrue(failed.HasPendingBookmark);
        Assert.IsNull(failed.BookmarkToRestore);

        var launcher = controller.Observe(
            Projection(4, LauncherRuntimeGameSessionState.Launcher),
            LauncherRuntimeUpdateKind.IncrementalEvent);
        Assert.AreEqual("GAME_DETAILS(game-a)", launcher.BookmarkToRestore?.Route);
        Assert.IsFalse(launcher.HasPendingBookmark);
    }

    [TestMethod]
    public void MissingIncrementalRevisionRequestsFreshSnapshotWithoutChangingPresentation()
    {
        var controller = ActiveController();

        var result = controller.Observe(
            Projection(3, LauncherRuntimeGameSessionState.GameRunning, "launch-a"),
            LauncherRuntimeUpdateKind.IncrementalEvent);

        Assert.AreEqual(LauncherPresentationDisposition.FreshSnapshotRequired, result.Disposition);
        Assert.AreEqual(LauncherPresentationReasonCodes.EventRevisionGap, result.ReasonCode);
        Assert.AreEqual(LauncherPresentationState.Active, result.PresentationState);
        Assert.AreEqual(1L, result.LastRuntimeRevision);
    }

    [TestMethod]
    public void IncrementalEventCannotEstablishInitialAuthority()
    {
        var controller = new LauncherPresentationController();

        var result = controller.Observe(
            Projection(10, LauncherRuntimeGameSessionState.GameRunning, "launch-a"),
            LauncherRuntimeUpdateKind.IncrementalEvent);

        Assert.AreEqual(LauncherPresentationDisposition.FreshSnapshotRequired, result.Disposition);
        Assert.AreEqual(LauncherPresentationReasonCodes.EventBaselineRequired, result.ReasonCode);
        Assert.AreEqual(-1L, result.LastRuntimeRevision);
        Assert.AreEqual(LauncherPresentationState.Inactive, result.PresentationState);
    }

    [TestMethod]
    public void FreshSnapshotCanReconcileAcrossRevisionGap()
    {
        var controller = ActiveController();

        var result = controller.Observe(
            Projection(20, LauncherRuntimeGameSessionState.GameRunning, "launch-a"),
            LauncherRuntimeUpdateKind.Snapshot);

        Assert.AreEqual(LauncherPresentationDisposition.Applied, result.Disposition);
        Assert.AreEqual(20L, result.LastRuntimeRevision);
        Assert.AreEqual(LauncherPresentationState.BackgroundGameRunning, result.PresentationState);
        Assert.AreEqual(LauncherWindowIntent.YieldForeground, result.WindowIntent);
    }

    [TestMethod]
    public void SameRevisionConflictRequiresRequeryAndDoesNotMutateState()
    {
        var controller = ActiveController();

        var conflict = controller.Observe(
            Projection(1, LauncherRuntimeGameSessionState.GameRunning, "launch-a"),
            LauncherRuntimeUpdateKind.Snapshot);

        Assert.AreEqual(LauncherPresentationDisposition.FreshSnapshotRequired, conflict.Disposition);
        Assert.AreEqual(LauncherPresentationReasonCodes.ConflictingSameRevision, conflict.ReasonCode);
        Assert.AreEqual(LauncherPresentationState.Active, controller.PresentationState);
        Assert.AreEqual(1L, controller.LastRuntimeRevision);
    }

    [TestMethod]
    public void OlderProjectionIsIgnoredAndCannotResurrectBackgroundState()
    {
        var controller = BackgroundController();
        _ = controller.Observe(
            Projection(3, LauncherRuntimeGameSessionState.GameExitDetected, "launch-a"),
            LauncherRuntimeUpdateKind.IncrementalEvent);
        _ = controller.Observe(
            Projection(4, LauncherRuntimeGameSessionState.ReturningToLauncher, "launch-a"),
            LauncherRuntimeUpdateKind.IncrementalEvent);
        _ = controller.Observe(
            Projection(5, LauncherRuntimeGameSessionState.Launcher),
            LauncherRuntimeUpdateKind.IncrementalEvent);

        var stale = controller.Observe(
            Projection(2, LauncherRuntimeGameSessionState.GameRunning, "launch-a"),
            LauncherRuntimeUpdateKind.IncrementalEvent);

        Assert.AreEqual(LauncherPresentationDisposition.NoOp, stale.Disposition);
        Assert.AreEqual(LauncherPresentationReasonCodes.StaleProjectionIgnored, stale.ReasonCode);
        Assert.AreEqual(LauncherPresentationState.Active, controller.PresentationState);
        Assert.AreEqual(5L, controller.LastRuntimeRevision);
    }

    [TestMethod]
    public void NonInactiveGameSessionWithoutCommittedGameIsRejectedFailClosed()
    {
        var controller = new LauncherPresentationController();

        var inconsistent = controller.Observe(
            new LauncherGameSessionProjection(
                1,
                LauncherRuntimeGameSessionState.GameRunning,
                IsGameModeCommitted: false,
                LaunchOperationId: "launch-a"),
            LauncherRuntimeUpdateKind.Snapshot);

        Assert.AreEqual(LauncherPresentationDisposition.Rejected, inconsistent.Disposition);
        Assert.AreEqual(LauncherPresentationReasonCodes.RuntimeProjectionInconsistent, inconsistent.ReasonCode);
        Assert.AreEqual(-1L, controller.LastRuntimeRevision);
        Assert.AreEqual(LauncherPresentationState.Inactive, controller.PresentationState);
    }

    [TestMethod]
    public void ModeExitYieldsForegroundAndSuppressesLauncherNavigation()
    {
        var controller = ActiveController();

        var inactive = controller.Observe(
            new LauncherGameSessionProjection(
                2,
                LauncherRuntimeGameSessionState.Inactive,
                IsGameModeCommitted: false),
            LauncherRuntimeUpdateKind.IncrementalEvent);

        Assert.AreEqual(LauncherPresentationState.Inactive, inactive.PresentationState);
        Assert.AreEqual(LauncherWindowIntent.YieldForeground, inactive.WindowIntent);
        Assert.AreEqual(LauncherNavigationIntent.Suppressed, inactive.NavigationIntent);
    }

    [TestMethod]
    public void BookmarkCannotBeCapturedWhileBackgroundedOrRestoring()
    {
        var controller = BackgroundController();

        var rejected = controller.CaptureBookmark(new LauncherPresentationBookmark("HOME"));

        Assert.AreEqual(LauncherPresentationDisposition.Rejected, rejected.Disposition);
        Assert.AreEqual(LauncherPresentationReasonCodes.BookmarkCaptureNotAllowed, rejected.ReasonCode);
        Assert.IsTrue(controller.HasPendingBookmark);
    }

    private static LauncherPresentationController ActiveController()
    {
        var controller = new LauncherPresentationController();
        var initial = controller.Observe(
            Projection(1, LauncherRuntimeGameSessionState.Launcher),
            LauncherRuntimeUpdateKind.Snapshot);
        Assert.AreEqual(LauncherPresentationDisposition.Applied, initial.Disposition);
        Assert.AreEqual(LauncherPresentationState.Active, initial.PresentationState);
        return controller;
    }

    private static LauncherPresentationController BackgroundController()
    {
        var controller = ActiveController();
        var captured = controller.CaptureBookmark(
            new LauncherPresentationBookmark("GAME_DETAILS(game-a)", "Launch"));
        Assert.AreEqual(LauncherPresentationDisposition.Applied, captured.Disposition);

        var running = controller.Observe(
            Projection(2, LauncherRuntimeGameSessionState.GameRunning, "launch-a"),
            LauncherRuntimeUpdateKind.IncrementalEvent);
        Assert.AreEqual(LauncherPresentationState.BackgroundGameRunning, running.PresentationState);
        return controller;
    }

    private static LauncherGameSessionProjection Projection(
        long revision,
        LauncherRuntimeGameSessionState state,
        string? launchOperationId = null)
        => new(
            revision,
            state,
            IsGameModeCommitted: true,
            launchOperationId);
}
