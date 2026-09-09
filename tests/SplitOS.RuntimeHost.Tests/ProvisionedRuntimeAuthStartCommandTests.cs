using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.Contracts.Protocol;
using SplitOS.RuntimeHost.Authentication;
using SplitOS.RuntimeHost.ProductIdentity;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class ProvisionedRuntimeAuthStartCommandTests
{
    private const string PackageDigestA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string PackageDigestB = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    [TestMethod]
    public async Task MissingAuthorityPackageDoesNotConstructInteractiveCommand()
    {
        var provider = new SequenceProvider(Result(NativeAuthAuthorityPackageStatus.Missing, "AUTH_AUTHORITY_PACKAGE_MISSING"));
        var factory = new RecordingFactory();
        var command = new ProvisionedRuntimeAuthStartCommand(provider, factory);

        var result = await command.StartAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.AreEqual("Unavailable", result.Disposition);
        Assert.AreEqual("AUTH_AUTHORITY_PACKAGE_MISSING", result.ProductCode);
        Assert.AreEqual(0, factory.CreateCount);
    }

    [TestMethod]
    public async Task VerifiedAuthorityConstructsCommandAndDelegatesSemanticRequest()
    {
        var metadata = Metadata(version: 7, epoch: 3);
        var provider = new SequenceProvider(Available(metadata));
        var inner = new RecordingCommand();
        var factory = new RecordingFactory(inner);
        var command = new ProvisionedRuntimeAuthStartCommand(provider, factory);
        var correlationId = Guid.NewGuid();
        var operationId = Guid.NewGuid();

        var result = await command.StartAsync(correlationId, operationId);

        Assert.AreEqual("Associated", result.Disposition);
        Assert.AreEqual("ACCOUNT_ASSOCIATED", result.ProductCode);
        Assert.AreEqual(1, factory.CreateCount);
        Assert.AreSame(metadata, factory.LastMetadata);
        Assert.AreSame(metadata.Authority, factory.LastMetadata!.Authority);
        Assert.AreSame(metadata.ProductApi, factory.LastMetadata.ProductApi);
        Assert.AreEqual(correlationId, inner.LastCorrelationId);
        Assert.AreEqual(operationId, inner.LastOperationId);
    }

    [TestMethod]
    public async Task SameVerifiedMetadataReusesExistingCommand()
    {
        var metadata = Metadata(version: 7, epoch: 3);
        var provider = new SequenceProvider(Available(metadata), Available(metadata));
        var factory = new RecordingFactory(new RecordingCommand());
        var command = new ProvisionedRuntimeAuthStartCommand(provider, factory);

        await command.StartAsync(Guid.NewGuid(), Guid.NewGuid());
        await command.StartAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.AreEqual(1, factory.CreateCount);
    }

    [TestMethod]
    public async Task SameVersionAndEpochWithDifferentPackageIdentityIsRejectedAsEquivocation()
    {
        var metadataA = Metadata(version: 7, epoch: 3);
        var metadataB = Metadata(version: 7, epoch: 3);
        var provider = new SequenceProvider(
            Available(metadataA, PackageDigestA),
            Available(metadataB, PackageDigestB));
        var factory = new RecordingFactory(new RecordingCommand());
        var command = new ProvisionedRuntimeAuthStartCommand(provider, factory);

        await command.StartAsync(Guid.NewGuid(), Guid.NewGuid());
        var equivocation = await command.StartAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.AreEqual("Unavailable", equivocation.Disposition);
        Assert.AreEqual("AUTH_AUTHORITY_EQUIVOCATION_REJECTED", equivocation.ProductCode);
        Assert.AreEqual(1, factory.CreateCount);
    }

    [TestMethod]
    public async Task AvailableMetadataWithoutExactPackageIdentityIsRejectedBeforeFactory()
    {
        var provider = new SequenceProvider(new NativeAuthAuthorityPackageReadResult(
            NativeAuthAuthorityPackageStatus.Available,
            "AUTH_AUTHORITY_PACKAGE_AVAILABLE",
            Metadata(7, 3)));
        var factory = new RecordingFactory(new RecordingCommand());
        var command = new ProvisionedRuntimeAuthStartCommand(provider, factory);

        var result = await command.StartAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.AreEqual("AUTH_AUTHORITY_PACKAGE_REJECTED", result.ProductCode);
        Assert.AreEqual(0, factory.CreateCount);
    }

    [TestMethod]
    public async Task NewerVerifiedMetadataRebuildsCommand()
    {
        var provider = new SequenceProvider(
            Available(Metadata(version: 7, epoch: 3), PackageDigestA),
            Available(Metadata(version: 8, epoch: 3), PackageDigestB));
        var factory = new RecordingFactory(new RecordingCommand());
        var command = new ProvisionedRuntimeAuthStartCommand(provider, factory);

        await command.StartAsync(Guid.NewGuid(), Guid.NewGuid());
        await command.StartAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.AreEqual(2, factory.CreateCount);
    }

    [TestMethod]
    public async Task VersionRollbackIsRejectedEvenWhenSecurityEpochIncreases()
    {
        var provider = new SequenceProvider(
            Available(Metadata(version: 7, epoch: 3)),
            Available(Metadata(version: 6, epoch: 4)));
        var factory = new RecordingFactory(new RecordingCommand());
        var command = new ProvisionedRuntimeAuthStartCommand(provider, factory);

        await command.StartAsync(Guid.NewGuid(), Guid.NewGuid());
        var rollback = await command.StartAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.AreEqual("Unavailable", rollback.Disposition);
        Assert.AreEqual("AUTH_AUTHORITY_ROLLBACK_REJECTED", rollback.ProductCode);
        Assert.AreEqual(1, factory.CreateCount);
    }

    [TestMethod]
    public async Task SecurityEpochRollbackIsRejectedEvenWhenVersionIncreases()
    {
        var provider = new SequenceProvider(
            Available(Metadata(version: 7, epoch: 3)),
            Available(Metadata(version: 8, epoch: 2)));
        var factory = new RecordingFactory(new RecordingCommand());
        var command = new ProvisionedRuntimeAuthStartCommand(provider, factory);

        await command.StartAsync(Guid.NewGuid(), Guid.NewGuid());
        var rollback = await command.StartAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.AreEqual("AUTH_AUTHORITY_ROLLBACK_REJECTED", rollback.ProductCode);
        Assert.AreEqual(1, factory.CreateCount);
    }

    [TestMethod]
    public async Task InvalidEnvelopeIdsAreRejectedBeforeReadingProvisionedAuthority()
    {
        var provider = new SequenceProvider(Available(Metadata(7, 3)));
        var factory = new RecordingFactory(new RecordingCommand());
        var command = new ProvisionedRuntimeAuthStartCommand(provider, factory);

        var result = await command.StartAsync(Guid.Empty, Guid.NewGuid());

        Assert.AreEqual("AUTH_REQUEST_ID_INVALID", result.ProductCode);
        Assert.AreEqual(0, provider.ReadCount);
        Assert.AreEqual(0, factory.CreateCount);
    }

    private static VerifiedNativeAuthAuthorityMetadata Metadata(long version, long epoch)
    {
        var authority = new NativeAuthAuthorityConfiguration(
            new Uri("https://identity.splitos.test/"),
            new Uri("https://identity.splitos.test/.well-known/openid-configuration"),
            new Uri("https://identity.splitos.test/authorize"),
            new Uri("https://identity.splitos.test/token"),
            new Uri("https://identity.splitos.test/.well-known/jwks.json"),
            "splitos-native",
            new[] { "openid", "offline_access" },
            new[] { "RS256" },
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(10));
        return new VerifiedNativeAuthAuthorityMetadata(
            NativeAuthReleaseTrustConfiguration.ProductionTrustDomain,
            version,
            epoch,
            authority,
            ProductApiConfiguration.FromAuthority(new Uri("https://api.splitos.test/")));
    }

    private static NativeAuthAuthorityPackageReadResult Available(
        VerifiedNativeAuthAuthorityMetadata metadata,
        string packageSha256 = PackageDigestA)
        => new(
            NativeAuthAuthorityPackageStatus.Available,
            "AUTH_AUTHORITY_PACKAGE_AVAILABLE",
            metadata,
            packageSha256);

    private static NativeAuthAuthorityPackageReadResult Result(NativeAuthAuthorityPackageStatus status, string code)
        => new(status, code, null);

    private sealed class SequenceProvider(params NativeAuthAuthorityPackageReadResult[] results)
        : INativeAuthAuthorityPackageProvider
    {
        private int _index;
        public int ReadCount { get; private set; }

        public ValueTask<NativeAuthAuthorityPackageReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            var result = results[Math.Min(_index, results.Length - 1)];
            _index++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RecordingFactory(IRuntimeAuthStartCommand? command = null) : IRuntimeAuthStartCommandFactory
    {
        private readonly IRuntimeAuthStartCommand _command = command ?? new RecordingCommand();
        public int CreateCount { get; private set; }
        public VerifiedNativeAuthAuthorityMetadata? LastMetadata { get; private set; }

        public IRuntimeAuthStartCommand Create(VerifiedNativeAuthAuthorityMetadata metadata)
        {
            CreateCount++;
            LastMetadata = metadata;
            return _command;
        }
    }

    private sealed class RecordingCommand : IRuntimeAuthStartCommand
    {
        public Guid LastCorrelationId { get; private set; }
        public Guid LastOperationId { get; private set; }

        public Task<RuntimeAuthStartResult> StartAsync(
            Guid correlationId,
            Guid operationId,
            CancellationToken cancellationToken = default)
        {
            LastCorrelationId = correlationId;
            LastOperationId = operationId;
            return Task.FromResult(new RuntimeAuthStartResult(
                "Associated",
                "ACCOUNT_ASSOCIATED",
                Guid.NewGuid(),
                "account-1",
                "association-1",
                true));
        }
    }
}
