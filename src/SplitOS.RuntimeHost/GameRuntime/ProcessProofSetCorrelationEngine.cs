using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.GameRuntime;

public static class ProcessProofSetIds
{
    public const string CuratedExecutableV1 = "CURATED_EXECUTABLE_V1";
    public const string InstallRootGenericV1 = "INSTALL_ROOT_GENERIC_V1";
}

public static class ProcessCorrelationReasonCodes
{
    public const string NoSupportedProof = "NO_SUPPORTED_PROOF";
    public const string ProcessIdentityIncomplete = "PROCESS_IDENTITY_INCOMPLETE";
    public const string CandidateNotStable = "CANDIDATE_NOT_STABLE";
    public const string ProofSetSatisfied = "PROOF_SET_SATISFIED";
    public const string MultipleEquivalentCandidates = "MULTIPLE_EQUIVALENT_CANDIDATES";
}

public enum ProcessCorrelationClassification
{
    NoMatch,
    Candidate,
    StartingConfirmed,
    RunningConfirmed,
    Ambiguous
}

public enum ProcessCorrelationEvidenceLevel
{
    None = 0,
    Weak = 1,
    Medium = 2,
    Strong = 3
}

public enum CorrelatedExecutableRole
{
    GamePrimary,
    UnknownCandidate
}

public sealed record ProcessCorrelationRules(
    int SessionId,
    DateTimeOffset HandoffUtc,
    string? ValidatedInstallRoot,
    IReadOnlyList<string> ExpectedExecutableNames,
    IReadOnlyList<string> KnownHelperExecutableNames,
    TimeSpan MinimumStabilityWindow)
{
    public void Validate()
    {
        if (SessionId < 0)
            throw new InvalidDataException("Process correlation session ID must be non-negative.");

        if (MinimumStabilityWindow < TimeSpan.Zero)
            throw new InvalidDataException("Process correlation stability window cannot be negative.");

        if (ExpectedExecutableNames is null)
            throw new InvalidDataException("Expected executable names are required.");

        if (KnownHelperExecutableNames is null)
            throw new InvalidDataException("Known helper executable names are required.");

        var expected = NormalizeExecutableNameSet(ExpectedExecutableNames, "expected executable");
        var helpers = NormalizeExecutableNameSet(KnownHelperExecutableNames, "helper executable");
        if (expected.Overlaps(helpers))
            throw new InvalidDataException("An executable cannot be both expected game evidence and a known helper.");

        if (ValidatedInstallRoot is not null)
            _ = WindowsPathRelationship.NormalizeAbsolute(ValidatedInstallRoot, "validated install root");
    }

    internal static HashSet<string> NormalizeExecutableNameSet(
        IEnumerable<string> values,
        string fieldName)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in values)
        {
            if (string.IsNullOrWhiteSpace(raw))
                throw new InvalidDataException($"{fieldName} cannot be empty.");

            var trimmed = raw.Trim();
            var fileName = Path.GetFileName(trimmed);
            if (!string.Equals(fileName, trimmed, StringComparison.Ordinal))
                throw new InvalidDataException($"{fieldName} must be a file name, not a path: '{raw}'.");

            result.Add(fileName);
        }

        return result;
    }
}

public sealed record CorrelatedProcessEvidence(
    int ProcessId,
    DateTimeOffset? ProcessCreationTimeUtc,
    int? SessionId,
    string? NormalizedImagePath,
    CorrelatedExecutableRole Role,
    ProcessCorrelationEvidenceLevel EvidenceLevel,
    string? ProofSetId,
    DateTimeOffset FirstObservedUtc,
    DateTimeOffset LastObservedUtc);

public sealed record ProcessCorrelationResult(
    ProcessCorrelationClassification Classification,
    ProcessCorrelationEvidenceLevel EvidenceLevel,
    string? ProofSetId,
    string ReasonCode,
    DateTimeOffset ObservedUtc,
    IReadOnlyList<CorrelatedProcessEvidence> CorrelatedProcesses);

/// <summary>
/// Per-launch correlation engine over the bounded Windows process snapshots from IMP-064.
/// It deliberately uses named proof sets instead of an opaque numeric score. PID-only evidence can
/// never satisfy a proof set because process creation time is required to survive PID reuse.
/// </summary>
public sealed class ProcessProofSetCorrelationEngine
{
    private readonly ProcessCorrelationRules _rules;
    private readonly HashSet<ProcessInstanceIdentity> _baselineIdentities;
    private readonly HashSet<int> _baselineUnprotectedProcessIds;
    private readonly HashSet<string> _expectedExecutableNames;
    private readonly HashSet<string> _helperExecutableNames;
    private readonly string? _validatedInstallRoot;
    private readonly Dictionary<ProcessInstanceIdentity, DateTimeOffset> _firstObservedUtc = [];

    public ProcessProofSetCorrelationEngine(
        ProcessEvidenceSnapshot baseline,
        ProcessCorrelationRules rules)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(rules);

        rules.Validate();
        if (baseline.ObservedUtc > rules.HandoffUtc)
        {
            throw new InvalidDataException(
                "Process correlation baseline must be captured at or before the launch handoff.");
        }

        _rules = rules;
        _expectedExecutableNames = ProcessCorrelationRules.NormalizeExecutableNameSet(
            rules.ExpectedExecutableNames,
            "expected executable");
        _helperExecutableNames = ProcessCorrelationRules.NormalizeExecutableNameSet(
            rules.KnownHelperExecutableNames,
            "helper executable");
        _validatedInstallRoot = rules.ValidatedInstallRoot is null
            ? null
            : WindowsPathRelationship.NormalizeAbsolute(rules.ValidatedInstallRoot, "validated install root");
        _baselineIdentities = baseline.Processes
            .Where(static process => process.ReuseProtectedIdentity is not null)
            .Select(static process => process.ReuseProtectedIdentity!)
            .ToHashSet();
        _baselineUnprotectedProcessIds = baseline.Processes
            .Where(static process => process.ProcessId > 0 && process.ReuseProtectedIdentity is null)
            .Select(static process => process.ProcessId)
            .ToHashSet();
    }

    public ProcessCorrelationResult Observe(ProcessEvidenceSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (current.ObservedUtc < _rules.HandoffUtc)
            throw new InvalidDataException("Launch correlation evidence cannot predate the launch handoff.");

        var proven = new List<Candidate>();
        var incomplete = new List<Candidate>();
        var liveIdentities = new HashSet<ProcessInstanceIdentity>();

        foreach (var process in current.Processes)
        {
            if (process.SessionId != _rules.SessionId)
                continue;

            var imagePath = TryNormalizeImagePath(process.ImagePath);
            var executableName = imagePath is null ? null : Path.GetFileName(imagePath);
            var expectedExecutable = executableName is not null && _expectedExecutableNames.Contains(executableName);
            var helperExecutable = executableName is not null && _helperExecutableNames.Contains(executableName);
            var insideInstallRoot = imagePath is not null
                && _validatedInstallRoot is not null
                && WindowsPathRelationship.IsDescendantFile(imagePath, _validatedInstallRoot);

            var identity = process.ReuseProtectedIdentity;
            if (identity is null)
            {
                // Exact-name/path evidence can still be surfaced as a candidate for diagnostics, but
                // it cannot prove a new process because PID reuse cannot be excluded.
                if (expectedExecutable && insideInstallRoot)
                {
                    incomplete.Add(new Candidate(
                        process,
                        imagePath,
                        CorrelatedExecutableRole.GamePrimary,
                        ProcessCorrelationEvidenceLevel.Weak,
                        ProofSetId: null,
                        current.ObservedUtc));
                }

                continue;
            }

            // If this PID was present in the baseline but Windows did not expose its creation time,
            // the later complete identity cannot safely be declared new. A true PID reuse is only
            // distinguishable when both sides have a creation-time identity.
            if (_baselineUnprotectedProcessIds.Contains(process.ProcessId))
            {
                if (insideInstallRoot && !helperExecutable)
                {
                    incomplete.Add(new Candidate(
                        process,
                        imagePath,
                        expectedExecutable
                            ? CorrelatedExecutableRole.GamePrimary
                            : CorrelatedExecutableRole.UnknownCandidate,
                        ProcessCorrelationEvidenceLevel.Weak,
                        ProofSetId: null,
                        current.ObservedUtc));
                }

                continue;
            }

            liveIdentities.Add(identity);
            if (_baselineIdentities.Contains(identity))
                continue;

            if (!_firstObservedUtc.TryGetValue(identity, out var firstObservedUtc))
            {
                firstObservedUtc = current.ObservedUtc;
                _firstObservedUtc.Add(identity, firstObservedUtc);
            }

            if (expectedExecutable && insideInstallRoot)
            {
                proven.Add(new Candidate(
                    process,
                    imagePath,
                    CorrelatedExecutableRole.GamePrimary,
                    ProcessCorrelationEvidenceLevel.Strong,
                    ProcessProofSetIds.CuratedExecutableV1,
                    firstObservedUtc));
                continue;
            }

            if (insideInstallRoot && !helperExecutable)
            {
                proven.Add(new Candidate(
                    process,
                    imagePath,
                    CorrelatedExecutableRole.UnknownCandidate,
                    ProcessCorrelationEvidenceLevel.Medium,
                    ProcessProofSetIds.InstallRootGenericV1,
                    firstObservedUtc));
            }
        }

        foreach (var identity in _firstObservedUtc.Keys.Where(identity => !liveIdentities.Contains(identity)).ToArray())
            _firstObservedUtc.Remove(identity);

        if (proven.Count == 0)
        {
            if (incomplete.Count == 0)
            {
                return new ProcessCorrelationResult(
                    ProcessCorrelationClassification.NoMatch,
                    ProcessCorrelationEvidenceLevel.None,
                    ProofSetId: null,
                    ProcessCorrelationReasonCodes.NoSupportedProof,
                    current.ObservedUtc,
                    Array.Empty<CorrelatedProcessEvidence>());
            }

            return new ProcessCorrelationResult(
                ProcessCorrelationClassification.Candidate,
                ProcessCorrelationEvidenceLevel.Weak,
                ProofSetId: null,
                ProcessCorrelationReasonCodes.ProcessIdentityIncomplete,
                current.ObservedUtc,
                incomplete.Select(candidate => candidate.ToEvidence(current.ObservedUtc)).ToArray());
        }

        var strongestLevel = proven.Max(static candidate => candidate.EvidenceLevel);
        var strongest = proven
            .Where(candidate => candidate.EvidenceLevel == strongestLevel)
            .OrderBy(static candidate => candidate.Process.ProcessId)
            .ToArray();

        if (strongest.Length > 1)
        {
            return new ProcessCorrelationResult(
                ProcessCorrelationClassification.Ambiguous,
                strongestLevel,
                ProofSetId: null,
                ProcessCorrelationReasonCodes.MultipleEquivalentCandidates,
                current.ObservedUtc,
                strongest.Select(candidate => candidate.ToEvidence(current.ObservedUtc)).ToArray());
        }

        var winner = strongest[0];
        var stable = current.ObservedUtc - winner.FirstObservedUtc >= _rules.MinimumStabilityWindow;
        return new ProcessCorrelationResult(
            stable
                ? ProcessCorrelationClassification.RunningConfirmed
                : ProcessCorrelationClassification.StartingConfirmed,
            winner.EvidenceLevel,
            winner.ProofSetId,
            stable
                ? ProcessCorrelationReasonCodes.ProofSetSatisfied
                : ProcessCorrelationReasonCodes.CandidateNotStable,
            current.ObservedUtc,
            proven
                .OrderByDescending(static candidate => candidate.EvidenceLevel)
                .ThenBy(static candidate => candidate.Process.ProcessId)
                .Select(candidate => candidate.ToEvidence(current.ObservedUtc))
                .ToArray());
    }

    private static string? TryNormalizeImagePath(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            return null;

        try
        {
            return WindowsPathRelationship.NormalizeAbsolute(imagePath, "process image path");
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private sealed record Candidate(
        ProcessEvidenceObservation Process,
        string? ImagePath,
        CorrelatedExecutableRole Role,
        ProcessCorrelationEvidenceLevel EvidenceLevel,
        string? ProofSetId,
        DateTimeOffset FirstObservedUtc)
    {
        public CorrelatedProcessEvidence ToEvidence(DateTimeOffset observedUtc)
            => new(
                Process.ProcessId,
                Process.ProcessCreationTimeUtc,
                Process.SessionId,
                ImagePath,
                Role,
                EvidenceLevel,
                ProofSetId,
                FirstObservedUtc,
                observedUtc);
    }
}

internal static class WindowsPathRelationship
{
    public static string NormalizeAbsolute(string path, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidDataException($"{fieldName} cannot be empty.");

        try
        {
            var normalized = Path.GetFullPath(path.Trim().Replace('/', '\\'));
            if (!Path.IsPathFullyQualified(normalized))
                throw new InvalidDataException($"{fieldName} must be an absolute path.");

            return Path.TrimEndingDirectorySeparator(normalized);
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            throw new InvalidDataException($"{fieldName} is not a valid Windows path.", exception);
        }
    }

    public static bool IsDescendantFile(string candidatePath, string validatedRoot)
    {
        string candidate;
        string root;
        try
        {
            candidate = NormalizeAbsolute(candidatePath, "process image path");
            root = NormalizeAbsolute(validatedRoot, "validated install root");
        }
        catch (InvalidDataException)
        {
            return false;
        }

        string relative;
        try
        {
            relative = Path.GetRelativePath(root, candidate);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (relative.Length == 0 || relative == "." || Path.IsPathRooted(relative))
            return false;

        return !relative.Equals("..", StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }
}
