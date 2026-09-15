using SplitOS.RuntimeHost.WindowsContext;

namespace SplitOS.RuntimeHost.GameRuntime;

public sealed record SteamCorrelationPolicy(
    IReadOnlyList<string> ExpectedExecutableNames,
    IReadOnlyList<ProcessReplacementRule> AllowedReplacementRules)
{
    public SteamCorrelationPolicy Normalize()
    {
        var expected = ProcessCorrelationRules.NormalizeExecutableNameSet(
                ExpectedExecutableNames ?? Array.Empty<string>(),
                "Steam expected executable")
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var replacements = (AllowedReplacementRules ?? Array.Empty<ProcessReplacementRule>())
            .Select(rule => rule ?? throw new InvalidDataException("Steam replacement rule cannot be null."))
            .Select(rule => new ProcessReplacementRule(
                ProcessExitCorrelationRules.NormalizeExecutableName(
                    rule.SourceExecutableName,
                    "Steam replacement source executable"),
                ProcessExitCorrelationRules.NormalizeExecutableName(
                    rule.ReplacementExecutableName,
                    "Steam replacement target executable")))
            .Distinct()
            .OrderBy(static rule => rule.SourceExecutableName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static rule => rule.ReplacementExecutableName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return this with
        {
            ExpectedExecutableNames = Array.AsReadOnly(expected),
            AllowedReplacementRules = Array.AsReadOnly(replacements)
        };
    }
}

public interface ISteamCorrelationPolicyProvider
{
    SteamCorrelationPolicy GetPolicy(string canonicalAppId);
}

/// <summary>
/// Production baseline contains no curated per-title executable assumptions yet. Generic Steam
/// install-root proof remains available; release-owned curated mappings can be added independently.
/// </summary>
public sealed class DefaultSteamCorrelationPolicyProvider : ISteamCorrelationPolicyProvider
{
    private static readonly SteamCorrelationPolicy Empty = new(
        Array.Empty<string>(),
        Array.Empty<ProcessReplacementRule>());

    public SteamCorrelationPolicy GetPolicy(string canonicalAppId)
    {
        if (!SteamRunUri.TryCanonicalAppId(canonicalAppId, out _))
            throw new InvalidDataException("Steam correlation policy requires a canonical AppID.");
        return Empty;
    }
}

/// <summary>
/// IMP-084 adapter layer. It does not invent another process-correlation algorithm: Steam launch
/// evidence is translated into the IMP-075 proof-set engine and exit tracker. Steam client/helper
/// processes are diagnostic-only and never establish GAME_RUNNING or keep a Game Session alive.
/// </summary>
public sealed class SteamCorrelationAdapter : IGameClientAdapter
{
    public const string AdapterVersion = "steam-adapter/4";
    public const string ProcessCorrelationMechanismId = "STEAM_PROCESS_PROOF_SET_V1";
    public const string ExitCorrelationMechanismId = "STEAM_PROCESS_EXIT_V1";
    public const int ReplacementWindowMs = 2_000;

    private static readonly string[] KnownSteamHelperExecutableNames =
    [
        "steam.exe",
        "steamwebhelper.exe",
        "steamservice.exe",
        "gameoverlayui.exe",
        "steamerrorreporter.exe"
    ];

    private readonly SteamProtocolLaunchAdapter _inner;
    private readonly IProcessEvidenceSnapshotReader _processReader;
    private readonly ISteamCorrelationPolicyProvider _policyProvider;
    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, CorrelationSession> _sessions = [];

    public SteamCorrelationAdapter(
        SteamProtocolLaunchAdapter inner,
        IProcessEvidenceSnapshotReader processReader,
        ISteamCorrelationPolicyProvider policyProvider,
        TimeProvider? timeProvider = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _processReader = processReader ?? throw new ArgumentNullException(nameof(processReader));
        _policyProvider = policyProvider ?? throw new ArgumentNullException(nameof(policyProvider));
        _timeProvider = timeProvider ?? TimeProvider.System;
        Descriptor = CreateDescriptor(inner.Descriptor).Normalize();
    }

    public GameClientAdapterDescriptor Descriptor { get; }

    public GameClientCompatibilitySnapshot GetCompatibilityStatus(GameClientCompatibilityContext context)
    {
        var normalizedContext = (context ?? throw new ArgumentNullException(nameof(context))).Normalize();
        var statuses = Descriptor.Capabilities
            .Select(capability =>
            {
                var unsupportedWindows = capability.MinimumWindowsBuild is int minimumBuild
                    && normalizedContext.WindowsBuild < minimumBuild;
                return new GameClientCapabilityStatus(
                    capability.CapabilityId,
                    unsupportedWindows ? GameMechanismStatus.Unsupported : capability.MechanismStatus,
                    capability.MechanismId,
                    unsupportedWindows ? "WINDOWS_BUILD_UNSUPPORTED" : capability.NotesCode);
            })
            .ToArray();

        return new GameClientCompatibilitySnapshot(
            GameClientType.Steam,
            Descriptor.AdapterVersion,
            statuses,
            _timeProvider.GetUtcNow(),
            Descriptor.CompatibilityPolicyId,
            normalizedContext.ObservedClientVersion).Normalize(Descriptor);
    }

    public Task<GameClientDiscoveryEvidence> DiscoverClientAsync(
        int sessionId,
        GameClientFreshnessRequirement freshnessRequirement,
        CancellationToken cancellationToken)
        => _inner.DiscoverClientAsync(sessionId, freshnessRequirement, cancellationToken);

    public Task<GameClientLibraryRefreshResult> RefreshLibraryAsync(
        GameClientLibraryRefreshRequest request,
        CancellationToken cancellationToken)
        => _inner.RefreshLibraryAsync(request, cancellationToken);

    public Task<AdapterGameProjection?> ResolveInstallationAsync(
        ExternalGameIdentity externalGameIdentity,
        GameClientFreshnessRequirement freshnessRequirement,
        CancellationToken cancellationToken)
        => _inner.ResolveInstallationAsync(externalGameIdentity, freshnessRequirement, cancellationToken);

    public async Task<PreparedClientLaunch> PrepareLaunchAsync(
        GameClientLaunchRequest request,
        AdapterGameProjection currentProjection,
        GameClientDiscoveryEvidence currentClientEvidence,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedRequest = (request ?? throw new ArgumentNullException(nameof(request)))
            .Normalize(GameClientType.Steam);
        var prepared = await _inner.PrepareLaunchAsync(
            normalizedRequest,
            currentProjection,
            currentClientEvidence,
            cancellationToken).ConfigureAwait(false);

        var appId = ValidateCanonicalSteamIdentity(prepared.NormalizedExternalGameIdentity);
        var policy = (_policyProvider.GetPolicy(appId)
            ?? throw new InvalidDataException("Steam correlation policy provider returned no policy."))
            .Normalize();
        var baseline = _processReader.Read()
            ?? throw new InvalidDataException("Steam launch baseline process snapshot is required.");
        if (baseline.ObservedUtc > _timeProvider.GetUtcNow())
            throw new InvalidDataException("Steam launch baseline process snapshot is from the future.");

        var session = new CorrelationSession(
            normalizedRequest.LaunchOperationId,
            normalizedRequest.UserSessionId,
            appId,
            baseline,
            policy,
            prepared);
        lock (_gate)
            _sessions[prepared.LaunchOperationId] = session;

        return prepared;
    }

    public async Task<GameClientLaunchHandoffResult> SubmitLaunchAsync(
        PreparedClientLaunch preparedLaunch,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(preparedLaunch);

        CorrelationSession? session;
        lock (_gate)
            _sessions.TryGetValue(preparedLaunch.LaunchOperationId, out session);

        var result = await _inner.SubmitLaunchAsync(preparedLaunch, cancellationToken).ConfigureAwait(false);
        if (result.ResultCode != GameClientLaunchHandoffResultCode.HandoffAccepted)
        {
            lock (_gate)
                _sessions.Remove(preparedLaunch.LaunchOperationId);
            return result;
        }

        if (session is null)
            return result;

        lock (session.Gate)
        {
            try
            {
                var rules = CreateCorrelationRules(session, preparedLaunch, result.SubmittedAtUtc);
                session.HandoffUtc = result.SubmittedAtUtc;
                session.CorrelationEngine = new ProcessProofSetCorrelationEngine(session.Baseline, rules);
                session.InitializationFailureCode = null;
            }
            catch (InvalidDataException)
            {
                // Handoff already succeeded. Never rewrite an accepted OS dispatch into a launch failure;
                // observation will surface the missing correlation state explicitly instead.
                session.CorrelationEngine = null;
                session.InitializationFailureCode = "STEAM_CORRELATION_INITIALIZATION_FAILED";
            }
        }

        return result;
    }

    public Task<GameClientObservationResult> ObserveLaunchAsync(
        PreparedClientLaunch preparedLaunch,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(preparedLaunch);

        var session = FindSession(preparedLaunch.LaunchOperationId);
        if (session is null)
        {
            return Task.FromResult(CorrelationLostObservation(
                _timeProvider.GetUtcNow(),
                "STEAM_CORRELATION_STATE_MISSING"));
        }

        lock (session.Gate)
        {
            if (session.CorrelationEngine is null || session.HandoffUtc is null)
            {
                return Task.FromResult(CorrelationLostObservation(
                    _timeProvider.GetUtcNow(),
                    session.InitializationFailureCode ?? "STEAM_HANDOFF_NOT_CORRELATED"));
            }

            var snapshot = _processReader.Read()
                ?? throw new InvalidDataException("Steam launch process snapshot is required.");
            var postHandoffSnapshot = FilterToPostHandoffEvidence(snapshot, session.HandoffUtc.Value);
            var correlation = session.CorrelationEngine.Observe(postHandoffSnapshot);
            session.LastCorrelation = correlation;

            if (correlation.Classification == ProcessCorrelationClassification.RunningConfirmed
                && session.ExitTracker is null)
            {
                session.ExitTracker = new ProcessExitCorrelationTracker(
                    correlation,
                    CreateExitRules(session, preparedLaunch));
            }

            return Task.FromResult(MapLaunchObservation(correlation));
        }
    }

    public Task<GameClientExitObservationResult> ObserveExitAsync(
        PreparedClientLaunch preparedLaunch,
        IReadOnlyList<GameClientCorrelatedProcessEvidence> currentCorrelation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(preparedLaunch);
        _ = (currentCorrelation ?? throw new ArgumentNullException(nameof(currentCorrelation)))
            .Select(process => (process ?? throw new InvalidDataException("Current Steam correlation cannot contain null process evidence.")).Normalize())
            .ToArray();

        var session = FindSession(preparedLaunch.LaunchOperationId);
        if (session is null)
        {
            return Task.FromResult(new GameClientExitObservationResult(
                GameClientExitObservationClassification.CorrelationLost,
                Array.Empty<GameClientCorrelatedProcessEvidence>(),
                _timeProvider.GetUtcNow(),
                "STEAM_CORRELATION_STATE_MISSING").Normalize());
        }

        lock (session.Gate)
        {
            if (session.ExitTracker is null)
            {
                return Task.FromResult(new GameClientExitObservationResult(
                    GameClientExitObservationClassification.CorrelationLost,
                    Array.Empty<GameClientCorrelatedProcessEvidence>(),
                    _timeProvider.GetUtcNow(),
                    "STEAM_EXIT_TRACKER_NOT_READY").Normalize());
            }

            var snapshot = _processReader.Read()
                ?? throw new InvalidDataException("Steam exit process snapshot is required.");
            var exit = session.ExitTracker.Observe(snapshot);
            var mapped = MapExitObservation(exit);
            if (exit.Classification is ProcessExitCorrelationClassification.ExitConfirmed
                or ProcessExitCorrelationClassification.CorrelationLost)
            {
                lock (_gate)
                    _sessions.Remove(preparedLaunch.LaunchOperationId);
            }

            return Task.FromResult(mapped);
        }
    }

    public void Invalidate(GameClientLibraryRefreshReason reason)
        => _inner.Invalidate(reason);

    private CorrelationSession? FindSession(Guid launchOperationId)
    {
        lock (_gate)
            return _sessions.TryGetValue(launchOperationId, out var session) ? session : null;
    }

    private static GameClientAdapterDescriptor CreateDescriptor(GameClientAdapterDescriptor innerDescriptor)
    {
        var normalized = innerDescriptor.Normalize();
        var capabilities = normalized.Capabilities
            .Select(capability => capability.CapabilityId switch
            {
                GameClientCapabilityId.GameProcessCorrelation => capability with
                {
                    MechanismStatus = GameMechanismStatus.SupportedVersionGated,
                    MechanismId = ProcessCorrelationMechanismId,
                    MinimumWindowsBuild = SteamClientAdapter.MinimumSupportedWindowsBuild,
                    NotesCode = null
                },
                GameClientCapabilityId.GameExitCorrelation => capability with
                {
                    MechanismStatus = GameMechanismStatus.SupportedOsMechanism,
                    MechanismId = ExitCorrelationMechanismId,
                    MinimumWindowsBuild = SteamClientAdapter.MinimumSupportedWindowsBuild,
                    NotesCode = null
                },
                _ => capability
            })
            .ToArray();

        return normalized with
        {
            AdapterVersion = AdapterVersion,
            Capabilities = Array.AsReadOnly(capabilities),
            // Release verification is still required before calling the full Steam vertical TARGET_SUPPORTED_V1.
            SupportStatus = GameClientSupportStatus.PartialSupportedV1
        };
    }

    private static ProcessCorrelationRules CreateCorrelationRules(
        CorrelationSession session,
        PreparedClientLaunch prepared,
        DateTimeOffset handoffUtc)
    {
        var observationRules = prepared.RequiredObservationRules.Normalize();
        if (string.IsNullOrWhiteSpace(observationRules.ValidatedInstallRoot))
            throw new InvalidDataException("Steam correlation requires a validated install root.");

        var expected = observationRules.ExpectedExecutableNames
            .Concat(session.Policy.ExpectedExecutableNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ProcessCorrelationRules(
            session.UserSessionId,
            handoffUtc,
            observationRules.ValidatedInstallRoot,
            expected,
            KnownSteamHelperExecutableNames,
            TimeSpan.FromMilliseconds(observationRules.StartStabilityWindowMs));
    }

    private static ProcessExitCorrelationRules CreateExitRules(
        CorrelationSession session,
        PreparedClientLaunch prepared)
    {
        var observationRules = prepared.RequiredObservationRules.Normalize();
        if (string.IsNullOrWhiteSpace(observationRules.ValidatedInstallRoot))
            throw new InvalidDataException("Steam exit correlation requires a validated install root.");

        var grace = TimeSpan.FromMilliseconds(observationRules.ExitGraceWindowMs);
        var replacement = TimeSpan.FromMilliseconds(Math.Min(
            observationRules.ExitGraceWindowMs,
            ReplacementWindowMs));
        return new ProcessExitCorrelationRules(
            session.UserSessionId,
            observationRules.ValidatedInstallRoot,
            grace,
            replacement,
            session.Policy.AllowedReplacementRules,
            KnownSteamHelperExecutableNames);
    }

    private static ProcessEvidenceSnapshot FilterToPostHandoffEvidence(
        ProcessEvidenceSnapshot snapshot,
        DateTimeOffset handoffUtc)
    {
        if (snapshot.ObservedUtc < handoffUtc)
            throw new InvalidDataException("Steam process observation predates protocol handoff.");

        // Complete identities created before handoff cannot satisfy Steam launch proof even when they
        // appeared after the baseline snapshot. Incomplete identities remain visible only as weak evidence.
        var processes = snapshot.Processes
            .Where(process => process.ProcessCreationTimeUtc is null
                || process.ProcessCreationTimeUtc.Value >= handoffUtc)
            .ToArray();
        return snapshot with { Processes = Array.AsReadOnly(processes) };
    }

    private static GameClientObservationResult MapLaunchObservation(ProcessCorrelationResult correlation)
    {
        var classification = correlation.Classification switch
        {
            ProcessCorrelationClassification.NoMatch => GameClientObservationClassification.NoMatch,
            ProcessCorrelationClassification.Candidate => GameClientObservationClassification.Candidate,
            ProcessCorrelationClassification.StartingConfirmed => GameClientObservationClassification.StartingConfirmed,
            ProcessCorrelationClassification.RunningConfirmed => GameClientObservationClassification.RunningConfirmed,
            ProcessCorrelationClassification.Ambiguous => GameClientObservationClassification.Ambiguous,
            _ => GameClientObservationClassification.CorrelationLost
        };
        var processes = correlation.CorrelatedProcesses
            .Where(static process => process.ProcessCreationTimeUtc is not null && process.SessionId is not null)
            .Select(MapProcessEvidence)
            .ToArray();

        return new GameClientObservationResult(
            classification,
            MapEvidenceLevel(correlation.EvidenceLevel),
            processes,
            ReplacementDetected: false,
            correlation.ObservedUtc,
            correlation.ReasonCode).Normalize();
    }

    private static GameClientObservationResult CorrelationLostObservation(
        DateTimeOffset observedAtUtc,
        string diagnosticsCode)
        => new(
            GameClientObservationClassification.CorrelationLost,
            GameClientEvidenceLevel.Weak,
            Array.Empty<GameClientCorrelatedProcessEvidence>(),
            ReplacementDetected: false,
            observedAtUtc,
            diagnosticsCode);

    private static GameClientExitObservationResult MapExitObservation(ProcessExitCorrelationResult exit)
    {
        var classification = exit.Classification switch
        {
            ProcessExitCorrelationClassification.StillRunning => GameClientExitObservationClassification.StillRunning,
            ProcessExitCorrelationClassification.ExitCandidate => GameClientExitObservationClassification.ExitCandidate,
            ProcessExitCorrelationClassification.ReplacementProcessFound => GameClientExitObservationClassification.ReplacementProcessFound,
            ProcessExitCorrelationClassification.ExitConfirmed => GameClientExitObservationClassification.ExitConfirmed,
            _ => GameClientExitObservationClassification.CorrelationLost
        };

        var processes = new[] { exit.TrackedPrimary, exit.Replacement }
            .Where(static process => process is not null)
            .Select(static process => MapProcessEvidence(process!))
            .GroupBy(static process => (process.Pid, process.CreationTimeUtc))
            .Select(static group => group.First())
            .ToArray();
        return new GameClientExitObservationResult(
            classification,
            processes,
            exit.ObservedUtc,
            exit.ReasonCode).Normalize();
    }

    private static GameClientCorrelatedProcessEvidence MapProcessEvidence(CorrelatedProcessEvidence process)
    {
        if (process.ProcessCreationTimeUtc is null || process.SessionId is null)
            throw new InvalidDataException("Steam correlated process requires reuse-protected identity and session evidence.");

        return new GameClientCorrelatedProcessEvidence(
            process.ProcessId,
            process.ProcessCreationTimeUtc.Value,
            process.SessionId.Value,
            process.Role == CorrelatedExecutableRole.GamePrimary
                ? GameClientExecutableRole.GamePrimary
                : GameClientExecutableRole.UnknownCandidate,
            MapEvidenceLevel(process.EvidenceLevel),
            process.FirstObservedUtc,
            process.LastObservedUtc,
            process.NormalizedImagePath).Normalize();
    }

    private static GameClientEvidenceLevel MapEvidenceLevel(ProcessCorrelationEvidenceLevel evidenceLevel)
        => evidenceLevel switch
        {
            ProcessCorrelationEvidenceLevel.Strong => GameClientEvidenceLevel.Strong,
            ProcessCorrelationEvidenceLevel.Medium => GameClientEvidenceLevel.Medium,
            _ => GameClientEvidenceLevel.Weak
        };

    private static string ValidateCanonicalSteamIdentity(ExternalGameIdentity identity)
    {
        var normalized = (identity ?? throw new InvalidDataException("Steam external identity is required.")).Normalize();
        if (normalized.ClientType != GameClientType.Steam
            || !string.Equals(normalized.ExternalIdKind, SteamClientAdapter.ExternalIdKind, StringComparison.Ordinal)
            || !SteamRunUri.TryCanonicalAppId(normalized.ExternalId, out var appId)
            || appId is null)
            throw new InvalidDataException("Steam correlation requires canonical STEAM_APP_ID identity.");
        return appId;
    }

    private sealed class CorrelationSession(
        Guid launchOperationId,
        int userSessionId,
        string appId,
        ProcessEvidenceSnapshot baseline,
        SteamCorrelationPolicy policy,
        PreparedClientLaunch prepared)
    {
        public object Gate { get; } = new();
        public Guid LaunchOperationId { get; } = launchOperationId;
        public int UserSessionId { get; } = userSessionId;
        public string AppId { get; } = appId;
        public ProcessEvidenceSnapshot Baseline { get; } = baseline;
        public SteamCorrelationPolicy Policy { get; } = policy;
        public PreparedClientLaunch Prepared { get; } = prepared;
        public DateTimeOffset? HandoffUtc { get; set; }
        public ProcessProofSetCorrelationEngine? CorrelationEngine { get; set; }
        public ProcessCorrelationResult? LastCorrelation { get; set; }
        public ProcessExitCorrelationTracker? ExitTracker { get; set; }
        public string? InitializationFailureCode { get; set; }
    }
}
