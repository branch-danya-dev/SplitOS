using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.ModeRuntime;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class ModeBlockerEngineTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 7, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task RuntimeAccessDenialHardBlocksManagedActivation()
    {
        var engine = new ModeBlockerEngine(
            new IModeBlockerProvider[] { new RuntimeAccessBlockerProvider() },
            new FixedTimeProvider(Now));

        var outcome = await engine.InspectAsync(Context(
            ModeOperationKind.Activate,
            OperationalMode.None,
            OperationalMode.Game,
            new RuntimeAccessEvaluation("DISABLED", "FREE_ENTITLEMENT", 7)));

        Assert.AreEqual(BlockerInspectionDisposition.Blocked, outcome.Disposition);
        Assert.AreEqual(1, outcome.Observations.Count);
        Assert.AreEqual(BlockerClass.HardBlock, outcome.Observations[0].BlockerClass);
        Assert.AreEqual("MANAGED_RUNTIME_ACCESS_REQUIRED", outcome.Observations[0].BlockerCode);
        Assert.AreEqual(RuntimeAccessBlockerProvider.Id, outcome.Observations[0].ProviderId);
    }

    [TestMethod]
    public async Task DeactivateToNoneRemainsAvailableAfterPremiumAuthorityLoss()
    {
        var engine = new ModeBlockerEngine(
            new IModeBlockerProvider[] { new RuntimeAccessBlockerProvider() },
            new FixedTimeProvider(Now));

        var outcome = await engine.InspectAsync(Context(
            ModeOperationKind.Deactivate,
            OperationalMode.Game,
            OperationalMode.None,
            new RuntimeAccessEvaluation("DISABLED", "ENTITLEMENT_EXPIRED", 9)));

        Assert.AreEqual(BlockerInspectionDisposition.Resolving, outcome.Disposition);
        Assert.AreEqual(0, outcome.Observations.Count);
    }

    [TestMethod]
    public async Task HardBlockTakesPrecedenceOverUserDecisionAndAutoResolution()
    {
        var engine = new ModeBlockerEngine(
            new IModeBlockerProvider[]
            {
                new StaticProvider("AutoProvider", Observation("AutoProvider", "AUTO", BlockerClass.AutoResolvable)),
                new StaticProvider("DecisionProvider", DecisionObservation("DecisionProvider")),
                new StaticProvider("HardProvider", Observation("HardProvider", "HARD", BlockerClass.HardBlock))
            },
            new FixedTimeProvider(Now));

        var outcome = await engine.InspectAsync(EnabledContext());

        Assert.AreEqual(BlockerInspectionDisposition.Blocked, outcome.Disposition);
        Assert.AreEqual(BlockerClass.HardBlock, outcome.Observations[0].BlockerClass);
        Assert.AreEqual(3, outcome.Observations.Count);
    }

    [TestMethod]
    public async Task UserDecisionTakesPrecedenceOverAutoResolvableEvidence()
    {
        var engine = new ModeBlockerEngine(
            new IModeBlockerProvider[]
            {
                new StaticProvider("AutoProvider", Observation("AutoProvider", "AUTO", BlockerClass.AutoResolvable)),
                new StaticProvider("DecisionProvider", DecisionObservation("DecisionProvider"))
            },
            new FixedTimeProvider(Now));

        var outcome = await engine.InspectAsync(EnabledContext());

        Assert.AreEqual(BlockerInspectionDisposition.AwaitingUser, outcome.Disposition);
        Assert.AreEqual(BlockerClass.UserDecisionRequired, outcome.Observations[0].BlockerClass);
    }

    [TestMethod]
    public async Task ProviderUnavailableFailsInspectionWithoutFabricatingBlockerEvidence()
    {
        var engine = new ModeBlockerEngine(
            new IModeBlockerProvider[] { new UnavailableProvider() },
            new FixedTimeProvider(Now));

        var outcome = await engine.InspectAsync(EnabledContext());

        Assert.AreEqual(BlockerInspectionDisposition.InspectionFailed, outcome.Disposition);
        Assert.AreEqual("UnavailableProvider", outcome.FailedProviderId);
        Assert.AreEqual("PROCESS_EVIDENCE_UNAVAILABLE", outcome.ProductCode);
        Assert.AreEqual(0, outcome.Observations.Count);
    }

    [TestMethod]
    public async Task ProviderIdentityMismatchFailsClosedAsContractViolation()
    {
        var bad = Observation("AnotherProvider", "MISMATCH", BlockerClass.NonBlocking);
        var engine = new ModeBlockerEngine(
            new IModeBlockerProvider[] { new StaticProvider("RegisteredProvider", bad) },
            new FixedTimeProvider(Now));

        var outcome = await engine.InspectAsync(EnabledContext());

        Assert.AreEqual(BlockerInspectionDisposition.InspectionFailed, outcome.Disposition);
        Assert.AreEqual("RegisteredProvider", outcome.FailedProviderId);
        Assert.AreEqual("BLOCKER_PROVIDER_CONTRACT_VIOLATION", outcome.ProductCode);
    }

    [TestMethod]
    public async Task DuplicateBlockerIdsAcrossProvidersFailClosed()
    {
        var blockerId = Guid.NewGuid();
        var first = Observation("ProviderA", "ONE", BlockerClass.NonBlocking) with { BlockerId = blockerId };
        var second = Observation("ProviderB", "TWO", BlockerClass.AutoResolvable) with { BlockerId = blockerId };
        var engine = new ModeBlockerEngine(
            new IModeBlockerProvider[]
            {
                new StaticProvider("ProviderA", first),
                new StaticProvider("ProviderB", second)
            },
            new FixedTimeProvider(Now));

        var outcome = await engine.InspectAsync(EnabledContext());

        Assert.AreEqual(BlockerInspectionDisposition.InspectionFailed, outcome.Disposition);
        Assert.AreEqual("ProviderB", outcome.FailedProviderId);
        Assert.AreEqual("BLOCKER_DUPLICATE_ID", outcome.ProductCode);
    }

    [TestMethod]
    public async Task UserDecisionRequiresVersionedClosedOptionSet()
    {
        var invalid = Observation("DecisionProvider", "DECIDE", BlockerClass.UserDecisionRequired);
        var engine = new ModeBlockerEngine(
            new IModeBlockerProvider[] { new StaticProvider("DecisionProvider", invalid) },
            new FixedTimeProvider(Now));

        var outcome = await engine.InspectAsync(EnabledContext());

        Assert.AreEqual(BlockerInspectionDisposition.InspectionFailed, outcome.Disposition);
        Assert.AreEqual("BLOCKER_PROVIDER_CONTRACT_VIOLATION", outcome.ProductCode);
    }

    private static ModeBlockerInspectionContext EnabledContext()
        => Context(
            ModeOperationKind.Activate,
            OperationalMode.None,
            OperationalMode.Work,
            new RuntimeAccessEvaluation("ENABLED", "PRO_ONLINE_CONFIRMED", 12));

    private static ModeBlockerInspectionContext Context(
        ModeOperationKind kind,
        OperationalMode source,
        OperationalMode target,
        RuntimeAccessEvaluation access)
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            kind,
            source,
            target,
            "console:S1:L100",
            access,
            Now,
            Now);

    private static BlockerObservation Observation(
        string providerId,
        string code,
        BlockerClass blockerClass)
        => new(
            Guid.NewGuid(),
            providerId,
            code,
            blockerClass,
            "subject:test",
            $"mode.blocker.{code.ToLowerInvariant()}",
            Now,
            Digest($"{providerId}:{code}"),
            null,
            null,
            Array.Empty<BlockerDecisionOption>());

    private static BlockerObservation DecisionObservation(string providerId)
        => Observation(providerId, "DECIDE", BlockerClass.UserDecisionRequired) with
        {
            DecisionSchemaVersion = 1,
            DecisionOptions = new[]
            {
                new BlockerDecisionOption("CONTINUE", "mode.decision.continue"),
                new BlockerDecisionOption("CANCEL", "mode.decision.cancel")
            }
        };

    private static string Digest(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class StaticProvider(string id, params BlockerObservation[] observations) : IModeBlockerProvider
    {
        public string ProviderId => id;
        public string ProviderVersion => "1";

        public ValueTask<BlockerProviderInspectionResult> InspectAsync(
            ModeBlockerInspectionContext context,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(BlockerProviderInspectionResult.Available(observations));
    }

    private sealed class UnavailableProvider : IModeBlockerProvider
    {
        public string ProviderId => "UnavailableProvider";
        public string ProviderVersion => "1";

        public ValueTask<BlockerProviderInspectionResult> InspectAsync(
            ModeBlockerInspectionContext context,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(BlockerProviderInspectionResult.Unavailable("PROCESS_EVIDENCE_UNAVAILABLE"));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
