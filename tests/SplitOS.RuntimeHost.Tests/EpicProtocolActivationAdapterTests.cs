using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.GameRuntime;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class EpicProtocolActivationAdapterTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 13, 0, 0, TimeSpan.Zero);
    private const string Sandbox = "fn";
    private const string Catalog = "4fe75bbc5a674f4f9b356b5c90567da5";
    private const string Artifact = "Fortnite";
    private const string ExternalId = Sandbox + ":" + Catalog + ":" + Artifact;

    [TestMethod]
    public void DescriptorAdvertisesOnlyProtocolCapabilitiesImplementedByImp085()
    {
        var adapter = Adapter(RegistrationMissing());

        Assert.AreEqual(GameClientSupportStatus.PartialSupportedV1, adapter.Descriptor.SupportStatus);
        Assert.AreEqual(12, adapter.Descriptor.Capabilities.Count);
        Assert.AreEqual(GameMechanismStatus.SupportedPublic,
            adapter.Descriptor.Capability(GameClientCapabilityId.ClientDiscovery).MechanismStatus);
        Assert.AreEqual(GameMechanismStatus.SupportedPublic,
            adapter.Descriptor.Capability(GameClientCapabilityId.LaunchIdentityResolution).MechanismStatus);
        Assert.AreEqual(GameMechanismStatus.SupportedPublic,
            adapter.Descriptor.Capability(GameClientCapabilityId.GameLaunch).MechanismStatus);
        Assert.AreEqual(GameMechanismStatus.Open,
            adapter.Descriptor.Capability(GameClientCapabilityId.LibraryDiscovery).MechanismStatus);
        Assert.AreEqual(GameMechanismStatus.Open,
            adapter.Descriptor.Capability(GameClientCapabilityId.InstallationEvidence).MechanismStatus);
        Assert.AreEqual(GameMechanismStatus.Open,
            adapter.Descriptor.Capability(GameClientCapabilityId.GameProcessCorrelation).MechanismStatus);
    }

    [TestMethod]
    public async Task PublicProtocolRegistrationDiscoversNonDefaultEpicInstallPath()
    {
        var executable = new RecordingExecutableReader(new EpicExecutableEvidence(true, "18.7.0", true));
        var adapter = Adapter(
            Registration("\"D:\\Launchers\\Epic Games\\Launcher\\Portal\\Binaries\\Win64\\EpicGamesLauncher.exe\" \"%1\""),
            executable);

        var evidence = await adapter.DiscoverClientAsync(1, GameClientFreshnessRequirement.FreshRequired, CancellationToken.None);

        Assert.AreEqual(GameClientDiscoveryAvailability.AvailableVerified, evidence.AvailabilityState);
        Assert.AreEqual(GameMechanismStatus.SupportedPublic, evidence.MechanismStatus);
        Assert.AreEqual(@"D:\Launchers\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe", evidence.ExecutableIdentity);
        Assert.AreEqual("18.7.0", evidence.ObservedClientVersion);
        Assert.AreEqual(EpicProtocolActivationAdapter.ProtocolScheme, evidence.ProtocolRegistration);
        Assert.AreEqual(1, executable.Calls);
    }

    [TestMethod]
    public async Task MissingProtocolIsVerifiedNotFoundWithoutHardcodedPathFallback()
    {
        var executable = new RecordingExecutableReader(new EpicExecutableEvidence(true, "unexpected", true));
        var adapter = Adapter(RegistrationMissing(), executable);

        var evidence = await adapter.DiscoverClientAsync(1, GameClientFreshnessRequirement.FreshRequired, CancellationToken.None);

        Assert.AreEqual(GameClientDiscoveryAvailability.NotFoundVerified, evidence.AvailabilityState);
        Assert.AreEqual("EPIC_PROTOCOL_NOT_REGISTERED", evidence.DiagnosticsCode);
        Assert.AreEqual(0, executable.Calls);
    }

    [TestMethod]
    public async Task MalformedOrArgumentInjectedHandlerFailsClosedBeforeFilesystemProbe()
    {
        var commands = new[]
        {
            "\"D:\\Epic\\EpicGamesLauncher.exe\" -silent \"%1\"",
            "\"D:\\Epic\\EpicGamesLauncher.exe\" \"%1\" --extra",
            "EpicGamesLauncher.exe \"%1\"",
            "\"D:\\Epic\\Other.exe\" \"%1\"",
            "\"%ProgramFiles%\\Epic\\EpicGamesLauncher.exe\" \"%1\""
        };

        foreach (var command in commands)
        {
            var executable = new RecordingExecutableReader(new EpicExecutableEvidence(true, "1", true));
            var adapter = Adapter(Registration(command), executable);
            var evidence = await adapter.DiscoverClientAsync(1, GameClientFreshnessRequirement.FreshRequired, CancellationToken.None);

            Assert.AreEqual(GameClientDiscoveryAvailability.Unknown, evidence.AvailabilityState, command);
            Assert.AreEqual(0, executable.Calls, command);
        }
    }

    [TestMethod]
    public void ProductTripleCanonicalizationRejectsInjectionAndAmbiguousForms()
    {
        Assert.IsTrue(EpicProductIdentity.TryParse(ExternalId, out var identity));
        Assert.AreEqual(ExternalId, identity!.CanonicalExternalId);

        var invalid = new[]
        {
            "fn:catalog",
            "fn:catalog:artifact:extra",
            "fn:catalog:artifact?x=1",
            "fn:catalog:artifact%2Funsafe",
            "fn :catalog:artifact",
            "fn:catalog:artifact/name"
        };
        foreach (var value in invalid)
            Assert.IsFalse(EpicProductIdentity.TryParse(value, out _), value);
    }

    [TestMethod]
    public void ProductTripleUriIsEncodedCanonicallyWithFixedQueryOnly()
    {
        var identity = new EpicProductIdentity(Sandbox, Catalog, Artifact);
        Assert.IsTrue(EpicLaunchUri.TryCreate(identity, out var uri));
        Assert.AreEqual(
            "com.epicgames.launcher://apps/fn%3A4fe75bbc5a674f4f9b356b5c90567da5%3AFortnite?action=launch&silent=true",
            uri);
        Assert.IsTrue(EpicLaunchUri.TryParse(uri, out var parsed));
        Assert.AreEqual(ExternalId, parsed!.CanonicalExternalId);

        var invalid = new[]
        {
            uri + "&extra=1",
            uri!.Replace("%3A", ":", StringComparison.Ordinal),
            uri.Replace("silent=true", "silent=false", StringComparison.Ordinal),
            uri.Replace("com.epicgames.launcher", "https", StringComparison.Ordinal),
            uri.Replace("Fortnite", "Fortnite%252Funsafe", StringComparison.Ordinal)
        };
        foreach (var value in invalid)
            Assert.IsFalse(EpicLaunchUri.TryParse(value, out _), value);
    }

    [TestMethod]
    public async Task PrepareAndAcceptedDispatchProduceHandoffAcceptedOnly()
    {
        var dispatcher = new RecordingDispatcher();
        var adapter = Adapter(Registration(), dispatcher: dispatcher);
        var prepared = await adapter.PrepareLaunchAsync(
            Request(),
            Projection(),
            ClientEvidence(),
            CancellationToken.None);

        var result = await adapter.SubmitLaunchAsync(prepared, CancellationToken.None);

        Assert.AreEqual(GameClientLaunchHandoffResultCode.HandoffAccepted, result.ResultCode);
        Assert.AreEqual(
            "com.epicgames.launcher://apps/fn%3A4fe75bbc5a674f4f9b356b5c90567da5%3AFortnite?action=launch&silent=true",
            dispatcher.LastUri);
        Assert.IsNull(result.ClientProcessEvidence);
        Assert.IsNull(result.ReturnedProcessIdentity);
        Assert.IsFalse(Enum.GetNames<GameClientLaunchHandoffResultCode>().Any(name =>
            string.Equals(name, "GameRunning", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task ProtocolLaunchDoesNotRequireFakeImplementedLibraryEvidence()
    {
        var adapter = Adapter(Registration());
        var projection = Projection() with
        {
            Installation = new GameInstallationEvidence(
                GameInstallState.Unknown,
                null,
                Now.AddMinutes(-1),
                null,
                GameEvidenceFreshness.Unknown,
                GameEvidenceConfidence.Low,
                GameMechanismStatus.Open,
                null).Normalize()
        };

        var prepared = await adapter.PrepareLaunchAsync(Request(), projection, ClientEvidence(), CancellationToken.None);

        Assert.AreEqual(GameMechanismStatus.SupportedPublic, prepared.HandoffMechanismStatus);
        Assert.IsNull(prepared.RequiredObservationRules.ValidatedInstallRoot);
    }

    [TestMethod]
    public async Task LaunchIdentityMustMatchCanonicalExternalTriple()
    {
        var adapter = Adapter(Registration());
        var request = Request() with
        {
            ResolvedLaunchIdentity = LaunchIdentity("other:catalog:artifact")
        };

        await AssertThrowsAsync<InvalidDataException>(() => adapter.PrepareLaunchAsync(
            request,
            Projection(),
            ClientEvidence(),
            CancellationToken.None));
    }

    [TestMethod]
    public async Task UnavailableClientEvidenceRejectsHandoffPreparation()
    {
        var adapter = Adapter(Registration());
        var evidence = ClientEvidence() with { AvailabilityState = GameClientDiscoveryAvailability.Unknown };

        await AssertThrowsAsync<InvalidDataException>(() => adapter.PrepareLaunchAsync(
            Request(),
            Projection(),
            evidence,
            CancellationToken.None));
    }

    [TestMethod]
    public async Task MissingUriAssociationIsTypedWithoutInventingAuthOrRunning()
    {
        var adapter = Adapter(
            Registration(),
            dispatcher: new RecordingDispatcher(new EpicUriLaunchResult(
                EpicUriLaunchDisposition.MechanismUnavailable,
                "EPIC_URI_ASSOCIATION_UNAVAILABLE")));
        var prepared = await adapter.PrepareLaunchAsync(Request(), Projection(), ClientEvidence(), CancellationToken.None);

        var result = await adapter.SubmitLaunchAsync(prepared, CancellationToken.None);

        Assert.AreEqual(GameClientLaunchHandoffResultCode.MechanismUnavailable, result.ResultCode);
        Assert.AreEqual("EPIC_URI_ASSOCIATION_UNAVAILABLE", result.DiagnosticsCode);
    }

    [TestMethod]
    public async Task LibraryAndCorrelationCapabilitiesRemainExplicitlyOpen()
    {
        var adapter = Adapter(Registration());
        var refreshRequest = new GameClientLibraryRefreshRequest(
            Guid.NewGuid(),
            GameClientType.Epic,
            GameClientLibraryRefreshReason.UserRefresh,
            GameClientFreshnessRequirement.FreshRequired,
            null,
            Now.AddMinutes(1));

        var refresh = await adapter.RefreshLibraryAsync(refreshRequest, CancellationToken.None);
        var observation = await adapter.ObserveLaunchAsync(
            await adapter.PrepareLaunchAsync(Request(), Projection(), ClientEvidence(), CancellationToken.None),
            CancellationToken.None);

        Assert.AreEqual(GameClientLibraryRefreshResultCode.SourceUnavailable, refresh.ResultCode);
        Assert.AreEqual("EPIC_LIBRARY_CAPABILITY_OPEN", refresh.ParserStatus);
        Assert.AreEqual(GameClientObservationClassification.CorrelationLost, observation.Classification);
        Assert.AreEqual("EPIC_PROCESS_CORRELATION_CAPABILITY_OPEN", observation.DiagnosticsCode);
    }

    [TestMethod]
    public void RegistryCanRouteSteamAndEpicWithoutCapabilityCrossContamination()
    {
        var steam = new SteamCorrelationAdapter(
            new SteamProtocolLaunchAdapter(
                new SteamClientAdapter(new SteamRegistrationReader(), new SteamExecutableReader(), new FixedTimeProvider(Now)),
                new SteamDispatcher(),
                new FixedTimeProvider(Now)),
            new EmptyProcessReader(),
            new DefaultSteamCorrelationPolicyProvider(),
            new FixedTimeProvider(Now));
        var epic = Adapter(Registration());
        var registry = new GameClientAdapterRegistry(new IGameClientAdapter[] { steam, epic });

        CollectionAssert.AreEquivalent(
            new[] { GameClientType.Steam, GameClientType.Epic },
            registry.RegisteredClientTypes.ToArray());
        Assert.AreEqual(GameMechanismStatus.SupportedPublic,
            registry.GetDescriptor(GameClientType.Epic).Capability(GameClientCapabilityId.GameLaunch).MechanismStatus);
        Assert.AreEqual(GameMechanismStatus.Open,
            registry.GetDescriptor(GameClientType.Epic).Capability(GameClientCapabilityId.GameProcessCorrelation).MechanismStatus);
        Assert.AreEqual(GameMechanismStatus.SupportedVersionGated,
            registry.GetDescriptor(GameClientType.Steam).Capability(GameClientCapabilityId.GameProcessCorrelation).MechanismStatus);
    }

    private static EpicProtocolActivationAdapter Adapter(
        EpicProtocolRegistrationSnapshot registration,
        IEpicExecutableEvidenceReader? executable = null,
        IEpicUriLaunchDispatcher? dispatcher = null)
        => new(
            new ConstantRegistrationReader(registration),
            executable ?? new RecordingExecutableReader(new EpicExecutableEvidence(true, "18.7.0", true)),
            dispatcher ?? new RecordingDispatcher(),
            new FixedTimeProvider(Now));

    private static EpicProtocolRegistrationSnapshot Registration(string? command = null)
        => new(true, true, true, command ?? "\"C:\\Epic\\EpicGamesLauncher.exe\" \"%1\"");

    private static EpicProtocolRegistrationSnapshot RegistrationMissing()
        => new(true, false, false, null);

    private static ExternalGameIdentity Identity()
        => new(GameClientType.Epic, EpicProtocolActivationAdapter.ExternalIdKind, ExternalId);

    private static AdapterLaunchIdentity LaunchIdentity(string payload = ExternalId)
        => new(
            EpicProtocolActivationAdapter.ExternalIdKind,
            EpicProtocolActivationAdapter.LaunchIdentitySchemaVersion,
            payload,
            Now.AddMinutes(-1),
            GameMechanismStatus.SupportedPublic);

    private static GameClientLaunchRequest Request()
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "game.fortnite",
            GameClientType.Epic,
            Identity(),
            LaunchIdentity(),
            1,
            1,
            Now.AddSeconds(-5),
            Now.AddMinutes(1));

    private static AdapterGameProjection Projection()
        => new AdapterGameProjection(
            Identity(),
            new GameInstallationEvidence(
                GameInstallState.Unknown,
                null,
                Now.AddMinutes(-1),
                null,
                GameEvidenceFreshness.Unknown,
                GameEvidenceConfidence.Low,
                GameMechanismStatus.Open,
                null).Normalize(),
            LaunchIdentity(),
            Array.Empty<string>(),
            new GameLibrarySourceProvenance(
                EpicProtocolActivationAdapter.LaunchIdentityMechanismId,
                EpicProtocolActivationAdapter.AdapterVersion,
                null,
                "18.7.0"),
            "Fortnite").Normalize(GameClientType.Epic);

    private static GameClientDiscoveryEvidence ClientEvidence()
        => new GameClientDiscoveryEvidence(
            GameClientType.Epic,
            GameClientDiscoveryAvailability.AvailableVerified,
            EpicProtocolActivationAdapter.DiscoveryMechanismId,
            GameMechanismStatus.SupportedPublic,
            Now.AddMinutes(-1),
            1,
            @"C:\Epic\EpicGamesLauncher.exe",
            EpicProtocolActivationAdapter.ProtocolScheme,
            null,
            "18.7.0",
            null).Normalize(GameClientType.Epic);

    private sealed class ConstantRegistrationReader(EpicProtocolRegistrationSnapshot snapshot) : IEpicProtocolRegistrationReader
    {
        public EpicProtocolRegistrationSnapshot Read() => snapshot;
    }

    private sealed class RecordingExecutableReader(EpicExecutableEvidence evidence) : IEpicExecutableEvidenceReader
    {
        public int Calls { get; private set; }
        public EpicExecutableEvidence Read(string normalizedExecutablePath)
        {
            Calls++;
            return evidence;
        }
    }

    private sealed class RecordingDispatcher : IEpicUriLaunchDispatcher
    {
        private readonly EpicUriLaunchResult _result;
        public RecordingDispatcher(EpicUriLaunchResult? result = null)
        {
            _result = result ?? new EpicUriLaunchResult(EpicUriLaunchDisposition.Accepted, "EPIC_URI_DISPATCH_ACCEPTED");
        }
        public string? LastUri { get; private set; }
        public EpicUriLaunchResult Dispatch(string canonicalEpicLaunchUri)
        {
            LastUri = canonicalEpicLaunchUri;
            return _result;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class SteamRegistrationReader : ISteamProtocolRegistrationReader
    {
        public SteamProtocolRegistrationSnapshot Read()
            => new(true, true, true, "\"C:\\Steam\\steam.exe\" \"%1\"");
    }

    private sealed class SteamExecutableReader : ISteamExecutableEvidenceReader
    {
        public SteamExecutableEvidence Read(string normalizedExecutablePath)
            => new(true, "1", true);
    }

    private sealed class SteamDispatcher : ISteamUriLaunchDispatcher
    {
        public SteamUriLaunchResult Dispatch(string canonicalSteamRunUri)
            => new(SteamUriLaunchDisposition.Accepted, "OK");
    }

    private sealed class EmptyProcessReader : SplitOS.RuntimeHost.WindowsContext.IProcessEvidenceSnapshotReader
    {
        public SplitOS.RuntimeHost.WindowsContext.ProcessEvidenceSnapshot Read()
            => new(Now, Array.Empty<SplitOS.RuntimeHost.WindowsContext.ProcessEvidenceObservation>());
    }

    private static async Task<T> AssertThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try
        {
            await action();
        }
        catch (T exception)
        {
            return exception;
        }
        catch (Exception exception)
        {
            Assert.Fail($"Expected {typeof(T).Name}, got {exception.GetType().Name}: {exception.Message}");
            throw;
        }
        Assert.Fail($"Expected {typeof(T).Name}.");
        throw new InvalidOperationException();
    }
}
