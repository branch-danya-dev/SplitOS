using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Runtime.Client;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class LauncherSemanticFocusControllerTests
{
    [TestMethod]
    public void DispatchBeforeScopeLoadFailsClosed()
    {
        var controller = new LauncherSemanticFocusController();

        var result = controller.Dispatch(LauncherSemanticAction.NavRight);

        Assert.AreEqual(LauncherSemanticDispatchKind.Rejected, result.Kind);
        Assert.AreEqual(LauncherSemanticFocusReasonCodes.FocusNotReady, result.ReasonCode);
        Assert.IsFalse(result.IsReady);
        Assert.IsNull(result.FocusKey);
    }

    [TestMethod]
    public void DirectionalNavigationPrefersExplicitGraphEdge()
    {
        var controller = new LauncherSemanticFocusController();
        _ = controller.LoadRootScope(new LauncherFocusScopeDefinition(
            "ROOT",
            "FIRST",
            [
                new LauncherFocusNode(
                    "FIRST",
                    Right: "EXPLICIT",
                    Bounds: new LauncherFocusRect(0, 0, 10, 10)),
                new LauncherFocusNode(
                    "NEARER",
                    Bounds: new LauncherFocusRect(15, 0, 10, 10)),
                new LauncherFocusNode(
                    "EXPLICIT",
                    Bounds: new LauncherFocusRect(100, 0, 10, 10))
            ]));

        var result = controller.Dispatch(LauncherSemanticAction.NavRight);

        Assert.AreEqual(LauncherSemanticDispatchKind.FocusMoved, result.Kind);
        Assert.AreEqual(LauncherSemanticFocusReasonCodes.FocusMoved, result.ReasonCode);
        Assert.AreEqual("EXPLICIT", result.FocusKey);
    }

    [TestMethod]
    public void DirectionalNavigationUsesGeometricNearestWhenNoUsableExplicitEdgeExists()
    {
        var controller = new LauncherSemanticFocusController();
        _ = controller.LoadRootScope(new LauncherFocusScopeDefinition(
            "ROOT",
            "A",
            [
                new LauncherFocusNode(
                    "A",
                    Right: "UNAVAILABLE",
                    Bounds: new LauncherFocusRect(0, 0, 10, 10)),
                new LauncherFocusNode(
                    "UNAVAILABLE",
                    IsAvailable: false,
                    Bounds: new LauncherFocusRect(12, 0, 10, 10)),
                new LauncherFocusNode(
                    "NEAREST",
                    Bounds: new LauncherFocusRect(24, 0, 10, 10)),
                new LauncherFocusNode(
                    "FAR",
                    Bounds: new LauncherFocusRect(80, 0, 10, 10))
            ]));

        var result = controller.Dispatch(LauncherSemanticAction.NavRight);

        Assert.AreEqual(LauncherSemanticDispatchKind.FocusMoved, result.Kind);
        Assert.AreEqual(LauncherSemanticFocusReasonCodes.GeometricFallback, result.ReasonCode);
        Assert.AreEqual("NEAREST", result.FocusKey);
    }

    [TestMethod]
    public void DirectionalNavigationUsesRouteFallbackAfterExplicitAndGeometryFail()
    {
        var controller = new LauncherSemanticFocusController();
        _ = controller.LoadRootScope(new LauncherFocusScopeDefinition(
            "ROOT",
            "A",
            [
                new LauncherFocusNode("A", Right: "UNAVAILABLE"),
                new LauncherFocusNode("UNAVAILABLE", IsAvailable: false),
                new LauncherFocusNode("ROUTE-FALLBACK")
            ],
            new LauncherDirectionalFocusFallbacks(Right: "ROUTE-FALLBACK")));

        var result = controller.Dispatch(LauncherSemanticAction.NavRight);

        Assert.AreEqual(LauncherSemanticDispatchKind.FocusMoved, result.Kind);
        Assert.AreEqual(LauncherSemanticFocusReasonCodes.RouteFallback, result.ReasonCode);
        Assert.AreEqual("ROUTE-FALLBACK", result.FocusKey);
    }

    [TestMethod]
    public void NavigationBoundaryDoesNotMoveFocusOrBumpRevision()
    {
        var controller = new LauncherSemanticFocusController();
        _ = controller.LoadRootScope(RowScope());
        _ = controller.Dispatch(LauncherSemanticAction.NavRight);
        var revision = controller.Revision;

        var result = controller.Dispatch(LauncherSemanticAction.NavDown);

        Assert.AreEqual(LauncherSemanticDispatchKind.NoOp, result.Kind);
        Assert.AreEqual(LauncherSemanticFocusReasonCodes.NavigationBoundary, result.ReasonCode);
        Assert.AreEqual("SECOND", result.FocusKey);
        Assert.AreEqual(revision, result.Revision);
    }

    [TestMethod]
    public void FocusHomeReturnsToDeclaredRouteDefault()
    {
        var controller = new LauncherSemanticFocusController();
        _ = controller.LoadRootScope(RowScope());
        _ = controller.Dispatch(LauncherSemanticAction.NavRight);

        var result = controller.Dispatch(LauncherSemanticAction.FocusHome);

        Assert.AreEqual(LauncherSemanticDispatchKind.FocusMoved, result.Kind);
        Assert.AreEqual(LauncherSemanticFocusReasonCodes.FocusHome, result.ReasonCode);
        Assert.AreEqual("FIRST", result.FocusKey);
    }

    [TestMethod]
    public void ActivateProducesTypedInvocationWithoutMovingFocus()
    {
        var controller = new LauncherSemanticFocusController();
        _ = controller.LoadRootScope(RowScope());
        _ = controller.Dispatch(LauncherSemanticAction.NavRight);
        var revision = controller.Revision;

        var result = controller.Dispatch(LauncherSemanticAction.Activate);

        Assert.AreEqual(LauncherSemanticDispatchKind.InvocationIssued, result.Kind);
        Assert.AreEqual("OPEN_SECOND", result.InvocationId);
        Assert.AreEqual("SECOND", result.FocusKey);
        Assert.AreEqual(revision, result.Revision);
    }

    [TestMethod]
    public void NonActivatableFocusTargetDoesNotInventAnAction()
    {
        var controller = new LauncherSemanticFocusController();
        _ = controller.LoadRootScope(RowScope());

        var result = controller.Dispatch(LauncherSemanticAction.Activate);

        Assert.AreEqual(LauncherSemanticDispatchKind.NoOp, result.Kind);
        Assert.AreEqual(LauncherSemanticFocusReasonCodes.NoActivation, result.ReasonCode);
        Assert.IsNull(result.InvocationId);
        Assert.AreEqual("FIRST", result.FocusKey);
    }

    [TestMethod]
    public void GlobalSemanticCommandPreservesDeterministicFocus()
    {
        var controller = new LauncherSemanticFocusController();
        _ = controller.LoadRootScope(RowScope());
        var revision = controller.Revision;

        var result = controller.Dispatch(LauncherSemanticAction.OpenSystemMenu);

        Assert.AreEqual(LauncherSemanticDispatchKind.CommandIssued, result.Kind);
        Assert.AreEqual(LauncherSemanticFocusReasonCodes.CommandIssued, result.ReasonCode);
        Assert.AreEqual(LauncherSemanticAction.OpenSystemMenu, result.Action);
        Assert.AreEqual("FIRST", result.FocusKey);
        Assert.AreEqual(revision, result.Revision);
    }

    [TestMethod]
    public void ModalScopePopRestoresExactParentFocusKey()
    {
        var controller = new LauncherSemanticFocusController();
        _ = controller.LoadRootScope(RowScope());
        _ = controller.Dispatch(LauncherSemanticAction.NavRight);
        Assert.AreEqual("SECOND", controller.CurrentFocusKey);

        var pushed = controller.PushScope(new LauncherFocusScopeDefinition(
            "MODAL",
            "CANCEL",
            [
                new LauncherFocusNode("CANCEL", Right: "CONFIRM", ActivationId: "CANCEL"),
                new LauncherFocusNode("CONFIRM", Left: "CANCEL", ActivationId: "CONFIRM")
            ]));
        Assert.AreEqual("MODAL", pushed.ScopeKey);
        Assert.AreEqual("CANCEL", pushed.FocusKey);

        _ = controller.Dispatch(LauncherSemanticAction.NavRight);
        var popped = controller.PopScope();

        Assert.AreEqual(LauncherSemanticDispatchKind.FocusMoved, popped.Kind);
        Assert.AreEqual(LauncherSemanticFocusReasonCodes.ScopePopped, popped.ReasonCode);
        Assert.AreEqual("ROOT", popped.ScopeKey);
        Assert.AreEqual("SECOND", popped.FocusKey);
    }

    [TestMethod]
    public void AsyncRefreshPreservesSemanticFocusAcrossReorder()
    {
        var controller = new LauncherSemanticFocusController();
        _ = controller.LoadRootScope(RowScope());
        _ = controller.Dispatch(LauncherSemanticAction.NavRight);

        var refreshed = controller.ReplaceCurrentScope(new LauncherFocusScopeDefinition(
            "ROOT",
            "FIRST",
            [
                new LauncherFocusNode("THIRD"),
                new LauncherFocusNode("SECOND", Left: "FIRST", ActivationId: "OPEN_SECOND"),
                new LauncherFocusNode("FIRST", Right: "SECOND")
            ]));

        Assert.AreEqual(LauncherSemanticDispatchKind.NoOp, refreshed.Kind);
        Assert.AreEqual(LauncherSemanticFocusReasonCodes.ScopeRefreshedFocusPreserved, refreshed.ReasonCode);
        Assert.AreEqual("SECOND", refreshed.FocusKey);
    }

    [TestMethod]
    public void AsyncRefreshUsesExplicitFallbackWhenFocusedItemDisappears()
    {
        var controller = new LauncherSemanticFocusController();
        _ = controller.LoadRootScope(RowScope());
        _ = controller.Dispatch(LauncherSemanticAction.NavRight);

        var refreshed = controller.ReplaceCurrentScope(
            new LauncherFocusScopeDefinition(
                "ROOT",
                "FIRST",
                [new LauncherFocusNode("FIRST"), new LauncherFocusNode("THIRD")]),
            fallbackFocusKey: "THIRD");

        Assert.AreEqual(LauncherSemanticDispatchKind.FocusMoved, refreshed.Kind);
        Assert.AreEqual(LauncherSemanticFocusReasonCodes.ScopeRefreshedFocusFallback, refreshed.ReasonCode);
        Assert.AreEqual("THIRD", refreshed.FocusKey);
    }

    [TestMethod]
    public void PointerOrKeyboardFocusCanUpdateLogicalSemanticBookmark()
    {
        var controller = new LauncherSemanticFocusController();
        _ = controller.LoadRootScope(RowScope());

        var result = controller.SetLogicalFocus("SECOND");

        Assert.AreEqual(LauncherSemanticDispatchKind.FocusMoved, result.Kind);
        Assert.AreEqual(LauncherSemanticFocusReasonCodes.LogicalFocusSet, result.ReasonCode);
        Assert.AreEqual("SECOND", controller.CurrentFocusKey);
    }

    [TestMethod]
    public void UnavailableExplicitTargetWithoutOtherFallbackFailsClosed()
    {
        var controller = new LauncherSemanticFocusController();
        _ = controller.LoadRootScope(new LauncherFocusScopeDefinition(
            "ROOT",
            "A",
            [
                new LauncherFocusNode("A", Right: "B"),
                new LauncherFocusNode("B", IsAvailable: false),
                new LauncherFocusNode("C")
            ]));
        var revision = controller.Revision;

        var result = controller.Dispatch(LauncherSemanticAction.NavRight);

        Assert.AreEqual(LauncherSemanticDispatchKind.NoOp, result.Kind);
        Assert.AreEqual(LauncherSemanticFocusReasonCodes.TargetUnavailable, result.ReasonCode);
        Assert.AreEqual("A", result.FocusKey);
        Assert.AreEqual(revision, result.Revision);
    }

    [TestMethod]
    public void PreferredFocusIsUsedOnlyWhenItExistsAndIsAvailable()
    {
        var controller = new LauncherSemanticFocusController();

        var preferred = controller.LoadRootScope(RowScope(), preferredFocusKey: "SECOND");
        Assert.AreEqual("SECOND", preferred.FocusKey);

        var fallback = controller.LoadRootScope(new LauncherFocusScopeDefinition(
            "ROOT-2",
            "DEFAULT",
            [
                new LauncherFocusNode("DEFAULT"),
                new LauncherFocusNode("UNAVAILABLE", IsAvailable: false)
            ]), preferredFocusKey: "UNAVAILABLE");
        Assert.AreEqual("DEFAULT", fallback.FocusKey);
    }

    [TestMethod]
    public void InvalidOrAmbiguousFocusGraphsAreRejectedAtLoadBoundary()
    {
        var controller = new LauncherSemanticFocusController();

        Assert.Throws<InvalidDataException>(() => controller.LoadRootScope(
            new LauncherFocusScopeDefinition(
                "BROKEN",
                "A",
                [new LauncherFocusNode("A", Right: "MISSING")])));

        Assert.Throws<InvalidDataException>(() => controller.LoadRootScope(
            new LauncherFocusScopeDefinition(
                "DUPLICATE",
                "A",
                [new LauncherFocusNode("A"), new LauncherFocusNode("A")])));

        Assert.Throws<InvalidDataException>(() => controller.LoadRootScope(
            new LauncherFocusScopeDefinition(
                "SELF",
                "A",
                [new LauncherFocusNode("A", Right: "A")])));

        Assert.Throws<InvalidDataException>(() => controller.LoadRootScope(
            new LauncherFocusScopeDefinition(
                "BAD-BOUNDS",
                "A",
                [new LauncherFocusNode("A", Bounds: new LauncherFocusRect(0, 0, 0, 10))])));
    }

    [TestMethod]
    public void RootScopeCannotBePoppedAway()
    {
        var controller = new LauncherSemanticFocusController();
        _ = controller.LoadRootScope(RowScope());
        var revision = controller.Revision;

        var result = controller.PopScope();

        Assert.AreEqual(LauncherSemanticDispatchKind.NoOp, result.Kind);
        Assert.AreEqual(LauncherSemanticFocusReasonCodes.RootScopeCannotPop, result.ReasonCode);
        Assert.AreEqual("FIRST", result.FocusKey);
        Assert.AreEqual(revision, result.Revision);
        Assert.IsTrue(result.IsReady);
    }

    private static LauncherFocusScopeDefinition RowScope()
        => new(
            "ROOT",
            "FIRST",
            [
                new LauncherFocusNode("FIRST", Right: "SECOND"),
                new LauncherFocusNode("SECOND", Left: "FIRST", ActivationId: "OPEN_SECOND")
            ]);
}
