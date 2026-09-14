using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.GameRuntime;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class GameLibraryProjectionOwnerTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 9, 13, 10, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void FreshVerifiedInstalledBindingWithLaunchIdentityProjectsReady()
    {
        var owner = new GameLibraryProjectionOwner();

        var decision = owner.ApplyClientRefresh(Refresh(
            GameClientType.Steam,
            generation: 1,
            GameLibraryRefreshCompleteness.Full,
            Binding(
                "game.cyberpunk",
                GameClientType.Steam,
                "APP_ID",
                "1091500",
                GameInstallState.InstalledVerifiedEvidence,
                GameEvidenceFreshness.Fresh,
                GameEvidenceConfidence.High,
                GameLaunchIdentityAvailability.Available,
                GameMechanismStatus.SupportedPublic,
                displayName: "Cyberpunk 2077")));

        Assert.AreEqual(GameLibraryRefreshDisposition.Applied, decision.Disposition);
        Assert.AreEqual(1L, decision.Snapshot.Revision);
        Assert.AreEqual(1, decision.Snapshot.Games.Count);
        Assert.AreEqual(GameLibraryCardState.Ready, decision.Snapshot.Games[0].CardState);
        Assert.AreEqual("Cyberpunk 2077", decision.Snapshot.Games[0].PreferredDisplayNameEvidence);
        Assert.AreEqual("LOCAL_METADATA", decision.Snapshot.Games[0].ClientBindings[0].SourceProvenance.SourceMechanism);
        Assert.AreEqual("adapter/1.0", decision.Snapshot.Games[0].ClientBindings[0].SourceProvenance.AdapterVersion);
    }

    [TestMethod]
    public void StaleOrLowConfidenceEvidenceCannotBePromotedToReady()
    {
        var staleOwner = new GameLibraryProjectionOwner();
        var stale = staleOwner.ApplyClientRefresh(Refresh(
            GameClientType.Steam,
            1,
            GameLibraryRefreshCompleteness.Full,
            Binding(
                "game.one",
                GameClientType.Steam,
                "APP_ID",
                "1",
                GameInstallState.StaleLastKnown,
                GameEvidenceFreshness.Stale,
                GameEvidenceConfidence.High,
                GameLaunchIdentityAvailability.Available,
                GameMechanismStatus.SupportedPublic)));
        Assert.AreEqual(GameLibraryCardState.StaleOrUnknown, stale.Snapshot.Games[0].CardState);

        var lowOwner = new GameLibraryProjectionOwner();
        var low = lowOwner.ApplyClientRefresh(Refresh(
            GameClientType.Epic,
            1,
            GameLibraryRefreshCompleteness.Full,
            Binding(
                "game.two",
                GameClientType.Epic,
                "CATALOG_ARTIFACT",
                "artifact-2",
                GameInstallState.InstalledVerifiedEvidence,
                GameEvidenceFreshness.Fresh,
                GameEvidenceConfidence.Low,
                GameLaunchIdentityAvailability.Available,
                GameMechanismStatus.SupportedPublic)));
        Assert.AreEqual(GameLibraryCardState.StaleOrUnknown, low.Snapshot.Games[0].CardState);
    }

    [TestMethod]
    public void VerifiedNotInstalledAndUserMediatedStatesRemainDistinct()
    {
        var notInstalledOwner = new GameLibraryProjectionOwner();
        var notInstalled = notInstalledOwner.ApplyClientRefresh(Refresh(
            GameClientType.Steam,
            1,
            GameLibraryRefreshCompleteness.Full,
            Binding(
                "game.missing",
                GameClientType.Steam,
                "APP_ID",
                "10",
                GameInstallState.NotInstalledVerifiedEvidence,
                GameEvidenceFreshness.Fresh,
                GameEvidenceConfidence.High,
                GameLaunchIdentityAvailability.Unknown,
                GameMechanismStatus.SupportedPublic)));
        Assert.AreEqual(GameLibraryCardState.NotInstalledVerified, notInstalled.Snapshot.Games[0].CardState);

        var actionOwner = new GameLibraryProjectionOwner();
        var action = actionOwner.ApplyClientRefresh(Refresh(
            GameClientType.BattleNet,
            1,
            GameLibraryRefreshCompleteness.Full,
            Binding(
                "game.action",
                GameClientType.BattleNet,
                "PRODUCT_CODE",
                "product",
                GameInstallState.InstalledVerifiedEvidence,
                GameEvidenceFreshness.Fresh,
                GameEvidenceConfidence.High,
                GameLaunchIdentityAvailability.Available,
                GameMechanismStatus.UserMediated,
                supportStatus: GameClientSupportStatus.Experimental)));
        Assert.AreEqual(GameLibraryCardState.ClientActionRequired, action.Snapshot.Games[0].CardState);
    }

    [TestMethod]
    public void PartialRefreshRetainsOmittedBindingAsStaleLastKnown()
    {
        var owner = new GameLibraryProjectionOwner();
        _ = owner.ApplyClientRefresh(Refresh(
            GameClientType.Steam,
            1,
            GameLibraryRefreshCompleteness.Full,
            Binding("game.one", GameClientType.Steam, "APP_ID", "1"),
            Binding("game.two", GameClientType.Steam, "APP_ID", "2")));

        var partial = owner.ApplyClientRefresh(Refresh(
            GameClientType.Steam,
            2,
            GameLibraryRefreshCompleteness.Partial,
            Binding("game.one", GameClientType.Steam, "APP_ID", "1", displayName: "One refreshed")));

        Assert.AreEqual(GameLibraryRefreshDisposition.Applied, partial.Disposition);
        Assert.AreEqual(2, partial.Snapshot.Games.Count);
        var retained = partial.Snapshot.Games.Single(game => game.GameId == "game.two");
        Assert.AreEqual(GameInstallState.StaleLastKnown, retained.ClientBindings[0].Installation.State);
        Assert.AreEqual(GameEvidenceFreshness.Stale, retained.ClientBindings[0].Installation.Freshness);
        Assert.AreEqual(GameLibraryCardState.StaleOrUnknown, retained.CardState);
    }

    [TestMethod]
    public void FullRefreshRemovesOmittedClientBinding()
    {
        var owner = new GameLibraryProjectionOwner();
        _ = owner.ApplyClientRefresh(Refresh(
            GameClientType.Steam,
            1,
            GameLibraryRefreshCompleteness.Full,
            Binding("game.one", GameClientType.Steam, "APP_ID", "1"),
            Binding("game.two", GameClientType.Steam, "APP_ID", "2")));

        var full = owner.ApplyClientRefresh(Refresh(
            GameClientType.Steam,
            2,
            GameLibraryRefreshCompleteness.Full,
            Binding("game.one", GameClientType.Steam, "APP_ID", "1")));

        Assert.AreEqual(1, full.Snapshot.Games.Count);
        Assert.AreEqual("game.one", full.Snapshot.Games[0].GameId);
    }

    [TestMethod]
    public void SameCanonicalGameCanCarryIndependentClientBindings()
    {
        var owner = new GameLibraryProjectionOwner();
        _ = owner.ApplyClientRefresh(Refresh(
            GameClientType.Steam,
            1,
            GameLibraryRefreshCompleteness.Full,
            Binding("game.shared", GameClientType.Steam, "APP_ID", "42", displayName: "Shared Game")));

        var epic = owner.ApplyClientRefresh(Refresh(
            GameClientType.Epic,
            1,
            GameLibraryRefreshCompleteness.Full,
            Binding("game.shared", GameClientType.Epic, "CATALOG_ARTIFACT", "epic-42", displayName: "Shared Game Epic")));

        Assert.AreEqual(1, epic.Snapshot.Games.Count);
        Assert.AreEqual(2, epic.Snapshot.Games[0].ClientBindings.Count);
        CollectionAssert.AreEquivalent(
            new[] { GameClientType.Steam, GameClientType.Epic },
            epic.Snapshot.Games[0].ClientBindings.Select(binding => binding.ClientType).ToArray());
    }

    [TestMethod]
    public void MatchingDisplayTitleOrInstallRootNeverMergesCanonicalGames()
    {
        var owner = new GameLibraryProjectionOwner();
        var first = Binding(
            "canonical.a",
            GameClientType.Steam,
            "APP_ID",
            "100",
            displayName: "Same Title",
            installRoot: @"C:\Games\Same");
        var second = Binding(
            "canonical.b",
            GameClientType.Steam,
            "APP_ID",
            "200",
            displayName: "Same Title",
            installRoot: @"C:\Games\Same");

        var decision = owner.ApplyClientRefresh(Refresh(
            GameClientType.Steam,
            1,
            GameLibraryRefreshCompleteness.Full,
            first,
            second));

        Assert.AreEqual(2, decision.Snapshot.Games.Count);
        CollectionAssert.AreEquivalent(
            new[] { "canonical.a", "canonical.b" },
            decision.Snapshot.Games.Select(game => game.GameId).ToArray());
    }

    [TestMethod]
    public void ExternalIdentityCannotSilentlyMoveToDifferentCanonicalGame()
    {
        var owner = new GameLibraryProjectionOwner();
        _ = owner.ApplyClientRefresh(Refresh(
            GameClientType.Steam,
            1,
            GameLibraryRefreshCompleteness.Full,
            Binding("canonical.original", GameClientType.Steam, "APP_ID", "100")));
        var revision = owner.Snapshot.Revision;

        var conflict = owner.ApplyClientRefresh(Refresh(
            GameClientType.Steam,
            2,
            GameLibraryRefreshCompleteness.Full,
            Binding("canonical.other", GameClientType.Steam, "APP_ID", "100")));

        Assert.AreEqual(GameLibraryRefreshDisposition.Rejected, conflict.Disposition);
        Assert.AreEqual(GameLibraryRefreshReasonCodes.ExternalIdentityConflict, conflict.ReasonCode);
        Assert.AreEqual(revision, owner.Snapshot.Revision);
        Assert.AreEqual("canonical.original", owner.Snapshot.Games[0].GameId);
    }

    [TestMethod]
    public void GenerationOrderingIsIdempotentAndFailClosedOnConflict()
    {
        var owner = new GameLibraryProjectionOwner();
        var firstRefresh = Refresh(
            GameClientType.Steam,
            5,
            GameLibraryRefreshCompleteness.Full,
            Binding("game.one", GameClientType.Steam, "APP_ID", "1"));
        var first = owner.ApplyClientRefresh(firstRefresh);
        var revision = first.Snapshot.Revision;

        var replay = owner.ApplyClientRefresh(firstRefresh);
        Assert.AreEqual(GameLibraryRefreshDisposition.NoOp, replay.Disposition);
        Assert.AreEqual(GameLibraryRefreshReasonCodes.IdempotentGeneration, replay.ReasonCode);
        Assert.AreEqual(revision, owner.Snapshot.Revision);

        var stale = owner.ApplyClientRefresh(Refresh(
            GameClientType.Steam,
            4,
            GameLibraryRefreshCompleteness.Full,
            Binding("game.older", GameClientType.Steam, "APP_ID", "4")));
        Assert.AreEqual(GameLibraryRefreshReasonCodes.StaleGeneration, stale.ReasonCode);
        Assert.AreEqual(revision, owner.Snapshot.Revision);

        var conflictingSameGeneration = owner.ApplyClientRefresh(Refresh(
            GameClientType.Steam,
            5,
            GameLibraryRefreshCompleteness.Full,
            Binding("game.conflict", GameClientType.Steam, "APP_ID", "5")));
        Assert.AreEqual(GameLibraryRefreshDisposition.Rejected, conflictingSameGeneration.Disposition);
        Assert.AreEqual(GameLibraryRefreshReasonCodes.GenerationConflict, conflictingSameGeneration.ReasonCode);
        Assert.AreEqual(revision, owner.Snapshot.Revision);
    }

    [TestMethod]
    public void InvalidClientMismatchContradictoryInstallAndMissingProvenanceAreRejectedWithoutMutation()
    {
        var owner = new GameLibraryProjectionOwner();
        var mismatch = owner.ApplyClientRefresh(Refresh(
            GameClientType.Steam,
            1,
            GameLibraryRefreshCompleteness.Full,
            Binding("game.one", GameClientType.Epic, "CATALOG", "epic")));
        Assert.AreEqual(GameLibraryRefreshDisposition.Rejected, mismatch.Disposition);
        Assert.AreEqual(GameLibraryRefreshReasonCodes.InvalidProjection, mismatch.ReasonCode);
        Assert.AreEqual(0L, owner.Snapshot.Revision);

        var invalidInstall = Binding(
            "game.two",
            GameClientType.Steam,
            "APP_ID",
            "2",
            state: GameInstallState.NotInstalledVerifiedEvidence,
            installRoot: @"C:\Games\ShouldNotExist");
        var invalid = owner.ApplyClientRefresh(Refresh(
            GameClientType.Steam,
            2,
            GameLibraryRefreshCompleteness.Full,
            invalidInstall));
        Assert.AreEqual(GameLibraryRefreshDisposition.Rejected, invalid.Disposition);
        Assert.AreEqual(GameLibraryRefreshReasonCodes.InvalidProjection, invalid.ReasonCode);
        Assert.AreEqual(0L, owner.Snapshot.Revision);

        var missingProvenance = new GameLibraryBindingProjection(
            "game.three",
            new ExternalGameIdentity(GameClientType.Steam, "APP_ID", "3"),
            Evidence(GameInstallState.InstalledVerifiedEvidence, null, GameEvidenceFreshness.Fresh, GameEvidenceConfidence.High, "3"),
            GameLaunchIdentityAvailability.Available,
            GameMechanismStatus.SupportedPublic,
            GameClientSupportStatus.TargetSupportedV1);
        var missing = owner.ApplyClientRefresh(Refresh(
            GameClientType.Steam,
            3,
            GameLibraryRefreshCompleteness.Full,
            missingProvenance));
        Assert.AreEqual(GameLibraryRefreshDisposition.Rejected, missing.Disposition);
        Assert.AreEqual(GameLibraryRefreshReasonCodes.InvalidProjection, missing.ReasonCode);
        Assert.AreEqual(0L, owner.Snapshot.Revision);
    }

    [TestMethod]
    public void AggregateStatePrefersReadyBindingButDoesNotHideUncertaintyWhenNoneReady()
    {
        var owner = new GameLibraryProjectionOwner();
        _ = owner.ApplyClientRefresh(Refresh(
            GameClientType.Steam,
            1,
            GameLibraryRefreshCompleteness.Full,
            Binding("game.multi", GameClientType.Steam, "APP_ID", "1")));
        var ready = owner.ApplyClientRefresh(Refresh(
            GameClientType.Epic,
            1,
            GameLibraryRefreshCompleteness.Full,
            Binding(
                "game.multi",
                GameClientType.Epic,
                "CATALOG",
                "e1",
                state: GameInstallState.StaleLastKnown,
                freshness: GameEvidenceFreshness.Stale)));
        Assert.AreEqual(GameLibraryCardState.Ready, ready.Snapshot.Games[0].CardState);

        var noReadyOwner = new GameLibraryProjectionOwner();
        _ = noReadyOwner.ApplyClientRefresh(Refresh(
            GameClientType.Steam,
            1,
            GameLibraryRefreshCompleteness.Full,
            Binding(
                "game.multi",
                GameClientType.Steam,
                "APP_ID",
                "1",
                state: GameInstallState.NotInstalledVerifiedEvidence,
                launchIdentity: GameLaunchIdentityAvailability.Unavailable)));
        var uncertain = noReadyOwner.ApplyClientRefresh(Refresh(
            GameClientType.Epic,
            1,
            GameLibraryRefreshCompleteness.Full,
            Binding(
                "game.multi",
                GameClientType.Epic,
                "CATALOG",
                "e1",
                state: GameInstallState.Unknown,
                freshness: GameEvidenceFreshness.Unknown,
                launchIdentity: GameLaunchIdentityAvailability.Unknown)));
        Assert.AreEqual(GameLibraryCardState.StaleOrUnknown, uncertain.Snapshot.Games[0].CardState);
    }

    [TestMethod]
    public void PreferredDisplayNameIsEvidenceAndUsesFreshThenHigherConfidenceSource()
    {
        var owner = new GameLibraryProjectionOwner();
        _ = owner.ApplyClientRefresh(Refresh(
            GameClientType.Steam,
            1,
            GameLibraryRefreshCompleteness.Full,
            Binding(
                "game.title",
                GameClientType.Steam,
                "APP_ID",
                "1",
                freshness: GameEvidenceFreshness.Stale,
                state: GameInstallState.StaleLastKnown,
                confidence: GameEvidenceConfidence.High,
                displayName: "Old Title")));

        _ = owner.ApplyClientRefresh(Refresh(
            GameClientType.Epic,
            1,
            GameLibraryRefreshCompleteness.Full,
            Binding(
                "game.title",
                GameClientType.Epic,
                "CATALOG",
                "e1",
                freshness: GameEvidenceFreshness.Fresh,
                confidence: GameEvidenceConfidence.Medium,
                displayName: "Fresh Medium")));

        var decision = owner.ApplyClientRefresh(Refresh(
            GameClientType.MicrosoftGaming,
            1,
            GameLibraryRefreshCompleteness.Full,
            Binding(
                "game.title",
                GameClientType.MicrosoftGaming,
                "AUMID",
                "pkg!app",
                freshness: GameEvidenceFreshness.Fresh,
                confidence: GameEvidenceConfidence.High,
                displayName: "Fresh High")));

        Assert.AreEqual("Fresh High", decision.Snapshot.Games[0].PreferredDisplayNameEvidence);
    }

    [TestMethod]
    public void SecondaryIdentityAndProvenanceChangesParticipateInSemanticRevision()
    {
        var owner = new GameLibraryProjectionOwner();
        var first = owner.ApplyClientRefresh(new GameLibraryClientRefresh(
            GameClientType.Steam,
            1,
            ObservedAt,
            GameLibraryRefreshCompleteness.Full,
            [BindingWithSecondaryIds("game.one", "1", ["legacy-a"], clientVersion: "client/1.0")]));
        var revision = first.Snapshot.Revision;

        var second = owner.ApplyClientRefresh(new GameLibraryClientRefresh(
            GameClientType.Steam,
            2,
            ObservedAt.AddMinutes(1),
            GameLibraryRefreshCompleteness.Full,
            [BindingWithSecondaryIds("game.one", "1", ["legacy-b"], clientVersion: "client/2.0")]));

        Assert.AreEqual(GameLibraryRefreshDisposition.Applied, second.Disposition);
        Assert.IsTrue(second.Snapshot.Revision > revision);
        CollectionAssert.AreEqual(
            new[] { "legacy-b" },
            second.Snapshot.Games[0].ClientBindings[0].ExternalIdentity.SecondaryIds!.ToArray());
        Assert.AreEqual("client/2.0", second.Snapshot.Games[0].ClientBindings[0].SourceProvenance.ClientVersionObserved);
    }

    [TestMethod]
    public void TryGetGameUsesOnlyCanonicalGameId()
    {
        var owner = new GameLibraryProjectionOwner();
        _ = owner.ApplyClientRefresh(Refresh(
            GameClientType.Steam,
            1,
            GameLibraryRefreshCompleteness.Full,
            Binding("canonical.game", GameClientType.Steam, "APP_ID", "777", displayName: "Display Title")));

        Assert.IsTrue(owner.TryGetGame("canonical.game", out var game));
        Assert.IsNotNull(game);
        Assert.AreEqual("canonical.game", game.GameId);
        Assert.IsFalse(owner.TryGetGame("777", out _));
        Assert.IsFalse(owner.TryGetGame("Display Title", out _));
    }

    private static GameLibraryClientRefresh Refresh(
        GameClientType clientType,
        long generation,
        GameLibraryRefreshCompleteness completeness,
        params GameLibraryBindingProjection[] records)
        => new(clientType, generation, ObservedAt, completeness, records);

    private static GameLibraryBindingProjection BindingWithSecondaryIds(
        string gameId,
        string externalId,
        IReadOnlyList<string> secondaryIds,
        string clientVersion)
        => new(
            gameId,
            new ExternalGameIdentity(GameClientType.Steam, "APP_ID", externalId, secondaryIds),
            Evidence(GameInstallState.InstalledVerifiedEvidence, null, GameEvidenceFreshness.Fresh, GameEvidenceConfidence.High, externalId),
            GameLaunchIdentityAvailability.Available,
            GameMechanismStatus.SupportedPublic,
            GameClientSupportStatus.TargetSupportedV1,
            null,
            Provenance(clientVersion));

    private static GameLibraryBindingProjection Binding(
        string gameId,
        GameClientType clientType,
        string externalIdKind,
        string externalId,
        GameInstallState state = GameInstallState.InstalledVerifiedEvidence,
        GameEvidenceFreshness freshness = GameEvidenceFreshness.Fresh,
        GameEvidenceConfidence confidence = GameEvidenceConfidence.High,
        GameLaunchIdentityAvailability launchIdentity = GameLaunchIdentityAvailability.Available,
        GameMechanismStatus launchMechanism = GameMechanismStatus.SupportedPublic,
        GameClientSupportStatus supportStatus = GameClientSupportStatus.TargetSupportedV1,
        string? displayName = null,
        string? installRoot = null)
        => new(
            gameId,
            new ExternalGameIdentity(clientType, externalIdKind, externalId),
            Evidence(state, installRoot, freshness, confidence, externalId),
            launchIdentity,
            launchMechanism,
            supportStatus,
            displayName,
            Provenance("client/1.0"));

    private static GameLibrarySourceProvenance Provenance(string clientVersion)
        => new("LOCAL_METADATA", "adapter/1.0", "schema/1", clientVersion);

    private static GameInstallationEvidence Evidence(
        GameInstallState state,
        string? installRoot,
        GameEvidenceFreshness freshness,
        GameEvidenceConfidence confidence,
        string sourceId)
        => new(
            state,
            installRoot,
            ObservedAt,
            ObservedAt.AddMinutes(15),
            freshness,
            confidence,
            GameMechanismStatus.BestEffortLocalEvidence,
            $"source:{sourceId}");
}
