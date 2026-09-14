using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.GameRuntime;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class GameLibraryProjectionEdgeTests
{
    private static readonly DateTimeOffset RefreshAt = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void ExpiredEvidenceCannotArriveMarkedFresh()
    {
        var owner = new GameLibraryProjectionOwner();
        var binding = Binding(
            "game.expired",
            GameClientType.Steam,
            observedAt: RefreshAt.AddMinutes(-30),
            expiresAt: RefreshAt.AddMinutes(-1),
            freshness: GameEvidenceFreshness.Fresh);

        var result = owner.ApplyClientRefresh(new GameLibraryClientRefresh(
            GameClientType.Steam,
            1,
            RefreshAt,
            GameLibraryRefreshCompleteness.Full,
            [binding]));

        Assert.AreEqual(GameLibraryRefreshDisposition.Rejected, result.Disposition);
        Assert.AreEqual(GameLibraryRefreshReasonCodes.InvalidProjection, result.ReasonCode);
        Assert.AreEqual(0L, owner.Snapshot.Revision);
    }

    [TestMethod]
    public void EvidenceCannotBeObservedAfterEnclosingRefresh()
    {
        var owner = new GameLibraryProjectionOwner();
        var binding = Binding(
            "game.future",
            GameClientType.Steam,
            observedAt: RefreshAt.AddSeconds(1),
            expiresAt: RefreshAt.AddMinutes(15));

        var result = owner.ApplyClientRefresh(new GameLibraryClientRefresh(
            GameClientType.Steam,
            1,
            RefreshAt,
            GameLibraryRefreshCompleteness.Full,
            [binding]));

        Assert.AreEqual(GameLibraryRefreshDisposition.Rejected, result.Disposition);
        Assert.AreEqual(GameLibraryRefreshReasonCodes.InvalidProjection, result.ReasonCode);
        Assert.AreEqual(0, owner.Snapshot.Games.Count);
    }

    [TestMethod]
    public void InstallationMechanismStatusParticipatesInCardState()
    {
        var unsupportedOwner = new GameLibraryProjectionOwner();
        var unsupported = unsupportedOwner.ApplyClientRefresh(new GameLibraryClientRefresh(
            GameClientType.Steam,
            1,
            RefreshAt,
            GameLibraryRefreshCompleteness.Full,
            [Binding(
                "game.unsupported",
                GameClientType.Steam,
                installationMechanism: GameMechanismStatus.Unsupported)]));
        Assert.AreEqual(
            GameLibraryCardState.UnsupportedClientCapability,
            unsupported.Snapshot.Games[0].CardState);

        var openOwner = new GameLibraryProjectionOwner();
        var open = openOwner.ApplyClientRefresh(new GameLibraryClientRefresh(
            GameClientType.Steam,
            1,
            RefreshAt,
            GameLibraryRefreshCompleteness.Full,
            [Binding(
                "game.open",
                GameClientType.Steam,
                installationMechanism: GameMechanismStatus.Open)]));
        Assert.AreEqual(GameLibraryCardState.StaleOrUnknown, open.Snapshot.Games[0].CardState);
    }

    [TestMethod]
    public async Task ConcurrentIndependentClientRefreshesProduceOneCoherentSnapshot()
    {
        for (var iteration = 0; iteration < 20; iteration++)
        {
            var owner = new GameLibraryProjectionOwner();
            var steam = new GameLibraryClientRefresh(
                GameClientType.Steam,
                1,
                RefreshAt,
                GameLibraryRefreshCompleteness.Full,
                [Binding("game.steam", GameClientType.Steam)]);
            var epic = new GameLibraryClientRefresh(
                GameClientType.Epic,
                1,
                RefreshAt,
                GameLibraryRefreshCompleteness.Full,
                [Binding("game.epic", GameClientType.Epic)]);

            await Task.WhenAll(
                Task.Run(() => owner.ApplyClientRefresh(steam)),
                Task.Run(() => owner.ApplyClientRefresh(epic)));

            Assert.AreEqual(2L, owner.Snapshot.Revision);
            Assert.AreEqual(2, owner.Snapshot.Games.Count);
            CollectionAssert.AreEquivalent(
                new[] { "game.steam", "game.epic" },
                owner.Snapshot.Games.Select(game => game.GameId).ToArray());
        }
    }

    [TestMethod]
    public void PublishedSnapshotCollectionsAreReadOnly()
    {
        var owner = new GameLibraryProjectionOwner();
        _ = owner.ApplyClientRefresh(new GameLibraryClientRefresh(
            GameClientType.Steam,
            1,
            RefreshAt,
            GameLibraryRefreshCompleteness.Full,
            [Binding("game.readonly", GameClientType.Steam, secondaryIds: ["legacy-id"])]));

        Assert.IsTrue(((IList<NormalizedGameLibraryEntry>)owner.Snapshot.Games).IsReadOnly);
        Assert.IsTrue(((IList<NormalizedGameClientBinding>)owner.Snapshot.Games[0].ClientBindings).IsReadOnly);
        Assert.IsTrue(((IList<string>)owner.Snapshot.Games[0].ClientBindings[0].ExternalIdentity.SecondaryIds!).IsReadOnly);
    }

    private static GameLibraryBindingProjection Binding(
        string gameId,
        GameClientType clientType,
        DateTimeOffset? observedAt = null,
        DateTimeOffset? expiresAt = null,
        GameEvidenceFreshness freshness = GameEvidenceFreshness.Fresh,
        GameMechanismStatus installationMechanism = GameMechanismStatus.BestEffortLocalEvidence,
        IReadOnlyList<string>? secondaryIds = null)
    {
        var observed = observedAt ?? RefreshAt;
        return new GameLibraryBindingProjection(
            gameId,
            new ExternalGameIdentity(
                clientType,
                clientType == GameClientType.Steam ? "APP_ID" : "CATALOG_ARTIFACT",
                gameId,
                secondaryIds),
            new GameInstallationEvidence(
                GameInstallState.InstalledVerifiedEvidence,
                null,
                observed,
                expiresAt ?? observed.AddMinutes(15),
                freshness,
                GameEvidenceConfidence.High,
                installationMechanism,
                $"source:{gameId}"),
            GameLaunchIdentityAvailability.Available,
            GameMechanismStatus.SupportedPublic,
            GameClientSupportStatus.TargetSupportedV1,
            gameId,
            new GameLibrarySourceProvenance(
                "LOCAL_METADATA",
                "adapter/1.0",
                "schema/1",
                "client/1.0"));
    }
}
