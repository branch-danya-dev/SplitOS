using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;

namespace SplitOS.RuntimeHost.GameRuntime;

public sealed record EpicProtocolRegistrationSnapshot(
    bool ReadSucceeded,
    bool SchemeKeyPresent,
    bool UrlProtocolMarkerPresent,
    string? CommandTemplate,
    string? DiagnosticsCode = null);

public interface IEpicProtocolRegistrationReader
{
    EpicProtocolRegistrationSnapshot Read();
}

public sealed record EpicExecutableEvidence(
    bool Exists,
    string? ObservedFileVersion,
    bool VersionReadSucceeded,
    string? DiagnosticsCode = null);

public interface IEpicExecutableEvidenceReader
{
    EpicExecutableEvidence Read(string normalizedExecutablePath);
}

public sealed class WindowsEpicProtocolRegistrationReader : IEpicProtocolRegistrationReader
{
    private const string SchemeKey = "com.epicgames.launcher";
    private const string CommandSubKey = @"shell\open\command";
    private const string UrlProtocolValueName = "URL Protocol";

    public EpicProtocolRegistrationSnapshot Read()
    {
        try
        {
            using var scheme = Registry.ClassesRoot.OpenSubKey(SchemeKey, writable: false);
            if (scheme is null)
                return new EpicProtocolRegistrationSnapshot(true, false, false, null);

            var hasUrlProtocolMarker = scheme.GetValueNames()
                .Any(name => string.Equals(name, UrlProtocolValueName, StringComparison.OrdinalIgnoreCase));
            using var commandKey = scheme.OpenSubKey(CommandSubKey, writable: false);
            if (commandKey is null)
            {
                return new EpicProtocolRegistrationSnapshot(
                    true,
                    true,
                    hasUrlProtocolMarker,
                    null,
                    "EPIC_PROTOCOL_HANDLER_MISSING");
            }

            var rawCommand = commandKey.GetValue(
                name: null,
                defaultValue: null,
                options: RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (rawCommand is not string command)
            {
                return new EpicProtocolRegistrationSnapshot(
                    true,
                    true,
                    hasUrlProtocolMarker,
                    null,
                    rawCommand is null
                        ? "EPIC_PROTOCOL_HANDLER_MISSING"
                        : "EPIC_PROTOCOL_HANDLER_NON_STRING");
            }

            return new EpicProtocolRegistrationSnapshot(
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
            return new EpicProtocolRegistrationSnapshot(
                false,
                false,
                false,
                null,
                "EPIC_PROTOCOL_REGISTRY_READ_FAILED");
        }
    }
}

public sealed class WindowsEpicExecutableEvidenceReader : IEpicExecutableEvidenceReader
{
    public EpicExecutableEvidence Read(string normalizedExecutablePath)
    {
        if (string.IsNullOrWhiteSpace(normalizedExecutablePath))
            throw new ArgumentException("Epic executable path is required.", nameof(normalizedExecutablePath));

        bool exists;
        try
        {
            exists = File.Exists(normalizedExecutablePath);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or IOException
            or NotSupportedException)
        {
            return new EpicExecutableEvidence(false, null, false, "EPIC_HANDLER_FILE_QUERY_FAILED");
        }

        if (!exists)
            return new EpicExecutableEvidence(false, null, true, "EPIC_HANDLER_FILE_NOT_FOUND");

        try
        {
            var info = FileVersionInfo.GetVersionInfo(normalizedExecutablePath);
            var version = FirstNonEmpty(info.ProductVersion, info.FileVersion);
            return new EpicExecutableEvidence(
                true,
                version,
                true,
                version is null ? "EPIC_CLIENT_VERSION_UNAVAILABLE" : null);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or IOException
            or Win32Exception)
        {
            return new EpicExecutableEvidence(true, null, false, "EPIC_CLIENT_VERSION_READ_FAILED");
        }
    }

    private static string? FirstNonEmpty(params string?[] candidates)
        => candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate))?.Trim();
}

public static class EpicProtocolHandlerCommandParser
{
    private static readonly string[] AcceptedPlaceholders = ["%1", "\"%1\""];

    public static bool TryParse(
        string? commandTemplate,
        out string? normalizedExecutablePath,
        out string diagnosticsCode)
    {
        normalizedExecutablePath = null;
        if (string.IsNullOrWhiteSpace(commandTemplate))
        {
            diagnosticsCode = "EPIC_PROTOCOL_HANDLER_MISSING";
            return false;
        }

        var command = commandTemplate.Trim();
        string candidate;
        string remainder;
        if (command[0] == '"')
        {
            var closingQuote = command.IndexOf('"', 1);
            if (closingQuote <= 1)
            {
                diagnosticsCode = "EPIC_PROTOCOL_HANDLER_MALFORMED";
                return false;
            }
            candidate = command[1..closingQuote];
            remainder = command[(closingQuote + 1)..].Trim();
        }
        else
        {
            var split = IndexOfWhitespace(command);
            if (split < 0)
            {
                diagnosticsCode = "EPIC_PROTOCOL_HANDLER_TEMPLATE_UNTRUSTED";
                return false;
            }
            candidate = command[..split];
            remainder = command[split..].Trim();
        }

        if (!AcceptedPlaceholders.Contains(remainder, StringComparer.OrdinalIgnoreCase))
        {
            diagnosticsCode = "EPIC_PROTOCOL_HANDLER_TEMPLATE_UNTRUSTED";
            return false;
        }
        if (candidate.IndexOf('%') >= 0 || candidate.IndexOf('\0') >= 0 || !Path.IsPathFullyQualified(candidate))
        {
            diagnosticsCode = "EPIC_PROTOCOL_HANDLER_PATH_INVALID";
            return false;
        }

        string normalized;
        try
        {
            normalized = Path.GetFullPath(candidate);
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            diagnosticsCode = "EPIC_PROTOCOL_HANDLER_PATH_INVALID";
            return false;
        }

        if (!string.Equals(Path.GetFileName(normalized), "EpicGamesLauncher.exe", StringComparison.OrdinalIgnoreCase))
        {
            diagnosticsCode = "EPIC_PROTOCOL_HANDLER_IMAGE_UNEXPECTED";
            return false;
        }

        normalizedExecutablePath = normalized;
        diagnosticsCode = "EPIC_PROTOCOL_HANDLER_VALID";
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

public sealed record EpicProductIdentity(
    string SandboxId,
    string CatalogId,
    string ArtifactId)
{
    public const int MaximumComponentLength = 256;

    public EpicProductIdentity Normalize()
        => new(
            NormalizeComponent(SandboxId, nameof(SandboxId)),
            NormalizeComponent(CatalogId, nameof(CatalogId)),
            NormalizeComponent(ArtifactId, nameof(ArtifactId)));

    public string CanonicalExternalId
    {
        get
        {
            var normalized = Normalize();
            return $"{normalized.SandboxId}:{normalized.CatalogId}:{normalized.ArtifactId}";
        }
    }

    public static bool TryParse(string? externalId, out EpicProductIdentity? identity)
    {
        identity = null;
        if (string.IsNullOrWhiteSpace(externalId))
            return false;
        var parts = externalId.Split(':');
        if (parts.Length != 3)
            return false;
        try
        {
            var candidate = new EpicProductIdentity(parts[0], parts[1], parts[2]).Normalize();
            if (!string.Equals(candidate.CanonicalExternalId, externalId, StringComparison.Ordinal))
                return false;
            identity = candidate;
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static string NormalizeComponent(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"Epic {fieldName} is required.");
        var normalized = value.Trim();
        if (normalized.Length > MaximumComponentLength
            || normalized.Any(char.IsControl)
            || normalized.Any(char.IsWhiteSpace)
            || normalized.IndexOfAny([':', '/', '\\', '?', '#', '%']) >= 0)
            throw new InvalidDataException($"Epic {fieldName} contains unsupported characters.");
        return normalized;
    }
}

public static class EpicLaunchUri
{
    private const string Prefix = "com.epicgames.launcher://apps/";
    private const string Suffix = "?action=launch&silent=true";

    public static bool TryCreate(EpicProductIdentity? identity, out string? canonicalUri)
    {
        canonicalUri = null;
        if (identity is null)
            return false;
        EpicProductIdentity normalized;
        try
        {
            normalized = identity.Normalize();
        }
        catch (InvalidDataException)
        {
            return false;
        }

        var payload = string.Join("%3A",
            Uri.EscapeDataString(normalized.SandboxId),
            Uri.EscapeDataString(normalized.CatalogId),
            Uri.EscapeDataString(normalized.ArtifactId));
        canonicalUri = Prefix + payload + Suffix;
        return true;
    }

    public static bool TryParse(string? uri, out EpicProductIdentity? identity)
    {
        identity = null;
        if (string.IsNullOrWhiteSpace(uri)
            || !uri.StartsWith(Prefix, StringComparison.Ordinal)
            || !uri.EndsWith(Suffix, StringComparison.Ordinal))
            return false;

        var encoded = uri[Prefix.Length..^Suffix.Length];
        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(encoded);
        }
        catch (UriFormatException)
        {
            return false;
        }
        if (!EpicProductIdentity.TryParse(decoded, out var parsed) || parsed is null)
            return false;
        if (!TryCreate(parsed, out var canonical)
            || !string.Equals(canonical, uri, StringComparison.Ordinal))
            return false;
        identity = parsed;
        return true;
    }
}

public enum EpicUriLaunchDisposition
{
    Accepted,
    MechanismUnavailable,
    Rejected,
    Failed
}

public sealed record EpicUriLaunchResult(EpicUriLaunchDisposition Disposition, string DiagnosticsCode);

public interface IEpicUriLaunchDispatcher
{
    EpicUriLaunchResult Dispatch(string canonicalEpicLaunchUri);
}

public sealed class WindowsEpicUriLaunchDispatcher : IEpicUriLaunchDispatcher
{
    private const int ErrorNoAssociation = 1155;

    public EpicUriLaunchResult Dispatch(string canonicalEpicLaunchUri)
    {
        if (!EpicLaunchUri.TryParse(canonicalEpicLaunchUri, out _))
            return new EpicUriLaunchResult(EpicUriLaunchDisposition.Rejected, "EPIC_LAUNCH_URI_INVALID");

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = canonicalEpicLaunchUri,
                UseShellExecute = true,
                ErrorDialog = false
            });
            return new EpicUriLaunchResult(EpicUriLaunchDisposition.Accepted, "EPIC_URI_DISPATCH_ACCEPTED");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorNoAssociation)
        {
            return new EpicUriLaunchResult(EpicUriLaunchDisposition.MechanismUnavailable, "EPIC_URI_ASSOCIATION_UNAVAILABLE");
        }
        catch (FileNotFoundException)
        {
            return new EpicUriLaunchResult(EpicUriLaunchDisposition.MechanismUnavailable, "EPIC_URI_ASSOCIATION_UNAVAILABLE");
        }
        catch (Win32Exception)
        {
            return new EpicUriLaunchResult(EpicUriLaunchDisposition.Rejected, "EPIC_URI_DISPATCH_REJECTED");
        }
        catch (InvalidOperationException)
        {
            return new EpicUriLaunchResult(EpicUriLaunchDisposition.Failed, "EPIC_URI_DISPATCH_FAILED");
        }
    }
}

/// <summary>
/// IMP-085 protocol-only Epic integration. Public protocol discovery, product-triple identity and
/// OS URI handoff are implemented here. Version-sensitive library/install parsing and process/exit
/// correlation deliberately remain OPEN until separately verified implementation slices exist.
/// </summary>
public sealed class EpicProtocolActivationAdapter : IGameClientAdapter
{
    public const string AdapterVersion = "epic-adapter/1";
    public const string CompatibilityPolicyId = "epic/v1";
    public const string ProtocolScheme = "com.epicgames.launcher:";
    public const string ExternalIdKind = "EPIC_SANDBOX_CATALOG_ARTIFACT";
    public const string DiscoveryMechanismId = "EPIC_PROTOCOL_REGISTRATION_V1";
    public const string VersionMechanismId = "EPIC_HANDLER_FILE_VERSION_V1";
    public const string LaunchIdentityMechanismId = "EPIC_PRODUCT_TRIPLE_V1";
    public const string LaunchMechanismId = "EPIC_PROTOCOL_APPS_LAUNCH_V1";
    public const int LaunchIdentitySchemaVersion = 1;
    public const int MinimumSupportedWindowsBuild = 19041;

    private readonly IEpicProtocolRegistrationReader _registrationReader;
    private readonly IEpicExecutableEvidenceReader _executableReader;
    private readonly IEpicUriLaunchDispatcher _dispatcher;
    private readonly TimeProvider _timeProvider;
    private long _generation;

    public EpicProtocolActivationAdapter(
        IEpicProtocolRegistrationReader registrationReader,
        IEpicExecutableEvidenceReader executableReader,
        IEpicUriLaunchDispatcher dispatcher,
        TimeProvider? timeProvider = null)
    {
        _registrationReader = registrationReader ?? throw new ArgumentNullException(nameof(registrationReader));
        _executableReader = executableReader ?? throw new ArgumentNullException(nameof(executableReader));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _timeProvider = timeProvider ?? TimeProvider.System;
        Descriptor = CreateDescriptor().Normalize();
    }

    public GameClientAdapterDescriptor Descriptor { get; }

    public GameClientCompatibilitySnapshot GetCompatibilityStatus(GameClientCompatibilityContext context)
    {
        var normalized = (context ?? throw new ArgumentNullException(nameof(context))).Normalize();
        var capabilities = Descriptor.Capabilities.Select(capability =>
        {
            var unsupportedWindows = capability.MinimumWindowsBuild is int minimum
                && normalized.WindowsBuild < minimum;
            return new GameClientCapabilityStatus(
                capability.CapabilityId,
                unsupportedWindows ? GameMechanismStatus.Unsupported : capability.MechanismStatus,
                capability.MechanismId,
                unsupportedWindows ? "WINDOWS_BUILD_UNSUPPORTED" : capability.NotesCode);
        }).ToArray();

        return new GameClientCompatibilitySnapshot(
            GameClientType.Epic,
            Descriptor.AdapterVersion,
            capabilities,
            _timeProvider.GetUtcNow(),
            Descriptor.CompatibilityPolicyId,
            normalized.ObservedClientVersion).Normalize(Descriptor);
    }

    public Task<GameClientDiscoveryEvidence> DiscoverClientAsync(
        int sessionId,
        GameClientFreshnessRequirement freshnessRequirement,
        CancellationToken cancellationToken)
    {
        if (sessionId < 0)
            throw new ArgumentOutOfRangeException(nameof(sessionId));
        cancellationToken.ThrowIfCancellationRequested();
        var observed = _timeProvider.GetUtcNow();
        var generation = Interlocked.Increment(ref _generation);
        var registration = _registrationReader.Read()
            ?? new EpicProtocolRegistrationSnapshot(false, false, false, null, "EPIC_PROTOCOL_REGISTRY_NO_RESULT");

        GameClientDiscoveryEvidence Evidence(
            GameClientDiscoveryAvailability availability,
            GameMechanismStatus mechanismStatus,
            string? executable = null,
            string? version = null,
            string? diagnostics = null,
            bool protocolRegistered = false)
            => new(
                GameClientType.Epic,
                availability,
                DiscoveryMechanismId,
                mechanismStatus,
                observed,
                generation,
                executable,
                protocolRegistered ? ProtocolScheme : null,
                null,
                version,
                diagnostics).Normalize(GameClientType.Epic);

        if (!registration.ReadSucceeded)
            return Task.FromResult(Evidence(GameClientDiscoveryAvailability.Unknown, GameMechanismStatus.SupportedPublic, diagnostics: registration.DiagnosticsCode ?? "EPIC_PROTOCOL_REGISTRY_READ_FAILED"));
        if (!registration.SchemeKeyPresent)
            return Task.FromResult(Evidence(GameClientDiscoveryAvailability.NotFoundVerified, GameMechanismStatus.SupportedPublic, diagnostics: "EPIC_PROTOCOL_NOT_REGISTERED"));
        if (!registration.UrlProtocolMarkerPresent)
            return Task.FromResult(Evidence(GameClientDiscoveryAvailability.Unknown, GameMechanismStatus.SupportedPublic, diagnostics: "EPIC_URL_PROTOCOL_MARKER_MISSING"));
        if (!EpicProtocolHandlerCommandParser.TryParse(registration.CommandTemplate, out var executablePath, out var parseCode)
            || executablePath is null)
            return Task.FromResult(Evidence(GameClientDiscoveryAvailability.Unknown, GameMechanismStatus.SupportedPublic, diagnostics: parseCode, protocolRegistered: true));

        var executableEvidence = _executableReader.Read(executablePath)
            ?? new EpicExecutableEvidence(false, null, false, "EPIC_HANDLER_FILE_QUERY_FAILED");
        if (!executableEvidence.Exists)
            return Task.FromResult(Evidence(GameClientDiscoveryAvailability.Unknown, GameMechanismStatus.SupportedPublic, executablePath, diagnostics: executableEvidence.DiagnosticsCode ?? "EPIC_HANDLER_FILE_NOT_FOUND", protocolRegistered: true));

        var version = string.IsNullOrWhiteSpace(executableEvidence.ObservedFileVersion)
            ? null
            : executableEvidence.ObservedFileVersion.Trim();
        return Task.FromResult(Evidence(
            version is null
                ? GameClientDiscoveryAvailability.AvailableUnverifiedVersion
                : GameClientDiscoveryAvailability.AvailableVerified,
            GameMechanismStatus.SupportedPublic,
            executablePath,
            version,
            version is null ? executableEvidence.DiagnosticsCode ?? "EPIC_CLIENT_VERSION_UNAVAILABLE" : null,
            protocolRegistered: true));
    }

    public Task<GameClientLibraryRefreshResult> RefreshLibraryAsync(
        GameClientLibraryRefreshRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = (request ?? throw new ArgumentNullException(nameof(request))).Normalize(GameClientType.Epic);
        return Task.FromResult(new GameClientLibraryRefreshResult(
            normalized.RefreshId,
            GameClientType.Epic,
            GameClientLibraryRefreshResultCode.SourceUnavailable,
            normalized.PreviousGeneration ?? 0,
            _timeProvider.GetUtcNow(),
            Array.Empty<AdapterGameProjection>(),
            ParserStatus: "EPIC_LIBRARY_CAPABILITY_OPEN").Normalize(normalized));
    }

    public Task<AdapterGameProjection?> ResolveInstallationAsync(
        ExternalGameIdentity externalGameIdentity,
        GameClientFreshnessRequirement freshnessRequirement,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = ValidateExternalIdentity(externalGameIdentity);
        return Task.FromResult<AdapterGameProjection?>(null);
    }

    public Task<PreparedClientLaunch> PrepareLaunchAsync(
        GameClientLaunchRequest request,
        AdapterGameProjection currentProjection,
        GameClientDiscoveryEvidence currentClientEvidence,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedRequest = (request ?? throw new ArgumentNullException(nameof(request))).Normalize(GameClientType.Epic);
        var projection = (currentProjection ?? throw new ArgumentNullException(nameof(currentProjection))).Normalize(GameClientType.Epic);
        var clientEvidence = (currentClientEvidence ?? throw new ArgumentNullException(nameof(currentClientEvidence))).Normalize(GameClientType.Epic);
        var now = _timeProvider.GetUtcNow();
        if (normalizedRequest.DeadlineUtc <= now)
            throw new InvalidDataException("Epic launch request has expired.");
        if (!AdapterContractNormalization.ExternalIdentityEquals(normalizedRequest.ExternalGameIdentity, projection.ExternalGameIdentity))
            throw new InvalidDataException("Epic launch projection belongs to a different external identity.");

        var productIdentity = ValidateExternalIdentity(normalizedRequest.ExternalGameIdentity);
        ValidateClientEvidence(clientEvidence, now);
        var projectedIdentity = projection.LaunchIdentity?.Normalize()
            ?? throw new InvalidDataException("Epic protocol launch requires a resolved product-triple launch identity.");
        var requestedIdentity = normalizedRequest.ResolvedLaunchIdentity.Normalize();
        ValidateLaunchIdentity(projectedIdentity, productIdentity, now);
        ValidateLaunchIdentity(requestedIdentity, productIdentity, now);
        if (!LaunchIdentityEquals(projectedIdentity, requestedIdentity))
            throw new InvalidDataException("Epic launch request identity differs from current projection evidence.");

        return Task.FromResult(new PreparedClientLaunch(
            normalizedRequest.LaunchOperationId,
            GameClientType.Epic,
            projection.ExternalGameIdentity,
            projectedIdentity,
            new GameClientObservationRules(
                projection.ExecutableCandidates,
                projection.Installation.ValidatedInstallRoot,
                null,
                null,
                Array.Empty<string>(),
                Array.Empty<string>(),
                GameClientEvidenceLevel.Medium,
                2_000,
                3_000).Normalize(),
            LaunchMechanismId,
            GameMechanismStatus.SupportedPublic,
            normalizedRequest.ExpectedInstallationEvidenceRevision,
            normalizedRequest.DeadlineUtc).Normalize(normalizedRequest));
    }

    public Task<GameClientLaunchHandoffResult> SubmitLaunchAsync(
        PreparedClientLaunch preparedLaunch,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(preparedLaunch);
        var submittedAt = _timeProvider.GetUtcNow();
        if (preparedLaunch.ClientType != GameClientType.Epic || preparedLaunch.LaunchOperationId == Guid.Empty)
            throw new InvalidDataException("Prepared Epic launch identity is invalid.");
        if (preparedLaunch.ExpiresAtUtc <= submittedAt)
            return Task.FromResult(Result(preparedLaunch, GameClientLaunchHandoffResultCode.StaleEvidence, submittedAt, "EPIC_PREPARED_LAUNCH_EXPIRED"));
        if (!string.Equals(preparedLaunch.HandoffMechanism, LaunchMechanismId, StringComparison.Ordinal)
            || preparedLaunch.HandoffMechanismStatus != GameMechanismStatus.SupportedPublic)
            return Task.FromResult(Result(preparedLaunch, GameClientLaunchHandoffResultCode.MechanismUnavailable, submittedAt, "EPIC_HANDOFF_MECHANISM_UNAVAILABLE"));

        EpicProductIdentity identity;
        try
        {
            identity = ValidateExternalIdentity(preparedLaunch.NormalizedExternalGameIdentity);
            ValidateLaunchIdentity(preparedLaunch.ResolvedLaunchIdentity.Normalize(), identity, submittedAt);
        }
        catch (InvalidDataException)
        {
            return Task.FromResult(Result(preparedLaunch, GameClientLaunchHandoffResultCode.LaunchIdentityInvalid, submittedAt, "EPIC_LAUNCH_IDENTITY_INVALID"));
        }
        if (!EpicLaunchUri.TryCreate(identity, out var uri) || uri is null)
            return Task.FromResult(Result(preparedLaunch, GameClientLaunchHandoffResultCode.LaunchIdentityInvalid, submittedAt, "EPIC_LAUNCH_URI_INVALID"));

        EpicUriLaunchResult dispatch;
        try
        {
            dispatch = _dispatcher.Dispatch(uri)
                ?? new EpicUriLaunchResult(EpicUriLaunchDisposition.Failed, "EPIC_URI_DISPATCH_NO_RESULT");
        }
        catch
        {
            dispatch = new EpicUriLaunchResult(EpicUriLaunchDisposition.Failed, "EPIC_URI_DISPATCH_FAILED");
        }
        var code = dispatch.Disposition switch
        {
            EpicUriLaunchDisposition.Accepted => GameClientLaunchHandoffResultCode.HandoffAccepted,
            EpicUriLaunchDisposition.MechanismUnavailable => GameClientLaunchHandoffResultCode.MechanismUnavailable,
            EpicUriLaunchDisposition.Rejected => GameClientLaunchHandoffResultCode.HandoffRejected,
            _ => GameClientLaunchHandoffResultCode.UnknownFailure
        };
        return Task.FromResult(Result(
            preparedLaunch,
            code,
            submittedAt,
            string.IsNullOrWhiteSpace(dispatch.DiagnosticsCode) ? "EPIC_URI_DISPATCH_FAILED" : dispatch.DiagnosticsCode.Trim()));
    }

    public Task<GameClientObservationResult> ObserveLaunchAsync(
        PreparedClientLaunch preparedLaunch,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new GameClientObservationResult(
            GameClientObservationClassification.CorrelationLost,
            GameClientEvidenceLevel.Weak,
            Array.Empty<GameClientCorrelatedProcessEvidence>(),
            false,
            _timeProvider.GetUtcNow(),
            "EPIC_PROCESS_CORRELATION_CAPABILITY_OPEN").Normalize());
    }

    public Task<GameClientExitObservationResult> ObserveExitAsync(
        PreparedClientLaunch preparedLaunch,
        IReadOnlyList<GameClientCorrelatedProcessEvidence> currentCorrelation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new GameClientExitObservationResult(
            GameClientExitObservationClassification.CorrelationLost,
            Array.Empty<GameClientCorrelatedProcessEvidence>(),
            _timeProvider.GetUtcNow(),
            "EPIC_EXIT_CORRELATION_CAPABILITY_OPEN").Normalize());
    }

    public void Invalidate(GameClientLibraryRefreshReason reason)
    {
    }

    private static GameClientAdapterDescriptor CreateDescriptor()
    {
        var capabilities = Enum.GetValues<GameClientCapabilityId>()
            .Select(capability => capability switch
            {
                GameClientCapabilityId.ClientDiscovery => Capability(capability, GameMechanismStatus.SupportedPublic, DiscoveryMechanismId, MinimumSupportedWindowsBuild),
                GameClientCapabilityId.ClientVersionEvidence => Capability(capability, GameMechanismStatus.SupportedOsMechanism, VersionMechanismId, MinimumSupportedWindowsBuild),
                GameClientCapabilityId.LaunchIdentityResolution => Capability(capability, GameMechanismStatus.SupportedPublic, LaunchIdentityMechanismId, MinimumSupportedWindowsBuild),
                GameClientCapabilityId.GameLaunch => Capability(capability, GameMechanismStatus.SupportedPublic, LaunchMechanismId, MinimumSupportedWindowsBuild),
                GameClientCapabilityId.AccountContextEvidence => Capability(capability, GameMechanismStatus.Unsupported, "EPIC_ACCOUNT_CONTEXT_UNSUPPORTED"),
                _ => Capability(capability, GameMechanismStatus.Open, $"EPIC_{capability.ToString().ToUpperInvariant()}_OPEN")
            })
            .ToArray();
        return new GameClientAdapterDescriptor(
            GameClientType.Epic,
            AdapterVersion,
            new[] { ExternalIdKind },
            capabilities,
            CompatibilityPolicyId,
            GameClientSupportStatus.PartialSupportedV1);
    }

    private static GameClientAdapterCapability Capability(
        GameClientCapabilityId id,
        GameMechanismStatus status,
        string mechanism,
        int? minimumWindowsBuild = null)
        => new(id, status, mechanism, minimumWindowsBuild);

    private static EpicProductIdentity ValidateExternalIdentity(ExternalGameIdentity identity)
    {
        var normalized = (identity ?? throw new InvalidDataException("Epic external identity is required.")).Normalize();
        if (normalized.ClientType != GameClientType.Epic
            || !string.Equals(normalized.ExternalIdKind, ExternalIdKind, StringComparison.Ordinal)
            || !EpicProductIdentity.TryParse(normalized.ExternalId, out var product)
            || product is null)
            throw new InvalidDataException("Epic activation requires canonical Sandbox:Catalog:Artifact identity.");
        return product;
    }

    private static void ValidateClientEvidence(GameClientDiscoveryEvidence evidence, DateTimeOffset now)
    {
        if (evidence.AvailabilityState is not GameClientDiscoveryAvailability.AvailableVerified
            and not GameClientDiscoveryAvailability.AvailableUnverifiedVersion)
            throw new InvalidDataException("Epic protocol handler is not currently available.");
        if (evidence.MechanismStatus != GameMechanismStatus.SupportedPublic
            || !string.Equals(evidence.MechanismId, DiscoveryMechanismId, StringComparison.Ordinal)
            || !string.Equals(evidence.ProtocolRegistration, ProtocolScheme, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(evidence.ExecutableIdentity))
            throw new InvalidDataException("Epic client discovery evidence is not valid for protocol handoff.");
        if (evidence.ObservedAtUtc > now)
            throw new InvalidDataException("Epic client discovery evidence is from the future.");
    }

    private static void ValidateLaunchIdentity(
        AdapterLaunchIdentity launchIdentity,
        EpicProductIdentity expected,
        DateTimeOffset now)
    {
        var normalized = launchIdentity.Normalize();
        if (!string.Equals(normalized.Kind, ExternalIdKind, StringComparison.Ordinal)
            || normalized.SchemaVersion != LaunchIdentitySchemaVersion
            || normalized.MechanismStatus != GameMechanismStatus.SupportedPublic
            || !EpicProductIdentity.TryParse(normalized.Payload, out var product)
            || product is null
            || !string.Equals(product.CanonicalExternalId, expected.CanonicalExternalId, StringComparison.Ordinal))
            throw new InvalidDataException("Epic product-triple launch identity is invalid.");
        if (normalized.ObservedAtUtc > now)
            throw new InvalidDataException("Epic launch identity evidence is from the future.");
    }

    private static bool LaunchIdentityEquals(AdapterLaunchIdentity left, AdapterLaunchIdentity right)
    {
        var a = left.Normalize();
        var b = right.Normalize();
        return string.Equals(a.Kind, b.Kind, StringComparison.Ordinal)
            && a.SchemaVersion == b.SchemaVersion
            && string.Equals(a.Payload, b.Payload, StringComparison.Ordinal)
            && a.MechanismStatus == b.MechanismStatus;
    }

    private static GameClientLaunchHandoffResult Result(
        PreparedClientLaunch prepared,
        GameClientLaunchHandoffResultCode code,
        DateTimeOffset submittedAt,
        string diagnostics)
        => new(
            prepared.LaunchOperationId,
            code,
            submittedAt,
            null,
            null,
            diagnostics).Normalize(prepared);
}
