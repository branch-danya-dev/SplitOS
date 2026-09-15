using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.GameRuntime;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class SteamProtocolLaunchAdapterTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);
    private const string AppId = "1091500";
    private const string InstallRoot = @"C:\Steam\steamapps\common\Cyberpunk 2077";

    [TestMethod]
    public void DescriptorEnablesOnlyProtocolLaunchCapabilityForThisSlice()
    {
        var adapter = Adapter(new RecordingDispatcher());

        Assert.AreEqual(SteamProtocolLaunchAdapter.AdapterVersion, adapter.Descriptor.AdapterVersion);
        Assert.AreEqual(
            GameMechanismStatus.SupportedPublic,
            adapter.Descriptor.Capability(GameClientCapabilityId.GameLaunch).MechanismStatus);
        Assert.IsTrue(
            adapter.Descriptor.Capability(GameClientCapabilityId.GameLaunch).RequiresFreshLibraryEvidence);
        Assert.AreEqual(
            GameMechanismStatus.Open,
            adapter.Descriptor.Capability(GameClientCapabilityId.GameProcessCorrelation).MechanismStatus);
        Assert.AreEqual(
            GameMechanismStatus.Open,
            adapter.Descriptor.Capability(GameClientCapabilityId.GameExitCorrelation).MechanismStatus);
    }

    [TestMethod]
    public async Task PrepareLaunchRequiresFreshVerifiedInstallationAndKeepsObservationSeparate()
    {
        var adapter = Adapter(new RecordingDispatcher());
        var request = Request();

        var prepared = await adapter.PrepareLaunchAsync(
            request,
            Projection(),
            ClientEvidence(GameClientDiscoveryAvailability.AvailableVerified),
            CancellationToken.None);

        Assert.AreEqual(request.LaunchOperationId, prepared.LaunchOperationId);
        Assert.AreEqual(GameClientType.Steam, prepared.ClientType);
        Assert.AreEqual(AppId, prepared.NormalizedExternalGameIdentity.ExternalId);
        Assert.AreEqual(AppId, prepared.ResolvedLaunchIdentity.Payload);
        Assert.AreEqual(SteamProtocolLaunchAdapter.HandoffMechanismId, prepared.HandoffMechanism);
        Assert.AreEqual(GameMechanismStatus.SupportedPublic, prepared.HandoffMechanismStatus);
        Assert.AreEqual(request.ExpectedInstallationEvidenceRevision, prepared.ProjectionGeneration);
        Assert.AreEqual(request.DeadlineUtc, prepared.ExpiresAtUtc);
        Assert.AreEqual(InstallRoot, prepared.RequiredObservationRules.ValidatedInstallRoot);
        Assert.AreEqual(GameClientEvidenceLevel.Medium, prepared.RequiredObservationRules.MinimumRunningEvidence);
        Assert.AreEqual(0, prepared.RequiredObservationRules.ExpectedExecutableNames.Count);
    }

    [TestMethod]
    public async Task UnverifiedClientVersionDoesNotDisablePublicSteamProtocolHandoff()
    {
        var adapter = Adapter(new RecordingDispatcher());

        var prepared = await adapter.PrepareLaunchAsync(
            Request(),
            Projection(),
            ClientEvidence(GameClientDiscoveryAvailability.AvailableUnverifiedVersion, observedVersion: null),
            CancellationToken.None);

        Assert.AreEqual(GameMechanismStatus.SupportedPublic, prepared.HandoffMechanismStatus);
    }

    [TestMethod]
    public async Task PrepareLaunchRejectsExpiredInstallationEvidence()
    {
        var adapter = Adapter(new RecordingDispatcher());
        var projection = Projection() with
        {
            Installation = Installation(
                observedAt: Now.AddMinutes(-10),
                expiresAt: Now.AddMinutes(-1))
        };

        await AssertThrowsAsync<InvalidDataException>(() => adapter.PrepareLaunchAsync(
            Request(),
            projection,
            ClientEvidence(GameClientDiscoveryAvailability.AvailableVerified),
            CancellationToken.None));
    }

    [TestMethod]
    public async Task PrepareLaunchRejectsLaunchIdentityFromDifferentAppId()
    {
        var adapter = Adapter(new RecordingDispatcher());
        var request = Request() with
        {
            ResolvedLaunchIdentity = LaunchIdentity("570")
        };

        await AssertThrowsAsync<InvalidDataException>(() => adapter.PrepareLaunchAsync(
            request,
            Projection(),
            ClientEvidence(GameClientDiscoveryAvailability.AvailableVerified),
            CancellationToken.None));
    }

    [TestMethod]
    public async Task PrepareLaunchRejectsUnavailableOrStaleClientEvidence()
    {
        var adapter = Adapter(new RecordingDispatcher());

        await AssertThrowsAsync<InvalidDataException>(() => adapter.PrepareLaunchAsync(
            Request(),
            Projection(),
            ClientEvidence(GameClientDiscoveryAvailability.Unknown),
            CancellationToken.None));
        await AssertThrowsAsync<InvalidDataException>(() => adapter.PrepareLaunchAsync(
            Request(),
            Projection(),
            ClientEvidence(GameClientDiscoveryAvailability.StaleLastKnown),
            CancellationToken.None));
    }

    [TestMethod]
    public async Task AcceptedShellDispatchProducesOnlyHandoffAcceptedWithExactCanonicalUri()
    {
        var dispatcher = new RecordingDispatcher(
            new SteamUriLaunchResult(
                SteamUriLaunchDisposition.Accepted,
                "STEAM_URI_DISPATCH_ACCEPTED"));
        var adapter = Adapter(dispatcher);
        var prepared = await Prepared(adapter);

        var result = await adapter.SubmitLaunchAsync(prepared, CancellationToken.None);

        Assert.AreEqual(GameClientLaunchHandoffResultCode.HandoffAccepted, result.ResultCode);
        Assert.AreEqual("steam://run/1091500", dispatcher.LastUri);
        Assert.AreEqual(1, dispatcher.Calls);
        Assert.IsNull(result.ClientProcessEvidence);
        Assert.IsNull(result.ReturnedProcessIdentity);
        Assert.AreEqual("STEAM_URI_DISPATCH_ACCEPTED", result.DiagnosticsCode);
        Assert.IsFalse(Enum.GetNames<GameClientLaunchHandoffResultCode>().Any(name =>
            string.Equals(name, "GameRunning", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "RunningConfirmed", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task MissingWindowsUriAssociationIsTypedMechanismUnavailable()
    {
        var dispatcher = new RecordingDispatcher(
            new SteamUriLaunchResult(
                SteamUriLaunchDisposition.MechanismUnavailable,
                "STEAM_URI_ASSOCIATION_UNAVAILABLE"));
        var adapter = Adapter(dispatcher);
        var prepared = await Prepared(adapter);

        var result = await adapter.SubmitLaunchAsync(prepared, CancellationToken.None);

        Assert.AreEqual(GameClientLaunchHandoffResultCode.MechanismUnavailable, result.ResultCode);
        Assert.AreEqual("STEAM_URI_ASSOCIATION_UNAVAILABLE", result.DiagnosticsCode);
        Assert.AreEqual(1, dispatcher.Calls);
    }

    [TestMethod]
    public async Task DispatcherFailureDoesNotInventAuthUpdateOrRunningOutcome()
    {
        var adapter = Adapter(new ThrowingDispatcher());
        var prepared = await Prepared(adapter);

        var result = await adapter.SubmitLaunchAsync(prepared, CancellationToken.None);

        Assert.AreEqual(GameClientLaunchHandoffResultCode.UnknownFailure, result.ResultCode);
        Assert.AreEqual("STEAM_URI_DISPATCH_FAILED", result.DiagnosticsCode);
    }

    [TestMethod]
    public async Task ExpiredPreparedLaunchDoesNotTouchWindowsDispatch()
    {
        var dispatcher = new RecordingDispatcher();
        var adapter = Adapter(dispatcher);
        var prepared = (await Prepared(adapter)) with
        {
            ExpiresAtUtc = Now.AddSeconds(-1)
        };

        var result = await adapter.SubmitLaunchAsync(prepared, CancellationToken.None);

        Assert.AreEqual(GameClientLaunchHandoffResultCode.StaleEvidence, result.ResultCode);
        Assert.AreEqual(0, dispatcher.Calls);
    }

    [TestMethod]
    public async Task CancellationIsObservedBeforeProtocolDispatch()
    {
        var dispatcher = new RecordingDispatcher();
        var adapter = Adapter(dispatcher);
        var prepared = await Prepared(adapter);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await AssertThrowsAsync<OperationCanceledException>(() =>
            adapter.SubmitLaunchAsync(prepared, cancellation.Token));
        Assert.AreEqual(0, dispatcher.Calls);
    }

    [TestMethod]
    public void SteamRunUriRejectsArgumentsQueriesFragmentsEncodingAndNonCanonicalAppIds()
    {
        Assert.IsTrue(SteamRunUri.TryCreate(AppId, out var canonical));
        Assert.AreEqual("steam://run/1091500", canonical);
        Assert.IsTrue(SteamRunUri.TryParse(canonical, out var parsed));
        Assert.AreEqual(AppId, parsed);

        var invalid = new[]
        {
            "steam://run/1091500//-unsafe",
            "steam://run/1091500?x=1",
            "steam://run/1091500#fragment",
            "steam://run/1091500%2Funsafe",
            "steam://run/001091500",
            "steam://rungameid/1091500",
            "https://run/1091500"
        };
        foreach (var value in invalid)
            Assert.IsFalse(SteamRunUri.TryParse(value, out _), value);
        Assert.IsFalse(SteamRunUri.TryCreate("001091500", out _));
        Assert.IsFalse(SteamRunUri.TryCreate("1091500//args", out _));
    }

    [TestMethod]
    public void RuntimeRegistryCanExposeProtocolLaunchWithoutEnablingCorrelation()
    {
        var adapter = Adapter(new RecordingDispatcher());
        var registry = new GameClientAdapterRegistry(new IGameClientAdapter[] { adapter });
        var descriptor = registry.GetDescriptor(GameClientType.Steam);

        Assert.AreEqual(GameMechanismStatus.SupportedPublic,
            descriptor.Capability(GameClientCapabilityId.GameLaunch).MechanismStatus);
        Assert.AreEqual(GameMechanismStatus.Open,
            descriptor.Capability(GameClientCapabilityId.GameProcessCorrelation).MechanismStatus);
        Assert.AreEqual(GameMechanismStatus.Open,
            descriptor.Capability(GameClientCapabilityId.GameExitCorrelation).MechanismStatus);
    }

    private static SteamProtocolLaunchAdapter Adapter(ISteamUriLaunchDispatcher dispatcher)
        => new(
            new SteamClientAdapter(
                new ConstantRegistrationReader(),
                new ConstantExecutableReader(),
                new FixedTimeProvider(Now)),
            dispatcher,
            new FixedTimeProvider(Now));

    private static async Task<PreparedClientLaunch> Prepared(SteamProtocolLaunchAdapter adapter)
        => await adapter.PrepareLaunchAsync(
            Request(),
            Projection(),
            ClientEvidence(GameClientDiscoveryAvailability.AvailableVerified),
            CancellationToken.None);

    private static GameClientLaunchRequest Request()
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "game.cyberpunk",
            GameClientType.Steam,
            Identity(),
            LaunchIdentity(AppId),
            7,
            1,
            Now.AddSeconds(-5),
            Now.AddMinutes(1));

    private static AdapterGameProjection Projection()
        => new AdapterGameProjection(
            Identity(),
            Installation(Now.AddMinutes(-1), Now.AddMinutes(4)),
            LaunchIdentity(AppId),
            Array.Empty<string>(),
            new GameLibrarySourceProvenance(
                SteamClientAdapter.InstallationMechanismId,
                SteamClientAdapter.AdapterVersion,
                SteamClientAdapter.EvidenceSchemaVersion,
                "10.20.30.40"),
            "Cyberpunk 2077").Normalize(GameClientType.Steam);

    private static ExternalGameIdentity Identity()
        => new(GameClientType.Steam, SteamClientAdapter.ExternalIdKind, AppId);

    private static AdapterLaunchIdentity LaunchIdentity(string appId)
        => new(
            SteamClientAdapter.ExternalIdKind,
            SteamProtocolLaunchAdapter.LaunchIdentitySchemaVersion,
            appId,
            Now.AddMinutes(-1),
            GameMechanismStatus.SupportedPublic);

    private static GameInstallationEvidence Installation(
        DateTimeOffset observedAt,
        DateTimeOffset expiresAt)
        => new GameInstallationEvidence(
            GameInstallState.InstalledVerifiedEvidence,
            InstallRoot,
            observedAt,
            expiresAt,
            GameEvidenceFreshness.Fresh,
            GameEvidenceConfidence.High,
            GameMechanismStatus.BestEffortLocalEvidence,
            @"C:\Steam\steamapps\appmanifest_1091500.acf").Normalize();

    private static GameClientDiscoveryEvidence ClientEvidence(
        GameClientDiscoveryAvailability availability,
        string? observedVersion = "10.20.30.40")
        => new GameClientDiscoveryEvidence(
            GameClientType.Steam,
            availability,
            SteamClientAdapter.DiscoveryMechanismId,
            GameMechanismStatus.SupportedOsMechanism,
            Now.AddMinutes(-1),
            1,
            @"C:\Steam\steam.exe",
            SteamClientAdapter.ProtocolRegistrationIdentity,
            null,
            observedVersion,
            null).Normalize(GameClientType.Steam);

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

        public int Calls { get; private set; }
        public string? LastUri { get; private set; }

        public SteamUriLaunchResult Dispatch(string canonicalSteamRunUri)
        {
            Calls++;
            LastUri = canonicalSteamRunUri;
            return _result;
        }
    }

    private sealed class ThrowingDispatcher : ISteamUriLaunchDispatcher
    {
        public SteamUriLaunchResult Dispatch(string canonicalSteamRunUri)
            => throw new InvalidOperationException("synthetic dispatch failure");
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
