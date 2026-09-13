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
    public void DirectionalNavigationUsesOnlyExplicitGraphEdges()
    {
        var controller = new LauncherSemanticFocusController();
        _ = controller.LoadRootScope(RowScope());
        var initialRevision = controller.Revision;

        var right = controller.Dispatch(LauncherSemanticAction.NavRight);
        Assert.AreEqual(LauncherSemanticDispatchKind.FocusMoved, right.Kind);
        Assert.AreEqual("SECOND", right.FocusKey);
        Assert.IsTrue(right.Revision > initialRevision);

        var boundaryRevision = right.Revision;
        var down = controller.Dispatch(LauncherSemanticAction.NavDown);
        Assert.AreEqual(LauncherSemanticDispatchKind.NoOp, down.Kind);
        Assert.AreEqual(LauncherSemanticFocusReasonCodes.NavigationBoundary, down.ReasonCode);
        Assert.AreEqual("SECOND", down.FocusKey);
        Assert.AreEqual(boundaryRevision, down.Revision);
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
    public void UnavailableExplicitTargetDoesNotFallBackToGeometryOrAnotherNode()
    {
        var controller = new LauncherSemanticFocusController();
        _ = controller.LoadRootScope(new LauncherFocusScopeDefinition(
            "ROOT",
            "A",
            [
                new LauncherFocusNode("A", Right: "B"),
                new LauncherFocusNode("B", Right: "C", IsAvailable: false),
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
