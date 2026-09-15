using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.GameRuntime;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class MicrosoftGamingPackageAdapterTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 9, 15, 16, 0, 0, TimeSpan.Zero);
    private const string Pfn = "Contoso.SpaceGame_8wekyb3d8bbwe";
    private const string Aumid = Pfn + "!Game";

    [TestMethod]
    public void DescriptorIsPartialAndDoesNotClaimAccountOrCorrelationCapabilities()
    {
        var adapter = Adapter(Snapshot(), Catalog());

        Assert.AreEqual(GameClientSupportStatus.PartialSupportedV1, adapter.Descriptor.SupportStatus);
        Assert.AreEqual(GameMechanismStatus.SupportedOsMechanism,
            adapter.Descriptor.Capability(GameClientCapabilityId.InstallationEvidence).MechanismStatus);
        Assert.AreEqual(GameMechanismStatus.SupportedOsMechanism,
            adapter.Descriptor.Capability(GameClientCapabilityId.GameLaunch).MechanismStatus);
        Assert.AreEqual(GameMechanismStatus.Open,
            adapter.Descriptor.Capability(GameClientCapabilityId.GameProcessCorrelation).MechanismStatus);
        Assert.AreEqual(GameMechanismStatus.Open,
            adapter.Descriptor.Capability(GameClientCapabilityId.GameExitCorrelation).MechanismStatus);
        Assert.AreEqual(GameMechanismStatus.Unsupported,
            adapter.Descriptor.Capability(GameClientCapabilityId.AccountContextEvidence).MechanismStatus);
    }

    [TestMethod]
    public void EmptyReleaseCatalogDowngradesOnlyLibraryDiscoveryAtRuntime()
    {
        var adapter = Adapter(Snapshot(), new EmptyMicrosoftGamingTitleCatalog());
        var status = adapter.GetCompatibilityStatus(new GameClientCompatibilityContext(26100, null));

        Assert.AreEqual(GameMechanismStatus.Open,
            status.Capability(GameClientCapabilityId.LibraryDiscovery).MechanismStatus);
        Assert.AreEqual("MSFT_KNOWN_TITLE_CATALOG_EMPTY",
            status.Capability(GameClientCapabilityId.LibraryDiscovery).NotesCode);
        Assert.AreEqual(GameMechanismStatus.SupportedOsMechanism,
            status.Capability(GameClientCapabilityId.GameLaunch).MechanismStatus);
    }

    [TestMethod]
    public async Task KnownPfnAndAumidProduceVerifiedInstalledProjection()
    {
        var package = Package("1.2.3.4");
        var adapter = Adapter(Snapshot(package), Catalog());
        var request = RefreshRequest();

        var result = await adapter.RefreshLibraryAsync(request, CancellationToken.None);

        Assert.AreEqual(GameClientLibraryRefreshResultCode.Refreshed, result.ResultCode);
        Assert.AreEqual(1, result.Records.Count);
        var projection = result.Records[0];
        Assert.AreEqual(GameInstallState.InstalledVerifiedEvidence, projection.Installation.State);
        Assert.AreEqual(GameEvidenceConfidence.High, projection.Installation.Confidence);
        Assert.IsNull(projection.Installation.ValidatedInstallRoot);
        Assert.AreEqual(Aumid, projection.ExternalGameIdentity.ExternalId);
        Assert.AreEqual(MicrosoftGamingIdentity.ExternalIdKind, projection.ExternalGameIdentity.ExternalIdKind);
        CollectionAssert.Contains(projection.ExternalGameIdentity.SecondaryIds!.ToArray(), "PFN:" + Pfn);
        Assert.AreEqual(MicrosoftGamingPackageAdapter.LaunchIdentityMechanismId, projection.LaunchIdentity!.Kind);
        Assert.AreEqual(Aumid, projection.LaunchIdentity.Payload);
        Assert.AreEqual("1.2.3.4", projection.SourceProvenance.ClientVersionObserved);
    }

    [TestMethod]
    public async Task PackageVersionChangePreservesStableExternalBinding()
    {
        var first = Adapter(Snapshot(Package("1.0.0.0")), Catalog());
        var second = Adapter(Snapshot(Package("2.0.0.0")), Catalog());

        var firstResult = await first.RefreshLibraryAsync(RefreshRequest(), CancellationToken.None);
        var secondResult = await second.RefreshLibraryAsync(RefreshRequest(), CancellationToken.None);

        Assert.AreEqual(firstResult.Records[0].ExternalGameIdentity.ExternalId,
            secondResult.Records[0].ExternalGameIdentity.ExternalId);
        Assert.AreEqual(firstResult.Records[0].ExternalGameIdentity.ExternalIdKind,
            secondResult.Records[0].ExternalGameIdentity.ExternalIdKind);
        CollectionAssert.AreEqual(
            firstResult.Records[0].ExternalGameIdentity.SecondaryIds!.ToArray(),
            secondResult.Records[0].ExternalGameIdentity.SecondaryIds!.ToArray());
        Assert.AreNotEqual(
            firstResult.Records[0].SourceProvenance.ClientVersionObserved,
            secondResult.Records[0].SourceProvenance.ClientVersionObserved);
    }

    [TestMethod]
    public async Task MissingCurrentUserRegistrationIsVerifiedNotInstalledNotLicenseDenied()
    {
        var adapter = Adapter(Snapshot(), Catalog());
        var result = await adapter.RefreshLibraryAsync(RefreshRequest(), CancellationToken.None);

        Assert.AreEqual(GameClientLibraryRefreshResultCode.Refreshed, result.ResultCode);
        Assert.AreEqual(GameInstallState.NotInstalledVerifiedEvidence, result.Records[0].Installation.State);
        Assert.IsNull(result.Records[0].LaunchIdentity);
        Assert.IsFalse(result.ParserStatus!.Contains("LICENSE", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task PackageWithoutExpectedAumidIsPartialUnknownInsteadOfInstalled()
    {
        var package = Package("1.0.0.0") with
        {
            Applications = new[] { new MicrosoftPackageApplicationRegistration(Pfn + "!Different", "Different") }
        };
        var adapter = Adapter(Snapshot(package), Catalog());

        var result = await adapter.RefreshLibraryAsync(RefreshRequest(), CancellationToken.None);

        Assert.AreEqual(GameClientLibraryRefreshResultCode.Partial, result.ResultCode);
        Assert.AreEqual(GameInstallState.Unknown, result.Records[0].Installation.State);
        Assert.IsNull(result.Records[0].LaunchIdentity);
        CollectionAssert.Contains(result.MissingEvidenceClasses!.ToArray(), "MSFT_AUMID_REGISTRATION_NOT_FOUND");
    }

    [TestMethod]
    public async Task ResolveInstallationUsesCanonicalAumidAndPfnEvidence()
    {
        var adapter = Adapter(Snapshot(Package("3.0.0.0")), Catalog());
        var identity = Identity().ToExternalIdentity();

        var projection = await adapter.ResolveInstallationAsync(
            identity,
            GameClientFreshnessRequirement.FreshRequired,
            CancellationToken.None);

        Assert.IsNotNull(projection);
        Assert.AreEqual(GameInstallState.InstalledVerifiedEvidence, projection.Installation.State);
        Assert.AreEqual(Aumid, projection.ExternalGameIdentity.ExternalId);
    }

    [TestMethod]
    public async Task FreshVerifiedRegistrationCanPrepareAumidLaunchWithoutExecutablePath()
    {
        var adapter = Adapter(Snapshot(Package("1.0.0.0")), Catalog());
        var refresh = await adapter.RefreshLibraryAsync(RefreshRequest(), CancellationToken.None);
        var projection = refresh.Records.Single();
        var discovery = await adapter.DiscoverClientAsync(2, GameClientFreshnessRequirement.FreshRequired, CancellationToken.None);
        var request = LaunchRequest(projection);

        var prepared = await adapter.PrepareLaunchAsync(request, projection, discovery, CancellationToken.None);

        Assert.AreEqual(MicrosoftGamingPackageAdapter.LaunchMechanismId, prepared.HandoffMechanism);
        Assert.AreEqual(GameMechanismStatus.SupportedOsMechanism, prepared.HandoffMechanismStatus);
        Assert.AreEqual(Aumid, prepared.ResolvedLaunchIdentity.Payload);
        Assert.AreEqual(Aumid, prepared.RequiredObservationRules.ExpectedAumid);
        Assert.AreEqual(Pfn, prepared.RequiredObservationRules.ExpectedPackageFamilyName);
        Assert.IsNull(prepared.RequiredObservationRules.ValidatedInstallRoot);
        Assert.AreEqual(0, prepared.RequiredObservationRules.ExpectedExecutableNames.Count);
    }

    [TestMethod]
    public async Task StalePackageEvidenceCannotPrepareLaunch()
    {
        var adapter = Adapter(Snapshot(Package("1.0.0.0")), Catalog());
        var refresh = await adapter.RefreshLibraryAsync(RefreshRequest(), CancellationToken.None);
        var projection = refresh.Records.Single() with
        {
            Installation = refresh.Records.Single().Installation with { ExpiresAtUtc = ObservedAt }
        };
        var discovery = await adapter.DiscoverClientAsync(2, GameClientFreshnessRequirement.FreshRequired, CancellationToken.None);

        await AssertThrowsAsync<InvalidDataException>(() => adapter.PrepareLaunchAsync(
            LaunchRequest(projection), projection, discovery, CancellationToken.None));
    }

    [TestMethod]
    public async Task AcceptedActivationReturnsOnlyHandoffAcceptedWithPidEvidence()
    {
        var dispatcher = new RecordingActivationDispatcher(new MicrosoftApplicationActivationResult(
            MicrosoftApplicationActivationDisposition.Accepted,
            4242,
            "MSFT_AUMID_ACTIVATION_ACCEPTED"));
        var adapter = Adapter(Snapshot(Package("1.0.0.0")), Catalog(), dispatcher);
        var refresh = await adapter.RefreshLibraryAsync(RefreshRequest(), CancellationToken.None);
        var projection = refresh.Records.Single();
        var discovery = await adapter.DiscoverClientAsync(2, GameClientFreshnessRequirement.FreshRequired, CancellationToken.None);
        var prepared = await adapter.PrepareLaunchAsync(LaunchRequest(projection), projection, discovery, CancellationToken.None);

        var result = await adapter.SubmitLaunchAsync(prepared, CancellationToken.None);

        Assert.AreEqual(GameClientLaunchHandoffResultCode.HandoffAccepted, result.ResultCode);
        Assert.AreEqual(Aumid, dispatcher.LastAumid);
        Assert.AreEqual("4242", result.ReturnedProcessIdentity);
        Assert.AreEqual("MSFT_ACTIVATION_PID", result.ClientProcessEvidence);
        Assert.IsFalse(Enum.GetNames<GameClientLaunchHandoffResultCode>()
            .Any(name => string.Equals(name, "GameRunning", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task MissingAumidRegistrationMapsToMechanismUnavailable()
    {
        var dispatcher = new RecordingActivationDispatcher(new MicrosoftApplicationActivationResult(
            MicrosoftApplicationActivationDisposition.MechanismUnavailable,
            0,
            "MSFT_AUMID_REGISTRATION_UNAVAILABLE"));
        var adapter = Adapter(Snapshot(Package("1.0.0.0")), Catalog(), dispatcher);
        var refresh = await adapter.RefreshLibraryAsync(RefreshRequest(), CancellationToken.None);
        var projection = refresh.Records.Single();
        var discovery = await adapter.DiscoverClientAsync(2, GameClientFreshnessRequirement.FreshRequired, CancellationToken.None);
        var prepared = await adapter.PrepareLaunchAsync(LaunchRequest(projection), projection, discovery, CancellationToken.None);

        var result = await adapter.SubmitLaunchAsync(prepared, CancellationToken.None);

        Assert.AreEqual(GameClientLaunchHandoffResultCode.MechanismUnavailable, result.ResultCode);
        Assert.AreEqual("MSFT_AUMID_REGISTRATION_UNAVAILABLE", result.DiagnosticsCode);
    }

    [TestMethod]
    public void IdentityRejectsPfnAumidMismatchAndInjectionShapes()
    {
        AssertThrows<InvalidDataException>(() => new MicrosoftGamingIdentity(Pfn, "Other.PFN_8wekyb3d8bbwe!Game").Normalize());
        AssertThrows<InvalidDataException>(() => new MicrosoftGamingIdentity(Pfn, Aumid + " ?arg=1").Normalize());
        AssertThrows<InvalidDataException>(() => MicrosoftGamingIdentity.FromExternalIdentity(new ExternalGameIdentity(
            GameClientType.MicrosoftGaming,
            MicrosoftGamingIdentity.ExternalIdKind,
            Aumid,
            Array.Empty<string>())));
    }

    [TestMethod]
    public void AdapterSurfaceContainsNoDirectExecutableLaunchPrimitiveOrLicenseClaim()
    {
        var propertyNames = typeof(MicrosoftGamingIdentity).GetProperties().Select(property => property.Name).ToArray();
        CollectionAssert.DoesNotContain(propertyNames, "ExecutablePath");
        CollectionAssert.DoesNotContain(propertyNames, "CommandLine");
        CollectionAssert.DoesNotContain(propertyNames, "LicenseOwned");
        CollectionAssert.DoesNotContain(propertyNames, "SubscriptionOwned");

        var dispatcherMethods = typeof(IMicrosoftApplicationActivationDispatcher).GetMethods();
        Assert.AreEqual(1, dispatcherMethods.Length);
        Assert.AreEqual("Activate", dispatcherMethods[0].Name);
    }

    [TestMethod]
    public void RegistryRoutesMicrosoftAdapterIndependently()
    {
        var adapter = Adapter(Snapshot(), Catalog());
        var registry = new GameClientAdapterRegistry(new IGameClientAdapter[] { adapter });

        Assert.AreSame(adapter, registry.GetRequiredAdapter(GameClientType.MicrosoftGaming));
        Assert.AreEqual(GameClientSupportStatus.PartialSupportedV1,
            registry.GetDescriptor(GameClientType.MicrosoftGaming).SupportStatus);
    }

    private static MicrosoftGamingPackageAdapter Adapter(
        MicrosoftPackageCatalogSnapshot snapshot,
        IMicrosoftGamingTitleCatalog catalog,
        IMicrosoftApplicationActivationDispatcher? dispatcher = null)
        => new(
            new SequencePackageReader(snapshot),
            catalog,
            dispatcher ?? new RecordingActivationDispatcher(new MicrosoftApplicationActivationResult(
                MicrosoftApplicationActivationDisposition.Accepted,
                1234,
                "MSFT_AUMID_ACTIVATION_ACCEPTED")),
            new FixedTimeProvider(ObservedAt));

    private static MicrosoftPackageCatalogSnapshot Snapshot(params MicrosoftPackageRegistration[] packages)
        => new(true, packages);

    private static MicrosoftPackageRegistration Package(string version)
        => new(
            Pfn,
            "Contoso.SpaceGame",
            $"Contoso.SpaceGame_{version}_x64__8wekyb3d8bbwe",
            version,
            false,
            false,
            true,
            new[] { new MicrosoftPackageApplicationRegistration(Aumid, "Space Game") });

    private static IMicrosoftGamingTitleCatalog Catalog()
        => new FixedCatalog(new MicrosoftGamingKnownTitle(Pfn, Aumid, "Space Game", "9NTEST"));

    private static MicrosoftGamingIdentity Identity()
        => new(Pfn, Aumid, "Contoso.SpaceGame", "9NTEST");

    private static GameClientLibraryRefreshRequest RefreshRequest()
        => new(
            Guid.NewGuid(),
            GameClientType.MicrosoftGaming,
            GameClientLibraryRefreshReason.RuntimeStart,
            GameClientFreshnessRequirement.FreshRequired,
            null,
            ObservedAt.AddMinutes(1));

    private static GameClientLaunchRequest LaunchRequest(AdapterGameProjection projection)
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "game.space",
            GameClientType.MicrosoftGaming,
            projection.ExternalGameIdentity,
            projection.LaunchIdentity!,
            1,
            2,
            ObservedAt.AddSeconds(-1),
            ObservedAt.AddMinutes(1));

    private sealed class FixedCatalog(params MicrosoftGamingKnownTitle[] titles) : IMicrosoftGamingTitleCatalog
    {
        public IReadOnlyList<MicrosoftGamingKnownTitle> Titles { get; } = titles;
    }

    private sealed class SequencePackageReader(params MicrosoftPackageCatalogSnapshot[] snapshots)
        : IMicrosoftPackageRegistrationReader
    {
        private readonly Queue<MicrosoftPackageCatalogSnapshot> _snapshots = new(snapshots);

        public Task<MicrosoftPackageCatalogSnapshot> ReadCurrentUserPackagesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_snapshots.Count == 0)
                throw new AssertFailedException("Unexpected Microsoft package read.");
            return Task.FromResult(_snapshots.Dequeue());
        }
    }

    private sealed class RecordingActivationDispatcher(MicrosoftApplicationActivationResult result)
        : IMicrosoftApplicationActivationDispatcher
    {
        public string? LastAumid { get; private set; }

        public MicrosoftApplicationActivationResult Activate(string canonicalAppUserModelId)
        {
            LastAumid = canonicalAppUserModelId;
            return result;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private static T AssertThrows<T>(Action action) where T : Exception
    {
        try
        {
            action();
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
