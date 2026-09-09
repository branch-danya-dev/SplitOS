using System.Security.Cryptography;
using System.Text;

namespace SplitOS.RuntimeHost.ModeRuntime;

public enum ModePolicyTarget
{
    Base,
    Work,
    Game
}

public enum ModePolicyDomain
{
    ManagedComponent,
    ApplicationLifecycle,
    Display,
    Audio,
    Input,
    Power,
    RuntimeServices,
    Launcher,
    SharedApps,
    Verification
}

public enum ModePolicyRequirement
{
    Mandatory,
    Preferred,
    Optional
}

public enum ModePolicyIntent
{
    ManagedActive,
    ManagedInactive,
    ManagedAvailable,
    ManagedUnchanged,
    ApplicationAllow,
    ApplicationKeepRunning,
    ApplicationRequestClose,
    ApplicationRequireClosed,
    ApplicationMoveToSharedPresentation,
    ApplicationIgnoreUnmanaged,
    ContextPreferred,
    ContextRequired,
    ContextUnchanged,
    LauncherReadyRequired,
    LauncherUnchanged,
    SharedAllow,
    SharedKeepRunning,
    SharedMoveToSharedPresentation,
    SharedIgnoreUnmanaged,
    VerificationRequired
}

public enum ModePolicyFallbackClass
{
    None,
    ReleaseDefault,
    ApprovedAlternate,
    PreserveCurrent
}

public static class ModeVerificationIds
{
    public const string DisplayTargetReached = "DISPLAY_TARGET_REACHED";
    public const string InputContextUsable = "INPUT_CONTEXT_USABLE";
    public const string PowerPolicyConfirmed = "POWER_POLICY_CONFIRMED";
    public const string ManagedComponentSetConfirmed = "MANAGED_COMPONENT_SET_CONFIRMED";
    public const string GameLauncherReady = "GAME_LAUNCHER_READY";
    public const string WorkDesktopUsable = "WORK_DESKTOP_USABLE";
    public const string BaseModeDeltasNeutralized = "BASE_MODE_DELTAS_NEUTRALIZED";

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        DisplayTargetReached,
        InputContextUsable,
        PowerPolicyConfirmed,
        ManagedComponentSetConfirmed,
        GameLauncherReady,
        WorkDesktopUsable,
        BaseModeDeltasNeutralized
    };

    public static bool IsKnown(string value) => Known.Contains(value);
}

public sealed record ModePolicyFallback(ModePolicyFallbackClass Class, string? TargetId)
{
    public static ModePolicyFallback None { get; } = new(ModePolicyFallbackClass.None, null);

    public void Validate()
    {
        if (Class == ModePolicyFallbackClass.ApprovedAlternate)
        {
            ModePolicyValidation.ValidateSemanticId(TargetId, nameof(TargetId));
            return;
        }

        if (TargetId is not null)
        {
            throw new InvalidDataException("Only APPROVED_ALTERNATE fallback may carry a target semantic ID.");
        }
    }
}

public sealed record ModePolicyRule(
    string RuleId,
    ModePolicyDomain Domain,
    string SubjectId,
    ModePolicyIntent Intent,
    ModePolicyRequirement Requirement,
    ModePolicyFallback Fallback)
{
    public void Validate()
    {
        ModePolicyValidation.ValidateSemanticId(RuleId, nameof(RuleId));
        ModePolicyValidation.ValidateSemanticId(SubjectId, nameof(SubjectId));
        ArgumentNullException.ThrowIfNull(Fallback);
        Fallback.Validate();

        if (!ModePolicyValidation.IsIntentAllowed(Domain, Intent))
        {
            throw new InvalidDataException($"Intent {Intent} is not valid for policy domain {Domain}.");
        }

        if (Domain == ModePolicyDomain.Verification)
        {
            if (!ModeVerificationIds.IsKnown(SubjectId))
            {
                throw new InvalidDataException($"Unknown verification semantic ID '{SubjectId}'.");
            }

            if (Fallback.Class != ModePolicyFallbackClass.None)
            {
                throw new InvalidDataException("Verification rules cannot define mechanism-level fallback behavior.");
            }
        }
    }
}

public sealed record ModePolicyTargetDefinition(
    ModePolicyTarget Target,
    IReadOnlyList<ModePolicyRule> Rules)
{
    public void Validate()
    {
        if (Rules is null || Rules.Count == 0 || Rules.Count > 256)
        {
            throw new InvalidDataException("Mode policy target must contain a bounded non-empty rule set.");
        }

        var ruleIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in Rules)
        {
            if (rule is null)
            {
                throw new InvalidDataException("Mode policy target contains a null rule.");
            }

            rule.Validate();
            if (!ruleIds.Add(rule.RuleId))
            {
                throw new InvalidDataException($"Duplicate mode policy rule id '{rule.RuleId}'.");
            }
        }

        ValidateMinimumSemanticTarget();
    }

    private void ValidateMinimumSemanticTarget()
    {
        var mandatoryVerificationIds = Rules
            .Where(static rule =>
                rule.Domain == ModePolicyDomain.Verification &&
                rule.Intent == ModePolicyIntent.VerificationRequired &&
                rule.Requirement == ModePolicyRequirement.Mandatory)
            .Select(static rule => rule.SubjectId)
            .ToHashSet(StringComparer.Ordinal);

        var required = Target switch
        {
            ModePolicyTarget.Game => new[]
            {
                ModeVerificationIds.DisplayTargetReached,
                ModeVerificationIds.InputContextUsable,
                ModeVerificationIds.ManagedComponentSetConfirmed,
                ModeVerificationIds.GameLauncherReady
            },
            ModePolicyTarget.Work => new[]
            {
                ModeVerificationIds.DisplayTargetReached,
                ModeVerificationIds.InputContextUsable,
                ModeVerificationIds.ManagedComponentSetConfirmed,
                ModeVerificationIds.WorkDesktopUsable
            },
            ModePolicyTarget.Base => new[]
            {
                ModeVerificationIds.InputContextUsable,
                ModeVerificationIds.BaseModeDeltasNeutralized
            },
            _ => throw new ArgumentOutOfRangeException(nameof(Target))
        };

        var missing = required.Where(id => !mandatoryVerificationIds.Contains(id)).ToArray();
        if (missing.Length != 0)
        {
            throw new InvalidDataException(
                $"{Target} policy is missing mandatory verification semantics: {string.Join(", ", missing)}.");
        }
    }
}

public sealed record ModePolicyCatalog(
    int SchemaVersion,
    string PolicyCatalogId,
    long PolicyVersion,
    string ReleaseId,
    IReadOnlyList<ModePolicyTargetDefinition> Targets,
    string ContentDigest)
{
    public const int CurrentSchemaVersion = 1;

    public static ModePolicyCatalog Create(
        string policyCatalogId,
        long policyVersion,
        string releaseId,
        IReadOnlyList<ModePolicyTargetDefinition> targets)
    {
        var provisional = new ModePolicyCatalog(
            CurrentSchemaVersion,
            policyCatalogId,
            policyVersion,
            releaseId,
            targets,
            string.Empty);
        provisional.ValidateShape();
        return provisional with { ContentDigest = ModePolicyDigest.ComputeCatalog(provisional) };
    }

    public void Validate(string expectedReleaseId)
    {
        ValidateShape();
        ModePolicyValidation.ValidateSemanticId(expectedReleaseId, nameof(expectedReleaseId));
        if (!string.Equals(ReleaseId, expectedReleaseId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Mode policy release identity is incompatible with the active Runtime release.");
        }

        ModePolicyValidation.ValidateSha256Digest(ContentDigest, nameof(ContentDigest));
        var expectedDigest = ModePolicyDigest.ComputeCatalog(this);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(ContentDigest.ToLowerInvariant()),
                Encoding.ASCII.GetBytes(expectedDigest)))
        {
            throw new InvalidDataException("Mode policy content digest does not match its declarative semantic content.");
        }
    }

    public ModePolicyTargetDefinition GetTarget(ModePolicyTarget target)
        => Targets.Single(definition => definition.Target == target);

    private void ValidateShape()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Unsupported mode policy schema version {SchemaVersion}.");
        }

        ModePolicyValidation.ValidateSemanticId(PolicyCatalogId, nameof(PolicyCatalogId));
        ModePolicyValidation.ValidateSemanticId(ReleaseId, nameof(ReleaseId));
        if (PolicyVersion < 1)
        {
            throw new InvalidDataException("Mode policy version must be positive.");
        }

        if (Targets is null || Targets.Count != 3)
        {
            throw new InvalidDataException("Mode policy catalog must define exactly BASE, WORK and GAME targets.");
        }

        var targetKinds = new HashSet<ModePolicyTarget>();
        foreach (var target in Targets)
        {
            if (target is null) throw new InvalidDataException("Mode policy catalog contains a null target.");
            target.Validate();
            if (!targetKinds.Add(target.Target))
            {
                throw new InvalidDataException($"Duplicate mode policy target {target.Target}.");
            }
        }

        if (!Enum.GetValues<ModePolicyTarget>().All(targetKinds.Contains))
        {
            throw new InvalidDataException("Mode policy catalog must contain BASE, WORK and GAME exactly once.");
        }
    }
}

public sealed record ModePolicyIdentity(
    string PolicyCatalogId,
    long PolicyVersion,
    string ReleaseId,
    string CatalogDigest);

public sealed record ModePolicyFallbackSelection(
    string RuleId,
    ModePolicyFallbackClass FallbackClass,
    string? TargetId);

public sealed record ResolvedModePolicySnapshot(
    ModePolicyIdentity Identity,
    ModePolicyTarget Target,
    IReadOnlyList<ModePolicyRule> Rules,
    IReadOnlyList<ModePolicyFallbackSelection> SelectedFallbacks,
    string ResolvedDigest);

/// <summary>
/// Resolves a release-owned declarative target into an immutable semantic snapshot. The resolver maps
/// OperationalMode.NONE to the internal BASE target and binds any approved runtime fallback choice into
/// the resolved digest. It accepts no command line, registry path, service name or executable payload.
/// </summary>
public sealed class ModePolicyResolver
{
    public ResolvedModePolicySnapshot Resolve(
        ModePolicyCatalog catalog,
        OperationalMode targetMode,
        string expectedReleaseId,
        IReadOnlyCollection<ModePolicyFallbackSelection>? selectedFallbacks = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        catalog.Validate(expectedReleaseId);

        var target = targetMode switch
        {
            OperationalMode.None => ModePolicyTarget.Base,
            OperationalMode.Work => ModePolicyTarget.Work,
            OperationalMode.Game => ModePolicyTarget.Game,
            _ => throw new ArgumentOutOfRangeException(nameof(targetMode))
        };

        var definition = catalog.GetTarget(target);
        var selections = ValidateSelections(definition, selectedFallbacks ?? Array.Empty<ModePolicyFallbackSelection>());
        var identity = new ModePolicyIdentity(
            catalog.PolicyCatalogId,
            catalog.PolicyVersion,
            catalog.ReleaseId,
            catalog.ContentDigest);
        var resolvedDigest = ModePolicyDigest.ComputeResolved(catalog.ContentDigest, target, selections);

        return new ResolvedModePolicySnapshot(
            identity,
            target,
            definition.Rules.OrderBy(static rule => rule.RuleId, StringComparer.Ordinal).ToArray(),
            selections,
            resolvedDigest);
    }

    private static IReadOnlyList<ModePolicyFallbackSelection> ValidateSelections(
        ModePolicyTargetDefinition target,
        IReadOnlyCollection<ModePolicyFallbackSelection> selectedFallbacks)
    {
        if (selectedFallbacks.Count > target.Rules.Count)
        {
            throw new InvalidDataException("Resolved fallback selection count exceeds target policy rule count.");
        }

        var rulesById = target.Rules.ToDictionary(static rule => rule.RuleId, StringComparer.Ordinal);
        var selectedRuleIds = new HashSet<string>(StringComparer.Ordinal);
        var validated = new List<ModePolicyFallbackSelection>(selectedFallbacks.Count);

        foreach (var selection in selectedFallbacks)
        {
            if (selection is null) throw new InvalidDataException("Resolved fallback selection must not be null.");
            ModePolicyValidation.ValidateSemanticId(selection.RuleId, nameof(selection.RuleId));
            if (!selectedRuleIds.Add(selection.RuleId))
            {
                throw new InvalidDataException($"Fallback for rule '{selection.RuleId}' was selected more than once.");
            }

            if (!rulesById.TryGetValue(selection.RuleId, out var rule))
            {
                throw new InvalidDataException($"Fallback selection references unknown rule '{selection.RuleId}'.");
            }

            if (rule.Fallback.Class == ModePolicyFallbackClass.None)
            {
                throw new InvalidDataException($"Rule '{selection.RuleId}' does not permit a fallback.");
            }

            if (selection.FallbackClass != rule.Fallback.Class ||
                !string.Equals(selection.TargetId, rule.Fallback.TargetId, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Fallback selection for rule '{selection.RuleId}' is not release-approved by the target policy.");
            }

            validated.Add(selection);
        }

        return validated
            .OrderBy(static selection => selection.RuleId, StringComparer.Ordinal)
            .ToArray();
    }
}

internal static class ModePolicyValidation
{
    public static void ValidateSemanticId(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
        {
            throw new ArgumentException("Release semantic ID is missing or outside supported bounds.", paramName);
        }

        foreach (var character in value)
        {
            var allowed = char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or ':' or '-';
            if (!allowed)
            {
                throw new ArgumentException(
                    "Release semantic IDs may contain only ASCII letters, digits, '.', '_', ':' and '-'.",
                    paramName);
            }
        }
    }

    public static void ValidateSha256Digest(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length != 64 ||
            value.Any(static character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException($"{paramName} must be a SHA-256 hexadecimal digest.");
        }
    }

    public static bool IsIntentAllowed(ModePolicyDomain domain, ModePolicyIntent intent)
        => domain switch
        {
            ModePolicyDomain.ManagedComponent or ModePolicyDomain.RuntimeServices => intent is
                ModePolicyIntent.ManagedActive or
                ModePolicyIntent.ManagedInactive or
                ModePolicyIntent.ManagedAvailable or
                ModePolicyIntent.ManagedUnchanged,
            ModePolicyDomain.ApplicationLifecycle => intent is
                ModePolicyIntent.ApplicationAllow or
                ModePolicyIntent.ApplicationKeepRunning or
                ModePolicyIntent.ApplicationRequestClose or
                ModePolicyIntent.ApplicationRequireClosed or
                ModePolicyIntent.ApplicationMoveToSharedPresentation or
                ModePolicyIntent.ApplicationIgnoreUnmanaged,
            ModePolicyDomain.Display or ModePolicyDomain.Audio or ModePolicyDomain.Input or ModePolicyDomain.Power => intent is
                ModePolicyIntent.ContextPreferred or
                ModePolicyIntent.ContextRequired or
                ModePolicyIntent.ContextUnchanged,
            ModePolicyDomain.Launcher => intent is
                ModePolicyIntent.LauncherReadyRequired or
                ModePolicyIntent.LauncherUnchanged,
            ModePolicyDomain.SharedApps => intent is
                ModePolicyIntent.SharedAllow or
                ModePolicyIntent.SharedKeepRunning or
                ModePolicyIntent.SharedMoveToSharedPresentation or
                ModePolicyIntent.SharedIgnoreUnmanaged,
            ModePolicyDomain.Verification => intent == ModePolicyIntent.VerificationRequired,
            _ => false
        };
}

internal static class ModePolicyDigest
{
    public static string ComputeCatalog(ModePolicyCatalog catalog)
    {
        var builder = new StringBuilder();
        Append(builder, catalog.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(builder, catalog.PolicyCatalogId);
        Append(builder, catalog.PolicyVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(builder, catalog.ReleaseId);

        foreach (var target in catalog.Targets.OrderBy(static target => target.Target))
        {
            Append(builder, target.Target.ToString());
            foreach (var rule in target.Rules.OrderBy(static rule => rule.RuleId, StringComparer.Ordinal))
            {
                Append(builder, rule.RuleId);
                Append(builder, rule.Domain.ToString());
                Append(builder, rule.SubjectId);
                Append(builder, rule.Intent.ToString());
                Append(builder, rule.Requirement.ToString());
                Append(builder, rule.Fallback.Class.ToString());
                Append(builder, rule.Fallback.TargetId ?? string.Empty);
            }
        }

        return Sha256(builder.ToString());
    }

    public static string ComputeResolved(
        string catalogDigest,
        ModePolicyTarget target,
        IReadOnlyList<ModePolicyFallbackSelection> selections)
    {
        var builder = new StringBuilder();
        Append(builder, catalogDigest.ToLowerInvariant());
        Append(builder, target.ToString());
        foreach (var selection in selections.OrderBy(static selection => selection.RuleId, StringComparer.Ordinal))
        {
            Append(builder, selection.RuleId);
            Append(builder, selection.FallbackClass.ToString());
            Append(builder, selection.TargetId ?? string.Empty);
        }

        return Sha256(builder.ToString());
    }

    private static void Append(StringBuilder builder, string value)
        => builder.Append(value.Length).Append(':').Append(value).Append('|');

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
