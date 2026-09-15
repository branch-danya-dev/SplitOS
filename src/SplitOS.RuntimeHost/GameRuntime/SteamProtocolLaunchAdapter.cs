using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace SplitOS.RuntimeHost.GameRuntime;

public enum SteamUriLaunchDisposition
{
    Accepted,
    MechanismUnavailable,
    Rejected,
    Failed
}

public sealed record SteamUriLaunchResult(
    SteamUriLaunchDisposition Disposition,
    string DiagnosticsCode);

public interface ISteamUriLaunchDispatcher
{
    SteamUriLaunchResult Dispatch(string canonicalSteamRunUri);
}

/// <summary>
/// Canonical Steam run URI codec. The only accepted payload is a normalized unsigned AppID.
/// Arbitrary paths, query strings, fragments and launch arguments are outside this boundary.
/// </summary>
public static class SteamRunUri
{
    private const string Prefix = "steam://run/";

    public static bool TryCreate(string? appId, out string? canonicalUri)
    {
        canonicalUri = null;
        if (!TryCanonicalAppId(appId, out var canonicalAppId))
            return false;

        canonicalUri = Prefix + canonicalAppId;
        return true;
    }

    public static bool TryParse(string? uri, out string? appId)
    {
        appId = null;
        if (string.IsNullOrWhiteSpace(uri)
            || !uri.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var payload = uri[Prefix.Length..];
        if (payload.Length == 0
            || payload.IndexOfAny(['/', '?', '#', '%', '\\']) >= 0
            || !TryCanonicalAppId(payload, out var canonicalAppId))
            return false;

        var canonical = Prefix + canonicalAppId;
        if (!string.Equals(uri, canonical, StringComparison.OrdinalIgnoreCase))
            return false;

        appId = canonicalAppId;
        return true;
    }

    public static bool TryCanonicalAppId(string? value, out string? canonicalAppId)
    {
        canonicalAppId = null;
        if (string.IsNullOrWhiteSpace(value)
            || !uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            || parsed == 0)
            return false;

        var normalized = parsed.ToString(CultureInfo.InvariantCulture);
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
            return false;

        canonicalAppId = normalized;
        return true;
    }
}

/// <summary>
/// Dispatches only a prevalidated canonical steam://run/&lt;appid&gt; URI through the Windows shell
/// in the interactive RuntimeHost user context. It never executes the registered handler command.
/// A successful shell dispatch is handoff evidence only, never game-running evidence.
/// </summary>
public sealed class WindowsSteamUriLaunchDispatcher : ISteamUriLaunchDispatcher
{
    private const int ErrorNoAssociation = 1155;

    public SteamUriLaunchResult Dispatch(string canonicalSteamRunUri)
    {
        if (!SteamRunUri.TryParse(canonicalSteamRunUri, out _))
        {
            return new SteamUriLaunchResult(
                SteamUriLaunchDisposition.Rejected,
                "STEAM_RUN_URI_INVALID");
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = canonicalSteamRunUri,
                UseShellExecute = true,
                ErrorDialog = false
            });

            return new SteamUriLaunchResult(
                SteamUriLaunchDisposition.Accepted,
                "STEAM_URI_DISPATCH_ACCEPTED");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorNoAssociation)
        {
            return new SteamUriLaunchResult(
                SteamUriLaunchDisposition.MechanismUnavailable,
                "STEAM_URI_ASSOCIATION_UNAVAILABLE");
        }
        catch (FileNotFoundException)
        {
            return new SteamUriLaunchResult(
                SteamUriLaunchDisposition.MechanismUnavailable,
                "STEAM_URI_ASSOCIATION_UNAVAILABLE");
        }
        catch (Win32Exception)
        {
            return new SteamUriLaunchResult(
                SteamUriLaunchDisposition.Rejected,
                "STEAM_URI_DISPATCH_REJECTED");
        }
        catch (InvalidOperationException)
        {
            return new SteamUriLaunchResult(
                SteamUriLaunchDisposition.Failed,
                "STEAM_URI_DISPATCH_FAILED");
        }
    }
}

/// <summary>
/// IMP-083 capability decorator. The already-verified SteamClientAdapter continues to own discovery
/// and local library evidence. This layer adds preparation and Windows protocol handoff without
/// mutating the earlier slices or weakening their capability boundaries.
/// </summary>
public sealed class SteamProtocolLaunchAdapter : IGameClientAdapter
{
    public const string AdapterVersion = "steam-adapter/3";
    public const string HandoffMechanismId = "STEAM_PROTOCOL_RUN_V1";
    public const int LaunchIdentitySchemaVersion = 1;
    public const int StartStabilityWindowMs = 2_000;
    public const int ExitGraceWindowMs = 3_000;

    private readonly SteamClientAdapter _inner;
    private readonly ISteamUriLaunchDispatcher _dispatcher;
    private readonly TimeProvider _timeProvider;

    public SteamProtocolLaunchAdapter(
        SteamClientAdapter inner,
        ISteamUriLaunchDispatcher dispatcher,
        TimeProvider? timeProvider = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
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

    public Task<PreparedClientLaunch> PrepareLaunchAsync(
        GameClientLaunchRequest request,
        AdapterGameProjection currentProjection,
        GameClientDiscoveryEvidence currentClientEvidence,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedRequest = (request ?? throw new ArgumentNullException(nameof(request)))
            .Normalize(GameClientType.Steam);
        var projection = (currentProjection ?? throw new ArgumentNullException(nameof(currentProjection)))
            .Normalize(GameClientType.Steam);
        var clientEvidence = (currentClientEvidence ?? throw new ArgumentNullException(nameof(currentClientEvidence)))
            .Normalize(GameClientType.Steam);
        var now = _timeProvider.GetUtcNow();

        if (normalizedRequest.DeadlineUtc <= now)
            throw new InvalidDataException("Steam launch request has already expired.");
        if (!AdapterContractNormalization.ExternalIdentityEquals(
                projection.ExternalGameIdentity,
                normalizedRequest.ExternalGameIdentity))
            throw new InvalidDataException("Steam launch projection belongs to a different external game identity.");

        var appId = ValidateCanonicalSteamIdentity(projection.ExternalGameIdentity);
        ValidateCurrentClientEvidence(clientEvidence);
        ValidateInstallationEvidence(projection.Installation, now);

        var projectedLaunchIdentity = projection.LaunchIdentity?.Normalize()
            ?? throw new InvalidDataException("Steam launch preparation requires current AppID launch identity evidence.");
        var requestedLaunchIdentity = normalizedRequest.ResolvedLaunchIdentity.Normalize();
        ValidateLaunchIdentity(projectedLaunchIdentity, appId, now);
        ValidateLaunchIdentity(requestedLaunchIdentity, appId, now);
        if (!LaunchIdentityEquals(projectedLaunchIdentity, requestedLaunchIdentity))
            throw new InvalidDataException("Steam launch request identity does not match the current projection identity.");

        var observationRules = new GameClientObservationRules(
            projection.ExecutableCandidates,
            projection.Installation.ValidatedInstallRoot,
            null,
            null,
            Array.Empty<string>(),
            Array.Empty<string>(),
            GameClientEvidenceLevel.Medium,
            StartStabilityWindowMs,
            ExitGraceWindowMs).Normalize();

        var prepared = new PreparedClientLaunch(
            normalizedRequest.LaunchOperationId,
            GameClientType.Steam,
            projection.ExternalGameIdentity,
            projectedLaunchIdentity,
            observationRules,
            HandoffMechanismId,
            GameMechanismStatus.SupportedPublic,
            normalizedRequest.ExpectedInstallationEvidenceRevision,
            normalizedRequest.DeadlineUtc);

        return Task.FromResult(prepared.Normalize(normalizedRequest));
    }

    public Task<GameClientLaunchHandoffResult> SubmitLaunchAsync(
        PreparedClientLaunch preparedLaunch,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(preparedLaunch);
        var submittedAt = _timeProvider.GetUtcNow();

        if (preparedLaunch.ClientType != GameClientType.Steam)
            throw new InvalidDataException("Prepared launch belongs to a different client type.");
        if (preparedLaunch.LaunchOperationId == Guid.Empty)
            throw new InvalidDataException("Prepared Steam launch requires a non-empty launch operation ID.");

        if (preparedLaunch.ExpiresAtUtc <= submittedAt)
        {
            return Task.FromResult(Result(
                preparedLaunch,
                GameClientLaunchHandoffResultCode.StaleEvidence,
                submittedAt,
                "STEAM_PREPARED_LAUNCH_EXPIRED"));
        }

        if (!string.Equals(preparedLaunch.HandoffMechanism, HandoffMechanismId, StringComparison.Ordinal)
            || preparedLaunch.HandoffMechanismStatus != GameMechanismStatus.SupportedPublic)
        {
            return Task.FromResult(Result(
                preparedLaunch,
                GameClientLaunchHandoffResultCode.MechanismUnavailable,
                submittedAt,
                "STEAM_HANDOFF_MECHANISM_UNAVAILABLE"));
        }

        string appId;
        try
        {
            appId = ValidateCanonicalSteamIdentity(preparedLaunch.NormalizedExternalGameIdentity);
            ValidateLaunchIdentity(preparedLaunch.ResolvedLaunchIdentity.Normalize(), appId, submittedAt, allowPastEvidence: true);
        }
        catch (InvalidDataException)
        {
            return Task.FromResult(Result(
                preparedLaunch,
                GameClientLaunchHandoffResultCode.LaunchIdentityInvalid,
                submittedAt,
                "STEAM_LAUNCH_IDENTITY_INVALID"));
        }

        if (!SteamRunUri.TryCreate(appId, out var canonicalUri) || canonicalUri is null)
        {
            return Task.FromResult(Result(
                preparedLaunch,
                GameClientLaunchHandoffResultCode.LaunchIdentityInvalid,
                submittedAt,
                "STEAM_RUN_URI_INVALID"));
        }

        SteamUriLaunchResult dispatch;
        try
        {
            dispatch = _dispatcher.Dispatch(canonicalUri)
                ?? new SteamUriLaunchResult(SteamUriLaunchDisposition.Failed, "STEAM_URI_DISPATCH_NO_RESULT");
        }
        catch
        {
            dispatch = new SteamUriLaunchResult(
                SteamUriLaunchDisposition.Failed,
                "STEAM_URI_DISPATCH_FAILED");
        }

        var resultCode = dispatch.Disposition switch
        {
            SteamUriLaunchDisposition.Accepted => GameClientLaunchHandoffResultCode.HandoffAccepted,
            SteamUriLaunchDisposition.MechanismUnavailable => GameClientLaunchHandoffResultCode.MechanismUnavailable,
            SteamUriLaunchDisposition.Rejected => GameClientLaunchHandoffResultCode.HandoffRejected,
            _ => GameClientLaunchHandoffResultCode.UnknownFailure
        };

        return Task.FromResult(Result(
            preparedLaunch,
            resultCode,
            submittedAt,
            string.IsNullOrWhiteSpace(dispatch.DiagnosticsCode)
                ? "STEAM_URI_DISPATCH_FAILED"
                : dispatch.DiagnosticsCode.Trim()));
    }

    public Task<GameClientObservationResult> ObserveLaunchAsync(
        PreparedClientLaunch preparedLaunch,
        CancellationToken cancellationToken)
        => _inner.ObserveLaunchAsync(preparedLaunch, cancellationToken);

    public Task<GameClientExitObservationResult> ObserveExitAsync(
        PreparedClientLaunch preparedLaunch,
        IReadOnlyList<GameClientCorrelatedProcessEvidence> currentCorrelation,
        CancellationToken cancellationToken)
        => _inner.ObserveExitAsync(preparedLaunch, currentCorrelation, cancellationToken);

    public void Invalidate(GameClientLibraryRefreshReason reason)
        => _inner.Invalidate(reason);

    private static GameClientAdapterDescriptor CreateDescriptor(GameClientAdapterDescriptor innerDescriptor)
    {
        var normalized = innerDescriptor.Normalize();
        var capabilities = normalized.Capabilities
            .Select(capability => capability.CapabilityId == GameClientCapabilityId.GameLaunch
                ? capability with
                {
                    MechanismStatus = GameMechanismStatus.SupportedPublic,
                    MechanismId = HandoffMechanismId,
                    MinimumWindowsBuild = SteamClientAdapter.MinimumSupportedWindowsBuild,
                    RequiresFreshLibraryEvidence = true,
                    NotesCode = null
                }
                : capability)
            .ToArray();

        return normalized with
        {
            AdapterVersion = AdapterVersion,
            Capabilities = Array.AsReadOnly(capabilities),
            SupportStatus = GameClientSupportStatus.PartialSupportedV1
        };
    }

    private static string ValidateCanonicalSteamIdentity(ExternalGameIdentity identity)
    {
        var normalized = (identity ?? throw new InvalidDataException("Steam external identity is required.")).Normalize();
        if (normalized.ClientType != GameClientType.Steam
            || !string.Equals(normalized.ExternalIdKind, SteamClientAdapter.ExternalIdKind, StringComparison.Ordinal)
            || !SteamRunUri.TryCanonicalAppId(normalized.ExternalId, out var appId)
            || appId is null)
            throw new InvalidDataException("Steam launch requires canonical STEAM_APP_ID identity.");
        return appId;
    }

    private static void ValidateCurrentClientEvidence(GameClientDiscoveryEvidence evidence)
    {
        if (evidence.AvailabilityState is not GameClientDiscoveryAvailability.AvailableVerified
            and not GameClientDiscoveryAvailability.AvailableUnverifiedVersion)
            throw new InvalidDataException("Current Steam client evidence does not prove an available protocol handler.");
        if (evidence.MechanismStatus != GameMechanismStatus.SupportedOsMechanism
            || !string.Equals(evidence.MechanismId, SteamClientAdapter.DiscoveryMechanismId, StringComparison.Ordinal)
            || !string.Equals(evidence.ProtocolRegistration, SteamClientAdapter.ProtocolRegistrationIdentity, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(evidence.ExecutableIdentity))
            throw new InvalidDataException("Current Steam client evidence is not valid for protocol handoff.");
    }

    private static void ValidateInstallationEvidence(
        GameInstallationEvidence installation,
        DateTimeOffset now)
    {
        var normalized = (installation ?? throw new InvalidDataException("Steam installation evidence is required.")).Normalize();
        if (normalized.State != GameInstallState.InstalledVerifiedEvidence
            || normalized.Freshness != GameEvidenceFreshness.Fresh
            || normalized.Confidence == GameEvidenceConfidence.Low
            || normalized.MechanismStatus != GameMechanismStatus.BestEffortLocalEvidence
            || string.IsNullOrWhiteSpace(normalized.ValidatedInstallRoot))
            throw new InvalidDataException("Steam launch requires fresh verified installation evidence.");
        if (normalized.ObservedAtUtc > now)
            throw new InvalidDataException("Steam installation evidence is from the future.");
        if (normalized.ExpiresAtUtc is DateTimeOffset expiresAt && expiresAt <= now)
            throw new InvalidDataException("Steam installation evidence has expired.");
    }

    private static void ValidateLaunchIdentity(
        AdapterLaunchIdentity identity,
        string expectedAppId,
        DateTimeOffset now,
        bool allowPastEvidence = false)
    {
        var normalized = (identity ?? throw new InvalidDataException("Steam launch identity is required.")).Normalize();
        if (!string.Equals(normalized.Kind, SteamClientAdapter.ExternalIdKind, StringComparison.Ordinal)
            || normalized.SchemaVersion != LaunchIdentitySchemaVersion
            || normalized.MechanismStatus != GameMechanismStatus.SupportedPublic
            || !SteamRunUri.TryCanonicalAppId(normalized.Payload, out var appId)
            || !string.Equals(appId, expectedAppId, StringComparison.Ordinal))
            throw new InvalidDataException("Steam launch identity is invalid.");
        if (normalized.ObservedAtUtc > now)
            throw new InvalidDataException("Steam launch identity evidence is from the future.");
        if (!allowPastEvidence && normalized.ObservedAtUtc == default)
            throw new InvalidDataException("Steam launch identity requires current evidence.");
    }

    private static bool LaunchIdentityEquals(AdapterLaunchIdentity left, AdapterLaunchIdentity right)
    {
        var normalizedLeft = left.Normalize();
        var normalizedRight = right.Normalize();
        return string.Equals(normalizedLeft.Kind, normalizedRight.Kind, StringComparison.Ordinal)
            && normalizedLeft.SchemaVersion == normalizedRight.SchemaVersion
            && string.Equals(normalizedLeft.Payload, normalizedRight.Payload, StringComparison.Ordinal)
            && normalizedLeft.ObservedAtUtc == normalizedRight.ObservedAtUtc
            && normalizedLeft.MechanismStatus == normalizedRight.MechanismStatus;
    }

    private static GameClientLaunchHandoffResult Result(
        PreparedClientLaunch prepared,
        GameClientLaunchHandoffResultCode resultCode,
        DateTimeOffset submittedAt,
        string diagnosticsCode)
        => new GameClientLaunchHandoffResult(
            prepared.LaunchOperationId,
            resultCode,
            submittedAt,
            null,
            null,
            diagnosticsCode).Normalize(prepared);
}
