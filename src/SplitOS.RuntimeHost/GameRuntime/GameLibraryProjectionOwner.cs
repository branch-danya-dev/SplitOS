namespace SplitOS.RuntimeHost.GameRuntime;

public enum GameClientType
{
    Steam,
    Epic,
    MicrosoftGaming,
    BattleNet
}

public enum GameMechanismStatus
{
    SupportedPublic,
    SupportedOsMechanism,
    SupportedVersionGated,
    BestEffortLocalEvidence,
    VersionSensitive,
    UserMediated,
    Open,
    Unsupported
}

public enum GameClientSupportStatus
{
    TargetSupportedV1,
    PartialSupportedV1,
    Experimental,
    NotSupported
}

public enum GameInstallState
{
    InstalledVerifiedEvidence,
    NotInstalledVerifiedEvidence,
    Installing,
    UpdateRequiredEvidence,
    StaleLastKnown,
    Unknown
}

public enum GameEvidenceFreshness
{
    Fresh,
    Stale,
    Unknown
}

public enum GameEvidenceConfidence
{
    High,
    Medium,
    Low
}

public enum GameLaunchIdentityAvailability
{
    Available,
    Unavailable,
    Unknown
}

public enum GameLibraryCardState
{
    Ready,
    ClientActionRequired,
    NotInstalledVerified,
    StaleOrUnknown,
    UnsupportedClientCapability
}

public enum GameLibraryRefreshCompleteness
{
    Full,
    Partial
}

public enum GameLibraryRefreshDisposition
{
    Applied,
    NoOp,
    Rejected
}

public static class GameLibraryRefreshReasonCodes
{
    public const string Applied = "GAME_LIBRARY_REFRESH_APPLIED";
    public const string SemanticNoChange = "GAME_LIBRARY_SEMANTIC_NO_CHANGE";
    public const string IdempotentGeneration = "GAME_LIBRARY_GENERATION_IDEMPOTENT";
    public const string StaleGeneration = "GAME_LIBRARY_GENERATION_STALE";
    public const string GenerationConflict = "GAME_LIBRARY_GENERATION_CONFLICT";
    public const string ExternalIdentityConflict = "GAME_LIBRARY_EXTERNAL_IDENTITY_CONFLICT";
    public const string InvalidProjection = "GAME_LIBRARY_PROJECTION_INVALID";
}

public sealed record ExternalGameIdentity(
    GameClientType ClientType,
    string ExternalIdKind,
    string ExternalId,
    IReadOnlyList<string>? SecondaryIds = null)
{
    public ExternalGameIdentity Normalize()
    {
        var idKind = NormalizeRequired(ExternalIdKind, nameof(ExternalIdKind));
        var externalId = NormalizeRequired(ExternalId, nameof(ExternalId));
        var secondary = (SecondaryIds ?? Array.Empty<string>())
            .Select(value => NormalizeRequired(value, nameof(SecondaryIds)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        return new ExternalGameIdentity(ClientType, idKind, externalId, secondary);
    }

    internal string StableKey
        => $"{ClientType}|{ExternalIdKind}|{ExternalId}";

    private static string NormalizeRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A non-empty semantic identifier is required.", parameterName);
        return value.Trim();
    }
}

public sealed record GameInstallationEvidence(
    GameInstallState State,
    string? ValidatedInstallRoot,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    GameEvidenceFreshness Freshness,
    GameEvidenceConfidence Confidence,
    GameMechanismStatus MechanismStatus,
    string? SourceRecordIdentity = null)
{
    public GameInstallationEvidence Normalize()
    {
        if (ObservedAtUtc == default)
            throw new InvalidDataException("Installation evidence requires an observation timestamp.");
        if (ExpiresAtUtc is not null && ExpiresAtUtc < ObservedAtUtc)
            throw new InvalidDataException("Installation evidence expiry cannot predate observation.");

        var installRoot = NormalizeOptional(ValidatedInstallRoot);
        var sourceRecord = NormalizeOptional(SourceRecordIdentity);

        if (State == GameInstallState.NotInstalledVerifiedEvidence && installRoot is not null)
            throw new InvalidDataException("Verified not-installed evidence cannot carry an install root.");
        if (State == GameInstallState.StaleLastKnown && Freshness == GameEvidenceFreshness.Fresh)
            throw new InvalidDataException("STALE_LAST_KNOWN evidence cannot be fresh.");

        return this with
        {
            ValidatedInstallRoot = installRoot,
            SourceRecordIdentity = sourceRecord
        };
    }

    internal GameInstallationEvidence MarkStale()
        => this with
        {
            State = GameInstallState.StaleLastKnown,
            Freshness = GameEvidenceFreshness.Stale,
            ExpiresAtUtc = null
        };

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record GameLibraryBindingProjection(
    string GameId,
    ExternalGameIdentity ExternalIdentity,
    GameInstallationEvidence Installation,
    GameLaunchIdentityAvailability LaunchIdentityAvailability,
    GameMechanismStatus LaunchMechanismStatus,
    GameClientSupportStatus SupportStatus,
    string? DisplayNameEvidence = null)
{
    public GameLibraryBindingProjection Normalize(GameClientType expectedClientType)
    {
        var gameId = NormalizeRequired(GameId, nameof(GameId));
        var identity = (ExternalIdentity ?? throw new InvalidDataException("External identity is required.")).Normalize();
        if (identity.ClientType != expectedClientType)
            throw new InvalidDataException("External identity client type does not match the refresh owner.");

        var installation = (Installation ?? throw new InvalidDataException("Installation evidence is required.")).Normalize();
        return this with
        {
            GameId = gameId,
            ExternalIdentity = identity,
            Installation = installation,
            DisplayNameEvidence = NormalizeOptional(DisplayNameEvidence)
        };
    }

    internal GameLibraryBindingProjection MarkStale()
        => this with { Installation = Installation.MarkStale() };

    internal GameLibraryCardState CardState
    {
        get
        {
            if (SupportStatus == GameClientSupportStatus.NotSupported
                || LaunchMechanismStatus == GameMechanismStatus.Unsupported)
                return GameLibraryCardState.UnsupportedClientCapability;

            if (Installation.State == GameInstallState.UpdateRequiredEvidence
                || Installation.State == GameInstallState.Installing
                || LaunchMechanismStatus == GameMechanismStatus.UserMediated)
                return GameLibraryCardState.ClientActionRequired;

            if (Installation.Freshness != GameEvidenceFreshness.Fresh
                || Installation.State is GameInstallState.StaleLastKnown or GameInstallState.Unknown
                || Installation.Confidence == GameEvidenceConfidence.Low
                || LaunchIdentityAvailability == GameLaunchIdentityAvailability.Unknown
                || LaunchMechanismStatus == GameMechanismStatus.Open)
                return GameLibraryCardState.StaleOrUnknown;

            if (Installation.State == GameInstallState.NotInstalledVerifiedEvidence)
                return GameLibraryCardState.NotInstalledVerified;

            if (Installation.State == GameInstallState.InstalledVerifiedEvidence
                && LaunchIdentityAvailability == GameLaunchIdentityAvailability.Available
                && LaunchMechanismStatus is GameMechanismStatus.SupportedPublic
                    or GameMechanismStatus.SupportedOsMechanism
                    or GameMechanismStatus.SupportedVersionGated
                    or GameMechanismStatus.BestEffortLocalEvidence
                    or GameMechanismStatus.VersionSensitive)
                return GameLibraryCardState.Ready;

            return GameLibraryCardState.ClientActionRequired;
        }
    }

    internal string StableKey => ExternalIdentity.StableKey;

    internal string CanonicalFingerprint()
        => string.Join(
            "~",
            Escape(GameId),
            ExternalIdentity.StableKey,
            string.Join(",", ExternalIdentity.SecondaryIds ?? Array.Empty<string>()),
            Installation.State,
            Escape(Installation.ValidatedInstallRoot),
            Installation.ObservedAtUtc.ToUniversalTime().ToString("O"),
            Installation.ExpiresAtUtc?.ToUniversalTime().ToString("O") ?? string.Empty,
            Installation.Freshness,
            Installation.Confidence,
            Installation.MechanismStatus,
            Escape(Installation.SourceRecordIdentity),
            LaunchIdentityAvailability,
            LaunchMechanismStatus,
            SupportStatus,
            Escape(DisplayNameEvidence));

    private static string NormalizeRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A non-empty canonical game ID is required.", parameterName);
        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Escape(string? value)
        => value?.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("~", "\\~", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal) ?? string.Empty;
}

public sealed record GameLibraryClientRefresh(
    GameClientType ClientType,
    long Generation,
    DateTimeOffset ObservedAtUtc,
    GameLibraryRefreshCompleteness Completeness,
    IReadOnlyList<GameLibraryBindingProjection> Records)
{
    public GameLibraryClientRefresh Normalize()
    {
        if (Generation < 0)
            throw new InvalidDataException("Game Library generation cannot be negative.");
        if (ObservedAtUtc == default)
            throw new InvalidDataException("Game Library refresh requires an observation timestamp.");
        if (Records is null)
            throw new InvalidDataException("Game Library refresh records are required.");

        var normalized = Records
            .Select(record => (record ?? throw new InvalidDataException("Game Library refresh cannot contain null records."))
                .Normalize(ClientType))
            .OrderBy(record => record.StableKey, StringComparer.Ordinal)
            .ToArray();

        var duplicate = normalized
            .GroupBy(record => record.StableKey, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidDataException($"Duplicate external identity '{duplicate.Key}' in one client refresh.");

        return this with { Records = normalized };
    }

    internal string SubmissionFingerprint()
        => $"{Completeness}|{string.Join("\n", Records.Select(record => record.CanonicalFingerprint()))}";
}

public sealed record NormalizedGameClientBinding(
    GameClientType ClientType,
    ExternalGameIdentity ExternalIdentity,
    GameInstallationEvidence Installation,
    GameLaunchIdentityAvailability LaunchIdentityAvailability,
    GameMechanismStatus LaunchMechanismStatus,
    GameClientSupportStatus SupportStatus,
    GameLibraryCardState CardState,
    string? DisplayNameEvidence);

public sealed record NormalizedGameLibraryEntry(
    string GameId,
    string? PreferredDisplayNameEvidence,
    GameLibraryCardState CardState,
    IReadOnlyList<NormalizedGameClientBinding> ClientBindings);

public sealed record GameLibrarySnapshot(
    long Revision,
    IReadOnlyList<NormalizedGameLibraryEntry> Games)
{
    public static GameLibrarySnapshot Empty { get; } = new(0, Array.Empty<NormalizedGameLibraryEntry>());
}

public sealed record GameLibraryRefreshDecision(
    GameLibraryRefreshDisposition Disposition,
    string ReasonCode,
    GameLibrarySnapshot Snapshot,
    GameClientType ClientType,
    long Generation);

/// <summary>
/// Runtime-owned normalized Game Library projection. Adapter-specific metadata is accepted only after
/// it has already been normalized into semantic evidence. The owner never infers canonical identity
/// from display title, install path, executable path, or another client's external identifier.
/// </summary>
public sealed class GameLibraryProjectionOwner
{
    private readonly Dictionary<GameClientType, ClientProjectionState> _clients = [];
    private readonly Dictionary<string, string> _canonicalAssignments = new(StringComparer.Ordinal);
    private GameLibrarySnapshot _snapshot = GameLibrarySnapshot.Empty;
    private string _semanticFingerprint = string.Empty;

    public GameLibrarySnapshot Snapshot => _snapshot;

    public GameLibraryRefreshDecision ApplyClientRefresh(GameLibraryClientRefresh refresh)
    {
        ArgumentNullException.ThrowIfNull(refresh);

        GameLibraryClientRefresh normalized;
        try
        {
            normalized = refresh.Normalize();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
        {
            return Decision(
                GameLibraryRefreshDisposition.Rejected,
                GameLibraryRefreshReasonCodes.InvalidProjection,
                refresh.ClientType,
                refresh.Generation);
        }

        var submissionFingerprint = normalized.SubmissionFingerprint();
        if (_clients.TryGetValue(normalized.ClientType, out var existingClient))
        {
            if (normalized.Generation < existingClient.Generation)
            {
                return Decision(
                    GameLibraryRefreshDisposition.NoOp,
                    GameLibraryRefreshReasonCodes.StaleGeneration,
                    normalized.ClientType,
                    normalized.Generation);
            }

            if (normalized.Generation == existingClient.Generation)
            {
                if (string.Equals(existingClient.LastSubmissionFingerprint, submissionFingerprint, StringComparison.Ordinal))
                {
                    return Decision(
                        GameLibraryRefreshDisposition.NoOp,
                        GameLibraryRefreshReasonCodes.IdempotentGeneration,
                        normalized.ClientType,
                        normalized.Generation);
                }

                return Decision(
                    GameLibraryRefreshDisposition.Rejected,
                    GameLibraryRefreshReasonCodes.GenerationConflict,
                    normalized.ClientType,
                    normalized.Generation);
            }
        }

        foreach (var record in normalized.Records)
        {
            if (_canonicalAssignments.TryGetValue(record.StableKey, out var assignedGameId)
                && !string.Equals(assignedGameId, record.GameId, StringComparison.Ordinal))
            {
                return Decision(
                    GameLibraryRefreshDisposition.Rejected,
                    GameLibraryRefreshReasonCodes.ExternalIdentityConflict,
                    normalized.ClientType,
                    normalized.Generation);
            }
        }

        var effectiveRecords = normalized.Records.ToDictionary(
            record => record.StableKey,
            record => record,
            StringComparer.Ordinal);

        if (normalized.Completeness == GameLibraryRefreshCompleteness.Partial && existingClient is not null)
        {
            foreach (var previous in existingClient.EffectiveRecords.Values)
            {
                if (!effectiveRecords.ContainsKey(previous.StableKey))
                    effectiveRecords.Add(previous.StableKey, previous.MarkStale());
            }
        }

        foreach (var record in normalized.Records)
            _canonicalAssignments.TryAdd(record.StableKey, record.GameId);

        _clients[normalized.ClientType] = new ClientProjectionState(
            normalized.Generation,
            submissionFingerprint,
            effectiveRecords);

        var nextSnapshot = BuildSnapshot(_snapshot.Revision);
        var nextFingerprint = SnapshotFingerprint(nextSnapshot);
        if (string.Equals(_semanticFingerprint, nextFingerprint, StringComparison.Ordinal))
        {
            return Decision(
                GameLibraryRefreshDisposition.NoOp,
                GameLibraryRefreshReasonCodes.SemanticNoChange,
                normalized.ClientType,
                normalized.Generation);
        }

        _snapshot = nextSnapshot with { Revision = checked(_snapshot.Revision + 1) };
        _semanticFingerprint = nextFingerprint;
        return Decision(
            GameLibraryRefreshDisposition.Applied,
            GameLibraryRefreshReasonCodes.Applied,
            normalized.ClientType,
            normalized.Generation);
    }

    public bool TryGetGame(string gameId, out NormalizedGameLibraryEntry? game)
    {
        game = _snapshot.Games.FirstOrDefault(entry => string.Equals(entry.GameId, gameId, StringComparison.Ordinal));
        return game is not null;
    }

    private GameLibrarySnapshot BuildSnapshot(long revision)
    {
        var records = _clients.Values
            .SelectMany(client => client.EffectiveRecords.Values)
            .OrderBy(record => record.GameId, StringComparer.Ordinal)
            .ThenBy(record => record.ExternalIdentity.ClientType)
            .ThenBy(record => record.StableKey, StringComparer.Ordinal)
            .ToArray();

        var games = records
            .GroupBy(record => record.GameId, StringComparer.Ordinal)
            .Select(group => BuildEntry(group.Key, group.ToArray()))
            .OrderBy(entry => entry.GameId, StringComparer.Ordinal)
            .ToArray();

        return new GameLibrarySnapshot(revision, games);
    }

    private static NormalizedGameLibraryEntry BuildEntry(
        string gameId,
        IReadOnlyList<GameLibraryBindingProjection> records)
    {
        var bindings = records
            .Select(record => new NormalizedGameClientBinding(
                record.ExternalIdentity.ClientType,
                record.ExternalIdentity,
                record.Installation,
                record.LaunchIdentityAvailability,
                record.LaunchMechanismStatus,
                record.SupportStatus,
                record.CardState,
                record.DisplayNameEvidence))
            .OrderBy(binding => binding.ClientType)
            .ThenBy(binding => binding.ExternalIdentity.StableKey, StringComparer.Ordinal)
            .ToArray();

        var preferredDisplayName = records
            .Where(record => record.DisplayNameEvidence is not null)
            .OrderByDescending(record => record.Installation.Freshness == GameEvidenceFreshness.Fresh)
            .ThenByDescending(record => record.Installation.Confidence)
            .ThenBy(record => record.ExternalIdentity.ClientType)
            .Select(record => record.DisplayNameEvidence)
            .FirstOrDefault();

        return new NormalizedGameLibraryEntry(
            gameId,
            preferredDisplayName,
            AggregateCardState(bindings),
            bindings);
    }

    private static GameLibraryCardState AggregateCardState(IReadOnlyList<NormalizedGameClientBinding> bindings)
    {
        if (bindings.Any(binding => binding.CardState == GameLibraryCardState.Ready))
            return GameLibraryCardState.Ready;
        if (bindings.Any(binding => binding.CardState == GameLibraryCardState.ClientActionRequired))
            return GameLibraryCardState.ClientActionRequired;
        if (bindings.Any(binding => binding.CardState == GameLibraryCardState.StaleOrUnknown))
            return GameLibraryCardState.StaleOrUnknown;
        if (bindings.Any(binding => binding.CardState == GameLibraryCardState.UnsupportedClientCapability))
            return GameLibraryCardState.UnsupportedClientCapability;
        return GameLibraryCardState.NotInstalledVerified;
    }

    private static string SnapshotFingerprint(GameLibrarySnapshot snapshot)
        => string.Join(
            "\n",
            snapshot.Games.SelectMany(game => game.ClientBindings.Select(binding => string.Join(
                "~",
                game.GameId,
                game.PreferredDisplayNameEvidence ?? string.Empty,
                game.CardState,
                binding.ExternalIdentity.StableKey,
                binding.Installation.State,
                binding.Installation.ValidatedInstallRoot ?? string.Empty,
                binding.Installation.ObservedAtUtc.ToUniversalTime().ToString("O"),
                binding.Installation.ExpiresAtUtc?.ToUniversalTime().ToString("O") ?? string.Empty,
                binding.Installation.Freshness,
                binding.Installation.Confidence,
                binding.Installation.MechanismStatus,
                binding.LaunchIdentityAvailability,
                binding.LaunchMechanismStatus,
                binding.SupportStatus,
                binding.CardState,
                binding.DisplayNameEvidence ?? string.Empty))));

    private GameLibraryRefreshDecision Decision(
        GameLibraryRefreshDisposition disposition,
        string reasonCode,
        GameClientType clientType,
        long generation)
        => new(disposition, reasonCode, _snapshot, clientType, generation);

    private sealed record ClientProjectionState(
        long Generation,
        string LastSubmissionFingerprint,
        IReadOnlyDictionary<string, GameLibraryBindingProjection> EffectiveRecords);
}
