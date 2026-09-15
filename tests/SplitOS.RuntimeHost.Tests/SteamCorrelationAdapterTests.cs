using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.GameRuntime;
using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class SteamCorrelationAdapterTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
    private const string AppId = "1091500";
    private const string InstallRoot = @"C:\Steam\steamapps\common\Cyberpunk 2077";

    [TestMethod]
    public void DescriptorEnablesSteamProcessAndExitCorrelationOnlyAtThisLayer()
    {
        var adapter = Adapter(new SequenceProcessReader(Baseline()), EmptyPolicy());

        Assert.AreEqual(SteamCorrelationAdapter.AdapterVersion, adapter.Descriptor.AdapterVersion);
        Assert.AreEqual(GameMechanismStatus.SupportedVersionGated,
            adapter.Descriptor.Capability(GameClientCapabilityId.GameProcessCorrelation).MechanismStatus);
        Assert.AreEqual(GameMechanismStatus.SupportedOsMechanism,
            adapter.Descriptor.Capability(GameClientCapabilityId.GameExitCorrelation).MechanismStatus);
        Assert.AreEqual(GameMechanismStatus.SupportedPublic,
            adapter.Descriptor.Capability(GameClientCapabilityId.GameLaunch).MechanismStatus);
    }

    [TestMethod]
    public async Task GenericInstallRootEvidenceRequiresStabilityBeforeMediumRunningConfirmation()
    {
        var creation = Now.AddMilliseconds(100);
        var processReader = new SequenceProcessReader(
            Baseline(),
            Snapshot(Now.AddSeconds(1), Process(200, 1, @"C:\Steam\steamapps\common\Cyberpunk 2077\bin\Cyberpunk2077.exe", creation, Now.AddSeconds(1))),
            Snapshot(Now.AddSeconds(4), Process(200, 1, @"C:\Steam\steamapps\common\Cyberpunk 2077\bin\Cyberpunk2077.exe", creation, Now.AddSeconds(4))));
        var adapter = Adapter(processReader, EmptyPolicy());
        var prepared = await PrepareAndSubmit(adapter);

        var starting = await adapter.ObserveLaunchAsync(prepared, CancellationToken.None);
        var running = await adapter.ObserveLaunchAsync(prepared, CancellationToken.None);

        Assert.AreEqual(GameClientObservationClassification.StartingConfirmed, starting.Classification);
        Assert.AreEqual(GameClientEvidenceLevel.Medium, starting.EvidenceLevel);
        Assert.AreEqual(GameClientObservationClassification.RunningConfirmed, running.Classification);
        Assert.AreEqual(GameClientEvidenceLevel.Medium, running.EvidenceLevel);
        Assert.AreEqual(GameClientExecutableRole.UnknownCandidate, running.CorrelatedProcesses.Single().ExecutableRole);
        Assert.AreEqual(ProcessCorrelationReasonCodes.ProofSetSatisfied, running.DiagnosticsCode);
    }

    [TestMethod]
    public async Task CuratedExecutableEvidenceCanProduceStrongRunningConfirmation()
    {
        var creation = Now.AddMilliseconds(100);
        var processReader = new SequenceProcessReader(
            Baseline(),
            Snapshot(Now.AddSeconds(1), Process(210, 1, @"C:\Steam\steamapps\common\Cyberpunk 2077\bin\Cyberpunk2077.exe", creation, Now.AddSeconds(1))),
            Snapshot(Now.AddSeconds(4), Process(210, 1, @"C:\Steam\steamapps\common\Cyberpunk 2077\bin\Cyberpunk2077.exe", creation, Now.AddSeconds(4))));
        var adapter = Adapter(processReader, Policy(["Cyberpunk2077.exe"]));
        var prepared = await PrepareAndSubmit(adapter);

        _ = await adapter.ObserveLaunchAsync(prepared, CancellationToken.None);
        var running = await adapter.ObserveLaunchAsync(prepared, CancellationToken.None);

        Assert.AreEqual(GameClientObservationClassification.RunningConfirmed, running.Classification);
        Assert.AreEqual(GameClientEvidenceLevel.Strong, running.EvidenceLevel);
        Assert.AreEqual(GameClientExecutableRole.GamePrimary, running.CorrelatedProcesses.Single().ExecutableRole);
    }

    [TestMethod]
    public async Task SteamHelpersWrongSessionOutsideRootAndPreHandoffProcessesNeverProveRunning()
    {
        var processReader = new SequenceProcessReader(
            Baseline(),
            Snapshot(
                Now.AddSeconds(4),
                Process(220, 1, @"C:\Steam\steamapps\common\Cyberpunk 2077\steam.exe", Now.AddMilliseconds(100), Now.AddSeconds(4)),
                Process(221, 2, @"C:\Steam\steamapps\common\Cyberpunk 2077\Game.exe", Now.AddMilliseconds(100), Now.AddSeconds(4)),
                Process(222, 1, @"C:\Other\Game.exe", Now.AddMilliseconds(100), Now.AddSeconds(4)),
                Process(223, 1, @"C:\Steam\steamapps\common\Cyberpunk 2077\Old.exe", Now.AddSeconds(-1), Now.AddSeconds(4))));
        var adapter = Adapter(processReader, EmptyPolicy());
        var prepared = await PrepareAndSubmit(adapter);

        var observation = await adapter.ObserveLaunchAsync(prepared, CancellationToken.None);

        Assert.AreEqual(GameClientObservationClassification.NoMatch, observation.Classification);
        Assert.AreEqual(GameClientEvidenceLevel.Weak, observation.EvidenceLevel);
        Assert.AreEqual(ProcessCorrelationReasonCodes.NoSupportedProof, observation.DiagnosticsCode);
    }

    [TestMethod]
    public async Task MultipleEquivalentGameRootCandidatesRemainAmbiguous()
    {
        var observed = Now.AddSeconds(4);
        var processReader = new SequenceProcessReader(
            Baseline(),
            Snapshot(
                observed,
                Process(230, 1, @"C:\Steam\steamapps\common\Cyberpunk 2077\A.exe", Now.AddMilliseconds(100), observed),
                Process(231, 1, @"C:\Steam\steamapps\common\Cyberpunk 2077\B.exe", Now.AddMilliseconds(200), observed)));
        var adapter = Adapter(processReader, EmptyPolicy());
        var prepared = await PrepareAndSubmit(adapter);

        var observation = await adapter.ObserveLaunchAsync(prepared, CancellationToken.None);

        Assert.AreEqual(GameClientObservationClassification.Ambiguous, observation.Classification);
        Assert.AreEqual(GameClientEvidenceLevel.Medium, observation.EvidenceLevel);
        Assert.AreEqual(ProcessCorrelationReasonCodes.MultipleEquivalentCandidates, observation.DiagnosticsCode);
        Assert.AreEqual(2, observation.CorrelatedProcesses.Count);
    }

    [TestMethod]
    public async Task MissingCreationTimeCannotEscalatePastWeakCandidate()
    {
        var observed = Now.AddSeconds(4);
        var processReader = new SequenceProcessReader(
            Baseline(),
            Snapshot(observed, new ProcessEvidenceObservation(
                240, 1, @"C:\Steam\steamapps\common\Cyberpunk 2077\Cyberpunk2077.exe", null, observed)));
        var adapter = Adapter(processReader, Policy(["Cyberpunk2077.exe"]));
        var prepared = await PrepareAndSubmit(adapter);

        var observation = await adapter.ObserveLaunchAsync(prepared, CancellationToken.None);

        Assert.AreEqual(GameClientObservationClassification.Candidate, observation.Classification);
        Assert.AreEqual(GameClientEvidenceLevel.Weak, observation.EvidenceLevel);
        Assert.AreEqual(ProcessCorrelationReasonCodes.ProcessIdentityIncomplete, observation.DiagnosticsCode);
    }

    [TestMethod]
    public async Task SteamClientRemainingAliveDoesNotPreventGameExitConfirmation()
    {
        var creation = Now.AddMilliseconds(100);
        var game = @"C:\Steam\steamapps\common\Cyberpunk 2077\Cyberpunk2077.exe";
        var processReader = new SequenceProcessReader(
            Baseline(),
            Snapshot(Now.AddSeconds(1), Process(250, 1, game, creation, Now.AddSeconds(1))),
            Snapshot(Now.AddSeconds(4), Process(250, 1, game, creation, Now.AddSeconds(4))),
            Snapshot(Now.AddSeconds(5), Process(99, 1, @"C:\Steam\steam.exe", Now.AddMinutes(-10), Now.AddSeconds(5))),
            Snapshot(Now.AddSeconds(9), Process(99, 1, @"C:\Steam\steam.exe", Now.AddMinutes(-10), Now.AddSeconds(9))));
        var adapter = Adapter(processReader, Policy(["Cyberpunk2077.exe"]));
        var prepared = await PrepareAndSubmit(adapter);
        _ = await adapter.ObserveLaunchAsync(prepared, CancellationToken.None);
        var running = await adapter.ObserveLaunchAsync(prepared, CancellationToken.None);

        var candidate = await adapter.ObserveExitAsync(prepared, running.CorrelatedProcesses, CancellationToken.None);
        var exited = await adapter.ObserveExitAsync(prepared, running.CorrelatedProcesses, CancellationToken.None);

        Assert.AreEqual(GameClientExitObservationClassification.ExitCandidate, candidate.Classification);
        Assert.AreEqual(GameClientExitObservationClassification.ExitConfirmed, exited.Classification);
        Assert.AreEqual(ProcessExitCorrelationReasonCodes.ExitGraceSatisfied, exited.DiagnosticsCode);
    }

    [TestMethod]
    public async Task AllowlistedBootstrapReplacementPreservesManagedGameLifetime()
    {
        var launcherCreation = Now.AddMilliseconds(100);
        var launcher = @"C:\Steam\steamapps\common\Cyberpunk 2077\Launcher.exe";
        var game = @"C:\Steam\steamapps\common\Cyberpunk 2077\Cyberpunk2077.exe";
        var replacementCreation = Now.AddSeconds(4.5);
        var processReader = new SequenceProcessReader(
            Baseline(),
            Snapshot(Now.AddSeconds(1), Process(260, 1, launcher, launcherCreation, Now.AddSeconds(1))),
            Snapshot(Now.AddSeconds(4), Process(260, 1, launcher, launcherCreation, Now.AddSeconds(4))),
            Snapshot(Now.AddSeconds(5), Process(261, 1, game, replacementCreation, Now.AddSeconds(5))),
            Snapshot(Now.AddSeconds(6), Process(261, 1, game, replacementCreation, Now.AddSeconds(6))));
        var adapter = Adapter(processReader, Policy(
            ["Launcher.exe"],
            [new ProcessReplacementRule("Launcher.exe", "Cyberpunk2077.exe")]));
        var prepared = await PrepareAndSubmit(adapter);
        _ = await adapter.ObserveLaunchAsync(prepared, CancellationToken.None);
        var running = await adapter.ObserveLaunchAsync(prepared, CancellationToken.None);

        var replacement = await adapter.ObserveExitAsync(prepared, running.CorrelatedProcesses, CancellationToken.None);
        var stillRunning = await adapter.ObserveExitAsync(prepared, replacement.CorrelatedProcesses, CancellationToken.None);

        Assert.AreEqual(GameClientExitObservationClassification.ReplacementProcessFound, replacement.Classification);
        Assert.AreEqual(261, replacement.CorrelatedProcesses.Single().Pid);
        Assert.AreEqual(GameClientExitObservationClassification.StillRunning, stillRunning.Classification);
    }

    [TestMethod]
    public async Task UnknownRecentReplacementInsideGameRootFailsClosed()
    {
        var creation = Now.AddMilliseconds(100);
        var game = @"C:\Steam\steamapps\common\Cyberpunk 2077\Cyberpunk2077.exe";
        var processReader = new SequenceProcessReader(
            Baseline(),
            Snapshot(Now.AddSeconds(1), Process(270, 1, game, creation, Now.AddSeconds(1))),
            Snapshot(Now.AddSeconds(4), Process(270, 1, game, creation, Now.AddSeconds(4))),
            Snapshot(Now.AddSeconds(5), Process(271, 1, @"C:\Steam\steamapps\common\Cyberpunk 2077\Mystery.exe", Now.AddSeconds(4.5), Now.AddSeconds(5))));
        var adapter = Adapter(processReader, Policy(["Cyberpunk2077.exe"]));
        var prepared = await PrepareAndSubmit(adapter);
        _ = await adapter.ObserveLaunchAsync(prepared, CancellationToken.None);
        var running = await adapter.ObserveLaunchAsync(prepared, CancellationToken.None);

        var exit = await adapter.ObserveExitAsync(prepared, running.CorrelatedProcesses, CancellationToken.None);

        Assert.AreEqual(GameClientExitObservationClassification.CorrelationLost, exit.Classification);
        Assert.AreEqual(ProcessExitCorrelationReasonCodes.UnknownReplacementCandidate, exit.DiagnosticsCode);
    }

    [TestMethod]
    public async Task FailedHandoffDoesNotLeaveUsableCorrelationSession()
    {
        var processReader = new SequenceProcessReader(Baseline());
        var adapter = Adapter(
            processReader,
            EmptyPolicy(),
            new RecordingDispatcher(new SteamUriLaunchResult(
                SteamUriLaunchDisposition.Rejected,
                "STEAM_URI_DISPATCH_REJECTED")));
        var prepared = await Prepare(adapter);

        var handoff = await adapter.SubmitLaunchAsync(prepared, CancellationToken.None);
        var observation = await adapter.ObserveLaunchAsync(prepared, CancellationToken.None);

        Assert.AreEqual(GameClientLaunchHandoffResultCode.HandoffRejected, handoff.ResultCode);
        Assert.AreEqual(GameClientObservationClassification.CorrelationLost, observation.Classification);
        Assert.AreEqual("STEAM_CORRELATION_STATE_MISSING", observation.DiagnosticsCode);
    }

    [TestMethod]
    public async Task RuntimeRegistryExposesFullSteamVerticalCorrelationCapabilities()
    {
        var adapter = Adapter(new SequenceProcessReader(Baseline()), EmptyPolicy());
        var registry = new GameClientAdapterRegistry(new IGameClientAdapter[] { adapter });
        var descriptor = registry.GetDescriptor(GameClientType.Steam);

        Assert.AreEqual(GameMechanismStatus.SupportedPublic,
            descriptor.Capability(GameClientCapabilityId.GameLaunch).MechanismStatus);
        Assert.AreEqual(GameMechanismStatus.SupportedVersionGated,
            descriptor.Capability(GameClientCapabilityId.GameProcessCorrelation).MechanismStatus);
        Assert.AreEqual(GameMechanismStatus.SupportedOsMechanism,
            descriptor.Capability(GameClientCapabilityId.GameExitCorrelation).MechanismStatus);
    }

    private static SteamCorrelationAdapter Adapter(
        IProcessEvidenceSnapshotReader processReader,
        ISteamCorrelationPolicyProvider policyProvider,
        ISteamUriLaunchDispatcher? dispatcher = null)
    {
        var inner = new SteamClientAdapter(
            new ConstantRegistrationReader(),
            new ConstantExecutableReader(),
            new FixedTimeProvider(Now));
        var launch = new SteamProtocolLaunchAdapter(
            inner,
            dispatcher ?? new RecordingDispatcher(),
            new FixedTimeProvider(Now));
        return new SteamCorrelationAdapter(
            launch,
            processReader,
            policyProvider,
            new FixedTimeProvider(Now));
    }

    private static async Task<PreparedClientLaunch> PrepareAndSubmit(SteamCorrelationAdapter adapter)
    {
        var prepared = await Prepare(adapter);
        var handoff = await adapter.SubmitLaunchAsync(prepared, CancellationToken.None);
        Assert.AreEqual(GameClientLaunchHandoffResultCode.HandoffAccepted, handoff.ResultCode);
        return prepared;
    }

    private static Task<PreparedClientLaunch> Prepare(SteamCorrelationAdapter adapter)
        => adapter.PrepareLaunchAsync(
            Request(),
            Projection(),
            ClientEvidence(),
            CancellationToken.None);

    private static GameClientLaunchRequest Request()
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "game.cyberpunk",
            GameClientType.Steam,
            Identity(),
            LaunchIdentity(),
            7,
            1,
            Now.AddSeconds(-5),
            Now.AddMinutes(1));

    private static AdapterGameProjection Projection()
        => new AdapterGameProjection(
            Identity(),
            new GameInstallationEvidence(
                GameInstallState.InstalledVerifiedEvidence,
                InstallRoot,
                Now.AddMinutes(-1),
                Now.AddMinutes(4),
                GameEvidenceFreshness.Fresh,
                GameEvidenceConfidence.High,
                GameMechanismStatus.BestEffortLocalEvidence,
                @"C:\Steam\steamapps\appmanifest_1091500.acf").Normalize(),
            LaunchIdentity(),
            Array.Empty<string>(),
            new GameLibrarySourceProvenance(
                SteamClientAdapter.InstallationMechanismId,
                SteamClientAdapter.AdapterVersion,
                SteamClientAdapter.EvidenceSchemaVersion,
                "10.20.30.40"),
            "Cyberpunk 2077").Normalize(GameClientType.Steam);

    private static ExternalGameIdentity Identity()
        => new(GameClientType.Steam, SteamClientAdapter.ExternalIdKind, AppId);

    private static AdapterLaunchIdentity LaunchIdentity()
        => new(
            SteamClientAdapter.ExternalIdKind,
            SteamProtocolLaunchAdapter.LaunchIdentitySchemaVersion,
            AppId,
            Now.AddMinutes(-1),
            GameMechanismStatus.SupportedPublic);

    private static GameClientDiscoveryEvidence ClientEvidence()
        => new GameClientDiscoveryEvidence(
            GameClientType.Steam,
            GameClientDiscoveryAvailability.AvailableVerified,
            SteamClientAdapter.DiscoveryMechanismId,
            GameMechanismStatus.SupportedOsMechanism,
            Now.AddMinutes(-1),
            1,
            @"C:\Steam\steam.exe",
            SteamClientAdapter.ProtocolRegistrationIdentity,
            null,
            "10.20.30.40",
            null).Normalize(GameClientType.Steam);

    private static ProcessEvidenceSnapshot Baseline()
        => Snapshot(Now.AddSeconds(-1));

    private static ProcessEvidenceSnapshot Snapshot(
        DateTimeOffset observedUtc,
        params ProcessEvidenceObservation[] processes)
        => new(observedUtc, processes);

    private static ProcessEvidenceObservation Process(
        int pid,
        int sessionId,
        string imagePath,
        DateTimeOffset creationUtc,
        DateTimeOffset observedUtc)
        => new(pid, sessionId, imagePath, creationUtc, observedUtc);

    private static ISteamCorrelationPolicyProvider EmptyPolicy()
        => Policy(Array.Empty<string>());

    private static ISteamCorrelationPolicyProvider Policy(
        IReadOnlyList<string> expected,
        IReadOnlyList<ProcessReplacementRule>? replacements = null)
        => new ConstantPolicyProvider(new SteamCorrelationPolicy(
            expected,
            replacements ?? Array.Empty<ProcessReplacementRule>()));

    private sealed class ConstantPolicyProvider(SteamCorrelationPolicy policy) : ISteamCorrelationPolicyProvider
    {
        public SteamCorrelationPolicy GetPolicy(string canonicalAppId)
            => policy;
    }

    private sealed class SequenceProcessReader(params ProcessEvidenceSnapshot[] snapshots)
        : IProcessEvidenceSnapshotReader
    {
        private readonly Queue<ProcessEvidenceSnapshot> _snapshots = new(snapshots);

        public ProcessEvidenceSnapshot Read()
        {
            if (_snapshots.Count == 0)
                throw new AssertFailedException("Unexpected process snapshot read.");
            return _snapshots.Dequeue();
        }
    }

    private sealed class ConstantRegistrationReader : ISteamProtocolRegistrationReader
    {
        public SteamProtocolRegistrationSnapshot Read()
            => new(true, true, true, "\"C:\\Steam\\steam.exe\" \"%1\"");
    }

    private sealed class ConstantExecutableReader : ISteamExecutableEvidenceReader
    {
        public SteamExecutableEvidence Read(string normalizedExecutablePath)
            => new(true, "10.20.30.40", true);
    }

    private sealed class RecordingDispatcher : ISteamUriLaunchDispatcher
    {
        private readonly SteamUriLaunchResult _result;

        public RecordingDispatcher(SteamUriLaunchResult? result = null)
        {
            _result = result ?? new SteamUriLaunchResult(
                SteamUriLaunchDisposition.Accepted,
                "STEAM_URI_DISPATCH_ACCEPTED");
        }

        public SteamUriLaunchResult Dispatch(string canonicalSteamRunUri) => _result;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
