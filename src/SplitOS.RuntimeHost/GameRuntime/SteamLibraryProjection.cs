using System.Text;

namespace SplitOS.RuntimeHost.GameRuntime;

public sealed record SteamMetadataTextReadResult(
    bool Success,
    bool Exists,
    string? Content,
    string? DiagnosticsCode = null);

public sealed record SteamMetadataEnumerationResult(
    bool Success,
    bool DirectoryExists,
    IReadOnlyList<string> Files,
    string? DiagnosticsCode = null);

public interface ISteamMetadataFileSystem
{
    bool DirectoryExists(string normalizedPath);

    SteamMetadataTextReadResult ReadTextFile(string normalizedPath, int maximumBytes);

    SteamMetadataEnumerationResult EnumerateFiles(
        string normalizedDirectory,
        string searchPattern,
        int maximumFiles);
}

/// <summary>
/// Read-only Steam metadata filesystem. Every text read is bounded before decoding and supports only
/// explicit UTF-8/UTF-16 encodings. Steam metadata is external evidence and is never executed or edited.
/// </summary>
public sealed class WindowsSteamMetadataFileSystem : ISteamMetadataFileSystem
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly UnicodeEncoding StrictUtf16Le = new(false, true, true);
    private static readonly UnicodeEncoding StrictUtf16Be = new(true, true, true);

    public bool DirectoryExists(string normalizedPath)
        => !string.IsNullOrWhiteSpace(normalizedPath) && Directory.Exists(normalizedPath);

    public SteamMetadataTextReadResult ReadTextFile(string normalizedPath, int maximumBytes)
    {
        if (string.IsNullOrWhiteSpace(normalizedPath))
            throw new ArgumentException("Steam metadata path is required.", nameof(normalizedPath));
        if (maximumBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));

        try
        {
            using var stream = new FileStream(
                normalizedPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            if (stream.Length > maximumBytes)
            {
                return new SteamMetadataTextReadResult(
                    false,
                    true,
                    null,
                    "STEAM_METADATA_FILE_TOO_LARGE");
            }

            var buffer = new byte[maximumBytes + 1];
            var total = 0;
            while (total < buffer.Length)
            {
                var read = stream.Read(buffer, total, buffer.Length - total);
                if (read == 0)
                    break;
                total += read;
            }

            if (total > maximumBytes)
            {
                return new SteamMetadataTextReadResult(
                    false,
                    true,
                    null,
                    "STEAM_METADATA_FILE_TOO_LARGE");
            }

            var content = Decode(buffer.AsSpan(0, total), out var encodingDiagnostic);
            if (content is null)
            {
                return new SteamMetadataTextReadResult(
                    false,
                    true,
                    null,
                    encodingDiagnostic);
            }

            return new SteamMetadataTextReadResult(true, true, content);
        }
        catch (FileNotFoundException)
        {
            return new SteamMetadataTextReadResult(true, false, null, "STEAM_METADATA_FILE_NOT_FOUND");
        }
        catch (DirectoryNotFoundException)
        {
            return new SteamMetadataTextReadResult(true, false, null, "STEAM_METADATA_FILE_NOT_FOUND");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or IOException
            or NotSupportedException)
        {
            return new SteamMetadataTextReadResult(false, File.Exists(normalizedPath), null, "STEAM_METADATA_READ_FAILED");
        }
    }

    public SteamMetadataEnumerationResult EnumerateFiles(
        string normalizedDirectory,
        string searchPattern,
        int maximumFiles)
    {
        if (string.IsNullOrWhiteSpace(normalizedDirectory))
            throw new ArgumentException("Steam metadata directory is required.", nameof(normalizedDirectory));
        if (string.IsNullOrWhiteSpace(searchPattern))
            throw new ArgumentException("Steam metadata search pattern is required.", nameof(searchPattern));
        if (maximumFiles <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumFiles));

        if (!Directory.Exists(normalizedDirectory))
            return new SteamMetadataEnumerationResult(true, false, Array.Empty<string>());

        try
        {
            var files = Directory
                .EnumerateFiles(normalizedDirectory, searchPattern, SearchOption.TopDirectoryOnly)
                .Take(maximumFiles + 1)
                .Select(Path.GetFullPath)
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

            return new SteamMetadataEnumerationResult(true, true, Array.AsReadOnly(files));
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or IOException
            or ArgumentException
            or NotSupportedException)
        {
            return new SteamMetadataEnumerationResult(
                false,
                Directory.Exists(normalizedDirectory),
                Array.Empty<string>(),
                "STEAM_METADATA_ENUMERATION_FAILED");
        }
    }

    private static string? Decode(ReadOnlySpan<byte> bytes, out string diagnosticsCode)
    {
        diagnosticsCode = "STEAM_METADATA_ENCODING_OK";
        try
        {
            if (bytes.Length >= 4
                && ((bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
                    || (bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)))
            {
                diagnosticsCode = "STEAM_METADATA_ENCODING_UNSUPPORTED";
                return null;
            }

            if (bytes.Length >= 3
                && bytes[0] == 0xEF
                && bytes[1] == 0xBB
                && bytes[2] == 0xBF)
                return StrictUtf8.GetString(bytes[3..]);

            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return StrictUtf16Le.GetString(bytes[2..]);

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return StrictUtf16Be.GetString(bytes[2..]);

            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            diagnosticsCode = "STEAM_METADATA_ENCODING_INVALID";
            return null;
        }
    }
}

public sealed record SteamLibraryFoldersParseResult(
    bool Success,
    IReadOnlyList<string> LibraryRoots,
    string? DiagnosticsCode = null);

public sealed record SteamAppManifestParseResult(
    bool Success,
    string? AppId,
    string? DisplayName,
    string? InstallDirectoryName,
    string? DiagnosticsCode = null);

/// <summary>
/// Minimal bounded Valve KeyValues parser for the Steam metadata shapes owned by IMP-082.
/// It is intentionally not regex-based and fails closed on malformed structure, duplicate keys,
/// excessive nesting/tokens, and unknown required schema.
/// </summary>
public static class SteamVdfMetadataParser
{
    public const int MaximumNestingDepth = 32;
    public const int MaximumTokenCount = 200_000;
    public const int MaximumStringLength = 32_768;

    public static SteamLibraryFoldersParseResult ParseLibraryFolders(string content)
    {
        if (!TryParseDocument(content, out var document, out var parseDiagnostic) || document is null)
            return new SteamLibraryFoldersParseResult(false, Array.Empty<string>(), parseDiagnostic);

        if (!document.TryGetObject("libraryfolders", out var folders) || folders is null)
        {
            return new SteamLibraryFoldersParseResult(
                false,
                Array.Empty<string>(),
                "STEAM_LIBRARY_SCHEMA_UNKNOWN");
        }

        var discovered = new List<string>();
        var numericEntryCount = 0;
        foreach (var pair in folders.Values.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!uint.TryParse(pair.Key, out _))
                continue;

            numericEntryCount++;
            string? path = pair.Value switch
            {
                VdfString value => value.Value,
                VdfObject value when value.Value.TryGetString("path", out var nestedPath) => nestedPath,
                _ => null
            };

            if (string.IsNullOrWhiteSpace(path))
            {
                return new SteamLibraryFoldersParseResult(
                    false,
                    Array.Empty<string>(),
                    "STEAM_LIBRARY_SCHEMA_UNKNOWN");
            }

            discovered.Add(path.Trim());
        }

        if (numericEntryCount == 0)
        {
            return new SteamLibraryFoldersParseResult(
                false,
                Array.Empty<string>(),
                "STEAM_LIBRARY_SCHEMA_UNKNOWN");
        }

        return new SteamLibraryFoldersParseResult(
            true,
            Array.AsReadOnly(discovered.ToArray()),
            "STEAM_LIBRARY_SCHEMA_V1");
    }

    public static SteamAppManifestParseResult ParseAppManifest(string content)
    {
        if (!TryParseDocument(content, out var document, out var parseDiagnostic) || document is null)
            return new SteamAppManifestParseResult(false, null, null, null, parseDiagnostic);

        if (!document.TryGetObject("AppState", out var appState) || appState is null)
            return new SteamAppManifestParseResult(false, null, null, null, "STEAM_APPMANIFEST_SCHEMA_UNKNOWN");

        if (!appState.TryGetString("appid", out var rawAppId)
            || !uint.TryParse(rawAppId, out var parsedAppId)
            || parsedAppId == 0)
        {
            return new SteamAppManifestParseResult(false, null, null, null, "STEAM_APPMANIFEST_APPID_INVALID");
        }

        appState.TryGetString("name", out var displayName);
        appState.TryGetString("installdir", out var installDirectory);

        return new SteamAppManifestParseResult(
            true,
            parsedAppId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            NormalizeOptional(displayName),
            NormalizeOptional(installDirectory),
            installDirectory is null
                ? "STEAM_APPMANIFEST_INSTALLDIR_MISSING"
                : "STEAM_APPMANIFEST_SCHEMA_V1");
    }

    private static bool TryParseDocument(
        string content,
        out VdfObject? document,
        out string diagnosticsCode)
    {
        document = null;
        if (content is null)
        {
            diagnosticsCode = "STEAM_VDF_CONTENT_MISSING";
            return false;
        }

        try
        {
            var parser = new Parser(content);
            document = parser.ParseDocument();
            diagnosticsCode = "STEAM_VDF_PARSE_OK";
            return true;
        }
        catch (InvalidDataException)
        {
            diagnosticsCode = "STEAM_VDF_PARSE_FAILED";
            return false;
        }
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private abstract record VdfValue;
    private sealed record VdfString(string Value) : VdfValue;
    private sealed record VdfObject(VdfDictionary Value) : VdfValue
    {
        public IReadOnlyDictionary<string, VdfValue> Values => Value.Values;
        public bool TryGetString(string key, out string? value) => Value.TryGetString(key, out value);
        public bool TryGetObject(string key, out VdfObject? value) => Value.TryGetObject(key, out value);
    }

    private sealed class VdfDictionary
    {
        private readonly Dictionary<string, VdfValue> _values = new(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyDictionary<string, VdfValue> Values => _values;

        public void Add(string key, VdfValue value)
        {
            if (!_values.TryAdd(key, value))
                throw new InvalidDataException("Duplicate VDF key is ambiguous.");
        }

        public bool TryGetString(string key, out string? value)
        {
            if (_values.TryGetValue(key, out var candidate) && candidate is VdfString text)
            {
                value = text.Value;
                return true;
            }

            value = null;
            return false;
        }

        public bool TryGetObject(string key, out VdfObject? value)
        {
            if (_values.TryGetValue(key, out var candidate) && candidate is VdfObject nested)
            {
                value = nested;
                return true;
            }

            value = null;
            return false;
        }
    }

    private sealed class Parser
    {
        private readonly string _text;
        private int _index;
        private int _tokenCount;

        public Parser(string text)
        {
            _text = text;
        }

        public VdfObject ParseDocument()
        {
            var root = ParseDictionary(braced: false, depth: 0);
            SkipTrivia();
            if (_index != _text.Length)
                throw new InvalidDataException("Unexpected trailing VDF content.");
            return new VdfObject(root);
        }

        private VdfDictionary ParseDictionary(bool braced, int depth)
        {
            if (depth > MaximumNestingDepth)
                throw new InvalidDataException("VDF nesting limit exceeded.");

            var result = new VdfDictionary();
            while (true)
            {
                SkipTrivia();
                if (_index >= _text.Length)
                {
                    if (braced)
                        throw new InvalidDataException("Unterminated VDF object.");
                    return result;
                }

                if (_text[_index] == '}')
                {
                    if (!braced)
                        throw new InvalidDataException("Unexpected VDF closing brace.");
                    _index++;
                    CountToken();
                    return result;
                }

                var key = ReadStringToken();
                SkipTrivia();
                if (_index >= _text.Length)
                    throw new InvalidDataException("VDF key is missing a value.");

                VdfValue value;
                if (_text[_index] == '{')
                {
                    _index++;
                    CountToken();
                    value = new VdfObject(ParseDictionary(braced: true, depth + 1));
                }
                else
                {
                    value = new VdfString(ReadStringToken());
                }

                result.Add(key, value);
            }
        }

        private string ReadStringToken()
        {
            SkipTrivia();
            if (_index >= _text.Length || _text[_index] is '{' or '}')
                throw new InvalidDataException("Expected VDF string token.");

            CountToken();
            if (_text[_index] != '"')
            {
                var start = _index;
                while (_index < _text.Length
                    && !char.IsWhiteSpace(_text[_index])
                    && _text[_index] is not '{' and not '}')
                {
                    _index++;
                    if (_index - start > MaximumStringLength)
                        throw new InvalidDataException("VDF string limit exceeded.");
                }

                if (_index == start)
                    throw new InvalidDataException("Empty VDF token.");
                return _text[start.._index];
            }

            _index++;
            var builder = new StringBuilder();
            while (_index < _text.Length)
            {
                var current = _text[_index++];
                if (current == '"')
                    return builder.ToString();

                if (current == '\\' && _index < _text.Length)
                {
                    var escaped = _text[_index++];
                    switch (escaped)
                    {
                        case '\\':
                            builder.Append('\\');
                            break;
                        case '"':
                            builder.Append('"');
                            break;
                        case 'n':
                            builder.Append('\n');
                            break;
                        case 'r':
                            builder.Append('\r');
                            break;
                        case 't':
                            builder.Append('\t');
                            break;
                        default:
                            builder.Append('\\');
                            builder.Append(escaped);
                            break;
                    }
                }
                else
                {
                    builder.Append(current);
                }

                if (builder.Length > MaximumStringLength)
                    throw new InvalidDataException("VDF string limit exceeded.");
            }

            throw new InvalidDataException("Unterminated quoted VDF string.");
        }

        private void SkipTrivia()
        {
            while (_index < _text.Length)
            {
                if (char.IsWhiteSpace(_text[_index]))
                {
                    _index++;
                    continue;
                }

                if (_text[_index] == '/'
                    && _index + 1 < _text.Length
                    && _text[_index + 1] == '/')
                {
                    _index += 2;
                    while (_index < _text.Length && _text[_index] is not '\r' and not '\n')
                        _index++;
                    continue;
                }

                break;
            }
        }

        private void CountToken()
        {
            _tokenCount++;
            if (_tokenCount > MaximumTokenCount)
                throw new InvalidDataException("VDF token limit exceeded.");
        }
    }
}
