using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.GameRuntime;

public static class ProcessExitProofSetIds
{
    public const string PermittedReplacementV1 = "PERMITTED_REPLACEMENT_V1";
}

public static class ProcessExitCorrelationReasonCodes
{
    public const string RequiredProcessStillPresent = "REQUIRED_PROCESS_STILL_PRESENT";
    public const string RequiredIdentityIncomplete = "REQUIRED_IDENTITY_INCOMPLETE";
    public const string ExitGraceStarted = "EXIT_GRACE_STARTED";
    public const string ExitGracePending = "EXIT_GRACE_PENDING";
    public const string ExitGraceSatisfied = "EXIT_GRACE_SATISFIED";
    public const string PermittedReplacementFound = "PERMITTED_REPLACEMENT_FOUND";
    public const string AmbiguousPermittedReplacement = "AMBIGUOUS_PERMITTED_REPLACEMENT";
    public const string UnknownReplacementCandidate = "UNKNOWN_REPLACEMENT_CANDIDATE";
}

public enum ProcessExitCorrelationClassification
{
    StillRunning,
    ExitCandidate,
    ReplacementProcessFound,
    ExitConfirmed,
    CorrelationLost
}

public sealed record ProcessReplacementRule(
    string SourceExecutableName,
    string ReplacementExecutableName);

public sealed record ProcessExitCorrelationRules(
    int SessionId,
    string ValidatedInstallRoot,
    TimeSpan ExitGraceWindow,
    TimeSpan ReplacementWindow,
    IReadOnlyList<ProcessReplacementRule> AllowedReplacementRules,
    IReadOnlyList<string> KnownIgnoredExecutableNames)
{
    public void Validate()
    {
        if (SessionId < 0)
            throw new InvalidDataException("Process exit correlation session ID must be non-negative.");

        if (ExitGraceWindow < TimeSpan.Zero)
            throw new InvalidDataException("Process exit grace window cannot be negative.");

        if (ReplacementWindow < TimeSpan.Zero)
            throw new InvalidDataException("Process replacement window cannot be negative.");

        if (ReplacementWindow > ExitGraceWindow)
        {
            throw new InvalidDataException(
                "Process replacement window cannot exceed the exit grace window.");
        }

        _ = WindowsPathRelationship.NormalizeAbsolute(ValidatedInstallRoot, "validated install root");

        if (AllowedReplacementRules is null)
            throw new InvalidDataException("Allowed replacement rules are required.");

        if (KnownIgnoredExecutableNames is null)
            throw new InvalidDataException("Known ignored executable names are required.");

        var ignored = ProcessCorrelationRules.NormalizeExecutableNameSet(
            KnownIgnoredExecutableNames,
            "ignored executable");
        var uniqueRules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in AllowedReplacementRules)
        {
            if (rule is null)
                throw new InvalidDataException("Replacement rule cannot be null.");

            var source = NormalizeExecutableName(rule.SourceExecutableName, "replacement source executable");
            var target = NormalizeExecutableName(rule.ReplacementExecutableName, "replacement target executable");
            if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Replacement source and target executables must differ.");

            if (ignored.Contains(source) || ignored.Contains(target))
            {
                throw new InvalidDataException(
                    "An executable cannot be both an allowed replacement endpoint and an ignored helper.");
            }

            if (!uniqueRules.Add(source + "\0" + target))
                throw new InvalidDataException("Duplicate process replacement rule.");
        }
    }

    internal static string NormalizeExecutableName(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"{fieldName} cannot be empty.");

        var trimmed = value.Trim();
        var fileName = Path.GetFileName(trimmed);
        if (!string.Equals(fileName, trimmed, StringComparison.Ordinal))
            throw new InvalidDataException($"{fieldName} must be a file name, not a path: '{value}'.");

        return fileName;
    }
}

public sealed record ProcessExitCorrelationResult(
    ProcessExitCorrelationClassification Classification,
    string ReasonCode,
    DateTimeOffset ObservedUtc,
    CorrelatedProcessEvidence TrackedPrimary,
    CorrelatedProcessEvidence? Replacement,
    DateTimeOffset? ExitCandidateSinceUtc);

/// <summary>
/// Tracks the required game process after launch correlation has already reached RUNNING_CONFIRMED.
/// A missing primary first becomes EXIT_CANDIDATE. Only an explicitly allowlisted replacement under
/// the validated install root and in the same Windows session can replace it. Unknown replacement-like
/// processes fail closed as CORRELATION_LOST; unrelated client/helper processes do not keep the game alive.
/// </summary>
public sealed class ProcessExitCorrelationTracker
{
    private readonly ProcessExitCorrelationRules _rules;
    private readonly string _validatedInstallRoot;
    private readonly HashSet<string> _ignoredExecutableNames;
    private readonly IReadOnlyList<NormalizedReplacementRule> _replacementRules;
    private CorrelatedProcessEvidence _trackedPrimary;
    private DateTimeOffset _lastObservedUtc;
    private DateTimeOffset? _exitCandidateSinceUtc;

    public ProcessExitCorrelationTracker(
        ProcessCorrelationResult runningCorrelation,
        ProcessExitCorrelationRules rules)
    {
        ArgumentNullException.ThrowIfNull(runningCorrelation);
        ArgumentNullException.ThrowIfNull(rules);
        rules.Validate();

        if (runningCorrelation.Classification != ProcessCorrelationClassification.RunningConfirmed)
        {
            throw new InvalidDataException(
                "Exit correlation can start only from RUNNING_CONFIRMED process evidence.");
        }

        if (string.IsNullOrWhiteSpace(runningCorrelation.ProofSetId))
            throw new InvalidDataException("Running correlation must identify the proof set that won.");

        var winners = runningCorrelation.CorrelatedProcesses
            .Where(process => process.EvidenceLevel == runningCorrelation.EvidenceLevel)
            .Where(process => string.Equals(
                process.ProofSetId,
                runningCorrelation.ProofSetId,
                StringComparison.Ordinal))
            .ToArray();
        if (winners.Length != 1)
        {
            throw new InvalidDataException(
                "Running correlation must contain exactly one strongest proof-set winner.");
        }

        var winner = winners[0];
        if (winner.ProcessCreationTimeUtc is null || winner.SessionId != rules.SessionId)
            throw new InvalidDataException("Tracked running process requires complete identity in the expected session.");

        if (string.IsNullOrWhiteSpace(winner.NormalizedImagePath))
            throw new InvalidDataException("Tracked running process requires a normalized image path.");

        _validatedInstallRoot = WindowsPathRelationship.NormalizeAbsolute(
            rules.ValidatedInstallRoot,
            "validated install root");
        if (!WindowsPathRelationship.IsDescendantFile(winner.NormalizedImagePath, _validatedInstallRoot))
            throw new InvalidDataException("Tracked running process is outside the validated install root.");

        _rules = rules;
        _ignoredExecutableNames = ProcessCorrelationRules.NormalizeExecutableNameSet(
            rules.KnownIgnoredExecutableNames,
            "ignored executable");
        _replacementRules = rules.AllowedReplacementRules
            .Select(rule => new NormalizedReplacementRule(
                ProcessExitCorrelationRules.NormalizeExecutableName(
                    rule.SourceExecutableName,
                    "replacement source executable"),
                ProcessExitCorrelationRules.NormalizeExecutableName(
                    rule.ReplacementExecutableName,
                    "replacement target executable")))
            .ToArray();
        _trackedPrimary = winner with { Role = CorrelatedExecutableRole.GamePrimary };
        _lastObservedUtc = runningCorrelation.ObservedUtc;
    }

    public ProcessExitCorrelationResult Observe(ProcessEvidenceSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (current.ObservedUtc < _lastObservedUtc)
            throw new InvalidDataException("Process exit evidence timestamp moved backwards.");

        _lastObservedUtc = current.ObservedUtc;
        var trackedIdentity = new ProcessInstanceIdentity(
            _trackedPrimary.ProcessId,
            _trackedPrimary.ProcessCreationTimeUtc!.Value);

        var samePidInExpectedSession = current.Processes
            .Where(process => process.ProcessId == trackedIdentity.ProcessId)
            .Where(process => process.SessionId == _rules.SessionId)
            .ToArray();

        if (samePidInExpectedSession.Any(process => process.ReuseProtectedIdentity == trackedIdentity))
        {
            _exitCandidateSinceUtc = null;
            _trackedPrimary = _trackedPrimary with { LastObservedUtc = current.ObservedUtc };
            return Result(
                ProcessExitCorrelationClassification.StillRunning,
                ProcessExitCorrelationReasonCodes.RequiredProcessStillPresent,
                current.ObservedUtc);
        }

        // The PID is still observed in the expected session but Windows withheld creation-time identity.
        // We cannot prove that the tracked process exited and must not advance an exit grace clock.
        if (samePidInExpectedSession.Any(process => process.ReuseProtectedIdentity is null))
        {
            _exitCandidateSinceUtc = null;
            return Result(
                ProcessExitCorrelationClassification.CorrelationLost,
                ProcessExitCorrelationReasonCodes.RequiredIdentityIncomplete,
                current.ObservedUtc);
        }

        var exitCandidateSinceUtc = _exitCandidateSinceUtc ?? current.ObservedUtc;
        var permittedReplacements = FindPermittedReplacements(current, exitCandidateSinceUtc);
        if (permittedReplacements.Length > 1)
        {
            _exitCandidateSinceUtc = exitCandidateSinceUtc;
            return Result(
                ProcessExitCorrelationClassification.CorrelationLost,
                ProcessExitCorrelationReasonCodes.AmbiguousPermittedReplacement,
                current.ObservedUtc);
        }

        if (permittedReplacements.Length == 1)
        {
            var replacement = permittedReplacements[0];
            var oldPrimary = _trackedPrimary;
            _trackedPrimary = replacement;
            _exitCandidateSinceUtc = null;
            return new ProcessExitCorrelationResult(
                ProcessExitCorrelationClassification.ReplacementProcessFound,
                ProcessExitCorrelationReasonCodes.PermittedReplacementFound,
                current.ObservedUtc,
                _trackedPrimary,
                replacement,
                ExitCandidateSinceUtc: null);
        }

        if (HasUnknownReplacementCandidate(current, exitCandidateSinceUtc))
        {
            _exitCandidateSinceUtc = exitCandidateSinceUtc;
            return Result(
                ProcessExitCorrelationClassification.CorrelationLost,
                ProcessExitCorrelationReasonCodes.UnknownReplacementCandidate,
                current.ObservedUtc);
        }

        _exitCandidateSinceUtc = exitCandidateSinceUtc;
        if (current.ObservedUtc - exitCandidateSinceUtc < _rules.ExitGraceWindow)
        {
            return Result(
                ProcessExitCorrelationClassification.ExitCandidate,
                exitCandidateSinceUtc == current.ObservedUtc
                    ? ProcessExitCorrelationReasonCodes.ExitGraceStarted
                    : ProcessExitCorrelationReasonCodes.ExitGracePending,
                current.ObservedUtc);
        }

        return Result(
            ProcessExitCorrelationClassification.ExitConfirmed,
            ProcessExitCorrelationReasonCodes.ExitGraceSatisfied,
            current.ObservedUtc);
    }

    private CorrelatedProcessEvidence[] FindPermittedReplacements(
        ProcessEvidenceSnapshot current,
        DateTimeOffset exitCandidateSinceUtc)
    {
        var sourceName = Path.GetFileName(_trackedPrimary.NormalizedImagePath!);
        var allowedTargets = _replacementRules
            .Where(rule => string.Equals(rule.SourceExecutableName, sourceName, StringComparison.OrdinalIgnoreCase))
            .Select(rule => rule.ReplacementExecutableName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (allowedTargets.Count == 0)
            return [];

        return current.Processes
            .Where(process => process.SessionId == _rules.SessionId)
            .Select(process => TryCreateReplacementEvidence(
                process,
                current.ObservedUtc,
                exitCandidateSinceUtc,
                allowedTargets))
            .Where(static evidence => evidence is not null)
            .Select(static evidence => evidence!)
            .OrderBy(static evidence => evidence.ProcessId)
            .ToArray();
    }

    private CorrelatedProcessEvidence? TryCreateReplacementEvidence(
        ProcessEvidenceObservation process,
        DateTimeOffset observedUtc,
        DateTimeOffset exitCandidateSinceUtc,
        HashSet<string> allowedTargets)
    {
        var identity = process.ReuseProtectedIdentity;
        if (identity is null || process.ImagePath is null)
            return null;

        string imagePath;
        try
        {
            imagePath = WindowsPathRelationship.NormalizeAbsolute(process.ImagePath, "replacement process image path");
        }
        catch (InvalidDataException)
        {
            return null;
        }

        if (!WindowsPathRelationship.IsDescendantFile(imagePath, _validatedInstallRoot))
            return null;

        var executableName = Path.GetFileName(imagePath);
        if (!allowedTargets.Contains(executableName))
            return null;

        var creationUtc = identity.ProcessCreationTimeUtc;
        if (creationUtc > observedUtc)
            return null;

        // A replacement may start just before the polling snapshot in which the old primary first
        // disappears. It must still belong to this game lifetime and be recent enough for the
        // adapter-owned replacement window.
        if (creationUtc < _trackedPrimary.FirstObservedUtc)
            return null;

        if (observedUtc - creationUtc > _rules.ReplacementWindow)
            return null;

        return new CorrelatedProcessEvidence(
            process.ProcessId,
            creationUtc,
            process.SessionId,
            imagePath,
            CorrelatedExecutableRole.GamePrimary,
            ProcessCorrelationEvidenceLevel.Strong,
            ProcessExitProofSetIds.PermittedReplacementV1,
            creationUtc < exitCandidateSinceUtc ? creationUtc : exitCandidateSinceUtc,
            observedUtc);
    }

    private bool HasUnknownReplacementCandidate(
        ProcessEvidenceSnapshot current,
        DateTimeOffset exitCandidateSinceUtc)
    {
        foreach (var process in current.Processes)
        {
            if (process.SessionId != _rules.SessionId || process.ReuseProtectedIdentity is not { } identity)
                continue;

            if (process.ImagePath is null)
                continue;

            string imagePath;
            try
            {
                imagePath = WindowsPathRelationship.NormalizeAbsolute(process.ImagePath, "candidate process image path");
            }
            catch (InvalidDataException)
            {
                continue;
            }

            if (!WindowsPathRelationship.IsDescendantFile(imagePath, _validatedInstallRoot))
                continue;

            var executableName = Path.GetFileName(imagePath);
            if (_ignoredExecutableNames.Contains(executableName))
                continue;

            if (identity.ProcessCreationTimeUtc > current.ObservedUtc)
                continue;

            if (identity.ProcessCreationTimeUtc < _trackedPrimary.FirstObservedUtc)
                continue;

            if (current.ObservedUtc - identity.ProcessCreationTimeUtc > _rules.ReplacementWindow)
                continue;

            // A valid permitted target would already have been returned by FindPermittedReplacements.
            // Anything else that looks like a recent game-root replacement is unresolved evidence.
            return true;
        }

        return false;
    }

    private ProcessExitCorrelationResult Result(
        ProcessExitCorrelationClassification classification,
        string reasonCode,
        DateTimeOffset observedUtc)
        => new(
            classification,
            reasonCode,
            observedUtc,
            _trackedPrimary,
            Replacement: null,
            _exitCandidateSinceUtc);

    private sealed record NormalizedReplacementRule(
        string SourceExecutableName,
        string ReplacementExecutableName);
}
