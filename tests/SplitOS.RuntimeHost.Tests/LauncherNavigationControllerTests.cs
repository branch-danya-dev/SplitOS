using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Runtime.Client;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class LauncherNavigationControllerTests
{
    [TestMethod]
    public void InitializeAcceptsOnlyRootRoute()
    {
        var controller = new LauncherNavigationController();

        var home = controller.Initialize();
        Assert.AreEqual(LauncherNavigationDisposition.Applied, home.Disposition);
        Assert.AreEqual(LauncherRouteKind.Home, home.Snapshot.CurrentRoute.Kind);
        Assert.AreEqual(0, home.Snapshot.NestedDepth);

        var invalid = new LauncherNavigationController().Initialize(LauncherRoute.GameDetails("game-1"));
        Assert.AreEqual(LauncherNavigationDisposition.Rejected, invalid.Disposition);
        Assert.AreEqual(LauncherNavigationReasonCodes.InvalidRoute, invalid.ReasonCode);
    }

    [TestMethod]
    public void RootSwitchDoesNotAccumulateBackHistory()
    {
        var controller = Initialized();

        var library = controller.GoLibrary("library.default");
        Assert.AreEqual(LauncherRouteKind.Library, library.Snapshot.CurrentRoute.Kind);
        Assert.AreEqual("library.default", library.Snapshot.PreferredFocusKey);
        Assert.AreEqual(0, library.Snapshot.NestedDepth);

        var home = controller.GoHome("home.library");
        Assert.AreEqual(LauncherRouteKind.Home, home.Snapshot.CurrentRoute.Kind);
        Assert.AreEqual(0, home.Snapshot.NestedDepth);

        var back = controller.Back();
        Assert.AreEqual(LauncherNavigationDisposition.NoOp, back.Disposition);
        Assert.AreEqual(LauncherNavigationReasonCodes.RootBackNoOp, back.ReasonCode);
        Assert.AreEqual(LauncherRouteKind.Home, back.Snapshot.CurrentRoute.Kind);
    }

    [TestMethod]
    public void GameDetailsBackRestoresExactParentRouteAndSemanticFocus()
    {
        var controller = Initialized();
        _ = controller.GoLibrary("game:cyberpunk");

        var details = controller.OpenGameDetails("cyberpunk", "game:cyberpunk");
        Assert.AreEqual(LauncherRouteKind.GameDetails, details.Snapshot.CurrentRoute.Kind);
        Assert.AreEqual("cyberpunk", details.Snapshot.CurrentRoute.GameId);
        Assert.AreEqual(1, details.Snapshot.NestedDepth);
        Assert.IsNull(details.Snapshot.PreferredFocusKey);

        var back = controller.Back();
        Assert.AreEqual(LauncherNavigationTransitionKind.BackRestored, back.TransitionKind);
        Assert.AreEqual(LauncherRouteKind.Library, back.Snapshot.CurrentRoute.Kind);
        Assert.AreEqual("game:cyberpunk", back.Snapshot.PreferredFocusKey);
        Assert.AreEqual(0, back.Snapshot.NestedDepth);
    }

    [TestMethod]
    public void NestedDetailsCanRestoreMultipleMeaningfulParents()
    {
        var controller = Initialized();
        _ = controller.OpenGameDetails("first", "home.continue");
        _ = controller.OpenGameDetails("second", "details.related:first");

        var firstBack = controller.Back();
        Assert.AreEqual("first", firstBack.Snapshot.CurrentRoute.GameId);
        Assert.AreEqual("details.related:first", firstBack.Snapshot.PreferredFocusKey);

        var secondBack = controller.Back();
        Assert.AreEqual(LauncherRouteKind.Home, secondBack.Snapshot.CurrentRoute.Kind);
        Assert.AreEqual("home.continue", secondBack.Snapshot.PreferredFocusKey);
    }

    [TestMethod]
    public void HomeBackNeverImpliesLeavingGameMode()
    {
        var controller = Initialized();
        var revision = controller.Snapshot.Revision;

        var result = controller.Back();

        Assert.AreEqual(LauncherNavigationDisposition.NoOp, result.Disposition);
        Assert.AreEqual(LauncherNavigationReasonCodes.RootBackNoOp, result.ReasonCode);
        Assert.AreEqual(LauncherRouteKind.Home, result.Snapshot.CurrentRoute.Kind);
        Assert.AreEqual(revision, result.Snapshot.Revision);
    }

    [TestMethod]
    public void BookmarkUsesStableSemanticRouteAndFocusKeys()
    {
        var controller = Initialized();
        _ = controller.GoLibrary();
        _ = controller.OpenGameDetails("game-42", "game:game-42");

        var bookmark = controller.CaptureBookmark("action.launch");

        Assert.AreEqual("GAME_DETAILS:game-42", bookmark.Route);
        Assert.AreEqual("action.launch", bookmark.FocusKey);
    }

    [TestMethod]
    public void ValidBookmarkRestoresExactRouteAndFocusWithoutInventingHistory()
    {
        var controller = Initialized();
        _ = controller.GoLibrary();

        var result = controller.RestoreBookmark(
            new LauncherPresentationBookmark("GAME_DETAILS:game-42", "action.launch"),
            gameDetailsStillAvailable: true);

        Assert.AreEqual(LauncherNavigationTransitionKind.BookmarkRestored, result.TransitionKind);
        Assert.AreEqual(LauncherRouteKind.GameDetails, result.Snapshot.CurrentRoute.Kind);
        Assert.AreEqual("game-42", result.Snapshot.CurrentRoute.GameId);
        Assert.AreEqual("action.launch", result.Snapshot.PreferredFocusKey);
        Assert.AreEqual(0, result.Snapshot.NestedDepth);
    }

    [TestMethod]
    public void MissingBookmarkedGameFallsBackLibraryThenHome()
    {
        var libraryFallback = Initialized();
        var library = libraryFallback.RestoreBookmark(
            new LauncherPresentationBookmark("GAME_DETAILS:missing", "action.launch"),
            gameDetailsStillAvailable: false,
            libraryAvailable: true);
        Assert.AreEqual(LauncherNavigationTransitionKind.FallbackApplied, library.TransitionKind);
        Assert.AreEqual(LauncherRouteKind.Library, library.Snapshot.CurrentRoute.Kind);

        var homeFallback = Initialized();
        var home = homeFallback.RestoreBookmark(
            new LauncherPresentationBookmark("GAME_DETAILS:missing", "action.launch"),
            gameDetailsStillAvailable: false,
            libraryAvailable: false);
        Assert.AreEqual(LauncherRouteKind.Home, home.Snapshot.CurrentRoute.Kind);
    }

    [TestMethod]
    public void InvalidBookmarkIsRejectedWithoutChangingCurrentRoute()
    {
        var controller = Initialized();
        _ = controller.GoLibrary("library.default");
        var revision = controller.Snapshot.Revision;

        var result = controller.RestoreBookmark(
            new LauncherPresentationBookmark("UNKNOWN", "anything"),
            gameDetailsStillAvailable: true);

        Assert.AreEqual(LauncherNavigationDisposition.Rejected, result.Disposition);
        Assert.AreEqual(LauncherNavigationReasonCodes.InvalidBookmark, result.ReasonCode);
        Assert.AreEqual(LauncherRouteKind.Library, result.Snapshot.CurrentRoute.Kind);
        Assert.AreEqual(revision, result.Snapshot.Revision);
    }

    [TestMethod]
    public void GameIdIsOpaqueSemanticIdentityNotExecutablePath()
    {
        var controller = Initialized();
        const string opaqueGameId = "steam:1091500";

        var result = controller.OpenGameDetails(opaqueGameId, "game:1091500");

        Assert.AreEqual(opaqueGameId, result.Snapshot.CurrentRoute.GameId);
        Assert.AreEqual($"GAME_DETAILS:{opaqueGameId}", result.Snapshot.CurrentRoute.RouteKey);
    }

    private static LauncherNavigationController Initialized()
    {
        var controller = new LauncherNavigationController();
        _ = controller.Initialize();
        return controller;
    }
}
