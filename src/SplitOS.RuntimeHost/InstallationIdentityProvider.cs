using System.Text;
using SplitOS.Persistence;

namespace SplitOS.RuntimeHost;

public enum InstallationIdentityReadStatus
{
    Available,
    Missing,
    Invalid,
    Unreadable
}

public sealed record InstallationIdentityReadResult(
    InstallationIdentityReadStatus Status,
    string? InstallationId,
    string ProductCode)
{
    public bool IsAvailable => Status == InstallationIdentityReadStatus.Available;
}

public interface IInstallationIdentityProvider
{
    ValueTask<InstallationIdentityReadResult> ReadAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads the machine-scoped SplitOS installation identity created by trusted provisioning.
/// RuntimeHost deliberately never generates or repairs this value itself: a missing/invalid identity
/// is a provisioning/recovery condition rather than an opportunity for an unprivileged process to
/// redefine server-side installation binding.
/// </summary>
public sealed class MachineInstallationIdentityProvider : IInstallationIdentityProvider
{
    private const int MaximumFileBytes = 512;
    private const int MaximumIdentityCharacters = 256;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly string _path;

    public MachineInstallationIdentityProvider()
        : this(StoragePaths.MachineInstallationIdentity)
    {
    }

    public MachineInstallationIdentityProvider(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Installation identity path is required.", nameof(path));
        }

        _path = Path.GetFullPath(path);
    }

    public async ValueTask<InstallationIdentityReadResult> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(_path))
        {
            return Missing();
        }

        try
        {
            var fileInfo = new FileInfo(_path);
            if (fileInfo.Length <= 0 || fileInfo.Length > MaximumFileBytes)
            {
                return Invalid();
            }

            var bytes = await File.ReadAllBytesAsync(_path, cancellationToken).ConfigureAwait(false);
            try
            {
                if (bytes.Length == 0 || bytes.Length > MaximumFileBytes)
                {
                    return Invalid();
                }

                string text;
                try
                {
                    text = StrictUtf8.GetString(bytes);
                }
                catch (DecoderFallbackException)
                {
                    return Invalid();
                }

                var installationId = TrimSingleTerminalNewline(text);
                if (!IsValidInstallationId(installationId))
                {
                    return Invalid();
                }

                return new InstallationIdentityReadResult(
                    InstallationIdentityReadStatus.Available,
                    installationId,
                    "INSTALLATION_ID_AVAILABLE");
            }
            finally
            {
                Array.Clear(bytes);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new InstallationIdentityReadResult(
                InstallationIdentityReadStatus.Unreadable,
                null,
                "INSTALLATION_ID_UNREADABLE");
        }
    }

    private static string TrimSingleTerminalNewline(string value)
    {
        if (value.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return value[..^2];
        }

        if (value.EndsWith("\n", StringComparison.Ordinal))
        {
            return value[..^1];
        }

        return value;
    }

    private static bool IsValidInstallationId(string value)
        => !string.IsNullOrWhiteSpace(value)
           && value.Length <= MaximumIdentityCharacters
           && string.Equals(value, value.Trim(), StringComparison.Ordinal)
           && !value.Any(static character => char.IsControl(character) || char.IsWhiteSpace(character));

    private static InstallationIdentityReadResult Missing()
        => new(
            InstallationIdentityReadStatus.Missing,
            null,
            "INSTALLATION_ID_MISSING");

    private static InstallationIdentityReadResult Invalid()
        => new(
            InstallationIdentityReadStatus.Invalid,
            null,
            "INSTALLATION_ID_INVALID");
}
