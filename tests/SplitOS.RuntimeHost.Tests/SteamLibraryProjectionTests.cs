using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitOS.RuntimeHost.GameRuntime;

namespace SplitOS.RuntimeHost.Tests;

[TestClass]
public sealed class SteamLibraryProjectionTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 9, 15, 1, 0, 0, TimeSpan.Zero);
    private const string SteamRoot = @"C:\Steam";
    private const string SecondaryRoot = @"D:\SteamLibrary";

    [TestMethod]
    public void LibraryFoldersParserSupportsModernAndLegacyEntries()
    {
        var result = SteamVdfMetadataParser.ParseLibraryFolders(
            "\"libraryfolders\"\n{\n\"0\" { \"path\" \"C:\\\\Steam\" \"label\" \"\" }\n\"1\" \"D:\\\\SteamLibrary\"\n\"TimeNextStatsReport\" \"123\"\n}");

        Assert.IsTrue(result.Success);
        Assert.AreEqual("STEAM_LIBRARY_SCHEMA_V1", result.DiagnosticsCode);
        CollectionAssert.AreEqual(
            new[] { SteamRoot, SecondaryRoot },
            result.LibraryRoots.ToArray());
    }

    [TestMethod]
    public void LibraryFoldersParserFailsClosedOnMalformedOrUnknownSchema()
    {
        var malformed = SteamVdfMetadataParser.ParseLibraryFolders(
            "\"libraryfolders\" { \"0\" { \"path\" \"C:\\\\Steam\" }");
        var unknown = SteamVdfMetadataParser.ParseLibraryFolders(
            "\"different-root\" { \"0\" { \"path\" \"C:\\\\Steam\" } }");

        Assert.IsFalse(malformed.Success);
        Assert.AreEqual("STEAM_VDF_PARSE_FAILED", malformed.DiagnosticsCode);
        Assert.IsFalse(unknown.Success);
        Assert.AreEqual("STEAM_LIBRARY_SCHEMA_UNKNOWN", unknown.DiagnosticsCode);
    }

    [TestMethod]
    public void AppManifestParserTreatsOnlyAppIdAsRequiredStableIdentity()
    {
        var parsed = SteamVdfMetadataParser.ParseAppManifest(
            "\"AppState\" { \"appid\" \"001091500\" \"name\" \"Cyberpunk 2077\" \"installdir\" \"Cyberpunk 2077\" \"StateFlags\" \"4\" }");

        Assert.IsTrue(parsed.Success);
        Assert.AreEqual("1091500", parsed.AppId);
        Assert.AreEqual("Cyberpunk 2077", parsed.DisplayName);
        Assert.AreEqual("Cyberpunk 2077", parsed.InstallDirectoryName);
    }

    [TestMethod]
    public async Task RefreshBuildsVerifiedProjectionOnlyWhenManifestAndFilesystemAgree()
    {
        var fs = BaseFileSystem();
        fs.AddDirectory(Path.Combine(SteamRoot, "steamapps", "common", "Cyberpunk 2077"));
        fs.AddFile(
            Path.Combine(SteamRoot, "steamapps", "appmanifest_1091500.acf"),
            AppManifest("1091500", "Cyberpunk 2077", "Cyberpunk 2077"));
        var adapter = Adapter(fs);

        var result = await adapter.RefreshLibraryAsync(Request(), CancellationToken.None);

        Assert.AreEqual(GameClientLibraryRefreshResultCode.Refreshed, result.ResultCode);
        Assert.AreEqual(1, result.Records.Count);
        var projection = result.Records.Single();
        Assert.AreEqual(SteamClientAdapter.ExternalIdKind, projection.ExternalGameIdentity.ExternalIdKind);
        Assert.AreEqual("1091500", projection.ExternalGameIdentity.ExternalId);
        Assert.AreEqual(GameInstallState.InstalledVerifiedEvidence, projection.Installation.State);
        Assert.AreEqual(GameEvidenceFreshness.Fresh, projection.Installation.Freshness);
        Assert.AreEqual(GameEvidenceConfidence.High, projection.Installation.Confidence);
        Assert.AreEqual(GameMechanismStatus.BestEffortLocalEvidence, projection.Installation.MechanismStatus);
        Assert.AreEqual(
            Path.GetFullPath(Path.Combine(SteamRoot, "steamapps", "common", "Cyberpunk 2077")),
            projection.Installation.ValidatedInstallRoot);
        Assert.IsNotNull(projection.LaunchIdentity);
        Assert.AreEqual("1091500", projection.LaunchIdentity!.Payload);
        Assert.AreEqual(GameMechanismStatus.SupportedPublic, projection.LaunchIdentity.MechanismStatus);
        Assert.AreEqual("Cyberpunk 2077", projection.DisplayNameEvidence);
        Assert.AreEqual(SteamClientAdapter.InstallationMechanismId, projection.SourceProvenance.SourceMechanism);
        Assert.AreEqual(SteamClientAdapter.AdapterVersion, projection.SourceProvenance.AdapterVersion);
        Assert.AreEqual(SteamClientAdapter.EvidenceSchemaVersion, projection.SourceProvenance.EvidenceSchemaVersion);
        Assert.AreEqual("10.20.30.40", projection.SourceProvenance.ClientVersionObserved);
        Assert.AreEqual(0, projection.ExecutableCandidates.Count);
    }

    [TestMethod]
    public async Task ManifestPresenceWithoutVerifiedInstallDirectoryNeverBecomesInstalled()
    {
        var fs = BaseFileSystem();
        fs.AddFile(
            Path.Combine(SteamRoot, "steamapps", "appmanifest_1091500.acf"),
            AppManifest("1091500", "Cyberpunk 2077", "Cyberpunk 2077"));
        var adapter = Adapter(fs);

        var result = await adapter.RefreshLibraryAsync(Request(), CancellationToken.None);

        Assert.AreEqual(GameClientLibraryRefreshResultCode.Partial, result.ResultCode);
        var projection = result.Records.Single();
        Assert.AreEqual(GameInstallState.Unknown, projection.Installation.State);
        Assert.AreEqual(GameEvidenceConfidence.Low, projection.Installation.Confidence);
        Assert.IsNull(projection.Installation.ValidatedInstallRoot);
        CollectionAssert.Contains(
            result.MissingEvidenceClasses!.ToArray(),
            "STEAM_INSTALL_ROOT_NOT_VERIFIED");
    }

    [TestMethod]
    public async Task MissingInstallDirProducesUnknownProjectionInsteadOfInventedInstallTruth()
    {
        var fs = BaseFileSystem();
        fs.AddFile(
            Path.Combine(SteamRoot, "steamapps", "appmanifest_1091500.acf"),
            "\"AppState\" { \"appid\" \"1091500\" \"name\" \"Cyberpunk 2077\" }");
        var adapter = Adapter(fs);

        var result = await adapter.RefreshLibraryAsync(Request(), CancellationToken.None);

        Assert.AreEqual(GameClientLibraryRefreshResultCode.Partial, result.ResultCode);
        Assert.AreEqual(GameInstallState.Unknown, result.Records.Single().Installation.State);
        CollectionAssert.Contains(
            result.MissingEvidenceClasses!.ToArray(),
            "STEAM_APPMANIFEST_INSTALLDIR_MISSING");
    }

    [TestMethod]
    public async Task AppIdMismatchIsPartialEvidenceFailureAndDoesNotPublishWrongIdentity()
    {
        var fs = BaseFileSystem();
        fs.AddFile(
            Path.Combine(SteamRoot, "steamapps", "appmanifest_1091500.acf"),
            AppManifest("570", "Dota 2", "dota 2 beta"));
        var adapter = Adapter(fs);

        var result = await adapter.RefreshLibraryAsync(Request(), CancellationToken.None);

        Assert.AreEqual(GameClientLibraryRefreshResultCode.Partial, result.ResultCode);
        Assert.AreEqual(0, result.Records.Count);
        CollectionAssert.Contains(
            result.MissingEvidenceClasses!.ToArray(),
            "STEAM_APPMANIFEST_APPID_MISMATCH");
    }

    [TestMethod]
    public async Task TraversalInstallDirIsRejectedAndCannotEscapeSteamCommonRoot()
    {
        var fs = BaseFileSystem();
        fs.AddDirectory(@"C:\Outside");
        fs.AddFile(
            Path.Combine(SteamRoot, "steamapps", "appmanifest_1091500.acf"),
            AppManifest("1091500", "Bad", @"..\..\..\Outside"));
        var adapter = Adapter(fs);

        var result = await adapter.RefreshLibraryAsync(Request(), CancellationToken.None);

        Assert.AreEqual(GameClientLibraryRefreshResultCode.Partial, result.ResultCode);
        Assert.IsNull(result.Records.Single().Installation.ValidatedInstallRoot);
        Assert.AreEqual(GameInstallState.Unknown, result.Records.Single().Installation.State);
        CollectionAssert.Contains(
            result.MissingEvidenceClasses!.ToArray(),
            "STEAM_INSTALL_ROOT_NOT_VERIFIED");
    }

    [TestMethod]
    public async Task MalformedLibrarySourceNeverMasqueradesAsEmptySuccessfulLibrary()
    {
        var fs = new FakeSteamMetadataFileSystem();
        fs.AddDirectory(Path.Combine(SteamRoot, "steamapps"));
        fs.AddFile(
            Path.Combine(SteamRoot, "steamapps", "libraryfolders.vdf"),
            "\"libraryfolders\" { \"0\" { \"path\" \"C:\\\\Steam\" }");
        var adapter = Adapter(fs);

        var result = await adapter.RefreshLibraryAsync(Request(), CancellationToken.None);

        Assert.AreEqual(GameClientLibraryRefreshResultCode.ParseFailed, result.ResultCode);
        Assert.AreEqual(0, result.Records.Count);
        Assert.AreEqual("STEAM_VDF_PARSE_FAILED", result.ParserStatus);
    }

    [TestMethod]
    public async Task UnknownLibrarySchemaIsTypedSeparatelyFromSyntaxFailure()
    {
        var fs = new FakeSteamMetadataFileSystem();
        fs.AddDirectory(Path.Combine(SteamRoot, "steamapps"));
        fs.AddFile(
            Path.Combine(SteamRoot, "steamapps", "libraryfolders.vdf"),
            "\"new-layout\" { \"0\" { \"path\" \"C:\\\\Steam\" } }");
        var adapter = Adapter(fs);

        var result = await adapter.RefreshLibraryAsync(Request(), CancellationToken.None);

        Assert.AreEqual(GameClientLibraryRefreshResultCode.SourceSchemaUnknown, result.ResultCode);
        Assert.AreEqual(0, result.Records.Count);
        Assert.AreEqual("STEAM_LIBRARY_SCHEMA_UNKNOWN", result.ParserStatus);
    }

    [TestMethod]
    public async Task MultipleValidatedLibrariesProduceIndependentCanonicalAppIds()
    {
        var fs = new FakeSteamMetadataFileSystem();
        fs.AddDirectory(Path.Combine(SteamRoot, "steamapps"));
        fs.AddDirectory(Path.Combine(SecondaryRoot, "steamapps"));
        fs.AddDirectory(Path.Combine(SteamRoot, "steamapps", "common", "Game A"));
        fs.AddDirectory(Path.Combine(SecondaryRoot, "steamapps", "common", "Game B"));
        fs.AddFile(
            Path.Combine(SteamRoot, "steamapps", "libraryfolders.vdf"),
            LibraryFolders(SteamRoot, SecondaryRoot));
        fs.AddFile(
            Path.Combine(SteamRoot, "steamapps", "appmanifest_100.acf"),
            AppManifest("100", "Game A", "Game A"));
        fs.AddFile(
            Path.Combine(SecondaryRoot, "steamapps", "appmanifest_200.acf"),
            AppManifest("200", "Game B", "Game B"));
        var adapter = Adapter(fs);

        var result = await adapter.RefreshLibraryAsync(Request(), CancellationToken.None);

        Assert.AreEqual(GameClientLibraryRefreshResultCode.Refreshed, result.ResultCode);
        CollectionAssert.AreEqual(
            new[] { "100", "200" },
            result.Records.Select(record => record.ExternalGameIdentity.ExternalId).ToArray());
        Assert.IsTrue(result.Records.All(record => record.Installation.State == GameInstallState.InstalledVerifiedEvidence));
    }

    [TestMethod]
    public async Task BrokenManifestMakesRefreshPartialButPreservesOtherVerifiedRecords()
    {
        var fs = BaseFileSystem();
        fs.AddDirectory(Path.Combine(SteamRoot, "steamapps", "common", "Good"));
        fs.AddFile(
            Path.Combine(SteamRoot, "steamapps", "appmanifest_100.acf"),
            AppManifest("100", "Good", "Good"));
        fs.AddFile(
            Path.Combine(SteamRoot, "steamapps", "appmanifest_200.acf"),
            "\"AppState\" { \"appid\"");
        var adapter = Adapter(fs);

        var result = await adapter.RefreshLibraryAsync(Request(), CancellationToken.None);

        Assert.AreEqual(GameClientLibraryRefreshResultCode.Partial, result.ResultCode);
        Assert.AreEqual(1, result.Records.Count);
        Assert.AreEqual("100", result.Records.Single().ExternalGameIdentity.ExternalId);
        CollectionAssert.Contains(
            result.MissingEvidenceClasses!.ToArray(),
            "STEAM_VDF_PARSE_FAILED");
    }

    [TestMethod]
    public async Task DuplicateAppIdAcrossLibrariesIsDeterministicPartialInsteadOfSilentMerge()
    {
        var fs = new FakeSteamMetadataFileSystem();
        fs.AddDirectory(Path.Combine(SteamRoot, "steamapps"));
        fs.AddDirectory(Path.Combine(SecondaryRoot, "steamapps"));
        fs.AddDirectory(Path.Combine(SteamRoot, "steamapps", "common", "First"));
        fs.AddDirectory(Path.Combine(SecondaryRoot, "steamapps", "common", "Second"));
        fs.AddFile(
            Path.Combine(SteamRoot, "steamapps", "libraryfolders.vdf"),
            LibraryFolders(SteamRoot, SecondaryRoot));
        fs.AddFile(
            Path.Combine(SteamRoot, "steamapps", "appmanifest_100.acf"),
            AppManifest("100", "First", "First"));
        fs.AddFile(
            Path.Combine(SecondaryRoot, "steamapps", "appmanifest_100.acf"),
            AppManifest("100", "Second", "Second"));
        var adapter = Adapter(fs);

        var result = await adapter.RefreshLibraryAsync(Request(), CancellationToken.None);

        Assert.AreEqual(GameClientLibraryRefreshResultCode.Partial, result.ResultCode);
        Assert.AreEqual(1, result.Records.Count);
        CollectionAssert.Contains(
            result.MissingEvidenceClasses!.ToArray(),
            "STEAM_APPMANIFEST_DUPLICATE_APPID");
    }

    [TestMethod]
    public async Task ResolveInstallationUsesCanonicalSteamAppIdAndFreshProjection()
    {
        var fs = BaseFileSystem();
        fs.AddDirectory(Path.Combine(SteamRoot, "steamapps", "common", "Game A"));
        fs.AddFile(
            Path.Combine(SteamRoot, "steamapps", "appmanifest_100.acf"),
            AppManifest("100", "Game A", "Game A"));
        var adapter = Adapter(fs);

        var found = await adapter.ResolveInstallationAsync(
            new ExternalGameIdentity(GameClientType.Steam, SteamClientAdapter.ExternalIdKind, "100"),
            GameClientFreshnessRequirement.FreshRequired,
            CancellationToken.None);
        var missing = await adapter.ResolveInstallationAsync(
            new ExternalGameIdentity(GameClientType.Steam, SteamClientAdapter.ExternalIdKind, "999"),
            GameClientFreshnessRequirement.FreshRequired,
            CancellationToken.None);

        Assert.IsNotNull(found);
        Assert.AreEqual(GameInstallState.InstalledVerifiedEvidence, found!.Installation.State);
        Assert.IsNull(missing);
    }

    [TestMethod]
    public async Task MetadataReadFailureIsNotReportedAsEmptyRefreshedLibrary()
    {
        var fs = new FakeSteamMetadataFileSystem();
        fs.AddDirectory(Path.Combine(SteamRoot, "steamapps"));
        fs.AddReadFailure(
            Path.Combine(SteamRoot, "steamapps", "libraryfolders.vdf"),
            "STEAM_METADATA_FILE_TOO_LARGE");
        var adapter = Adapter(fs);

        var result = await adapter.RefreshLibraryAsync(Request(), CancellationToken.None);

        Assert.AreEqual(GameClientLibraryRefreshResultCode.ParseFailed, result.ResultCode);
        Assert.AreEqual("STEAM_METADATA_FILE_TOO_LARGE", result.ParserStatus);
        Assert.AreEqual(0, result.Records.Count);
    }

    private static FakeSteamMetadataFileSystem BaseFileSystem()
    {
        var fs = new FakeSteamMetadataFileSystem();
        fs.AddDirectory(Path.Combine(SteamRoot, "steamapps"));
        fs.AddFile(
            Path.Combine(SteamRoot, "steamapps", "libraryfolders.vdf"),
            LibraryFolders(SteamRoot));
        return fs;
    }

    private static SteamClientAdapter Adapter(FakeSteamMetadataFileSystem fs)
        => new(
            new ConstantRegistrationReader(
                new SteamProtocolRegistrationSnapshot(
                    true,
                    true,
                    true,
                    "\"C:\\Steam\\steam.exe\" \"%1\"")),
            new ConstantExecutableReader(
                new SteamExecutableEvidence(true, "10.20.30.40", true)),
            fs,
            new FixedTimeProvider(ObservedAt));

    private static GameClientLibraryRefreshRequest Request()
        => new(
            Guid.NewGuid(),
            GameClientType.Steam,
            GameClientLibraryRefreshReason.RuntimeStart,
            GameClientFreshnessRequirement.FreshRequired,
            null,
            ObservedAt.AddMinutes(1));

    private static string LibraryFolders(params string[] roots)
    {
        var entries = roots.Select((root, index) =>
            $"\"{index}\" {{ \"path\" \"{root.Replace("\\", "\\\\", StringComparison.Ordinal)}\" }}");
        return $"\"libraryfolders\" {{ {string.Join(" ", entries)} }}";
    }

    private static string AppManifest(string appId, string name, string installDir)
        => $"\"AppState\" {{ \"appid\" \"{appId}\" \"name\" \"{name}\" \"installdir\" \"{installDir.Replace("\\", "\\\\", StringComparison.Ordinal)}\" }}";

    private sealed class ConstantRegistrationReader(SteamProtocolRegistrationSnapshot snapshot)
        : ISteamProtocolRegistrationReader
    {
        public SteamProtocolRegistrationSnapshot Read() => snapshot;
    }

    private sealed class ConstantExecutableReader(SteamExecutableEvidence evidence)
        : ISteamExecutableEvidenceReader
    {
        public SteamExecutableEvidence Read(string normalizedExecutablePath) => evidence;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakeSteamMetadataFileSystem : ISteamMetadataFileSystem
    {
        private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _readFailures = new(StringComparer.OrdinalIgnoreCase);

        public void AddDirectory(string path) => _directories.Add(Normalize(path));

        public void AddFile(string path, string content)
        {
            var normalized = Normalize(path);
            _files[normalized] = content;
            var directory = Path.GetDirectoryName(normalized);
            if (!string.IsNullOrWhiteSpace(directory))
                _directories.Add(Normalize(directory));
        }

        public void AddReadFailure(string path, string diagnosticsCode)
            => _readFailures[Normalize(path)] = diagnosticsCode;

        public bool DirectoryExists(string normalizedPath)
            => _directories.Contains(Normalize(normalizedPath));

        public SteamMetadataTextReadResult ReadTextFile(string normalizedPath, int maximumBytes)
        {
            var path = Normalize(normalizedPath);
            if (_readFailures.TryGetValue(path, out var failure))
                return new SteamMetadataTextReadResult(false, true, null, failure);
            if (!_files.TryGetValue(path, out var content))
                return new SteamMetadataTextReadResult(true, false, null, "STEAM_METADATA_FILE_NOT_FOUND");
            if (Encoding.UTF8.GetByteCount(content) > maximumBytes)
                return new SteamMetadataTextReadResult(false, true, null, "STEAM_METADATA_FILE_TOO_LARGE");
            return new SteamMetadataTextReadResult(true, true, content);
        }

        public SteamMetadataEnumerationResult EnumerateFiles(
            string normalizedDirectory,
            string searchPattern,
            int maximumFiles)
        {
            var directory = Normalize(normalizedDirectory);
            if (!_directories.Contains(directory))
                return new SteamMetadataEnumerationResult(true, false, Array.Empty<string>());

            if (!string.Equals(searchPattern, "appmanifest_*.acf", StringComparison.Ordinal))
                throw new AssertFailedException($"Unexpected pattern: {searchPattern}");

            var files = _files.Keys
                .Where(path => string.Equals(Path.GetDirectoryName(path), directory, StringComparison.OrdinalIgnoreCase))
                .Where(path =>
                {
                    var name = Path.GetFileName(path);
                    return name.StartsWith("appmanifest_", StringComparison.OrdinalIgnoreCase)
                        && name.EndsWith(".acf", StringComparison.OrdinalIgnoreCase);
                })
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (files.Length > maximumFiles)
            {
                return new SteamMetadataEnumerationResult(
                    false,
                    true,
                    Array.Empty<string>(),
                    "STEAM_METADATA_FILE_COUNT_LIMIT_EXCEEDED");
            }

            return new SteamMetadataEnumerationResult(true, true, files);
        }

        private static string Normalize(string path)
            => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
}
