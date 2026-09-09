using System.Security.Cryptography;
using System.Text;

namespace SplitOS.RuntimeHost.Authentication;

public enum NativeAuthAuthorityPackageStatus
{
    Available,
    Missing,
    Unreadable,
    Rejected
}

public sealed record NativeAuthAuthorityPackageReadResult(
    NativeAuthAuthorityPackageStatus Status,
    string ProductCode,
    VerifiedNativeAuthAuthorityMetadata? Metadata,
    string? PackageSha256 = null)
{
    public bool IsAvailable
        => Status == NativeAuthAuthorityPackageStatus.Available &&
           Metadata is not null &&
           PackageSha256 is { Length: 64 };
}

public interface INativeAuthAuthorityPackageProvider
{
    ValueTask<NativeAuthAuthorityPackageReadResult> ReadAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads the machine-provisioned, release-signed native-auth authority package and verifies it before
/// exposing any OAuth/OIDC endpoint or client metadata to RuntimeHost authentication code.
///
/// This provider is intentionally read-only. RuntimeHost does not download, generate, repair or replace
/// release trust material, and it never falls back to user-supplied configuration. Successful reads also
/// return the SHA-256 identity of the exact verified package bytes so same-version equivocation can be
/// detected by the semantic Auth.Start gate.
/// </summary>
public sealed class ProvisionedNativeAuthAuthorityProvider : INativeAuthAuthorityPackageProvider
{
    private const int MaximumPackageBytes = 128 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly string _path;
    private readonly SignedNativeAuthAuthorityVerifier _verifier;

    public ProvisionedNativeAuthAuthorityProvider(SignedNativeAuthAuthorityVerifier verifier)
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "SplitOS",
                "Trust",
                "native-auth-authority.jws"),
            verifier)
    {
    }

    public ProvisionedNativeAuthAuthorityProvider(
        string path,
        SignedNativeAuthAuthorityVerifier verifier)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Native auth authority package path is required.", nameof(path));
        }

        _path = Path.GetFullPath(path);
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
    }

    public async ValueTask<NativeAuthAuthorityPackageReadResult> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(_path))
        {
            return Result(NativeAuthAuthorityPackageStatus.Missing, "AUTH_AUTHORITY_PACKAGE_MISSING");
        }

        byte[] bytes;
        try
        {
            var fileInfo = new FileInfo(_path);
            if (fileInfo.Length <= 0 || fileInfo.Length > MaximumPackageBytes)
            {
                return Result(NativeAuthAuthorityPackageStatus.Rejected, "AUTH_AUTHORITY_PACKAGE_REJECTED");
            }

            bytes = await File.ReadAllBytesAsync(_path, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Result(NativeAuthAuthorityPackageStatus.Unreadable, "AUTH_AUTHORITY_PACKAGE_UNREADABLE");
        }

        try
        {
            if (bytes.Length == 0 || bytes.Length > MaximumPackageBytes)
            {
                return Result(NativeAuthAuthorityPackageStatus.Rejected, "AUTH_AUTHORITY_PACKAGE_REJECTED");
            }

            string compactEnvelope;
            try
            {
                compactEnvelope = StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return Result(NativeAuthAuthorityPackageStatus.Rejected, "AUTH_AUTHORITY_PACKAGE_REJECTED");
            }

            VerifiedNativeAuthAuthorityMetadata metadata;
            try
            {
                metadata = await _verifier.VerifyAsync(compactEnvelope, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is InvalidDataException or ArgumentException or CryptographicException)
            {
                return Result(NativeAuthAuthorityPackageStatus.Rejected, "AUTH_AUTHORITY_PACKAGE_REJECTED");
            }

            var packageSha256 = Convert.ToHexString(SHA256.HashData(bytes));
            return new NativeAuthAuthorityPackageReadResult(
                NativeAuthAuthorityPackageStatus.Available,
                "AUTH_AUTHORITY_PACKAGE_AVAILABLE",
                metadata,
                packageSha256);
        }
        finally
        {
            Array.Clear(bytes);
        }
    }

    private static NativeAuthAuthorityPackageReadResult Result(
        NativeAuthAuthorityPackageStatus status,
        string productCode)
        => new(status, productCode, null);
}
