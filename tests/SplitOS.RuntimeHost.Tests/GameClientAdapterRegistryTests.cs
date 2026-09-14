using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.GameRuntime;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class GameClientAdapterRegistryTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void RegistryRejectsTwoAdaptersClaimingSameClientType()
    {
        var first = new FakeAdapter(Descriptor(GameClientType.Steam, "adapter/1"));
        var second = new FakeAdapter(Descriptor(GameClientType.Steam, "adapter/2"));

        Assert.ThrowsException<InvalidDataException>(() =>
            new GameClientAdapterRegistry(new IGameClientAdapter[] { first, second }));
    }

    [TestMethod]
    public void DescriptorRequiresExplicitStatusForEveryCapability()
    {
        var capabilities = AllCapabilities()
            .Where(capability => capability.CapabilityId != GameClientCapabilityId.LibraryDiscovery)
            .ToArray();
        var descriptor = new GameClientAdapterDescriptor(
            GameClientType.Steam,
            "adapter/1",
            new[] { "APP_ID" },
            capabilities,
            "steam/v1",
            GameClientSupportStatus.TargetSupportedV1);

        Assert.ThrowsException<InvalidDataException>(() => descriptor.Normalize());
    }

    [TestMethod]
    public void RegistryPreservesIndependentCapabilityStatuses()
    {
        var descriptor = Descriptor(
            GameClientType.Steam,
            "adapter/1",
            overrides: new Dictionary<GameClientCapabilityId, GameMechanismStatus>
            {
                [GameClientCapabilityId.GameLaunch] = GameMechanismStatus.SupportedPublic,
                [GameClientCapabilityId.LibraryDiscovery] = GameMechanismStatus.VersionSensitive
            });
        var registry = new GameClientAdapterRegistry(new[] { new FakeAdapter(descriptor) });

        var normalized = registry.GetDescriptor(GameClientType.Steam);
        Assert.AreEqual(
            GameMechanismStatus.SupportedPublic,
            normalized.Capability(GameClientCapabilityId.GameLaunch).MechanismStatus);
        Assert.AreEqual(
            GameMechanismStatus.VersionSensitive,
            normalized.Capability(GameClientCapabilityId.LibraryDiscovery).MechanismStatus);
    }

    [TestMethod]
    public void RuntimeCompatibilityCanDegradeLibraryWithoutDisablingPublicLaunch()
    {
        var descriptor = Descriptor(
            GameClientType.Steam,
            "adapter/1",
            overrides: new Dictionary<GameClientCapabilityId, GameMechanismStatus>
            {
                [GameClientCapabilityId.GameLaunch] = GameMechanismStatus.SupportedPublic,
                [GameClientCapabilityId.LibraryDiscovery] = GameMechanismStatus.VersionSensitive
            });
        var adapter = new FakeAdapter(descriptor, context => Compatibility(
            descriptor,
            new Dictionary<GameClientCapabilityId, GameMechanismStatus>
            {
                [GameClientCapabilityId.GameLaunch] = GameMechanismStatus.SupportedPublic,
                [GameClientCapabilityId.LibraryDiscovery] = GameMechanismStatus.Open
            },
            context.ObservedClientVersion));
        var registry = new GameClientAdapterRegistry(new[] { adapter });
        var context = new GameClientCompatibilityContext(26100, "999.0", new[] { "unknown-vdf-schema" });

        Assert.AreEqual(
            GameMechanismStatus.SupportedPublic,
            registry.GetCapabilityStatus(GameClientType.Steam, GameClientCapabilityId.GameLaunch, context).MechanismStatus);
        Assert.AreEqual(
            GameMechanismStatus.Open,
            registry.GetCapabilityStatus(GameClientType.Steam, GameClientCapabilityId.LibraryDiscovery, context).MechanismStatus);
    }

    [TestMethod]
    public void CompatibilitySnapshotMustCoverEveryDeclaredCapability()
    {
        var descriptor = Descriptor(GameClientType.Epic, "adapter/1");
        var incomplete = Compatibility(descriptor) with
        {
            Capabilities = Compatibility(descriptor).Capabilities
                .Where(status => status.CapabilityId != GameClientCapabilityId.GameLaunch)
                .ToArray()
        };
        var registry = new GameClientAdapterRegistry(new[]
        {
            new FakeAdapter(descriptor, _ => incomplete)
        });

        Assert.ThrowsException<InvalidDataException>(() =>
            registry.GetCompatibilityStatus(
                GameClientType.Epic,
                new GameClientCompatibilityContext(26100, "1.0")));
    }

    [TestMethod]
    public void CompatibilitySnapshotCannotChangeCapabilityMechanismIdentity()
    {
        var descriptor = Descriptor(GameClientType.MicrosoftGaming, "adapter/1");
        var statuses = Compatibility(descriptor).Capabilities
            .Select(status => status.CapabilityId == GameClientCapabilityId.GameLaunch
                ? status with { MechanismId = "different-mechanism" }
                : status)
            .ToArray();
        var registry = new GameClientAdapterRegistry(new[]
        {
            new FakeAdapter(descriptor, _ => Compatibility(descriptor) with { Capabilities = statuses })
        });

        Assert.ThrowsException<InvalidDataException>(() =>
            registry.GetCompatibilityStatus(
                GameClientType.MicrosoftGaming,
                new GameClientCompatibilityContext(26100, null)));
    }

    [TestMethod]
    public void UnknownClientTypeFailsClosed()
    {
        var registry = new GameClientAdapterRegistry(new[]
        {
            new FakeAdapter(Descriptor(GameClientType.Steam, "adapter/1"))
        });

        Assert.IsFalse(registry.TryGetAdapter(GameClientType.Epic, out var adapter));
        Assert.IsNull(adapter);
        Assert.ThrowsException<KeyNotFoundException>(() => registry.GetRequiredAdapter(GameClientType.Epic));
    }

    [TestMethod]
    public void DescriptorNormalizesExternalIdKindsDeterministically()
    {
        var descriptor = new GameClientAdapterDescriptor(
            GameClientType.Epic,
            " adapter/1 ",
            new[] { " CATALOG_ARTIFACT ", "INSTALL_ID", "CATALOG_ARTIFACT" },
            AllCapabilities(),
            " epic/v1 ",
            GameClientSupportStatus.TargetSupportedV1).Normalize();

        CollectionAssert.AreEqual(
            new[] { "CATALOG_ARTIFACT", "INSTALL_ID" },
            descriptor.SupportedExternalIdKinds.ToArray());
        Assert.AreEqual("adapter/1", descriptor.AdapterVersion);
        Assert.AreEqual("epic/v1", descriptor.CompatibilityPolicyId);
    }

    [TestMethod]
    public void HandoffAcceptedIsSeparateFromRunningConfirmation()
    {
        Assert.IsTrue(Enum.IsDefined(typeof(GameClientLaunchHandoffResultCode), GameClientLaunchHandoffResultCode.HandoffAccepted));
        Assert.IsFalse(Enum.GetNames<GameClientLaunchHandoffResultCode>().Any(name =>
            string.Equals(name, "GameRunning", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "RunningConfirmed", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(Enum.IsDefined(typeof(GameClientObservationClassification), GameClientObservationClassification.RunningConfirmed));
    }

    [TestMethod]
    public void WeakEvidenceCannotEstablishRunningConfirmed()
    {
        var observation = new GameClientObservationResult(
            GameClientObservationClassification.RunningConfirmed,
            GameClientEvidenceLevel.Weak,
            Array.Empty<GameClientCorrelatedProcessEvidence>(),
            false,
            ObservedAt);

        Assert.ThrowsException<InvalidDataException>(() => observation.Normalize());
    }

    [TestMethod]
    public void PartialRefreshMustIdentifyMissingEvidenceClasses()
    {
        var request = new GameClientLibraryRefreshRequest(
            Guid.NewGuid(),
            GameClientType.Steam,
            GameClientLibraryRefreshReason.RuntimeStart,
            GameClientFreshnessRequirement.FreshIfAvailable,
            null,
            ObservedAt.AddMinutes(1));
        var result = new GameClientLibraryRefreshResult(
            request.RefreshId,
            GameClientType.Steam,
            GameClientLibraryRefreshResultCode.Partial,
            1,
            ObservedAt,
            Array.Empty<AdapterGameProjection>());

        Assert.ThrowsException<InvalidDataException>(() => result.Normalize(request));
    }

    [TestMethod]
    public void PreparedLaunchUsesSemanticExternalIdentityNotObjectReference()
    {
        var request = LaunchRequest(new ExternalGameIdentity(
            GameClientType.Steam,
            "APP_ID",
            "1091500",
            new[] { "store:1091500" })).Normalize(GameClientType.Steam);
        var prepared = Prepared(request, new ExternalGameIdentity(
            GameClientType.Steam,
            "APP_ID",
            "1091500",
            new[] { "store:1091500" }));

        var normalized = prepared.Normalize(request);
        Assert.AreEqual(request.LaunchOperationId, normalized.LaunchOperationId);
        Assert.AreEqual("1091500", normalized.NormalizedExternalGameIdentity.ExternalId);
    }

    [TestMethod]
    public void LaunchRequestSurfaceCannotAcceptRawCommandOrArbitraryUriFromUi()
    {
        var propertyNames = typeof(GameClientLaunchRequest)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        CollectionAssert.DoesNotContain(propertyNames, "CommandLine");
        CollectionAssert.DoesNotContain(propertyNames, "ExecutablePath");
        CollectionAssert.DoesNotContain(propertyNames, "Uri");
        CollectionAssert.Contains(propertyNames, nameof(GameClientLaunchRequest.ResolvedLaunchIdentity));
        CollectionAssert.Contains(propertyNames, nameof(GameClientLaunchRequest.ExternalGameIdentity));
    }

    [TestMethod]
    public void CompletelyUnsupportedAdapterStillPreservesStableSemanticContract()
    {
        var descriptor = Descriptor(
            GameClientType.BattleNet,
            "adapter/experimental",
            GameClientSupportStatus.Experimental);
        var registry = new GameClientAdapterRegistry(new[] { new FakeAdapter(descriptor) });

        Assert.AreEqual(12, registry.GetDescriptor(GameClientType.BattleNet).Capabilities.Count);
        Assert.IsTrue(registry.GetDescriptor(GameClientType.BattleNet).Capabilities.All(capability =>
            capability.MechanismStatus == GameMechanismStatus.Unsupported));
    }

    private static GameClientAdapterDescriptor Descriptor(
        GameClientType clientType,
        string adapterVersion,
        GameClientSupportStatus supportStatus = GameClientSupportStatus.TargetSupportedV1,
        IReadOnlyDictionary<GameClientCapabilityId, GameMechanismStatus>? overrides = null)
        => new(
            clientType,
            adapterVersion,
            clientType switch
            {
                GameClientType.Steam => new[] { "APP_ID" },
                GameClientType.Epic => new[] { "CATALOG_ARTIFACT" },
                GameClientType.MicrosoftGaming => new[] { "AUMID" },
                _ => new[] { "PRODUCT_CODE" }
            },
            AllCapabilities(overrides),
            $"{clientType}/v1",
            supportStatus);

    private static IReadOnlyList<GameClientAdapterCapability> AllCapabilities(
        IReadOnlyDictionary<GameClientCapabilityId, GameMechanismStatus>? overrides = null)
        => Enum.GetValues<GameClientCapabilityId>()
            .Select(capability => new GameClientAdapterCapability(
                capability,
                overrides is not null && overrides.TryGetValue(capability, out var status)
                    ? status
                    : GameMechanismStatus.Unsupported,
                $"{capability}.mechanism"))
            .ToArray();

    private static GameClientCompatibilitySnapshot Compatibility(
        GameClientAdapterDescriptor descriptor,
        IReadOnlyDictionary<GameClientCapabilityId, GameMechanismStatus>? overrides = null,
        string? observedClientVersion = null)
    {
        var normalized = descriptor.Normalize();
        return new GameClientCompatibilitySnapshot(
            normalized.ClientType,
            normalized.AdapterVersion,
            normalized.Capabilities.Select(capability => new GameClientCapabilityStatus(
                capability.CapabilityId,
                overrides is not null && overrides.TryGetValue(capability.CapabilityId, out var status)
                    ? status
                    : capability.MechanismStatus,
                capability.MechanismId)).ToArray(),
            ObservedAt,
            normalized.CompatibilityPolicyId,
            observedClientVersion);
    }

    private static GameClientLaunchRequest LaunchRequest(ExternalGameIdentity identity)
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "game.cyberpunk",
            identity.ClientType,
            identity,
            LaunchIdentity(),
            3,
            1,
            ObservedAt,
            ObservedAt.AddMinutes(1));

    private static PreparedClientLaunch Prepared(
        GameClientLaunchRequest request,
        ExternalGameIdentity identity)
        => new(
            request.LaunchOperationId,
            request.ClientType,
            identity,
            LaunchIdentity(),
            new GameClientObservationRules(
                new[] { "Cyberpunk2077.exe" },
                @"C:\Games\Cyberpunk2077",
                null,
                null,
                Array.Empty<string>(),
                Array.Empty<string>(),
                GameClientEvidenceLevel.Strong,
                1000,
                2000),
            "STEAM_PROTOCOL",
            GameMechanismStatus.SupportedPublic,
            4,
            ObservedAt.AddMinutes(1));

    private static AdapterLaunchIdentity LaunchIdentity()
        => new(
            "STEAM_APP_ID",
            1,
            "1091500",
            ObservedAt,
            GameMechanismStatus.SupportedPublic);

    private sealed class FakeAdapter : IGameClientAdapter
    {
        private readonly Func<GameClientCompatibilityContext, GameClientCompatibilitySnapshot> _compatibility;

        public FakeAdapter(
            GameClientAdapterDescriptor descriptor,
            Func<GameClientCompatibilityContext, GameClientCompatibilitySnapshot>? compatibility = null)
        {
            Descriptor = descriptor;
            _compatibility = compatibility ?? (context => Compatibility(descriptor, observedClientVersion: context.ObservedClientVersion));
        }

        public GameClientAdapterDescriptor Descriptor { get; }

        public GameClientCompatibilitySnapshot GetCompatibilityStatus(GameClientCompatibilityContext context)
            => _compatibility(context);

        public Task<GameClientDiscoveryEvidence> DiscoverClientAsync(
            int sessionId,
            GameClientFreshnessRequirement freshnessRequirement,
            CancellationToken cancellationToken)
            => Task.FromException<GameClientDiscoveryEvidence>(new NotSupportedException());

        public Task<GameClientLibraryRefreshResult> RefreshLibraryAsync(
            GameClientLibraryRefreshRequest request,
            CancellationToken cancellationToken)
            => Task.FromException<GameClientLibraryRefreshResult>(new NotSupportedException());

        public Task<AdapterGameProjection?> ResolveInstallationAsync(
            ExternalGameIdentity externalGameIdentity,
            GameClientFreshnessRequirement freshnessRequirement,
            CancellationToken cancellationToken)
            => Task.FromException<AdapterGameProjection?>(new NotSupportedException());

        public Task<PreparedClientLaunch> PrepareLaunchAsync(
            GameClientLaunchRequest request,
            AdapterGameProjection currentProjection,
            GameClientDiscoveryEvidence currentClientEvidence,
            CancellationToken cancellationToken)
            => Task.FromException<PreparedClientLaunch>(new NotSupportedException());

        public Task<GameClientLaunchHandoffResult> SubmitLaunchAsync(
            PreparedClientLaunch preparedLaunch,
            CancellationToken cancellationToken)
            => Task.FromException<GameClientLaunchHandoffResult>(new NotSupportedException());

        public Task<GameClientObservationResult> ObserveLaunchAsync(
            PreparedClientLaunch preparedLaunch,
            CancellationToken cancellationToken)
            => Task.FromException<GameClientObservationResult>(new NotSupportedException());

        public Task<GameClientExitObservationResult> ObserveExitAsync(
            PreparedClientLaunch preparedLaunch,
            IReadOnlyList<GameClientCorrelatedProcessEvidence> currentCorrelation,
            CancellationToken cancellationToken)
            => Task.FromException<GameClientExitObservationResult>(new NotSupportedException());

        public void Invalidate(GameClientLibraryRefreshReason reason)
        {
        }
    }
}