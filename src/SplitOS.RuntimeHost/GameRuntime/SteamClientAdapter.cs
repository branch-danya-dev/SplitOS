using System.Diagnostics;
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
            // Existence is still current verified evidence. Version evidence degrades independently.
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
/// First concrete game-client adapter slice. IMP-081 implements only Steam client discovery and
/// client-version evidence. Remaining capabilities deliberately stay OPEN/UNSUPPORTED until their
/// owning backlog slices land, so the registry never advertises behavior that does not exist yet.
/// </summary>
public sealed class SteamClientAdapter : IGameClientAdapter
{
    public const string AdapterVersion = "steam-adapter/1";
    public const string CompatibilityPolicyId = "steam/v1";
    public const string DiscoveryMechanismId = "STEAM_PROTOCOL_REGISTRATION_V1";
    public const string VersionMechanismId = "STEAM_HANDLER_FILE_VERSION_V1";
    public const string ProtocolRegistrationIdentity = "steam:";
    public const int MinimumSupportedWindowsBuild = 19041;

    private readonly ISteamProtocolRegistrationReader _registrationReader;
    private readonly ISteamExecutableEvidenceReader _executableEvidenceReader;
    private readonly TimeProvider _timeProvider;
    private long _snapshotGeneration;

    public SteamClientAdapter(
        ISteamProtocolRegistrationReader registrationReader,
        ISteamExecutableEvidenceReader executableEvidenceReader,
        TimeProvider? timeProvider = null)
    {
        _registrationReader = registrationReader ?? throw new ArgumentNullException(nameof(registrationReader));
        _executableEvidenceReader = executableEvidenceReader ?? throw new ArgumentNullException(nameof(executableEvidenceReader));
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
            return Task.FromResult(Evidence(
                GameClientDiscoveryAvailability.Unknown,
                observedAt,
                generation,
                diagnosticsCode: "STEAM_PROTOCOL_REGISTRY_READ_FAILED"));
        }

        if (!registration.ReadSucceeded)
        {
            return Task.FromResult(Evidence(
                GameClientDiscoveryAvailability.Unknown,
                observedAt,
                generation,
                diagnosticsCode: registration.DiagnosticsCode ?? "STEAM_PROTOCOL_REGISTRY_READ_FAILED"));
        }

        if (!registration.SchemeKeyPresent)
        {
            return Task.FromResult(Evidence(
                GameClientDiscoveryAvailability.NotFoundVerified,
                observedAt,
                generation,
                diagnosticsCode: "STEAM_PROTOCOL_NOT_REGISTERED"));
        }

        if (!registration.UrlProtocolMarkerPresent)
        {
            return Task.FromResult(Evidence(
                GameClientDiscoveryAvailability.Unknown,
                observedAt,
                generation,
                diagnosticsCode: "STEAM_URL_PROTOCOL_MARKER_MISSING"));
        }

        if (!SteamProtocolHandlerCommandParser.TryParse(
                registration.CommandTemplate,
                out var executablePath,
                out var parseDiagnostics)
            || executablePath is null)
        {
            return Task.FromResult(Evidence(
                GameClientDiscoveryAvailability.Unknown,
                observedAt,
                generation,
                diagnosticsCode: parseDiagnostics));
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
            return Task.FromResult(Evidence(
                GameClientDiscoveryAvailability.Unknown,
                observedAt,
                generation,
                executablePath,
                diagnosticsCode: "STEAM_HANDLER_FILE_QUERY_FAILED"));
        }

        if (!executableEvidence.Exists)
        {
            // A stale registration does not prove that Steam is not installed elsewhere; fail closed.
            return Task.FromResult(Evidence(
                GameClientDiscoveryAvailability.Unknown,
                observedAt,
                generation,
                executablePath,
                diagnosticsCode: executableEvidence.DiagnosticsCode ?? "STEAM_HANDLER_FILE_NOT_FOUND"));
        }

        var observedVersion = string.IsNullOrWhiteSpace(executableEvidence.ObservedFileVersion)
            ? null
            : executableEvidence.ObservedFileVersion.Trim();
        var availability = observedVersion is null
            ? GameClientDiscoveryAvailability.AvailableUnverifiedVersion
            : GameClientDiscoveryAvailability.AvailableVerified;

        return Task.FromResult(Evidence(
            availability,
            observedAt,
            generation,
            executablePath,
            observedVersion,
            observedVersion is null
                ? executableEvidence.DiagnosticsCode ?? "STEAM_CLIENT_VERSION_UNAVAILABLE"
                : null));
    }

    public Task<GameClientLibraryRefreshResult> RefreshLibraryAsync(
        GameClientLibraryRefreshRequest request,
        CancellationToken cancellationToken)
        => Unsupported<GameClientLibraryRefreshResult>("IMP-082 owns Steam library refresh.", cancellationToken);

    public Task<AdapterGameProjection?> ResolveInstallationAsync(
        ExternalGameIdentity externalGameIdentity,
        GameClientFreshnessRequirement freshnessRequirement,
        CancellationToken cancellationToken)
        => Unsupported<AdapterGameProjection?>("IMP-082 owns Steam installation evidence.", cancellationToken);

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
        // IMP-081 has no cache. Explicit discovery always re-reads HKCR and executable evidence.
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

    private static GameClientAdapterDescriptor CreateDescriptor()
        => new(
            GameClientType.Steam,
            AdapterVersion,
            ["STEAM_APP_ID"],
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
                    GameMechanismStatus.Open,
                    "STEAM_LIBRARY_VDF_V1"),
                new GameClientAdapterCapability(
                    GameClientCapabilityId.InstallationEvidence,
                    GameMechanismStatus.Open,
                    "STEAM_APPMANIFEST_INSTALL_EVIDENCE_V1"),
                new GameClientAdapterCapability(
                    GameClientCapabilityId.LaunchIdentityResolution,
                    GameMechanismStatus.Open,
                    "STEAM_APP_ID_LAUNCH_IDENTITY_V1"),
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
