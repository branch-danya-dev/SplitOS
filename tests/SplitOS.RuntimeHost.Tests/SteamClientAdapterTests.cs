using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.GameRuntime;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class SteamClientAdapterTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void DescriptorAdvertisesOnlyImplementedSteamCapabilities()
    {
        var adapter = Adapter(RegistrationMissing(), new RecordingExecutableReader());

        Assert.AreEqual(GameClientSupportStatus.PartialSupportedV1, adapter.Descriptor.SupportStatus);
        Assert.AreEqual(12, adapter.Descriptor.Capabilities.Count);
        Assert.AreEqual(
            GameMechanismStatus.SupportedOsMechanism,
            adapter.Descriptor.Capability(GameClientCapabilityId.ClientDiscovery).MechanismStatus);
        Assert.AreEqual(
            GameMechanismStatus.SupportedOsMechanism,
            adapter.Descriptor.Capability(GameClientCapabilityId.ClientVersionEvidence).MechanismStatus);
        Assert.AreEqual(
            GameMechanismStatus.VersionSensitive,
            adapter.Descriptor.Capability(GameClientCapabilityId.LibraryDiscovery).MechanismStatus);
        Assert.AreEqual(
            GameMechanismStatus.BestEffortLocalEvidence,
            adapter.Descriptor.Capability(GameClientCapabilityId.InstallationEvidence).MechanismStatus);
        Assert.AreEqual(
            GameMechanismStatus.SupportedPublic,
            adapter.Descriptor.Capability(GameClientCapabilityId.LaunchIdentityResolution).MechanismStatus);
        Assert.AreEqual(
            GameMechanismStatus.Open,
            adapter.Descriptor.Capability(GameClientCapabilityId.GameLaunch).MechanismStatus);
        Assert.AreEqual(
            GameMechanismStatus.Unsupported,
            adapter.Descriptor.Capability(GameClientCapabilityId.AccountContextEvidence).MechanismStatus);
    }

    [TestMethod]
    public async Task NonDefaultProtocolHandlerProducesVerifiedDiscoveryEvidence()
    {
        var fileReader = new RecordingExecutableReader(
            new SteamExecutableEvidence(true, "10.20.30.40", true));
        var adapter = Adapter(
            Registration("\"D:\\Portable Clients\\Steam\\steam.exe\" \"%1\""),
            fileReader);

        var evidence = await adapter.DiscoverClientAsync(
            3,
            GameClientFreshnessRequirement.FreshRequired,
            CancellationToken.None);

        Assert.AreEqual(GameClientDiscoveryAvailability.AvailableVerified, evidence.AvailabilityState);
        Assert.AreEqual(GameMechanismStatus.SupportedOsMechanism, evidence.MechanismStatus);
        Assert.AreEqual(SteamClientAdapter.DiscoveryMechanismId, evidence.MechanismId);
        Assert.AreEqual(SteamClientAdapter.ProtocolRegistrationIdentity, evidence.ProtocolRegistration);
        Assert.AreEqual(@"D:\Portable Clients\Steam\steam.exe", evidence.ExecutableIdentity);
        Assert.AreEqual("10.20.30.40", evidence.ObservedClientVersion);
        Assert.AreEqual(1L, evidence.SnapshotGeneration);
        Assert.AreEqual(ObservedAt, evidence.ObservedAtUtc);
        Assert.AreEqual(1, fileReader.Calls);
        Assert.AreEqual(@"D:\Portable Clients\Steam\steam.exe", fileReader.LastPath);
    }

    [TestMethod]
    public async Task MissingProtocolRegistrationIsVerifiedNotFoundWithoutDefaultPathFallback()
    {
        var fileReader = new RecordingExecutableReader(
            new SteamExecutableEvidence(true, "unexpected", true));
        var adapter = Adapter(RegistrationMissing(), fileReader);

        var evidence = await adapter.DiscoverClientAsync(
            4,
            GameClientFreshnessRequirement.FreshRequired,
            CancellationToken.None);

        Assert.AreEqual(GameClientDiscoveryAvailability.NotFoundVerified, evidence.AvailabilityState);
        Assert.AreEqual("STEAM_PROTOCOL_NOT_REGISTERED", evidence.DiagnosticsCode);
        Assert.IsNull(evidence.ExecutableIdentity);
        Assert.IsNull(evidence.ProtocolRegistration);
        Assert.AreEqual(0, fileReader.Calls);
    }

    [TestMethod]
    public async Task MissingUrlProtocolMarkerFailsClosedInsteadOfClaimingSteamAvailable()
    {
        var fileReader = new RecordingExecutableReader(
            new SteamExecutableEvidence(true, "1.0", true));
        var adapter = Adapter(
            new SteamProtocolRegistrationSnapshot(
                true,
                true,
                false,
                "\"D:\\Steam\\steam.exe\" \"%1\""),
            fileReader);

        var evidence = await adapter.DiscoverClientAsync(
            1,
            GameClientFreshnessRequirement.FreshRequired,
            CancellationToken.None);

        Assert.AreEqual(GameClientDiscoveryAvailability.Unknown, evidence.AvailabilityState);
        Assert.AreEqual("STEAM_URL_PROTOCOL_MARKER_MISSING", evidence.DiagnosticsCode);
        Assert.AreEqual(0, fileReader.Calls);
    }

    [TestMethod]
    public async Task StaleRegistrationPointingAtMissingFileDoesNotProveSteamAbsent()
    {
        var fileReader = new RecordingExecutableReader(
            new SteamExecutableEvidence(false, null, true, "STEAM_HANDLER_FILE_NOT_FOUND"));
        var adapter = Adapter(Registration("\"E:\\OldSteam\\steam.exe\" \"%1\""), fileReader);

        var evidence = await adapter.DiscoverClientAsync(
            2,
            GameClientFreshnessRequirement.FreshRequired,
            CancellationToken.None);

        Assert.AreEqual(GameClientDiscoveryAvailability.Unknown, evidence.AvailabilityState);
        Assert.AreEqual("STEAM_HANDLER_FILE_NOT_FOUND", evidence.DiagnosticsCode);
        Assert.AreEqual(@"E:\OldSteam\steam.exe", evidence.ExecutableIdentity);
        Assert.AreEqual(1, fileReader.Calls);
    }

    [TestMethod]
    public async Task ExistingSteamHandlerWithoutReadableVersionIsAvailableUnverifiedVersion()
    {
        var fileReader = new RecordingExecutableReader(
            new SteamExecutableEvidence(true, null, false, "STEAM_CLIENT_VERSION_READ_FAILED"));
        var adapter = Adapter(Registration("\"C:\\Steam\\steam.exe\" \"%1\""), fileReader);

        var evidence = await adapter.DiscoverClientAsync(
            5,
            GameClientFreshnessRequirement.FreshIfAvailable,
            CancellationToken.None);

        Assert.AreEqual(GameClientDiscoveryAvailability.AvailableUnverifiedVersion, evidence.AvailabilityState);
        Assert.IsNull(evidence.ObservedClientVersion);
        Assert.AreEqual("STEAM_CLIENT_VERSION_READ_FAILED", evidence.DiagnosticsCode);
        Assert.AreEqual(SteamClientAdapter.ProtocolRegistrationIdentity, evidence.ProtocolRegistration);
    }

    [TestMethod]
    public async Task MalformedOrArgumentInjectedHandlerIsRejectedBeforeFilesystemProbe()
    {
        var commands = new[]
        {
            "\"D:\\Steam\\steam.exe\" -silent \"%1\"",
            "\"D:\\Steam\\steam.exe\" \"%1\" --extra",
            "\"D:\\Steam\\steam.exe",
            "steam.exe \"%1\"",
            "\"D:\\Steam\\not-steam.exe\" \"%1\"",
            "\"%ProgramFiles(x86)%\\Steam\\steam.exe\" \"%1\""
        };

        foreach (var command in commands)
        {
            var fileReader = new RecordingExecutableReader(
                new SteamExecutableEvidence(true, "1.0", true));
            var adapter = Adapter(Registration(command), fileReader);

            var evidence = await adapter.DiscoverClientAsync(
                1,
                GameClientFreshnessRequirement.FreshRequired,
                CancellationToken.None);

            Assert.AreEqual(
                GameClientDiscoveryAvailability.Unknown,
                evidence.AvailabilityState,
                $"Handler unexpectedly accepted: {command}");
            Assert.AreEqual(0, fileReader.Calls, $"Filesystem probe ran for untrusted handler: {command}");
        }
    }

    [TestMethod]
    public void StrictHandlerParserAcceptsOnlyCanonicalSteamExecutablePlusUriPlaceholder()
    {
        Assert.IsTrue(SteamProtocolHandlerCommandParser.TryParse(
            "\"F:\\Games\\Steam Client\\steam.exe\" \"%1\"",
            out var quoted,
            out var quotedDiagnostic));
        Assert.AreEqual(@"F:\Games\Steam Client\steam.exe", quoted);
        Assert.AreEqual("STEAM_PROTOCOL_HANDLER_VALID", quotedDiagnostic);

        Assert.IsTrue(SteamProtocolHandlerCommandParser.TryParse(
            @"F:\Steam\steam.exe %1",
            out var unquoted,
            out _));
        Assert.AreEqual(@"F:\Steam\steam.exe", unquoted);

        Assert.IsFalse(SteamProtocolHandlerCommandParser.TryParse(
            "\"F:\\Steam\\steam.exe\" -applaunch 1091500",
            out _,
            out var unsafeDiagnostic));
        Assert.AreEqual("STEAM_PROTOCOL_HANDLER_TEMPLATE_UNTRUSTED", unsafeDiagnostic);
    }

    [TestMethod]
    public async Task EveryDiscoveryCallReadsFreshEvidenceAndAdvancesGeneration()
    {
        var registrationReader = new SequenceRegistrationReader(
            Registration("\"D:\\Steam\\steam.exe\" \"%1\""),
            RegistrationMissing());
        var fileReader = new RecordingExecutableReader(
            new SteamExecutableEvidence(true, "1.2.3", true));
        var adapter = new SteamClientAdapter(
            registrationReader,
            fileReader,
            new FixedTimeProvider(ObservedAt));

        var first = await adapter.DiscoverClientAsync(
            1,
            GameClientFreshnessRequirement.AllowCached,
            CancellationToken.None);
        var second = await adapter.DiscoverClientAsync(
            1,
            GameClientFreshnessRequirement.AllowCached,
            CancellationToken.None);

        Assert.AreEqual(GameClientDiscoveryAvailability.AvailableVerified, first.AvailabilityState);
        Assert.AreEqual(GameClientDiscoveryAvailability.NotFoundVerified, second.AvailabilityState);
        Assert.AreEqual(1L, first.SnapshotGeneration);
        Assert.AreEqual(2L, second.SnapshotGeneration);
        Assert.AreEqual(2, registrationReader.Calls);
    }

    [TestMethod]
    public void CompatibilityDowngradesImplementedCapabilitiesBelowSupportedWindowsFloorOnly()
    {
        var adapter = Adapter(RegistrationMissing(), new RecordingExecutableReader());

        var current = adapter.GetCompatibilityStatus(
            new GameClientCompatibilityContext(26100, "client/1"));
        var oldWindows = adapter.GetCompatibilityStatus(
            new GameClientCompatibilityContext(18362, "client/1"));

        Assert.AreEqual(
            GameMechanismStatus.SupportedOsMechanism,
            current.Capability(GameClientCapabilityId.ClientDiscovery).MechanismStatus);
        Assert.AreEqual(
            GameMechanismStatus.Unsupported,
            oldWindows.Capability(GameClientCapabilityId.ClientDiscovery).MechanismStatus);
        Assert.AreEqual(
            "WINDOWS_BUILD_UNSUPPORTED",
            oldWindows.Capability(GameClientCapabilityId.ClientDiscovery).NotesCode);
        Assert.AreEqual(
            GameMechanismStatus.Unsupported,
            oldWindows.Capability(GameClientCapabilityId.LibraryDiscovery).MechanismStatus);
        Assert.AreEqual(
            GameMechanismStatus.Open,
            oldWindows.Capability(GameClientCapabilityId.GameLaunch).MechanismStatus);
    }

    [TestMethod]
    public void RegistryCanRouteTheConcreteSteamAdapterWithoutAdvertisingFutureSlices()
    {
        var adapter = Adapter(RegistrationMissing(), new RecordingExecutableReader());
        var registry = new GameClientAdapterRegistry(new IGameClientAdapter[] { adapter });

        CollectionAssert.AreEqual(
            new[] { GameClientType.Steam },
            registry.RegisteredClientTypes.ToArray());
        Assert.AreSame(adapter, registry.GetRequiredAdapter(GameClientType.Steam));
        Assert.AreEqual(
            GameMechanismStatus.Open,
            registry.GetDescriptor(GameClientType.Steam)
                .Capability(GameClientCapabilityId.GameLaunch)
                .MechanismStatus);
    }

    [TestMethod]
    public async Task CancellationIsObservedBeforeExternalEvidenceReads()
    {
        var registrationReader = new SequenceRegistrationReader(RegistrationMissing());
        var adapter = new SteamClientAdapter(
            registrationReader,
            new RecordingExecutableReader(),
            new FixedTimeProvider(ObservedAt));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await AssertThrowsAsync<OperationCanceledException>(() => adapter.DiscoverClientAsync(
            1,
            GameClientFreshnessRequirement.FreshRequired,
            cancellation.Token));
        Assert.AreEqual(0, registrationReader.Calls);
    }

    [TestMethod]
    public async Task NegativeWindowsSessionIdIsRejected()
    {
        var adapter = Adapter(RegistrationMissing(), new RecordingExecutableReader());

        await AssertThrowsAsync<ArgumentOutOfRangeException>(() => adapter.DiscoverClientAsync(
            -1,
            GameClientFreshnessRequirement.FreshRequired,
            CancellationToken.None));
    }

    private static SteamClientAdapter Adapter(
        SteamProtocolRegistrationSnapshot registration,
        ISteamExecutableEvidenceReader executableReader)
        => new(
            new SequenceRegistrationReader(registration),
            executableReader,
            new FixedTimeProvider(ObservedAt));

    private static SteamProtocolRegistrationSnapshot Registration(string command)
        => new(true, true, true, command);

    private static SteamProtocolRegistrationSnapshot RegistrationMissing()
        => new(true, false, false, null);

    private sealed class SequenceRegistrationReader(params SteamProtocolRegistrationSnapshot[] snapshots)
        : ISteamProtocolRegistrationReader
    {
        private readonly Queue<SteamProtocolRegistrationSnapshot> _snapshots = new(snapshots);

        public int Calls { get; private set; }

        public SteamProtocolRegistrationSnapshot Read()
        {
            Calls++;
            if (_snapshots.Count == 0)
                throw new AssertFailedException("Unexpected Steam registration read.");
            return _snapshots.Dequeue();
        }
    }

    private sealed class RecordingExecutableReader : ISteamExecutableEvidenceReader
    {
        private readonly SteamExecutableEvidence _evidence;

        public RecordingExecutableReader(SteamExecutableEvidence? evidence = null)
        {
            _evidence = evidence ?? new SteamExecutableEvidence(false, null, true, "NOT_CONFIGURED");
        }

        public int Calls { get; private set; }
        public string? LastPath { get; private set; }

        public SteamExecutableEvidence Read(string normalizedExecutablePath)
        {
            Calls++;
            LastPath = normalizedExecutablePath;
            return _evidence;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
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
