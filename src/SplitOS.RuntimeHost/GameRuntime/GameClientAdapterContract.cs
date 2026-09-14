namespace SplitOS.RuntimeHost.GameRuntime;

public enum GameClientCapabilityId
{
    ClientDiscovery,
    ClientVersionEvidence,
    LibraryDiscovery,
    InstallationEvidence,
    LaunchIdentityResolution,
    LaunchEligibilityEvidence,
    GameLaunch,
    ClientInteractionEvidence,
    GameProcessCorrelation,
    GameExitCorrelation,
    AccountContextEvidence,
    UpdateStateEvidence
}

public enum GameClientDiscoveryAvailability
{
    AvailableVerified,
    NotFoundVerified,
    AvailableUnverifiedVersion,
    StaleLastKnown,
    Unknown
}

public enum GameClientFreshnessRequirement
{
    AllowCached,
    FreshIfAvailable,
    FreshRequired
}

public enum GameClientLibraryRefreshReason
{
    RuntimeStart,
    GameLaunchRequest,
    ClientStarted,
    ClientExited,
    ClientVersionChanged,
    LocalMetadataChanged,
    UserRefresh,
    StaleCache,
    PostGameExit
}

public enum GameClientLibraryRefreshResultCode
{
    Refreshed,
    NoChange,
    ClientUnavailable,
    SourceUnavailable,
    SourceSchemaUnknown,
    ParseFailed,
    Timeout,
    Partial
}

public enum GameClientLaunchHandoffResultCode
{
    HandoffAccepted,
    HandoffRejected,
    ClientNotAvailable,
    GameNotInstalled,
    ClientInteractionRequired,
    AuthRequired,
    UpdateRequired,
    ClientVersionUnsupported,
    LaunchIdentityInvalid,
    StaleEvidence,
    MechanismUnavailable,
    UnknownFailure
}

public enum GameClientObservationClassification
{
    NoMatch,
    Candidate,
    StartingConfirmed,
    RunningConfirmed,
    Ambiguous,
    CorrelationLost
}

public enum GameClientExitObservationClassification
{
    StillRunning,
    ExitCandidate,
    ExitConfirmed,
    ReplacementProcessFound,
    CorrelationLost
}

public enum GameClientEvidenceLevel
{
    Strong,
    Medium,
    Weak
}

public enum GameClientExecutableRole
{
    Bootstrap,
    PublisherLauncher,
    GamePrimary,
    GameSecondary,
    UnknownCandidate
}

public sealed record GameClientAdapterCapability(
    GameClientCapabilityId CapabilityId,
    GameMechanismStatus MechanismStatus,
    string MechanismId,
    int? MinimumWindowsBuild = null,
    string? MinimumClientVersion = null,
    string? MaximumValidatedClientVersion = null,
    bool RequiresFreshLibraryEvidence = false,
    string? NotesCode = null)
{
    public GameClientAdapterCapability Normalize()
    {
        if (MinimumWindowsBuild is < 0)
            throw new InvalidDataException("Minimum Windows build cannot be negative.");

        return this with
        {
            MechanismId = AdapterContractNormalization.Required(MechanismId, nameof(MechanismId)),
            MinimumClientVersion = AdapterContractNormalization.Optional(MinimumClientVersion),
            MaximumValidatedClientVersion = AdapterContractNormalization.Optional(MaximumValidatedClientVersion),
            NotesCode = AdapterContractNormalization.Optional(NotesCode)
        };
    }
}

public sealed record GameClientAdapterDescriptor(
    GameClientType ClientType,
    string AdapterVersion,
    IReadOnlyList<string> SupportedExternalIdKinds,
    IReadOnlyList<GameClientAdapterCapability> Capabilities,
    string CompatibilityPolicyId,
    GameClientSupportStatus SupportStatus)
{
    public GameClientAdapterDescriptor Normalize()
    {
        var externalKinds = (SupportedExternalIdKinds ?? throw new InvalidDataException("Supported external ID kinds are required."))
            .Select(value => AdapterContractNormalization.Required(value, nameof(SupportedExternalIdKinds)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        var normalizedCapabilities = (Capabilities ?? throw new InvalidDataException("Adapter capabilities are required."))
            .Select(capability => (capability ?? throw new InvalidDataException("Adapter capability cannot be null.")).Normalize())
            .ToArray();

        var duplicate = normalizedCapabilities
            .GroupBy(capability => capability.CapabilityId)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidDataException($"Capability {duplicate.Key} is declared more than once.");

        var required = Enum.GetValues<GameClientCapabilityId>();
        var present = normalizedCapabilities.Select(capability => capability.CapabilityId).ToHashSet();
        var missing = required.Where(capability => !present.Contains(capability)).ToArray();
        if (missing.Length > 0)
            throw new InvalidDataException($"Every adapter capability requires an explicit status. Missing: {string.Join(", ", missing)}.");

        return this with
        {
            AdapterVersion = AdapterContractNormalization.Required(AdapterVersion, nameof(AdapterVersion)),
            SupportedExternalIdKinds = Array.AsReadOnly(externalKinds),
            Capabilities = Array.AsReadOnly(normalizedCapabilities.OrderBy(capability => capability.CapabilityId).ToArray()),
            CompatibilityPolicyId = AdapterContractNormalization.Required(CompatibilityPolicyId, nameof(CompatibilityPolicyId))
        };
    }

    public GameClientAdapterCapability Capability(GameClientCapabilityId capabilityId)
        => Capabilities.Single(capability => capability.CapabilityId == capabilityId);
}

public sealed record GameClientCapabilityStatus(
    GameClientCapabilityId CapabilityId,
    GameMechanismStatus MechanismStatus,
    string MechanismId,
    string? NotesCode = null)
{
    public GameClientCapabilityStatus Normalize()
        => this with
        {
            MechanismId = AdapterContractNormalization.Required(MechanismId, nameof(MechanismId)),
            NotesCode = AdapterContractNormalization.Optional(NotesCode)
        };
}

public sealed record GameClientCompatibilityContext(
    int WindowsBuild,
    string? ObservedClientVersion,
    IReadOnlyList<string>? ObservedSchemaSignatures = null)
{
    public GameClientCompatibilityContext Normalize()
    {
        if (WindowsBuild < 0)
            throw new InvalidDataException("Windows build cannot be negative.");

        var signatures = (ObservedSchemaSignatures ?? Array.Empty<string>())
            .Select(value => AdapterContractNormalization.Required(value, nameof(ObservedSchemaSignatures)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        return this with
        {
            ObservedClientVersion = AdapterContractNormalization.Optional(ObservedClientVersion),
            ObservedSchemaSignatures = Array.AsReadOnly(signatures)
        };
    }
}

public sealed record GameClientCompatibilitySnapshot(
    GameClientType ClientType,
    string AdapterVersion,
    IReadOnlyList<GameClientCapabilityStatus> Capabilities,
    DateTimeOffset EvaluatedAtUtc,
    string CompatibilityPolicyId,
    string? ObservedClientVersion = null)
{
    public GameClientCompatibilitySnapshot Normalize(GameClientAdapterDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (ClientType != descriptor.ClientType)
            throw new InvalidDataException("Compatibility snapshot client type does not match its adapter descriptor.");
        if (!string.Equals(AdapterVersion?.Trim(), descriptor.AdapterVersion, StringComparison.Ordinal))
            throw new InvalidDataException("Compatibility snapshot adapter version does not match its adapter descriptor.");
        if (!string.Equals(CompatibilityPolicyId?.Trim(), descriptor.CompatibilityPolicyId, StringComparison.Ordinal))
            throw new InvalidDataException("Compatibility snapshot policy does not match its adapter descriptor.");
        if (EvaluatedAtUtc == default)
            throw new InvalidDataException("Compatibility snapshot requires an evaluation timestamp.");

        var normalized = (Capabilities ?? throw new InvalidDataException("Compatibility capability statuses are required."))
            .Select(status => (status ?? throw new InvalidDataException("Compatibility capability status cannot be null.")).Normalize())
            .ToArray();
        var duplicate = normalized.GroupBy(status => status.CapabilityId).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidDataException($"Compatibility capability {duplicate.Key} is reported more than once.");

        foreach (var descriptorCapability in descriptor.Capabilities)
        {
            var status = normalized.SingleOrDefault(candidate => candidate.CapabilityId == descriptorCapability.CapabilityId)
                ?? throw new InvalidDataException($"Compatibility snapshot omitted capability {descriptorCapability.CapabilityId}.");
            if (!string.Equals(status.MechanismId, descriptorCapability.MechanismId, StringComparison.Ordinal))
                throw new InvalidDataException($"Compatibility snapshot changed mechanism identity for {status.CapabilityId}.");
        }

        if (normalized.Length != descriptor.Capabilities.Count)
            throw new InvalidDataException("Compatibility snapshot contains capabilities not declared by the adapter descriptor.");

        return this with
        {
            AdapterVersion = descriptor.AdapterVersion,
            CompatibilityPolicyId = descriptor.CompatibilityPolicyId,
            ObservedClientVersion = AdapterContractNormalization.Optional(ObservedClientVersion),
            Capabilities = Array.AsReadOnly(normalized.OrderBy(status => status.CapabilityId).ToArray())
        };
    }

    public GameClientCapabilityStatus Capability(GameClientCapabilityId capabilityId)
        => Capabilities.Single(capability => capability.CapabilityId == capabilityId);
}

public sealed record GameClientDiscoveryEvidence(
    GameClientType ClientType,
    GameClientDiscoveryAvailability AvailabilityState,
    string MechanismId,
    GameMechanismStatus MechanismStatus,
    DateTimeOffset ObservedAtUtc,
    long SnapshotGeneration,
    string? ExecutableIdentity = null,
    string? ProtocolRegistration = null,
    string? PackageIdentity = null,
    string? ObservedClientVersion = null,
    string? DiagnosticsCode = null)
{
    public GameClientDiscoveryEvidence Normalize(GameClientType expectedClientType)
    {
        if (ClientType != expectedClientType)
            throw new InvalidDataException("Client discovery evidence belongs to a different client type.");
        if (ObservedAtUtc == default)
            throw new InvalidDataException("Client discovery evidence requires an observation timestamp.");
        if (SnapshotGeneration < 0)
            throw new InvalidDataException("Client discovery generation cannot be negative.");

        return this with
        {
            MechanismId = AdapterContractNormalization.Required(MechanismId, nameof(MechanismId)),
            ExecutableIdentity = AdapterContractNormalization.Optional(ExecutableIdentity),
            ProtocolRegistration = AdapterContractNormalization.Optional(ProtocolRegistration),
            PackageIdentity = AdapterContractNormalization.Optional(PackageIdentity),
            ObservedClientVersion = AdapterContractNormalization.Optional(ObservedClientVersion),
            DiagnosticsCode = AdapterContractNormalization.Optional(DiagnosticsCode)
        };
    }
}

public sealed record AdapterLaunchIdentity(
    string Kind,
    int SchemaVersion,
    string Payload,
    DateTimeOffset ObservedAtUtc,
    GameMechanismStatus MechanismStatus)
{
    public AdapterLaunchIdentity Normalize()
    {
        if (SchemaVersion <= 0)
            throw new InvalidDataException("Launch identity schema version must be positive.");
        if (ObservedAtUtc == default)
            throw new InvalidDataException("Launch identity requires an observation timestamp.");
        return this with
        {
            Kind = AdapterContractNormalization.Required(Kind, nameof(Kind)),
            Payload = AdapterContractNormalization.Required(Payload, nameof(Payload))
        };
    }
}

public sealed record AdapterGameProjection(
    ExternalGameIdentity ExternalGameIdentity,
    GameInstallationEvidence Installation,
    AdapterLaunchIdentity? LaunchIdentity,
    IReadOnlyList<string> ExecutableCandidates,
    GameLibrarySourceProvenance SourceProvenance,
    string? DisplayNameEvidence = null)
{
    public AdapterGameProjection Normalize(GameClientType expectedClientType)
    {
        var identity = (ExternalGameIdentity ?? throw new InvalidDataException("External game identity is required.")).Normalize();
        if (identity.ClientType != expectedClientType)
            throw new InvalidDataException("Adapter game projection belongs to a different client type.");

        var candidates = (ExecutableCandidates ?? Array.Empty<string>())
            .Select(value => AdapterContractNormalization.Required(value, nameof(ExecutableCandidates)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return this with
        {
            ExternalGameIdentity = identity,
            Installation = (Installation ?? throw new InvalidDataException("Installation evidence is required.")).Normalize(),
            LaunchIdentity = LaunchIdentity?.Normalize(),
            ExecutableCandidates = Array.AsReadOnly(candidates),
            SourceProvenance = (SourceProvenance ?? throw new InvalidDataException("Source provenance is required.")).Normalize(),
            DisplayNameEvidence = AdapterContractNormalization.Optional(DisplayNameEvidence)
        };
    }
}

public sealed record GameClientLibraryRefreshRequest(
    Guid RefreshId,
    GameClientType ClientType,
    GameClientLibraryRefreshReason Reason,
    GameClientFreshnessRequirement FreshnessRequirement,
    long? PreviousGeneration,
    DateTimeOffset DeadlineUtc)
{
    public GameClientLibraryRefreshRequest Normalize(GameClientType expectedClientType)
    {
        if (RefreshId == Guid.Empty)
            throw new InvalidDataException("Library refresh requires a non-empty refresh ID.");
        if (ClientType != expectedClientType)
            throw new InvalidDataException("Library refresh request targets a different client type.");
        if (PreviousGeneration is < 0)
            throw new InvalidDataException("Previous generation cannot be negative.");
        if (DeadlineUtc == default)
            throw new InvalidDataException("Library refresh requires a deadline.");
        return this;
    }
}

public sealed record GameClientLibraryRefreshResult(
    Guid RefreshId,
    GameClientType ClientType,
    GameClientLibraryRefreshResultCode ResultCode,
    long Generation,
    DateTimeOffset ObservedAtUtc,
    IReadOnlyList<AdapterGameProjection> Records,
    string? ClientVersionObserved = null,
    string? ParserStatus = null,
    IReadOnlyList<string>? MissingEvidenceClasses = null)
{
    public GameClientLibraryRefreshResult Normalize(GameClientLibraryRefreshRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (RefreshId != request.RefreshId || ClientType != request.ClientType)
            throw new InvalidDataException("Library refresh result identity does not match its request.");
        if (Generation < 0)
            throw new InvalidDataException("Library refresh generation cannot be negative.");
        if (ObservedAtUtc == default)
            throw new InvalidDataException("Library refresh result requires an observation timestamp.");

        var records = (Records ?? throw new InvalidDataException("Library refresh records are required."))
            .Select(record => (record ?? throw new InvalidDataException("Library refresh record cannot be null.")).Normalize(ClientType))
            .ToArray();
        var missing = (MissingEvidenceClasses ?? Array.Empty<string>())
            .Select(value => AdapterContractNormalization.Required(value, nameof(MissingEvidenceClasses)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (ResultCode == GameClientLibraryRefreshResultCode.Partial && missing.Length == 0)
            throw new InvalidDataException("PARTIAL refresh result must identify missing evidence classes.");
        if (ResultCode != GameClientLibraryRefreshResultCode.Partial && missing.Length > 0)
            throw new InvalidDataException("Missing evidence classes are valid only for PARTIAL refresh results.");

        return this with
        {
            Records = Array.AsReadOnly(records),
            ClientVersionObserved = AdapterContractNormalization.Optional(ClientVersionObserved),
            ParserStatus = AdapterContractNormalization.Optional(ParserStatus),
            MissingEvidenceClasses = Array.AsReadOnly(missing)
        };
    }
}

public sealed record GameClientLaunchRequest(
    Guid LaunchOperationId,
    Guid CorrelationId,
    string GameId,
    GameClientType ClientType,
    ExternalGameIdentity ExternalGameIdentity,
    AdapterLaunchIdentity ResolvedLaunchIdentity,
    long ExpectedInstallationEvidenceRevision,
    int UserSessionId,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset DeadlineUtc)
{
    public GameClientLaunchRequest Normalize(GameClientType expectedClientType)
    {
        if (LaunchOperationId == Guid.Empty || CorrelationId == Guid.Empty)
            throw new InvalidDataException("Launch operation and correlation IDs must be non-empty.");
        if (ClientType != expectedClientType)
            throw new InvalidDataException("Launch request targets a different client type.");
        var identity = (ExternalGameIdentity ?? throw new InvalidDataException("External game identity is required.")).Normalize();
        if (identity.ClientType != ClientType)
            throw new InvalidDataException("External game identity belongs to a different client type.");
        if (ExpectedInstallationEvidenceRevision < 0)
            throw new InvalidDataException("Expected installation evidence revision cannot be negative.");
        if (UserSessionId < 0)
            throw new InvalidDataException("Windows user session ID cannot be negative.");
        if (RequestedAtUtc == default || DeadlineUtc <= RequestedAtUtc)
            throw new InvalidDataException("Launch request deadline must be after its request timestamp.");

        return this with
        {
            GameId = AdapterContractNormalization.Required(GameId, nameof(GameId)),
            ExternalGameIdentity = identity,
            ResolvedLaunchIdentity = (ResolvedLaunchIdentity ?? throw new InvalidDataException("Resolved launch identity is required.")).Normalize()
        };
    }
}

public sealed record GameClientObservationRules(
    IReadOnlyList<string> ExpectedExecutableNames,
    string? ValidatedInstallRoot,
    string? ExpectedAumid,
    string? ExpectedPackageFamilyName,
    IReadOnlyList<string> KnownBootstrapExecutables,
    IReadOnlyList<string> AllowedProcessReplacementPatterns,
    GameClientEvidenceLevel MinimumRunningEvidence,
    int StartStabilityWindowMs,
    int ExitGraceWindowMs)
{
    public GameClientObservationRules Normalize()
    {
        if (StartStabilityWindowMs < 0 || ExitGraceWindowMs < 0)
            throw new InvalidDataException("Observation windows cannot be negative.");
        return this with
        {
            ExpectedExecutableNames = AdapterContractNormalization.StringSet(ExpectedExecutableNames, nameof(ExpectedExecutableNames), StringComparer.OrdinalIgnoreCase),
            KnownBootstrapExecutables = AdapterContractNormalization.StringSet(KnownBootstrapExecutables, nameof(KnownBootstrapExecutables), StringComparer.OrdinalIgnoreCase),
            AllowedProcessReplacementPatterns = AdapterContractNormalization.StringSet(AllowedProcessReplacementPatterns, nameof(AllowedProcessReplacementPatterns), StringComparer.Ordinal),
            ValidatedInstallRoot = AdapterContractNormalization.Optional(ValidatedInstallRoot),
            ExpectedAumid = AdapterContractNormalization.Optional(ExpectedAumid),
            ExpectedPackageFamilyName = AdapterContractNormalization.Optional(ExpectedPackageFamilyName)
        };
    }
}

public sealed record PreparedClientLaunch(
    Guid LaunchOperationId,
    GameClientType ClientType,
    ExternalGameIdentity NormalizedExternalGameIdentity,
    AdapterLaunchIdentity ResolvedLaunchIdentity,
    GameClientObservationRules RequiredObservationRules,
    string HandoffMechanism,
    GameMechanismStatus HandoffMechanismStatus,
    long ProjectionGeneration,
    DateTimeOffset ExpiresAtUtc)
{
    public PreparedClientLaunch Normalize(GameClientLaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (LaunchOperationId != request.LaunchOperationId || ClientType != request.ClientType)
            throw new InvalidDataException("Prepared launch identity does not match its request.");
        var identity = (NormalizedExternalGameIdentity ?? throw new InvalidDataException("Prepared external identity is required.")).Normalize();
        if (!AdapterContractNormalization.ExternalIdentityEquals(identity, request.ExternalGameIdentity))
            throw new InvalidDataException("Prepared launch external identity does not match its request.");
        if (ProjectionGeneration < 0)
            throw new InvalidDataException("Prepared launch projection generation cannot be negative.");
        if (ExpiresAtUtc <= request.RequestedAtUtc)
            throw new InvalidDataException("Prepared launch expiry must be after request time.");

        return this with
        {
            NormalizedExternalGameIdentity = identity,
            ResolvedLaunchIdentity = (ResolvedLaunchIdentity ?? throw new InvalidDataException("Prepared launch identity is required.")).Normalize(),
            RequiredObservationRules = (RequiredObservationRules ?? throw new InvalidDataException("Observation rules are required.")).Normalize(),
            HandoffMechanism = AdapterContractNormalization.Required(HandoffMechanism, nameof(HandoffMechanism))
        };
    }
}

public sealed record GameClientLaunchHandoffResult(
    Guid LaunchOperationId,
    GameClientLaunchHandoffResultCode ResultCode,
    DateTimeOffset SubmittedAtUtc,
    string? ClientProcessEvidence = null,
    string? ReturnedProcessIdentity = null,
    string? DiagnosticsCode = null)
{
    public GameClientLaunchHandoffResult Normalize(PreparedClientLaunch prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (LaunchOperationId != prepared.LaunchOperationId)
            throw new InvalidDataException("Launch handoff result belongs to a different launch operation.");
        if (SubmittedAtUtc == default)
            throw new InvalidDataException("Launch handoff result requires a submission timestamp.");
        return this with
        {
            ClientProcessEvidence = AdapterContractNormalization.Optional(ClientProcessEvidence),
            ReturnedProcessIdentity = AdapterContractNormalization.Optional(ReturnedProcessIdentity),
            DiagnosticsCode = AdapterContractNormalization.Optional(DiagnosticsCode)
        };
    }
}

public sealed record CorrelatedProcessEvidence(
    int Pid,
    DateTimeOffset CreationTimeUtc,
    int SessionId,
    GameClientExecutableRole ExecutableRole,
    GameClientEvidenceLevel EvidenceLevel,
    DateTimeOffset FirstObservedUtc,
    DateTimeOffset LastObservedUtc,
    string? NormalizedImagePath = null)
{
    public CorrelatedProcessEvidence Normalize()
    {
        if (Pid <= 0 || SessionId < 0)
            throw new InvalidDataException("Correlated process identity is invalid.");
        if (CreationTimeUtc == default || FirstObservedUtc == default || LastObservedUtc < FirstObservedUtc)
            throw new InvalidDataException("Correlated process timestamps are invalid.");
        return this with { NormalizedImagePath = AdapterContractNormalization.Optional(NormalizedImagePath) };
    }
}

public sealed record GameClientObservationResult(
    GameClientObservationClassification Classification,
    GameClientEvidenceLevel EvidenceLevel,
    IReadOnlyList<CorrelatedProcessEvidence> CorrelatedProcesses,
    bool ReplacementDetected,
    DateTimeOffset ObservedAtUtc,
    string? DiagnosticsCode = null)
{
    public GameClientObservationResult Normalize()
    {
        if (ObservedAtUtc == default)
            throw new InvalidDataException("Launch observation requires an observation timestamp.");
        var processes = (CorrelatedProcesses ?? Array.Empty<CorrelatedProcessEvidence>())
            .Select(process => (process ?? throw new InvalidDataException("Correlated process cannot be null.")).Normalize())
            .ToArray();
        if (Classification == GameClientObservationClassification.RunningConfirmed
            && EvidenceLevel == GameClientEvidenceLevel.Weak)
            throw new InvalidDataException("Weak evidence cannot establish RUNNING_CONFIRMED.");
        return this with
        {
            CorrelatedProcesses = Array.AsReadOnly(processes),
            DiagnosticsCode = AdapterContractNormalization.Optional(DiagnosticsCode)
        };
    }
}

public sealed record GameClientExitObservationResult(
    GameClientExitObservationClassification Classification,
    IReadOnlyList<CorrelatedProcessEvidence> CorrelatedProcesses,
    DateTimeOffset ObservedAtUtc,
    string? DiagnosticsCode = null)
{
    public GameClientExitObservationResult Normalize()
    {
        if (ObservedAtUtc == default)
            throw new InvalidDataException("Exit observation requires an observation timestamp.");
        var processes = (CorrelatedProcesses ?? Array.Empty<CorrelatedProcessEvidence>())
            .Select(process => (process ?? throw new InvalidDataException("Correlated process cannot be null.")).Normalize())
            .ToArray();
        return this with
        {
            CorrelatedProcesses = Array.AsReadOnly(processes),
            DiagnosticsCode = AdapterContractNormalization.Optional(DiagnosticsCode)
        };
    }
}

public interface IGameClientAdapter
{
    GameClientAdapterDescriptor Descriptor { get; }

    GameClientCompatibilitySnapshot GetCompatibilityStatus(GameClientCompatibilityContext context);

    Task<GameClientDiscoveryEvidence> DiscoverClientAsync(
        int sessionId,
        GameClientFreshnessRequirement freshnessRequirement,
        CancellationToken cancellationToken);

    Task<GameClientLibraryRefreshResult> RefreshLibraryAsync(
        GameClientLibraryRefreshRequest request,
        CancellationToken cancellationToken);

    Task<AdapterGameProjection?> ResolveInstallationAsync(
        ExternalGameIdentity externalGameIdentity,
        GameClientFreshnessRequirement freshnessRequirement,
        CancellationToken cancellationToken);

    Task<PreparedClientLaunch> PrepareLaunchAsync(
        GameClientLaunchRequest request,
        AdapterGameProjection currentProjection,
        GameClientDiscoveryEvidence currentClientEvidence,
        CancellationToken cancellationToken);

    Task<GameClientLaunchHandoffResult> SubmitLaunchAsync(
        PreparedClientLaunch preparedLaunch,
        CancellationToken cancellationToken);

    Task<GameClientObservationResult> ObserveLaunchAsync(
        PreparedClientLaunch preparedLaunch,
        CancellationToken cancellationToken);

    Task<GameClientExitObservationResult> ObserveExitAsync(
        PreparedClientLaunch preparedLaunch,
        IReadOnlyList<CorrelatedProcessEvidence> currentCorrelation,
        CancellationToken cancellationToken);

    void Invalidate(GameClientLibraryRefreshReason reason);
}

internal static class AdapterContractNormalization
{
    public static string Required(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"{parameterName} requires a non-empty semantic value.");
        return value.Trim();
    }

    public static string? Optional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static IReadOnlyList<string> StringSet(
        IReadOnlyList<string>? values,
        string parameterName,
        StringComparer comparer)
    {
        var normalized = (values ?? Array.Empty<string>())
            .Select(value => Required(value, parameterName))
            .Distinct(comparer)
            .OrderBy(value => value, comparer)
            .ToArray();
        return Array.AsReadOnly(normalized);
    }

    public static bool ExternalIdentityEquals(ExternalGameIdentity left, ExternalGameIdentity right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var normalizedLeft = left.Normalize();
        var normalizedRight = right.Normalize();
        return normalizedLeft.ClientType == normalizedRight.ClientType
            && string.Equals(normalizedLeft.ExternalIdKind, normalizedRight.ExternalIdKind, StringComparison.Ordinal)
            && string.Equals(normalizedLeft.ExternalId, normalizedRight.ExternalId, StringComparison.Ordinal)
            && (normalizedLeft.SecondaryIds ?? Array.Empty<string>())
                .SequenceEqual(normalizedRight.SecondaryIds ?? Array.Empty<string>(), StringComparer.Ordinal);
    }
}