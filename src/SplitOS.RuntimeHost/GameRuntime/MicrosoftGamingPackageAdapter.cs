using System.Globalization;
using System.Runtime.InteropServices;
using Windows.Management.Deployment;

namespace SplitOS.RuntimeHost.GameRuntime;

public sealed record MicrosoftPackageApplicationRegistration(
    string AppUserModelId,
    string? DisplayName = null)
{
    public MicrosoftPackageApplicationRegistration Normalize()
        => this with
        {
            AppUserModelId = MicrosoftGamingIdentity.NormalizeAumid(AppUserModelId),
            DisplayName = Optional(DisplayName)
        };

    private static string? Optional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record MicrosoftPackageRegistration(
    string PackageFamilyName,
    string PackageIdentityName,
    string PackageFullName,
    string PackageVersion,
    bool IsFramework,
    bool IsResourcePackage,
    bool IsUsable,
    IReadOnlyList<MicrosoftPackageApplicationRegistration> Applications,
    string? DiagnosticsCode = null)
{
    public MicrosoftPackageRegistration Normalize()
    {
        var family = MicrosoftGamingIdentity.NormalizePackageFamilyName(PackageFamilyName);
        var identityName = Required(PackageIdentityName, nameof(PackageIdentityName));
        var fullName = Required(PackageFullName, nameof(PackageFullName));
        var version = Required(PackageVersion, nameof(PackageVersion));
        var applications = (Applications ?? Array.Empty<MicrosoftPackageApplicationRegistration>())
            .Select(application => (application ?? throw new InvalidDataException("Microsoft package application registration cannot be null.")).Normalize())
            .GroupBy(application => application.AppUserModelId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(application => application.AppUserModelId, StringComparer.Ordinal)
            .ToArray();

        return this with
        {
            PackageFamilyName = family,
            PackageIdentityName = identityName,
            PackageFullName = fullName,
            PackageVersion = version,
            Applications = Array.AsReadOnly(applications),
            DiagnosticsCode = Optional(DiagnosticsCode)
        };
    }

    private static string Required(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"{field} is required.");
        return value.Trim();
    }

    private static string? Optional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record MicrosoftPackageCatalogSnapshot(
    bool ReadSucceeded,
    IReadOnlyList<MicrosoftPackageRegistration> Packages,
    string? DiagnosticsCode = null)
{
    public MicrosoftPackageCatalogSnapshot Normalize()
        => this with
        {
            Packages = Array.AsReadOnly((Packages ?? Array.Empty<MicrosoftPackageRegistration>())
                .Select(package => (package ?? throw new InvalidDataException("Microsoft package registration cannot be null.")).Normalize())
                .OrderBy(package => package.PackageFamilyName, StringComparer.Ordinal)
                .ThenBy(package => package.PackageFullName, StringComparer.Ordinal)
                .ToArray()),
            DiagnosticsCode = string.IsNullOrWhiteSpace(DiagnosticsCode) ? null : DiagnosticsCode.Trim()
        };
}

public interface IMicrosoftPackageRegistrationReader
{
    Task<MicrosoftPackageCatalogSnapshot> ReadCurrentUserPackagesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Uses supported current-user Windows package registration APIs. It does not enumerate all users,
/// infer Xbox/Game Pass ownership, or inspect WindowsApps content directly.
/// </summary>
public sealed class WindowsMicrosoftPackageRegistrationReader : IMicrosoftPackageRegistrationReader
{
    public async Task<MicrosoftPackageCatalogSnapshot> ReadCurrentUserPackagesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var manager = new PackageManager();
            var packages = new List<MicrosoftPackageRegistration>();
            foreach (var package in manager.FindPackagesForUser(string.Empty))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = package.Id;
                var applications = new List<MicrosoftPackageApplicationRegistration>();
                try
                {
                    var entries = await package.GetAppListEntriesAsync();
                    foreach (var entry in entries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string? displayName = null;
                        try
                        {
                            displayName = entry.DisplayInfo?.DisplayName;
                        }
                        catch
                        {
                            // Localized display metadata is optional evidence only.
                        }

                        if (!string.IsNullOrWhiteSpace(entry.AppUserModelId))
                            applications.Add(new MicrosoftPackageApplicationRegistration(entry.AppUserModelId, displayName));
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Package registration itself remains useful evidence when app-list extraction fails.
                }

                var version = id.Version;
                var versionText = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}");
                bool isUsable;
                try
                {
                    isUsable = package.Status.VerifyIsOK();
                }
                catch
                {
                    isUsable = false;
                }

                packages.Add(new MicrosoftPackageRegistration(
                    id.FamilyName,
                    id.Name,
                    id.FullName,
                    versionText,
                    package.IsFramework,
                    package.IsResourcePackage,
                    isUsable,
                    applications,
                    isUsable ? null : "MSFT_PACKAGE_STATUS_UNUSABLE"));
            }

            return new MicrosoftPackageCatalogSnapshot(true, packages, null).Normalize();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or COMException
            or InvalidOperationException)
        {
            return new MicrosoftPackageCatalogSnapshot(
                false,
                Array.Empty<MicrosoftPackageRegistration>(),
                "MSFT_PACKAGE_REGISTRATION_READ_FAILED");
        }
    }
}

public sealed record MicrosoftGamingKnownTitle(
    string PackageFamilyName,
    string AppUserModelId,
    string? DisplayNameEvidence = null,
    string? StoreProductId = null)
{
    public MicrosoftGamingKnownTitle Normalize()
    {
        var family = MicrosoftGamingIdentity.NormalizePackageFamilyName(PackageFamilyName);
        var aumid = MicrosoftGamingIdentity.NormalizeAumid(AppUserModelId);
        MicrosoftGamingIdentity.RequireMatchingFamily(family, aumid);
        return this with
        {
            PackageFamilyName = family,
            AppUserModelId = aumid,
            DisplayNameEvidence = Optional(DisplayNameEvidence),
            StoreProductId = Optional(StoreProductId)
        };
    }

    private static string? Optional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public interface IMicrosoftGamingTitleCatalog
{
    IReadOnlyList<MicrosoftGamingKnownTitle> Titles { get; }
}

/// <summary>
/// Release-owned game bindings are intentionally empty until validated title data is provisioned.
/// Generic package enumeration must not be mislabeled as the user's complete Xbox/Game Pass library.
/// </summary>
public sealed class EmptyMicrosoftGamingTitleCatalog : IMicrosoftGamingTitleCatalog
{
    public IReadOnlyList<MicrosoftGamingKnownTitle> Titles { get; } = Array.Empty<MicrosoftGamingKnownTitle>();
}

public sealed record MicrosoftGamingIdentity(
    string PackageFamilyName,
    string AppUserModelId,
    string? PackageIdentityName = null,
    string? StoreProductId = null)
{
    public const string ExternalIdKind = "AUMID";
    public const string PackageFamilySecondaryPrefix = "PFN:";
    public const string PackageIdentitySecondaryPrefix = "PACKAGE_IDENTITY_NAME:";
    public const string StoreProductSecondaryPrefix = "STORE_PRODUCT_ID:";

    public MicrosoftGamingIdentity Normalize()
    {
        var family = NormalizePackageFamilyName(PackageFamilyName);
        var aumid = NormalizeAumid(AppUserModelId);
        RequireMatchingFamily(family, aumid);
        return this with
        {
            PackageFamilyName = family,
            AppUserModelId = aumid,
            PackageIdentityName = Optional(PackageIdentityName),
            StoreProductId = Optional(StoreProductId)
        };
    }

    public ExternalGameIdentity ToExternalIdentity()
    {
        var normalized = Normalize();
        var secondary = new List<string>
        {
            PackageFamilySecondaryPrefix + normalized.PackageFamilyName
        };
        if (normalized.PackageIdentityName is not null)
            secondary.Add(PackageIdentitySecondaryPrefix + normalized.PackageIdentityName);
        if (normalized.StoreProductId is not null)
            secondary.Add(StoreProductSecondaryPrefix + normalized.StoreProductId);
        return new ExternalGameIdentity(
            GameClientType.MicrosoftGaming,
            ExternalIdKind,
            normalized.AppUserModelId,
            secondary).Normalize();
    }

    public static MicrosoftGamingIdentity FromExternalIdentity(ExternalGameIdentity externalIdentity)
    {
        var identity = (externalIdentity ?? throw new ArgumentNullException(nameof(externalIdentity))).Normalize();
        if (identity.ClientType != GameClientType.MicrosoftGaming
            || !string.Equals(identity.ExternalIdKind, ExternalIdKind, StringComparison.Ordinal))
            throw new InvalidDataException("Microsoft Gaming identity requires an AUMID external identity.");

        var pfn = SingleSecondary(identity.SecondaryIds, PackageFamilySecondaryPrefix, required: true)
            ?? throw new InvalidDataException("Microsoft Gaming identity requires PFN evidence.");
        var packageIdentityName = SingleSecondary(identity.SecondaryIds, PackageIdentitySecondaryPrefix, required: false);
        var storeProductId = SingleSecondary(identity.SecondaryIds, StoreProductSecondaryPrefix, required: false);
        return new MicrosoftGamingIdentity(pfn, identity.ExternalId, packageIdentityName, storeProductId).Normalize();
    }

    public static string NormalizePackageFamilyName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException("Microsoft package family name is required.");
        var normalized = value.Trim();
        if (normalized.Length > 256
            || normalized.Any(char.IsControl)
            || normalized.Any(char.IsWhiteSpace)
            || normalized.IndexOfAny(['!', '|', '/', '\\', '?', '#', '%', '"', '\'']) >= 0)
            throw new InvalidDataException("Microsoft package family name contains unsupported characters.");
        return normalized;
    }

    public static string NormalizeAumid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException("Microsoft AUMID is required.");
        var normalized = value.Trim();
        var separator = normalized.IndexOf('!');
        if (separator <= 0 || separator == normalized.Length - 1
            || normalized.Length > 512
            || normalized.Any(char.IsControl)
            || normalized.Any(char.IsWhiteSpace)
            || normalized.IndexOfAny(['|', '/', '\\', '?', '#', '%', '"', '\'']) >= 0)
            throw new InvalidDataException("Microsoft AUMID is not a canonical package application identity.");
        return normalized;
    }

    public static void RequireMatchingFamily(string packageFamilyName, string appUserModelId)
    {
        var prefix = packageFamilyName + "!";
        if (!appUserModelId.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidDataException("Microsoft AUMID does not belong to the expected package family.");
    }

    private static string? SingleSecondary(
        IReadOnlyList<string>? values,
        string prefix,
        bool required)
    {
        var matches = (values ?? Array.Empty<string>())
            .Where(value => value.StartsWith(prefix, StringComparison.Ordinal))
            .Select(value => value[prefix.Length..])
            .ToArray();
        if (matches.Length > 1 || (required && matches.Length != 1))
            throw new InvalidDataException($"Microsoft identity contains invalid {prefix} evidence.");
        return matches.Length == 0 ? null : matches[0];
    }

    private static string? Optional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public enum MicrosoftApplicationActivationDisposition
{
    Accepted,
    MechanismUnavailable,
    Rejected,
    Failed
}

public sealed record MicrosoftApplicationActivationResult(
    MicrosoftApplicationActivationDisposition Disposition,
    uint ProcessId,
    string DiagnosticsCode);

public interface IMicrosoftApplicationActivationDispatcher
{
    MicrosoftApplicationActivationResult Activate(string canonicalAppUserModelId);
}

public sealed class WindowsMicrosoftApplicationActivationDispatcher : IMicrosoftApplicationActivationDispatcher
{
    private const int ClassNotRegistered = unchecked((int)0x80040154);
    private const int FileNotFound = unchecked((int)0x80070002);
    private const int NotFound = unchecked((int)0x80070490);

    public MicrosoftApplicationActivationResult Activate(string canonicalAppUserModelId)
    {
        string aumid;
        try
        {
            aumid = MicrosoftGamingIdentity.NormalizeAumid(canonicalAppUserModelId);
        }
        catch (InvalidDataException)
        {
            return new MicrosoftApplicationActivationResult(
                MicrosoftApplicationActivationDisposition.Rejected,
                0,
                "MSFT_AUMID_INVALID");
        }

        IApplicationActivationManager? manager = null;
        try
        {
            manager = (IApplicationActivationManager)(object)new ApplicationActivationManager();
            var hr = manager.ActivateApplication(aumid, null, ActivateOptions.None, out var processId);
            if (hr >= 0)
            {
                return new MicrosoftApplicationActivationResult(
                    MicrosoftApplicationActivationDisposition.Accepted,
                    processId,
                    "MSFT_AUMID_ACTIVATION_ACCEPTED");
            }

            if (hr is ClassNotRegistered or FileNotFound or NotFound)
            {
                return new MicrosoftApplicationActivationResult(
                    MicrosoftApplicationActivationDisposition.MechanismUnavailable,
                    0,
                    "MSFT_AUMID_REGISTRATION_UNAVAILABLE");
            }

            return new MicrosoftApplicationActivationResult(
                MicrosoftApplicationActivationDisposition.Rejected,
                0,
                "MSFT_AUMID_ACTIVATION_REJECTED");
        }
        catch (COMException exception) when (exception.HResult is ClassNotRegistered or FileNotFound or NotFound)
        {
            return new MicrosoftApplicationActivationResult(
                MicrosoftApplicationActivationDisposition.MechanismUnavailable,
                0,
                "MSFT_AUMID_REGISTRATION_UNAVAILABLE");
        }
        catch (COMException)
        {
            return new MicrosoftApplicationActivationResult(
                MicrosoftApplicationActivationDisposition.Rejected,
                0,
                "MSFT_AUMID_ACTIVATION_REJECTED");
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException)
        {
            return new MicrosoftApplicationActivationResult(
                MicrosoftApplicationActivationDisposition.Failed,
                0,
                "MSFT_AUMID_ACTIVATION_FAILED");
        }
        finally
        {
            if (manager is not null && Marshal.IsComObject(manager))
                Marshal.FinalReleaseComObject(manager);
        }
    }

    [Flags]
    private enum ActivateOptions
    {
        None = 0,
        DesignMode = 1,
        NoErrorUi = 2,
        NoSplashScreen = 4
    }

    [ComImport]
    [Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string? arguments,
            ActivateOptions options,
            out uint processId);

        [PreserveSig]
        int ActivateForFile(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            IntPtr itemArray,
            [MarshalAs(UnmanagedType.LPWStr)] string verb,
            out uint processId);

        [PreserveSig]
        int ActivateForProtocol(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            IntPtr itemArray,
            out uint processId);
    }

    [ComImport]
    [Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    private sealed class ApplicationActivationManager
    {
    }
}

/// <summary>
/// Partial v1 Microsoft Gaming adapter. Stable binding is AUMID + PFN evidence, local installation
/// truth comes from current-user Windows package registration, and launch handoff uses supported
/// Windows application activation. Full Xbox/Game Pass account-library and proactive license truth
/// are deliberately outside this adapter.
/// </summary>
public sealed class MicrosoftGamingPackageAdapter : IGameClientAdapter
{
    public const string AdapterVersion = "microsoft-gaming-adapter/1";
    public const string CompatibilityPolicyId = "microsoft-gaming/v1";
    public const string DiscoveryMechanismId = "MSFT_CURRENT_USER_PACKAGE_REGISTRATION_V1";
    public const string VersionMechanismId = "MSFT_PACKAGE_VERSION_EVIDENCE_V1";
    public const string LibraryMechanismId = "MSFT_RELEASE_KNOWN_PACKAGE_LIBRARY_V1";
    public const string InstallationMechanismId = "MSFT_PACKAGE_APP_REGISTRATION_V1";
    public const string LaunchIdentityMechanismId = "MICROSOFT_AUMID_V1";
    public const string LaunchMechanismId = "MSFT_APPLICATION_ACTIVATION_MANAGER_V1";
    public const string EvidenceSchemaVersion = "MSFT_PACKAGE_REGISTRATION_V1";
    public const int LaunchIdentitySchemaVersion = 1;
    public const int MinimumSupportedWindowsBuild = 19041;

    private readonly IMicrosoftPackageRegistrationReader _packageReader;
    private readonly IMicrosoftGamingTitleCatalog _titleCatalog;
    private readonly IMicrosoftApplicationActivationDispatcher _activationDispatcher;
    private readonly TimeProvider _timeProvider;
    private long _discoveryGeneration;
    private long _libraryGeneration;

    public MicrosoftGamingPackageAdapter(
        IMicrosoftPackageRegistrationReader packageReader,
        IMicrosoftGamingTitleCatalog titleCatalog,
        IMicrosoftApplicationActivationDispatcher activationDispatcher,
        TimeProvider? timeProvider = null)
    {
        _packageReader = packageReader ?? throw new ArgumentNullException(nameof(packageReader));
        _titleCatalog = titleCatalog ?? throw new ArgumentNullException(nameof(titleCatalog));
        _activationDispatcher = activationDispatcher ?? throw new ArgumentNullException(nameof(activationDispatcher));
        _timeProvider = timeProvider ?? TimeProvider.System;
        Descriptor = CreateDescriptor().Normalize();
    }

    public GameClientAdapterDescriptor Descriptor { get; }

    public GameClientCompatibilitySnapshot GetCompatibilityStatus(GameClientCompatibilityContext context)
    {
        var normalized = (context ?? throw new ArgumentNullException(nameof(context))).Normalize();
        var hasKnownTitles = NormalizeKnownTitles().Count > 0;
        var capabilities = Descriptor.Capabilities.Select(capability =>
        {
            var unsupportedWindows = capability.MinimumWindowsBuild is int minimum
                && normalized.WindowsBuild < minimum;
            var emptyKnownCatalog = capability.CapabilityId == GameClientCapabilityId.LibraryDiscovery
                && !hasKnownTitles;
            return new GameClientCapabilityStatus(
                capability.CapabilityId,
                unsupportedWindows
                    ? GameMechanismStatus.Unsupported
                    : emptyKnownCatalog
                        ? GameMechanismStatus.Open
                        : capability.MechanismStatus,
                capability.MechanismId,
                unsupportedWindows
                    ? "WINDOWS_BUILD_UNSUPPORTED"
                    : emptyKnownCatalog
                        ? "MSFT_KNOWN_TITLE_CATALOG_EMPTY"
                        : capability.NotesCode);
        }).ToArray();

        return new GameClientCompatibilitySnapshot(
            GameClientType.MicrosoftGaming,
            Descriptor.AdapterVersion,
            capabilities,
            _timeProvider.GetUtcNow(),
            Descriptor.CompatibilityPolicyId,
            normalized.ObservedClientVersion).Normalize(Descriptor);
    }

    public async Task<GameClientDiscoveryEvidence> DiscoverClientAsync(
        int sessionId,
        GameClientFreshnessRequirement freshnessRequirement,
        CancellationToken cancellationToken)
    {
        if (sessionId < 0)
            throw new ArgumentOutOfRangeException(nameof(sessionId));
        if (!Enum.IsDefined(freshnessRequirement))
            throw new ArgumentOutOfRangeException(nameof(freshnessRequirement));
        cancellationToken.ThrowIfCancellationRequested();

        var observedAt = _timeProvider.GetUtcNow();
        var generation = Interlocked.Increment(ref _discoveryGeneration);
        var snapshot = await ReadPackagesAsync(cancellationToken);
        var available = snapshot.ReadSucceeded;
        return new GameClientDiscoveryEvidence(
            GameClientType.MicrosoftGaming,
            available ? GameClientDiscoveryAvailability.AvailableUnverifiedVersion : GameClientDiscoveryAvailability.Unknown,
            DiscoveryMechanismId,
            GameMechanismStatus.SupportedOsMechanism,
            observedAt,
            generation,
            PackageIdentity: available ? "CURRENT_USER_PACKAGE_REGISTRATION" : null,
            DiagnosticsCode: available
                ? "MSFT_PACKAGE_REGISTRATION_AVAILABLE"
                : snapshot.DiagnosticsCode ?? "MSFT_PACKAGE_REGISTRATION_READ_FAILED")
            .Normalize(GameClientType.MicrosoftGaming);
    }

    public async Task<GameClientLibraryRefreshResult> RefreshLibraryAsync(
        GameClientLibraryRefreshRequest request,
        CancellationToken cancellationToken)
    {
        var normalized = (request ?? throw new ArgumentNullException(nameof(request))).Normalize(GameClientType.MicrosoftGaming);
        cancellationToken.ThrowIfCancellationRequested();
        var observedAt = _timeProvider.GetUtcNow();
        var generation = Interlocked.Increment(ref _libraryGeneration);
        if (normalized.DeadlineUtc <= observedAt)
            return Result(normalized, GameClientLibraryRefreshResultCode.Timeout, generation, observedAt, [], "MSFT_LIBRARY_REFRESH_DEADLINE_EXPIRED");

        var titles = NormalizeKnownTitles();
        if (titles.Count == 0)
            return Result(normalized, GameClientLibraryRefreshResultCode.SourceUnavailable, generation, observedAt, [], "MSFT_KNOWN_TITLE_CATALOG_EMPTY");

        var snapshot = await ReadPackagesAsync(cancellationToken);
        if (!snapshot.ReadSucceeded)
            return Result(normalized, GameClientLibraryRefreshResultCode.SourceUnavailable, generation, observedAt, [], snapshot.DiagnosticsCode ?? "MSFT_PACKAGE_REGISTRATION_READ_FAILED");

        var missingEvidence = new HashSet<string>(StringComparer.Ordinal);
        var records = new List<AdapterGameProjection>();
        foreach (var title in titles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (normalized.DeadlineUtc <= _timeProvider.GetUtcNow())
            {
                missingEvidence.Add("MSFT_LIBRARY_REFRESH_DEADLINE_EXPIRED");
                break;
            }
            records.Add(BuildProjection(title, snapshot.Packages, observedAt, missingEvidence));
        }
        var partial = missingEvidence.Count > 0;
        return Result(
            normalized,
            partial ? GameClientLibraryRefreshResultCode.Partial : GameClientLibraryRefreshResultCode.Refreshed,
            generation,
            observedAt,
            records.OrderBy(record => record.ExternalGameIdentity.ExternalId, StringComparer.Ordinal).ToArray(),
            partial ? "MSFT_LIBRARY_PARTIAL" : "MSFT_LIBRARY_REFRESHED",
            missingEvidence: partial ? missingEvidence : null);
    }

    public async Task<AdapterGameProjection?> ResolveInstallationAsync(
        ExternalGameIdentity externalGameIdentity,
        GameClientFreshnessRequirement freshnessRequirement,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(freshnessRequirement))
            throw new ArgumentOutOfRangeException(nameof(freshnessRequirement));
        var identity = MicrosoftGamingIdentity.FromExternalIdentity(externalGameIdentity);
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = await ReadPackagesAsync(cancellationToken);
        if (!snapshot.ReadSucceeded)
            return null;

        var missing = new HashSet<string>(StringComparer.Ordinal);
        return BuildProjection(
            new MicrosoftGamingKnownTitle(
                identity.PackageFamilyName,
                identity.AppUserModelId,
                null,
                identity.StoreProductId),
            snapshot.Packages,
            _timeProvider.GetUtcNow(),
            missing);
    }

    public Task<PreparedClientLaunch> PrepareLaunchAsync(
        GameClientLaunchRequest request,
        AdapterGameProjection currentProjection,
        GameClientDiscoveryEvidence currentClientEvidence,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedRequest = (request ?? throw new ArgumentNullException(nameof(request))).Normalize(GameClientType.MicrosoftGaming);
        var projection = (currentProjection ?? throw new ArgumentNullException(nameof(currentProjection))).Normalize(GameClientType.MicrosoftGaming);
        var clientEvidence = (currentClientEvidence ?? throw new ArgumentNullException(nameof(currentClientEvidence))).Normalize(GameClientType.MicrosoftGaming);
        var now = _timeProvider.GetUtcNow();
        if (normalizedRequest.DeadlineUtc <= now)
            throw new InvalidDataException("Microsoft Gaming launch request has expired.");
        if (!AdapterContractNormalization.ExternalIdentityEquals(normalizedRequest.ExternalGameIdentity, projection.ExternalGameIdentity))
            throw new InvalidDataException("Microsoft Gaming launch projection belongs to a different external identity.");

        var identity = MicrosoftGamingIdentity.FromExternalIdentity(normalizedRequest.ExternalGameIdentity);
        ValidateClientEvidence(clientEvidence);
        ValidateInstallationEvidence(projection.Installation, now);
        var projectedLaunchIdentity = projection.LaunchIdentity?.Normalize()
            ?? throw new InvalidDataException("Microsoft Gaming launch requires current AUMID registration evidence.");
        var requestedLaunchIdentity = normalizedRequest.ResolvedLaunchIdentity.Normalize();
        ValidateLaunchIdentity(projectedLaunchIdentity, identity, now);
        ValidateLaunchIdentity(requestedLaunchIdentity, identity, now);
        if (!LaunchIdentityEquals(projectedLaunchIdentity, requestedLaunchIdentity))
            throw new InvalidDataException("Microsoft Gaming launch identity differs from current projection evidence.");

        return Task.FromResult(new PreparedClientLaunch(
            normalizedRequest.LaunchOperationId,
            GameClientType.MicrosoftGaming,
            projection.ExternalGameIdentity,
            projectedLaunchIdentity,
            new GameClientObservationRules(
                Array.Empty<string>(),
                null,
                identity.AppUserModelId,
                identity.PackageFamilyName,
                Array.Empty<string>(),
                Array.Empty<string>(),
                GameClientEvidenceLevel.Strong,
                2_000,
                3_000).Normalize(),
            LaunchMechanismId,
            GameMechanismStatus.SupportedOsMechanism,
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
        if (preparedLaunch.ClientType != GameClientType.MicrosoftGaming || preparedLaunch.LaunchOperationId == Guid.Empty)
            throw new InvalidDataException("Prepared Microsoft Gaming launch identity is invalid.");
        if (preparedLaunch.ExpiresAtUtc <= submittedAt)
            return Task.FromResult(Handoff(preparedLaunch, GameClientLaunchHandoffResultCode.StaleEvidence, submittedAt, "MSFT_PREPARED_LAUNCH_EXPIRED"));
        if (!string.Equals(preparedLaunch.HandoffMechanism, LaunchMechanismId, StringComparison.Ordinal)
            || preparedLaunch.HandoffMechanismStatus != GameMechanismStatus.SupportedOsMechanism)
            return Task.FromResult(Handoff(preparedLaunch, GameClientLaunchHandoffResultCode.MechanismUnavailable, submittedAt, "MSFT_HANDOFF_MECHANISM_UNAVAILABLE"));

        MicrosoftGamingIdentity identity;
        try
        {
            identity = MicrosoftGamingIdentity.FromExternalIdentity(preparedLaunch.NormalizedExternalGameIdentity);
            ValidateLaunchIdentity(preparedLaunch.ResolvedLaunchIdentity.Normalize(), identity, submittedAt);
        }
        catch (InvalidDataException)
        {
            return Task.FromResult(Handoff(preparedLaunch, GameClientLaunchHandoffResultCode.LaunchIdentityInvalid, submittedAt, "MSFT_LAUNCH_IDENTITY_INVALID"));
        }

        var activation = _activationDispatcher.Activate(identity.AppUserModelId);
        return Task.FromResult(activation.Disposition switch
        {
            MicrosoftApplicationActivationDisposition.Accepted => Handoff(
                preparedLaunch,
                GameClientLaunchHandoffResultCode.HandoffAccepted,
                submittedAt,
                activation.DiagnosticsCode,
                activation.ProcessId),
            MicrosoftApplicationActivationDisposition.MechanismUnavailable => Handoff(
                preparedLaunch,
                GameClientLaunchHandoffResultCode.MechanismUnavailable,
                submittedAt,
                activation.DiagnosticsCode),
            MicrosoftApplicationActivationDisposition.Rejected => Handoff(
                preparedLaunch,
                GameClientLaunchHandoffResultCode.HandoffRejected,
                submittedAt,
                activation.DiagnosticsCode),
            _ => Handoff(
                preparedLaunch,
                GameClientLaunchHandoffResultCode.UnknownFailure,
                submittedAt,
                activation.DiagnosticsCode)
        });
    }

    public Task<GameClientObservationResult> ObserveLaunchAsync(
        PreparedClientLaunch preparedLaunch,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(preparedLaunch);
        return Task.FromResult(new GameClientObservationResult(
            GameClientObservationClassification.CorrelationLost,
            GameClientEvidenceLevel.Weak,
            Array.Empty<GameClientCorrelatedProcessEvidence>(),
            false,
            _timeProvider.GetUtcNow(),
            "MSFT_PROCESS_CORRELATION_OPEN").Normalize());
    }

    public Task<GameClientExitObservationResult> ObserveExitAsync(
        PreparedClientLaunch preparedLaunch,
        IReadOnlyList<GameClientCorrelatedProcessEvidence> currentCorrelation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(preparedLaunch);
        return Task.FromResult(new GameClientExitObservationResult(
            GameClientExitObservationClassification.CorrelationLost,
            Array.Empty<GameClientCorrelatedProcessEvidence>(),
            _timeProvider.GetUtcNow(),
            "MSFT_EXIT_CORRELATION_OPEN").Normalize());
    }

    public void Invalidate(GameClientLibraryRefreshReason reason)
    {
        // No authoritative cache is kept. Every discovery/refresh re-reads current-user package state.
    }

    private async Task<MicrosoftPackageCatalogSnapshot> ReadPackagesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return (await _packageReader.ReadCurrentUserPackagesAsync(cancellationToken)
                ?? new MicrosoftPackageCatalogSnapshot(false, Array.Empty<MicrosoftPackageRegistration>(), "MSFT_PACKAGE_REGISTRATION_NO_RESULT"))
                .Normalize();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new MicrosoftPackageCatalogSnapshot(
                false,
                Array.Empty<MicrosoftPackageRegistration>(),
                "MSFT_PACKAGE_REGISTRATION_READ_FAILED").Normalize();
        }
    }

    private IReadOnlyList<MicrosoftGamingKnownTitle> NormalizeKnownTitles()
    {
        var titles = (_titleCatalog.Titles ?? Array.Empty<MicrosoftGamingKnownTitle>())
            .Select(title => (title ?? throw new InvalidDataException("Microsoft Gaming title catalog entry cannot be null.")).Normalize())
            .ToArray();
        var duplicate = titles.GroupBy(title => title.AppUserModelId, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidDataException($"Microsoft Gaming title catalog contains duplicate AUMID {duplicate.Key}.");
        return titles.OrderBy(title => title.AppUserModelId, StringComparer.Ordinal).ToArray();
    }

    private AdapterGameProjection BuildProjection(
        MicrosoftGamingKnownTitle title,
        IReadOnlyList<MicrosoftPackageRegistration> packages,
        DateTimeOffset observedAt,
        ISet<string> missingEvidence)
    {
        var normalizedTitle = title.Normalize();
        var familyMatches = packages
            .Where(package => !package.IsFramework
                && !package.IsResourcePackage
                && string.Equals(package.PackageFamilyName, normalizedTitle.PackageFamilyName, StringComparison.Ordinal))
            .ToArray();

        if (familyMatches.Length == 0)
            return BuildNotInstalledProjection(normalizedTitle, observedAt);
        if (familyMatches.Length > 1)
        {
            missingEvidence.Add("MSFT_PACKAGE_FAMILY_AMBIGUOUS");
            return BuildUnknownProjection(normalizedTitle, null, observedAt, "MSFT_PACKAGE_FAMILY_AMBIGUOUS");
        }

        var package = familyMatches[0];
        if (!package.IsUsable)
        {
            missingEvidence.Add(package.DiagnosticsCode ?? "MSFT_PACKAGE_STATUS_UNUSABLE");
            return BuildUnknownProjection(normalizedTitle, package, observedAt, package.DiagnosticsCode ?? "MSFT_PACKAGE_STATUS_UNUSABLE");
        }

        var app = package.Applications.SingleOrDefault(application =>
            string.Equals(application.AppUserModelId, normalizedTitle.AppUserModelId, StringComparison.Ordinal));
        if (app is null)
        {
            missingEvidence.Add("MSFT_AUMID_REGISTRATION_NOT_FOUND");
            return BuildUnknownProjection(normalizedTitle, package, observedAt, "MSFT_AUMID_REGISTRATION_NOT_FOUND");
        }

        var identity = new MicrosoftGamingIdentity(
            normalizedTitle.PackageFamilyName,
            normalizedTitle.AppUserModelId,
            package.PackageIdentityName,
            normalizedTitle.StoreProductId);
        var installation = new GameInstallationEvidence(
            GameInstallState.InstalledVerifiedEvidence,
            null,
            observedAt,
            observedAt.AddMinutes(5),
            GameEvidenceFreshness.Fresh,
            GameEvidenceConfidence.High,
            GameMechanismStatus.SupportedOsMechanism,
            package.PackageFullName + "|" + app.AppUserModelId).Normalize();
        var launchIdentity = new AdapterLaunchIdentity(
            LaunchIdentityMechanismId,
            LaunchIdentitySchemaVersion,
            identity.Normalize().AppUserModelId,
            observedAt,
            GameMechanismStatus.SupportedOsMechanism).Normalize();

        return new AdapterGameProjection(
            identity.ToExternalIdentity(),
            installation,
            launchIdentity,
            Array.Empty<string>(),
            new GameLibrarySourceProvenance(
                InstallationMechanismId,
                AdapterVersion,
                EvidenceSchemaVersion,
                package.PackageVersion),
            normalizedTitle.DisplayNameEvidence ?? app.DisplayName).Normalize(GameClientType.MicrosoftGaming);
    }

    private static AdapterGameProjection BuildNotInstalledProjection(
        MicrosoftGamingKnownTitle title,
        DateTimeOffset observedAt)
    {
        var identity = new MicrosoftGamingIdentity(
            title.PackageFamilyName,
            title.AppUserModelId,
            null,
            title.StoreProductId);
        return new AdapterGameProjection(
            identity.ToExternalIdentity(),
            new GameInstallationEvidence(
                GameInstallState.NotInstalledVerifiedEvidence,
                null,
                observedAt,
                observedAt.AddMinutes(5),
                GameEvidenceFreshness.Fresh,
                GameEvidenceConfidence.High,
                GameMechanismStatus.SupportedOsMechanism,
                "PFN:" + title.PackageFamilyName).Normalize(),
            null,
            Array.Empty<string>(),
            new GameLibrarySourceProvenance(
                InstallationMechanismId,
                AdapterVersion,
                EvidenceSchemaVersion),
            title.DisplayNameEvidence).Normalize(GameClientType.MicrosoftGaming);
    }

    private static AdapterGameProjection BuildUnknownProjection(
        MicrosoftGamingKnownTitle title,
        MicrosoftPackageRegistration? package,
        DateTimeOffset observedAt,
        string diagnosticsCode)
    {
        var identity = new MicrosoftGamingIdentity(
            title.PackageFamilyName,
            title.AppUserModelId,
            package?.PackageIdentityName,
            title.StoreProductId);
        return new AdapterGameProjection(
            identity.ToExternalIdentity(),
            new GameInstallationEvidence(
                GameInstallState.Unknown,
                null,
                observedAt,
                observedAt.AddMinutes(5),
                GameEvidenceFreshness.Fresh,
                GameEvidenceConfidence.Low,
                GameMechanismStatus.SupportedOsMechanism,
                package?.PackageFullName ?? diagnosticsCode).Normalize(),
            null,
            Array.Empty<string>(),
            new GameLibrarySourceProvenance(
                InstallationMechanismId,
                AdapterVersion,
                EvidenceSchemaVersion,
                package?.PackageVersion),
            title.DisplayNameEvidence).Normalize(GameClientType.MicrosoftGaming);
    }

    private static void ValidateClientEvidence(GameClientDiscoveryEvidence evidence)
    {
        if (evidence.ClientType != GameClientType.MicrosoftGaming
            || evidence.AvailabilityState is not (GameClientDiscoveryAvailability.AvailableVerified
                or GameClientDiscoveryAvailability.AvailableUnverifiedVersion)
            || evidence.MechanismStatus != GameMechanismStatus.SupportedOsMechanism)
            throw new InvalidDataException("Microsoft Gaming package registration evidence is unavailable.");
    }

    private static void ValidateInstallationEvidence(GameInstallationEvidence installation, DateTimeOffset now)
    {
        var normalized = (installation ?? throw new InvalidDataException("Microsoft Gaming installation evidence is required.")).Normalize();
        if (normalized.State != GameInstallState.InstalledVerifiedEvidence
            || normalized.Freshness != GameEvidenceFreshness.Fresh
            || normalized.Confidence != GameEvidenceConfidence.High
            || normalized.MechanismStatus != GameMechanismStatus.SupportedOsMechanism
            || normalized.ExpiresAtUtc is null
            || normalized.ExpiresAtUtc <= now)
            throw new InvalidDataException("Microsoft Gaming launch requires fresh verified current-user package registration evidence.");
    }

    private static void ValidateLaunchIdentity(
        AdapterLaunchIdentity launchIdentity,
        MicrosoftGamingIdentity identity,
        DateTimeOffset now)
    {
        var normalized = launchIdentity.Normalize();
        if (!string.Equals(normalized.Kind, LaunchIdentityMechanismId, StringComparison.Ordinal)
            || normalized.SchemaVersion != LaunchIdentitySchemaVersion
            || normalized.MechanismStatus != GameMechanismStatus.SupportedOsMechanism
            || normalized.ObservedAtUtc > now
            || !string.Equals(normalized.Payload, identity.AppUserModelId, StringComparison.Ordinal))
            throw new InvalidDataException("Microsoft Gaming launch identity is invalid or stale.");
    }

    private static bool LaunchIdentityEquals(AdapterLaunchIdentity left, AdapterLaunchIdentity right)
        => left.SchemaVersion == right.SchemaVersion
            && left.MechanismStatus == right.MechanismStatus
            && string.Equals(left.Kind, right.Kind, StringComparison.Ordinal)
            && string.Equals(left.Payload, right.Payload, StringComparison.Ordinal)
            && left.ObservedAtUtc == right.ObservedAtUtc;

    private static GameClientLaunchHandoffResult Handoff(
        PreparedClientLaunch prepared,
        GameClientLaunchHandoffResultCode resultCode,
        DateTimeOffset submittedAt,
        string diagnosticsCode,
        uint processId = 0)
        => new GameClientLaunchHandoffResult(
            prepared.LaunchOperationId,
            resultCode,
            submittedAt,
            processId == 0 ? null : "MSFT_ACTIVATION_PID",
            processId == 0 ? null : processId.ToString(CultureInfo.InvariantCulture),
            diagnosticsCode).Normalize(prepared);

    private static GameClientLibraryRefreshResult Result(
        GameClientLibraryRefreshRequest request,
        GameClientLibraryRefreshResultCode resultCode,
        long generation,
        DateTimeOffset observedAt,
        IReadOnlyList<AdapterGameProjection> records,
        string parserStatus,
        IEnumerable<string>? missingEvidence = null)
    {
        var missing = missingEvidence?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return new GameClientLibraryRefreshResult(
            request.RefreshId,
            GameClientType.MicrosoftGaming,
            resultCode,
            generation,
            observedAt,
            records,
            null,
            parserStatus,
            resultCode == GameClientLibraryRefreshResultCode.Partial ? missing : null).Normalize(request);
    }

    private static GameClientAdapterDescriptor CreateDescriptor()
        => new(
            GameClientType.MicrosoftGaming,
            AdapterVersion,
            [MicrosoftGamingIdentity.ExternalIdKind],
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
                    GameMechanismStatus.SupportedVersionGated,
                    LibraryMechanismId,
                    MinimumWindowsBuild: MinimumSupportedWindowsBuild,
                    NotesCode: "KNOWN_REGISTERED_TITLES_ONLY"),
                new GameClientAdapterCapability(
                    GameClientCapabilityId.InstallationEvidence,
                    GameMechanismStatus.SupportedOsMechanism,
                    InstallationMechanismId,
                    MinimumWindowsBuild: MinimumSupportedWindowsBuild),
                new GameClientAdapterCapability(
                    GameClientCapabilityId.LaunchIdentityResolution,
                    GameMechanismStatus.SupportedOsMechanism,
                    LaunchIdentityMechanismId,
                    MinimumWindowsBuild: MinimumSupportedWindowsBuild),
                new GameClientAdapterCapability(
                    GameClientCapabilityId.LaunchEligibilityEvidence,
                    GameMechanismStatus.Open,
                    "MSFT_LAUNCH_ELIGIBILITY_V1"),
                new GameClientAdapterCapability(
                    GameClientCapabilityId.GameLaunch,
                    GameMechanismStatus.SupportedOsMechanism,
                    LaunchMechanismId,
                    MinimumWindowsBuild: MinimumSupportedWindowsBuild),
                new GameClientAdapterCapability(
                    GameClientCapabilityId.ClientInteractionEvidence,
                    GameMechanismStatus.UserMediated,
                    "MSFT_EXTERNAL_PLATFORM_UI_V1"),
                new GameClientAdapterCapability(
                    GameClientCapabilityId.GameProcessCorrelation,
                    GameMechanismStatus.Open,
                    "MSFT_PROCESS_CORRELATION_V1"),
                new GameClientAdapterCapability(
                    GameClientCapabilityId.GameExitCorrelation,
                    GameMechanismStatus.Open,
                    "MSFT_EXIT_CORRELATION_V1"),
                new GameClientAdapterCapability(
                    GameClientCapabilityId.AccountContextEvidence,
                    GameMechanismStatus.Unsupported,
                    "MSFT_ACCOUNT_CONTEXT_V1"),
                new GameClientAdapterCapability(
                    GameClientCapabilityId.UpdateStateEvidence,
                    GameMechanismStatus.Open,
                    "MSFT_UPDATE_STATE_V1")
            ],
            CompatibilityPolicyId,
            GameClientSupportStatus.PartialSupportedV1);
}
