using System.Diagnostics;
using System.Globalization;
using Microsoft.Win32;

namespace SplitOS.RuntimeHost.GameRuntime;

public sealed record SteamProtocolRegistrationSnapshot(
    bool ReadSucceeded,
    bool SchemeKeyPresent,
    bool UrlProtocolMarkerPresent,
    string? CommandTemplate,
    string? DiagnosticsCode = null);

public interface ISteamProtocolRegistrationReader
{
    SteamProtocolRegistrationSnapshot Read();
}

public sealed record SteamExecutableEvidence(
    bool Exists,
    string? ObservedFileVersion,
    bool VersionReadSucceeded,
    string? DiagnosticsCode = null);

public interface ISteamExecutableEvidenceReader
{
    SteamExecutableEvidence Read(string normalizedExecutablePath);
}

/// <summary>
/// Reads the merged HKCR Steam URI registration for the current RuntimeHost user context.
/// The registration is treated strictly as external evidence; this reader never executes it.
/// </summary>
public sealed class WindowsSteamProtocolRegistrationReader : ISteamProtocolRegistrationReader
{
    private const string SteamSchemeKey = "steam";
    private const string CommandSubKey = @"shell\open\command";
    private const string UrlProtocolValueName = "URL Protocol";

    public SteamProtocolRegistrationSnapshot Read()
    {
        try
        {
            using var scheme = Registry.ClassesRoot.OpenSubKey(SteamSchemeKey, writable: false);
            if (scheme is null)
                return new SteamProtocolRegistrationSnapshot(true, false, false, null);

            var hasUrlProtocolMarker = scheme.GetValueNames()
                .Any(name => string.Equals(name, UrlProtocolValueName, StringComparison.OrdinalIgnoreCase));

            using var commandKey = scheme.OpenSubKey(CommandSubKey, writable: false);
            if (commandKey is null)
            {
                return new SteamProtocolRegistrationSnapshot(
                    true,
                    true,
                    hasUrlProtocolMarker,
                    null,
                    "STEAM_PROTOCOL_HANDLER_MISSING");
            }

            var rawCommand = commandKey.GetValue(
                name: null,
                defaultValue: null,
                options: RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (rawCommand is null)
            {
                return new SteamProtocolRegistrationSnapshot(
                    true,
                    true,
                    hasUrlProtocolMarker,
                    null,
                    "STEAM_PROTOCOL_HANDLER_MISSING");
            }

            if (rawCommand is not string command)
            {
                return new SteamProtocolRegistrationSnapshot(
                    true,
                    true,
                    hasUrlProtocolMarker,
                    null,
                    "STEAM_PROTOCOL_HANDLER_NON_STRING");
            }

            return new SteamProtocolRegistrationSnapshot(
                true,
                true,
                hasUrlProtocolMarker,
                command,
                null);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or System.Security.SecurityException
            or IOException)
        {
            return new SteamProtocolRegistrationSnapshot(
                false,
                false,
                false,
                null,
                "STEAM_PROTOCOL_REGISTRY_READ_FAILED");
        }
    }
}

/// <summary>
/// Produces read-only filesystem/file-version evidence for a normalized Steam handler path.
/// </summary>
public sealed class WindowsSteamExecutableEvidenceReader : ISteamExecutableEvidenceReader
{
    public SteamExecutableEvidence Read(string normalizedExecutablePath)
    {
        if (string.IsNullOrWhiteSpace(normalizedExecutablePath))
            throw new ArgumentException("Steam executable path is required.", nameof(normalizedExecutablePath));

        bool exists;
        try
        {
            exists = File.Exists(normalizedExecutablePath);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or IOException
            or NotSupportedException)
        {
            return new SteamExecutableEvidence(false, null, false, "STEAM_HANDLER_FILE_QUERY_FAILED");
        }

        if (!exists)
            return new SteamExecutableEvidence(false, null, true, "STEAM_HANDLER_FILE_NOT_FOUND");

        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(normalizedExecutablePath);
            var version = FirstNonEmpty(versionInfo.ProductVersion, versionInfo.FileVersion);
            return new SteamExecutableEvidence(
                true,
                version,
                true,
                version is null ? "STEAM_CLIENT_VERSION_UNAVAILABLE" : null);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or IOException
            or System.ComponentModel.Win32Exception)
        {
            return new SteamExecutableEvidence(true, null, false, "STEAM_CLIENT_VERSION_READ_FAILED");
        }
    }

    private static string? FirstNonEmpty(params string?[] candidates)
        => candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate))?.Trim();
}

/// <summary>
/// Strict parser for the registered Steam URI command template. It extracts evidence only;
/// SplitOS never invokes this command directly. Launch handoff remains owned by IMP-083.
/// </summary>
public static class SteamProtocolHandlerCommandParser
{
    private static readonly string[] AcceptedUriPlaceholders = ["%1", "\"%1\""];

    public static bool TryParse(
        string? commandTemplate,
        out string? normalizedExecutablePath,
        out string diagnosticsCode)
    {
        normalizedExecutablePath = null;

        if (string.IsNullOrWhiteSpace(commandTemplate))
        {
            diagnosticsCode = "STEAM_PROTOCOL_HANDLER_MISSING";
            return false;
        }

        var command = commandTemplate.Trim();
        string executableCandidate;
        string remainder;

        if (command[0] == '"')
        {
            var closingQuote = command.IndexOf('"', 1);
            if (closingQuote <= 1)
            {
                diagnosticsCode = "STEAM_PROTOCOL_HANDLER_MALFORMED";
                return false;
            }

            executableCandidate = command[1..closingQuote];
            remainder = command[(closingQuote + 1)..].Trim();
        }
        else
        {
            var firstWhitespace = IndexOfWhitespace(command);
            if (firstWhitespace < 0)
            {
                diagnosticsCode = "STEAM_PROTOCOL_HANDLER_TEMPLATE_UNTRUSTED";
                return false;
            }

            executableCandidate = command[..firstWhitespace];
            remainder = command[firstWhitespace..].Trim();
        }

        if (!AcceptedUriPlaceholders.Contains(remainder, StringComparer.OrdinalIgnoreCase))
        {
            diagnosticsCode = "STEAM_PROTOCOL_HANDLER_TEMPLATE_UNTRUSTED";
            return false;
        }

        if (string.IsNullOrWhiteSpace(executableCandidate)
            || executableCandidate.IndexOf('%') >= 0
            || executableCandidate.IndexOf('\0') >= 0)
        {
            diagnosticsCode = "STEAM_PROTOCOL_HANDLER_PATH_INVALID";
            return false;
        }

        if (!Path.IsPathFullyQualified(executableCandidate))
        {
            diagnosticsCode = "STEAM_PROTOCOL_HANDLER_PATH_NOT_ABSOLUTE";
            return false;
        }

        string normalized;
        try
        {
            normalized = Path.GetFullPath(executableCandidate);
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            diagnosticsCode = "STEAM_PROTOCOL_HANDLER_PATH_INVALID";
            return false;
        }

        if (!string.Equals(Path.GetFileName(normalized), "steam.exe", StringComparison.OrdinalIgnoreCase))
        {
            diagnosticsCode = "STEAM_PROTOCOL_HANDLER_IMAGE_UNEXPECTED";
            return false;
        }

        normalizedExecutablePath = normalized;
        diagnosticsCode = "STEAM_PROTOCOL_HANDLER_VALID";
        return true;
    }

    private static int IndexOfWhitespace(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsWhiteSpace(value[index]))
                return index;
        }

        return -1;
    }
}

/// <summary>
/// Steam adapter through IMP-082. Discovery/version evidence comes from the registered Steam URI
/// handler; library/install evidence comes from bounded read-only VDF/ACF parsing. Launch submission
/// and process correlation deliberately remain OPEN until IMP-083/084.
/// </summary>
public sealed class SteamClientAdapter : IGameClientAdapter
{
    public const string AdapterVersion = "steam-adapter/2";
    public const string CompatibilityPolicyId = "steam/v1";
    public const string DiscoveryMechanismId = "STEAM_PROTOCOL_REGISTRATION_V1";
    public const string VersionMechanismId = "STEAM_HANDLER_FILE_VERSION_V1";
    public const string LibraryMechanismId = "STEAM_LIBRARY_VDF_V1";
    public const string InstallationMechanismId = "STEAM_APPMANIFEST_INSTALL_EVIDENCE_V1";
    public const string LaunchIdentityMechanismId = "STEAM_APP_ID_LAUNCH_IDENTITY_V1";
    public const string ProtocolRegistrationIdentity = "steam:";
    public const string EvidenceSchemaVersion = "STEAM_KEYVALUES_V1";
    public const string ExternalIdKind = "STEAM_APP_ID";
    public const int MinimumSupportedWindowsBuild = 19041;
    public const int MaximumLibraryFoldersBytes = 4 * 1024 * 1024;
    public const int MaximumAppManifestBytes = 1024 * 1024;
    public const int MaximumAppManifestsPerLibrary = 10_000;

    private readonly ISteamProtocolRegistrationReader _registrationReader;
    private readonly ISteamExecutableEvidenceReader _executableEvidenceReader;
    private readonly ISteamMetadataFileSystem _metadataFileSystem;
    private readonly TimeProvider _timeProvider;
    private long _snapshotGeneration;
    private long _libraryGeneration;

    public SteamClientAdapter(
        ISteamProtocolRegistrationReader registrationReader,
        ISteamExecutableEvidenceReader executableEvidenceReader,
        TimeProvider? timeProvider = null)
        : this(
            registrationReader,
            executableEvidenceReader,
            new WindowsSteamMetadataFileSystem(),
            timeProvider)
    {
    }

    public SteamClientAdapter(
        ISteamProtocolRegistrationReader registrationReader,
        ISteamExecutableEvidenceReader executableEvidenceReader,
        ISteamMetadataFileSystem metadataFileSystem,
        TimeProvider? timeProvider = null)
    {
        _registrationReader = registrationReader ?? throw new ArgumentNullException(nameof(registrationReader));
        _executableEvidenceReader = executableEvidenceReader ?? throw new ArgumentNullException(nameof(executableEvidenceReader));
        _metadataFileSystem = metadataFileSystem ?? throw new ArgumentNullException(nameof(metadataFileSystem));
        _timeProvider = timeProvider ?? TimeProvider.System;
        Descriptor = CreateDescriptor().Normalize();
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
    {
        if (sessionId < 0)
            throw new ArgumentOutOfRangeException(nameof(sessionId), "Windows session ID cannot be negative.");
        if (!Enum.IsDefined(freshnessRequirement))
            throw new ArgumentOutOfRangeException(nameof(freshnessRequirement));

        return Task.FromResult(DiscoverClientCore(cancellationToken));
    }

    public Task<GameClientLibraryRefreshResult> RefreshLibraryAsync(
        GameClientLibraryRefreshRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedRequest = (request ?? throw new ArgumentNullException(nameof(request)))
            .Normalize(GameClientType.Steam);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(BuildLibraryRefresh(normalizedRequest, cancellationToken));
    }

    public Task<AdapterGameProjection?> ResolveInstallationAsync(
        ExternalGameIdentity externalGameIdentity,
        GameClientFreshnessRequirement freshnessRequirement,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(freshnessRequirement))
            throw new ArgumentOutOfRangeException(nameof(freshnessRequirement));

        var identity = (externalGameIdentity ?? throw new ArgumentNullException(nameof(externalGameIdentity))).Normalize();
        if (identity.ClientType != GameClientType.Steam
            || !string.Equals(identity.ExternalIdKind, ExternalIdKind, StringComparison.Ordinal)
            || !uint.TryParse(identity.ExternalId, NumberStyles.None, CultureInfo.InvariantCulture, out var appId)
            || appId == 0)
            throw new InvalidDataException("Steam installation resolution requires a canonical STEAM_APP_ID identity.");

        cancellationToken.ThrowIfCancellationRequested();
        var now = _timeProvider.GetUtcNow();
        var request = new GameClientLibraryRefreshRequest(
            Guid.NewGuid(),
            GameClientType.Steam,
            GameClientLibraryRefreshReason.GameLaunchRequest,
            freshnessRequirement,
            null,
            now.AddSeconds(30));
        var refresh = BuildLibraryRefresh(request, cancellationToken);
        var projection = refresh.Records.FirstOrDefault(record =>
            AdapterContractNormalization.ExternalIdentityEquals(record.ExternalGameIdentity, identity));
        return Task.FromResult(projection);
    }

    public Task<PreparedClientLaunch> PrepareLaunchAsync(
        GameClientLaunchRequest request,
        AdapterGameProjection currentProjection,
        GameClientDiscoveryEvidence currentClientEvidence,
        CancellationToken cancellationToken)
        => Unsupported<PreparedClientLaunch>("IMP-083 owns Steam launch preparation.", cancellationToken);

    public Task<GameClientLaunchHandoffResult> SubmitLaunchAsync(
        PreparedClientLaunch preparedLaunch,
        CancellationToken cancellationToken)
        => Unsupported<GameClientLaunchHandoffResult>("IMP-083 owns Steam protocol launch handoff.", cancellationToken);

    public Task<GameClientObservationResult> ObserveLaunchAsync(
        PreparedClientLaunch preparedLaunch,
        CancellationToken cancellationToken)
        => Unsupported<GameClientObservationResult>("IMP-084 owns Steam launch correlation.", cancellationToken);

    public Task<GameClientExitObservationResult> ObserveExitAsync(
        PreparedClientLaunch preparedLaunch,
        IReadOnlyList<GameClientCorrelatedProcessEvidence> currentCorrelation,
        CancellationToken cancellationToken)
        => Unsupported<GameClientExitObservationResult>("IMP-084 owns Steam exit correlation.", cancellationToken);

    public void Invalidate(GameClientLibraryRefreshReason reason)
    {
        // IMP-082 intentionally keeps no authoritative cache inside the adapter. Every explicit
        // discovery/refresh re-reads external evidence, so invalidation remains a no-op hint.
    }

    private GameClientDiscoveryEvidence DiscoverClientCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var observedAt = _timeProvider.GetUtcNow();
        var generation = Interlocked.Increment(ref _snapshotGeneration);

        SteamProtocolRegistrationSnapshot registration;
        try
        {
            registration = _registrationReader.Read()
                ?? throw new InvalidDataException("Steam registration reader returned no snapshot.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Evidence(
                GameClientDiscoveryAvailability.Unknown,
                observedAt,
                generation,
                diagnosticsCode: "STEAM_PROTOCOL_REGISTRY_READ_FAILED");
        }

        if (!registration.ReadSucceeded)
        {
            return Evidence(
                GameClientDiscoveryAvailability.Unknown,
                observedAt,
                generation,
                diagnosticsCode: registration.DiagnosticsCode ?? "STEAM_PROTOCOL_REGISTRY_READ_FAILED");
        }

        if (!registration.SchemeKeyPresent)
        {
            return Evidence(
                GameClientDiscoveryAvailability.NotFoundVerified,
                observedAt,
                generation,
                diagnosticsCode: "STEAM_PROTOCOL_NOT_REGISTERED");
        }

        if (!registration.UrlProtocolMarkerPresent)
        {
            return Evidence(
                GameClientDiscoveryAvailability.Unknown,
                observedAt,
                generation,
                diagnosticsCode: "STEAM_URL_PROTOCOL_MARKER_MISSING");
        }

        if (!SteamProtocolHandlerCommandParser.TryParse(
                registration.CommandTemplate,
                out var executablePath,
                out var parseDiagnostics)
            || executablePath is null)
        {
            return Evidence(
                GameClientDiscoveryAvailability.Unknown,
                observedAt,
                generation,
                diagnosticsCode: parseDiagnostics);
        }

        cancellationToken.ThrowIfCancellationRequested();

        SteamExecutableEvidence executableEvidence;
        try
        {
            executableEvidence = _executableEvidenceReader.Read(executablePath)
                ?? throw new InvalidDataException("Steam executable reader returned no evidence.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Evidence(
                GameClientDiscoveryAvailability.Unknown,
                observedAt,
                generation,
                executablePath,
                diagnosticsCode: "STEAM_HANDLER_FILE_QUERY_FAILED");
        }

        if (!executableEvidence.Exists)
        {
            return Evidence(
                GameClientDiscoveryAvailability.Unknown,
                observedAt,
                generation,
                executablePath,
                diagnosticsCode: executableEvidence.DiagnosticsCode ?? "STEAM_HANDLER_FILE_NOT_FOUND");
        }

        var observedVersion = string.IsNullOrWhiteSpace(executableEvidence.ObservedFileVersion)
            ? null
            : executableEvidence.ObservedFileVersion.Trim();
        var availability = observedVersion is null
            ? GameClientDiscoveryAvailability.AvailableUnverifiedVersion
            : GameClientDiscoveryAvailability.AvailableVerified;

        return Evidence(
            availability,
            observedAt,
            generation,
            executablePath,
            observedVersion,
            observedVersion is null
                ? executableEvidence.DiagnosticsCode ?? "STEAM_CLIENT_VERSION_UNAVAILABLE"
                : null);
    }

    private GameClientLibraryRefreshResult BuildLibraryRefresh(
        GameClientLibraryRefreshRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var observedAt = _timeProvider.GetUtcNow();
        var generation = Interlocked.Increment(ref _libraryGeneration);
        if (request.DeadlineUtc <= observedAt)
            return Result(request, GameClientLibraryRefreshResultCode.Timeout, generation, observedAt, [], "STEAM_LIBRARY_REFRESH_DEADLINE_EXPIRED");

        var discovery = DiscoverClientCore(cancellationToken);
        if (discovery.AvailabilityState == GameClientDiscoveryAvailability.NotFoundVerified)
        {
            return Result(
                request,
                GameClientLibraryRefreshResultCode.ClientUnavailable,
                generation,
                observedAt,
                [],
                discovery.DiagnosticsCode,
                discovery.ObservedClientVersion);
        }

        if (discovery.AvailabilityState is GameClientDiscoveryAvailability.Unknown
            or GameClientDiscoveryAvailability.StaleLastKnown
            || string.IsNullOrWhiteSpace(discovery.ExecutableIdentity))
        {
            return Result(
                request,
                GameClientLibraryRefreshResultCode.SourceUnavailable,
                generation,
                observedAt,
                [],
                discovery.DiagnosticsCode ?? "STEAM_LIBRARY_CLIENT_EVIDENCE_UNAVAILABLE",
                discovery.ObservedClientVersion);
        }

        string steamRoot;
        try
        {
            steamRoot = Path.GetDirectoryName(discovery.ExecutableIdentity) is { Length: > 0 } directory
                ? Path.GetFullPath(directory)
                : throw new InvalidDataException("Steam handler has no parent directory.");
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or PathTooLongException
            or InvalidDataException)
        {
            return Result(
                request,
                GameClientLibraryRefreshResultCode.SourceUnavailable,
                generation,
                observedAt,
                [],
                "STEAM_ROOT_INVALID",
                discovery.ObservedClientVersion);
        }

        var libraryFoldersPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        var libraryRead = _metadataFileSystem.ReadTextFile(libraryFoldersPath, MaximumLibraryFoldersBytes);
        if (!libraryRead.Success)
        {
            return Result(
                request,
                GameClientLibraryRefreshResultCode.ParseFailed,
                generation,
                observedAt,
                [],
                libraryRead.DiagnosticsCode ?? "STEAM_LIBRARY_SOURCE_READ_FAILED",
                discovery.ObservedClientVersion);
        }

        if (!libraryRead.Exists || libraryRead.Content is null)
        {
            return Result(
                request,
                GameClientLibraryRefreshResultCode.SourceUnavailable,
                generation,
                observedAt,
                [],
                "LIBRARY_SOURCE_NOT_FOUND",
                discovery.ObservedClientVersion);
        }

        var libraryParse = SteamVdfMetadataParser.ParseLibraryFolders(libraryRead.Content);
        if (!libraryParse.Success)
        {
            var resultCode = string.Equals(
                    libraryParse.DiagnosticsCode,
                    "STEAM_LIBRARY_SCHEMA_UNKNOWN",
                    StringComparison.Ordinal)
                ? GameClientLibraryRefreshResultCode.SourceSchemaUnknown
                : GameClientLibraryRefreshResultCode.ParseFailed;
            return Result(
                request,
                resultCode,
                generation,
                observedAt,
                [],
                libraryParse.DiagnosticsCode,
                discovery.ObservedClientVersion);
        }

        var missingEvidence = new HashSet<string>(StringComparer.Ordinal);
        var libraryRoots = NormalizeLibraryRoots(steamRoot, libraryParse.LibraryRoots, missingEvidence);
        if (libraryRoots.Count == 0)
        {
            return Result(
                request,
                GameClientLibraryRefreshResultCode.SourceUnavailable,
                generation,
                observedAt,
                [],
                "STEAM_LIBRARY_ROOTS_UNAVAILABLE",
                discovery.ObservedClientVersion);
        }

        var projections = new Dictionary<string, AdapterGameProjection>(StringComparer.Ordinal);
        foreach (var libraryRoot in libraryRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.DeadlineUtc <= _timeProvider.GetUtcNow())
            {
                missingEvidence.Add("STEAM_LIBRARY_REFRESH_DEADLINE_EXPIRED");
                break;
            }

            var steamAppsRoot = Path.Combine(libraryRoot, "steamapps");
            var enumeration = _metadataFileSystem.EnumerateFiles(
                steamAppsRoot,
                "appmanifest_*.acf",
                MaximumAppManifestsPerLibrary);
            if (!enumeration.Success)
            {
                missingEvidence.Add(enumeration.DiagnosticsCode ?? "STEAM_APPMANIFEST_ENUMERATION_FAILED");
                continue;
            }

            foreach (var manifestPath in enumeration.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (request.DeadlineUtc <= _timeProvider.GetUtcNow())
                {
                    missingEvidence.Add("STEAM_LIBRARY_REFRESH_DEADLINE_EXPIRED");
                    break;
                }

                if (!TryManifestFileAppId(manifestPath, out var fileAppId))
                {
                    missingEvidence.Add("STEAM_APPMANIFEST_FILENAME_INVALID");
                    continue;
                }

                var manifestRead = _metadataFileSystem.ReadTextFile(manifestPath, MaximumAppManifestBytes);
                if (!manifestRead.Success || !manifestRead.Exists || manifestRead.Content is null)
                {
                    missingEvidence.Add(manifestRead.DiagnosticsCode ?? "STEAM_APPMANIFEST_READ_FAILED");
                    continue;
                }

                var parsed = SteamVdfMetadataParser.ParseAppManifest(manifestRead.Content);
                if (!parsed.Success || parsed.AppId is null)
                {
                    missingEvidence.Add(parsed.DiagnosticsCode ?? "STEAM_APPMANIFEST_PARSE_FAILED");
                    continue;
                }

                if (!string.Equals(parsed.AppId, fileAppId, StringComparison.Ordinal))
                {
                    missingEvidence.Add("STEAM_APPMANIFEST_APPID_MISMATCH");
                    continue;
                }

                if (projections.ContainsKey(parsed.AppId))
                {
                    missingEvidence.Add("STEAM_APPMANIFEST_DUPLICATE_APPID");
                    continue;
                }

                var projection = BuildProjection(
                    parsed,
                    manifestPath,
                    libraryRoot,
                    observedAt,
                    discovery.ObservedClientVersion,
                    missingEvidence);
                projections.Add(parsed.AppId, projection);
            }
        }

        var records = projections.Values
            .OrderBy(record => uint.Parse(record.ExternalGameIdentity.ExternalId, CultureInfo.InvariantCulture))
            .ToArray();
        var partial = missingEvidence.Count > 0;
        return Result(
            request,
            partial ? GameClientLibraryRefreshResultCode.Partial : GameClientLibraryRefreshResultCode.Refreshed,
            generation,
            observedAt,
            records,
            partial ? "STEAM_LIBRARY_PARTIAL" : "STEAM_LIBRARY_REFRESHED",
            discovery.ObservedClientVersion,
            partial ? missingEvidence : null);
    }

    private IReadOnlyList<string> NormalizeLibraryRoots(
        string steamRoot,
        IReadOnlyList<string> candidates,
        ISet<string> missingEvidence)
    {
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddLibraryRoot(steamRoot, normalized, missingEvidence, "STEAM_PRIMARY_LIBRARY_INVALID");

        foreach (var candidate in candidates)
            AddLibraryRoot(candidate, normalized, missingEvidence, "STEAM_LIBRARY_PATH_INVALID");

        return normalized.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private void AddLibraryRoot(
        string candidate,
        ISet<string> output,
        ISet<string> missingEvidence,
        string invalidCode)
    {
        if (string.IsNullOrWhiteSpace(candidate) || !Path.IsPathFullyQualified(candidate))
        {
            missingEvidence.Add(invalidCode);
            return;
        }

        string normalized;
        try
        {
            normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            missingEvidence.Add(invalidCode);
            return;
        }

        if (!_metadataFileSystem.DirectoryExists(Path.Combine(normalized, "steamapps")))
        {
            missingEvidence.Add("STEAM_LIBRARY_STEAMAPPS_NOT_FOUND");
            return;
        }

        output.Add(normalized);
    }

    private AdapterGameProjection BuildProjection(
        SteamAppManifestParseResult parsed,
        string manifestPath,
        string libraryRoot,
        DateTimeOffset observedAt,
        string? clientVersion,
        ISet<string> missingEvidence)
    {
        var identity = new ExternalGameIdentity(
            GameClientType.Steam,
            ExternalIdKind,
            parsed.AppId!).Normalize();
        var launchIdentity = new AdapterLaunchIdentity(
            ExternalIdKind,
            1,
            parsed.AppId!,
            observedAt,
            GameMechanismStatus.SupportedPublic).Normalize();

        var installRoot = ResolveValidatedInstallRoot(libraryRoot, parsed.InstallDirectoryName);
        GameInstallationEvidence installation;
        if (installRoot is not null && _metadataFileSystem.DirectoryExists(installRoot))
        {
            installation = new GameInstallationEvidence(
                GameInstallState.InstalledVerifiedEvidence,
                installRoot,
                observedAt,
                observedAt.AddMinutes(5),
                GameEvidenceFreshness.Fresh,
                GameEvidenceConfidence.High,
                GameMechanismStatus.BestEffortLocalEvidence,
                manifestPath).Normalize();
        }
        else
        {
            missingEvidence.Add(parsed.InstallDirectoryName is null
                ? "STEAM_APPMANIFEST_INSTALLDIR_MISSING"
                : "STEAM_INSTALL_ROOT_NOT_VERIFIED");
            installation = new GameInstallationEvidence(
                GameInstallState.Unknown,
                null,
                observedAt,
                observedAt.AddMinutes(5),
                GameEvidenceFreshness.Fresh,
                GameEvidenceConfidence.Low,
                GameMechanismStatus.BestEffortLocalEvidence,
                manifestPath).Normalize();
        }

        return new AdapterGameProjection(
            identity,
            installation,
            launchIdentity,
            Array.Empty<string>(),
            new GameLibrarySourceProvenance(
                InstallationMechanismId,
                AdapterVersion,
                EvidenceSchemaVersion,
                clientVersion),
            parsed.DisplayName).Normalize(GameClientType.Steam);
    }

    private static string? ResolveValidatedInstallRoot(string libraryRoot, string? installDirectoryName)
    {
        if (string.IsNullOrWhiteSpace(installDirectoryName)
            || Path.IsPathFullyQualified(installDirectoryName)
            || installDirectoryName.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return null;

        try
        {
            var commonRoot = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(Path.Combine(libraryRoot, "steamapps", "common")));
            var candidate = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(Path.Combine(commonRoot, installDirectoryName)));
            var prefix = commonRoot + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return null;
            return candidate;
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return null;
        }
    }

    private static bool TryManifestFileAppId(string path, out string appId)
    {
        appId = string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(path);
        const string prefix = "appmanifest_";
        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var raw = fileName[prefix.Length..];
        if (!uint.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed == 0)
            return false;

        appId = parsed.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private GameClientDiscoveryEvidence Evidence(
        GameClientDiscoveryAvailability availability,
        DateTimeOffset observedAt,
        long generation,
        string? executablePath = null,
        string? observedVersion = null,
        string? diagnosticsCode = null)
        => new GameClientDiscoveryEvidence(
            GameClientType.Steam,
            availability,
            DiscoveryMechanismId,
            GameMechanismStatus.SupportedOsMechanism,
            observedAt,
            generation,
            executablePath,
            availability == GameClientDiscoveryAvailability.NotFoundVerified ? null : ProtocolRegistrationIdentity,
            null,
            observedVersion,
            diagnosticsCode).Normalize(GameClientType.Steam);

    private static GameClientLibraryRefreshResult Result(
        GameClientLibraryRefreshRequest request,
        GameClientLibraryRefreshResultCode resultCode,
        long generation,
        DateTimeOffset observedAt,
        IReadOnlyList<AdapterGameProjection> records,
        string? parserStatus,
        string? clientVersion = null,
        IEnumerable<string>? missingEvidence = null)
    {
        var missing = missingEvidence?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return new GameClientLibraryRefreshResult(
            request.RefreshId,
            GameClientType.Steam,
            resultCode,
            generation,
            observedAt,
            records,
            clientVersion,
            parserStatus,
            resultCode == GameClientLibraryRefreshResultCode.Partial ? missing : null)
            .Normalize(request);
    }

    private static GameClientAdapterDescriptor CreateDescriptor()
        => new(
            GameClientType.Steam,
            AdapterVersion,
            [ExternalIdKind],
            [
                new GameClientAdapterCapability(
                    GameClientCapabilityId.ClientDiscovery,
                    GameMechanismStatus.SupportedOsMechanism,
                    DiscoveryMechanismId,
                    MinimumWindowsBuild: MinimumSupportedWindowsBuild),
                new GameClientAdapterCapability(
                    GameClientCapabilityId.ClientVersionEvidence,
                    GameMechanismStatus.SupportedOsMechanism,
                    VersionMechanismId,
                    MinimumWindowsBuild: MinimumSupportedWindowsBuild),
                new GameClientAdapterCapability(
                    GameClientCapabilityId.LibraryDiscovery,
                    GameMechanismStatus.VersionSensitive,
                    LibraryMechanismId,
                    MinimumWindowsBuild: MinimumSupportedWindowsBuild),
                new GameClientAdapterCapability(
                    GameClientCapabilityId.InstallationEvidence,
                    GameMechanismStatus.BestEffortLocalEvidence,
                    InstallationMechanismId,
                    MinimumWindowsBuild: MinimumSupportedWindowsBuild),
                new GameClientAdapterCapability(
                    GameClientCapabilityId.LaunchIdentityResolution,
                    GameMechanismStatus.SupportedPublic,
                    LaunchIdentityMechanismId,
                    MinimumWindowsBuild: MinimumSupportedWindowsBuild),
                new GameClientAdapterCapability(
                    GameClientCapabilityId.LaunchEligibilityEvidence,
                    GameMechanismStatus.Open,
                    "STEAM_LAUNCH_ELIGIBILITY_V1"),
                new GameClientAdapterCapability(
                    GameClientCapabilityId.GameLaunch,
                    GameMechanismStatus.Open,
                    "STEAM_PROTOCOL_RUN_V1"),
                new GameClientAdapterCapability(
                    GameClientCapabilityId.ClientInteractionEvidence,
                    GameMechanismStatus.Open,
                    "STEAM_CLIENT_INTERACTION_V1"),
                new GameClientAdapterCapability(
                    GameClientCapabilityId.GameProcessCorrelation,
                    GameMechanismStatus.Open,
                    "STEAM_PROCESS_CORRELATION_V1"),
                new GameClientAdapterCapability(
                    GameClientCapabilityId.GameExitCorrelation,
                    GameMechanismStatus.Open,
                    "STEAM_EXIT_CORRELATION_V1"),
                new GameClientAdapterCapability(
                    GameClientCapabilityId.AccountContextEvidence,
                    GameMechanismStatus.Unsupported,
                    "STEAM_ACCOUNT_CONTEXT_V1"),
                new GameClientAdapterCapability(
                    GameClientCapabilityId.UpdateStateEvidence,
                    GameMechanismStatus.Open,
                    "STEAM_UPDATE_STATE_V1")
            ],
            CompatibilityPolicyId,
            GameClientSupportStatus.PartialSupportedV1);

    private static Task<T> Unsupported<T>(string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException<T>(new NotSupportedException(message));
    }
}
