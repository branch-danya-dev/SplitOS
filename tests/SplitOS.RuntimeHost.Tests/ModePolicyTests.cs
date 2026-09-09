using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.ModeRuntime;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class ModePolicyTests
{
    [TestMethod]
    public void CatalogDigestIsDeterministicAcrossInputOrdering()
    {
        var first = ModePolicyCatalog.Create(
            "catalog.v1",
            7,
            "release.2026-09",
            new[] { BaseTarget(), WorkTarget(), GameTarget() });
        var second = ModePolicyCatalog.Create(
            "catalog.v1",
            7,
            "release.2026-09",
            new[]
            {
                ReverseRules(GameTarget()),
                ReverseRules(BaseTarget()),
                ReverseRules(WorkTarget())
            });

        Assert.AreEqual(first.ContentDigest, second.ContentDigest);
    }

    [TestMethod]
    public void ResolverMapsOperationalNoneToInternalBaseTarget()
    {
        var catalog = Catalog();
        var resolver = new ModePolicyResolver();

        var resolved = resolver.Resolve(catalog, OperationalMode.None, "release.2026-09");

        Assert.AreEqual(ModePolicyTarget.Base, resolved.Target);
        Assert.AreEqual(catalog.ContentDigest, resolved.Identity.CatalogDigest);
        Assert.IsFalse(string.IsNullOrWhiteSpace(resolved.ResolvedDigest));
    }

    [TestMethod]
    public void CatalogMustContainBaseWorkAndGameExactlyOnce()
    {
        AssertThrows<InvalidDataException>(() => ModePolicyCatalog.Create(
            "catalog.v1",
            1,
            "release.2026-09",
            new[] { BaseTarget(), WorkTarget() }));
    }

    [TestMethod]
    public void RawWindowsPathCannotBecomePolicySubjectIdentity()
    {
        var game = GameTarget();
        var unsafeRule = new ModePolicyRule(
            "game.component.unsafe",
            ModePolicyDomain.ManagedComponent,
            @"C:\Windows\System32\cmd.exe",
            ModePolicyIntent.ManagedInactive,
            ModePolicyRequirement.Mandatory,
            ModePolicyFallback.None);

        AssertThrows<ArgumentException>(() => ModePolicyCatalog.Create(
            "catalog.v1",
            1,
            "release.2026-09",
            new[]
            {
                BaseTarget(),
                WorkTarget(),
                game with { Rules = game.Rules.Concat(new[] { unsafeRule }).ToArray() }
            }));
    }

    [TestMethod]
    public void UnknownVerificationSemanticIdIsRejected()
    {
        var baseTarget = BaseTarget();
        var unknown = VerificationRule("base.verify.unknown", "RUN_CUSTOM_SCRIPT");

        AssertThrows<InvalidDataException>(() => ModePolicyCatalog.Create(
            "catalog.v1",
            1,
            "release.2026-09",
            new[]
            {
                baseTarget with { Rules = baseTarget.Rules.Concat(new[] { unknown }).ToArray() },
                WorkTarget(),
                GameTarget()
            }));
    }

    [TestMethod]
    public void MissingMandatoryGameSemanticFailsClosed()
    {
        var game = GameTarget();
        var reduced = game with
        {
            Rules = game.Rules
                .Where(rule => !string.Equals(rule.SubjectId, ModeVerificationIds.GameLauncherReady, StringComparison.Ordinal))
                .ToArray()
        };

        AssertThrows<InvalidDataException>(() => ModePolicyCatalog.Create(
            "catalog.v1",
            1,
            "release.2026-09",
            new[] { BaseTarget(), WorkTarget(), reduced }));
    }

    [TestMethod]
    public void ApprovedFallbackSelectionBecomesPartOfResolvedSnapshotDigest()
    {
        var catalog = CatalogWithGameDisplayFallback();
        var resolver = new ModePolicyResolver();
        var normal = resolver.Resolve(catalog, OperationalMode.Game, "release.2026-09");
        var selected = resolver.Resolve(
            catalog,
            OperationalMode.Game,
            "release.2026-09",
            new[]
            {
                new ModePolicyFallbackSelection(
                    "game.display.preferred",
                    ModePolicyFallbackClass.ApprovedAlternate,
                    "display.any_usable")
            });

        Assert.AreNotEqual(normal.ResolvedDigest, selected.ResolvedDigest);
        Assert.AreEqual(1, selected.SelectedFallbacks.Count);
        Assert.AreEqual("display.any_usable", selected.SelectedFallbacks[0].TargetId);
    }

    [TestMethod]
    public void RuntimeCannotSelectFallbackNotApprovedByReleasePolicy()
    {
        var catalog = CatalogWithGameDisplayFallback();
        var resolver = new ModePolicyResolver();

        AssertThrows<InvalidDataException>(() => resolver.Resolve(
            catalog,
            OperationalMode.Game,
            "release.2026-09",
            new[]
            {
                new ModePolicyFallbackSelection(
                    "game.display.preferred",
                    ModePolicyFallbackClass.ApprovedAlternate,
                    "display.user_injected")
            }));
    }

    [TestMethod]
    public void TamperedCatalogDigestIsRejectedBeforeResolution()
    {
        var catalog = Catalog() with { ContentDigest = new string('0', 64) };
        var resolver = new ModePolicyResolver();

        AssertThrows<InvalidDataException>(() =>
            resolver.Resolve(catalog, OperationalMode.Work, "release.2026-09"));
    }

    [TestMethod]
    public void PolicyFromAnotherRuntimeReleaseIsRejected()
    {
        var resolver = new ModePolicyResolver();

        AssertThrows<InvalidDataException>(() =>
            resolver.Resolve(Catalog(), OperationalMode.Work, "release.other"));
    }

    [TestMethod]
    public void IntentMustMatchTypedPolicyDomain()
    {
        var invalid = new ModePolicyRule(
            "game.power.invalid",
            ModePolicyDomain.Power,
            "power.performance",
            ModePolicyIntent.ApplicationRequestClose,
            ModePolicyRequirement.Mandatory,
            ModePolicyFallback.None);
        var game = GameTarget();

        AssertThrows<InvalidDataException>(() => ModePolicyCatalog.Create(
            "catalog.v1",
            1,
            "release.2026-09",
            new[]
            {
                BaseTarget(),
                WorkTarget(),
                game with { Rules = game.Rules.Concat(new[] { invalid }).ToArray() }
            }));
    }

    private static ModePolicyCatalog Catalog()
        => ModePolicyCatalog.Create(
            "catalog.v1",
            7,
            "release.2026-09",
            new[] { BaseTarget(), WorkTarget(), GameTarget() });

    private static ModePolicyCatalog CatalogWithGameDisplayFallback()
    {
        var game = GameTarget();
        var preferredDisplay = new ModePolicyRule(
            "game.display.preferred",
            ModePolicyDomain.Display,
            "display.preferred",
            ModePolicyIntent.ContextPreferred,
            ModePolicyRequirement.Preferred,
            new ModePolicyFallback(ModePolicyFallbackClass.ApprovedAlternate, "display.any_usable"));

        return ModePolicyCatalog.Create(
            "catalog.v1",
            8,
            "release.2026-09",
            new[]
            {
                BaseTarget(),
                WorkTarget(),
                game with { Rules = game.Rules.Concat(new[] { preferredDisplay }).ToArray() }
            });
    }

    private static ModePolicyTargetDefinition BaseTarget()
        => new(
            ModePolicyTarget.Base,
            new[]
            {
                VerificationRule("base.verify.input", ModeVerificationIds.InputContextUsable),
                VerificationRule("base.verify.neutral", ModeVerificationIds.BaseModeDeltasNeutralized),
                new ModePolicyRule(
                    "base.components.mode_exclusive",
                    ModePolicyDomain.ManagedComponent,
                    "component.mode_exclusive",
                    ModePolicyIntent.ManagedInactive,
                    ModePolicyRequirement.Mandatory,
                    ModePolicyFallback.None)
            });

    private static ModePolicyTargetDefinition WorkTarget()
        => new(
            ModePolicyTarget.Work,
            new[]
            {
                VerificationRule("work.verify.display", ModeVerificationIds.DisplayTargetReached),
                VerificationRule("work.verify.input", ModeVerificationIds.InputContextUsable),
                VerificationRule("work.verify.components", ModeVerificationIds.ManagedComponentSetConfirmed),
                VerificationRule("work.verify.desktop", ModeVerificationIds.WorkDesktopUsable),
                new ModePolicyRule(
                    "work.power.context",
                    ModePolicyDomain.Power,
                    "power.work",
                    ModePolicyIntent.ContextRequired,
                    ModePolicyRequirement.Mandatory,
                    ModePolicyFallback.None)
            });

    private static ModePolicyTargetDefinition GameTarget()
        => new(
            ModePolicyTarget.Game,
            new[]
            {
                VerificationRule("game.verify.display", ModeVerificationIds.DisplayTargetReached),
                VerificationRule("game.verify.input", ModeVerificationIds.InputContextUsable),
                VerificationRule("game.verify.components", ModeVerificationIds.ManagedComponentSetConfirmed),
                VerificationRule("game.verify.launcher", ModeVerificationIds.GameLauncherReady),
                new ModePolicyRule(
                    "game.launcher.ready",
                    ModePolicyDomain.Launcher,
                    "launcher.game",
                    ModePolicyIntent.LauncherReadyRequired,
                    ModePolicyRequirement.Mandatory,
                    ModePolicyFallback.None)
            });

    private static ModePolicyRule VerificationRule(string ruleId, string verificationId)
        => new(
            ruleId,
            ModePolicyDomain.Verification,
            verificationId,
            ModePolicyIntent.VerificationRequired,
            ModePolicyRequirement.Mandatory,
            ModePolicyFallback.None);

    private static ModePolicyTargetDefinition ReverseRules(ModePolicyTargetDefinition definition)
        => definition with { Rules = definition.Rules.Reverse().ToArray() };

    private static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
            Assert.Fail($"Expected exception {typeof(TException).Name}.");
        }
        catch (TException)
        {
        }
    }
}
