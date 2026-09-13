using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Contracts.Protocol;
using SplitOS.Runtime.Client;
using SplitOS.RuntimeHost;
using SplitOS.RuntimeHost.GameRuntime;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class LauncherRuntimeBindingEdgeTests
{
    [TestMethod]
    public void SnapshotUsesCanonicalSnakeCaseGameSessionState()
    {
        var runtime = new RuntimeStateState();
        runtime.Report(new RuntimeStateReadResult(
            "READY", "ENABLED", "GAME", "ASSOCIATED", 1, 1, 1, DateTimeOffset.UtcNow));
        var session = new GameSessionStateMachine();
        var launch = new GameSessionLaunchIdentity("launch-1", "correlation-1", "game-1");

        _ = session.Apply(new GameSessionTransitionRequest(GameSessionSignal.LauncherReady, true));
        _ = session.Apply(new GameSessionTransitionRequest(GameSessionSignal.LaunchRequested, true, launch));
        _ = session.Apply(new GameSessionTransitionRequest(GameSessionSignal.ClientHandoffSubmitted, true, launch));
        _ = session.Apply(new GameSessionTransitionRequest(GameSessionSignal.ClientHandoffAccepted, true, launch));
        _ = session.Apply(new GameSessionTransitionRequest(GameSessionSignal.GameStartingConfirmed, true, launch));

        var provider = new LauncherRuntimeSnapshotProvider(runtime, session, new LauncherReadinessState());
        var snapshot = provider.Read();

        Assert.AreEqual("GAME_STARTING", snapshot.GameSessionState);
    }

    [TestMethod]
    public void PresentationReadinessBeforeTransportConnectDoesNotSkipConnecting()
    {
        var controller = new LauncherRuntimeBindingController();
        _ = controller.BeginStart();
        _ = controller.BeginConnecting();

        var earlyReady = controller.ReportPresentationSubsystemReady();
        Assert.AreEqual(LauncherLifecycleState.Connecting, earlyReady.State);
        Assert.IsFalse(earlyReady.HasFreshRuntimeSnapshot);

        var connected = controller.ReportTransportConnected();
        Assert.AreEqual(LauncherLifecycleState.Preparing, connected.State);
    }
}
