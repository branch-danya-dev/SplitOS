using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using SplitOS.Persistence;

namespace SplitOS.Persistence.ProtectedSecrets;

public sealed class AccountSecretEnvelope
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; init; } = CurrentFormatVersion;
    public required string AccountId { get; init; }
    public required string RefreshToken { get; init; }
    public string? RefreshTokenFamilyId { get; init; }
    public DateTimeOffset RefreshIssuedUtc { get; init; }
    public DateTimeOffset RefreshAbsoluteExpiryUtc { get; init; }
    public DateTimeOffset? LastTrustedServerUtc { get; init; }
    public DateTimeOffset? LastTrustedServerObservationLocalUtc { get; init; }
    public string? LastValidAssertionJti { get; init; }
    public string? OfflineEntitlementAssertion { get; init; }
    public DateTimeOffset? OfflineAssertionStoredUtc { get; init; }

    public override string ToString()
        => $"AccountSecretEnvelope(FormatVersion={FormatVersion}, AccountId={AccountId}, Secrets=[REDACTED])";
}

public enum AccountSecretReadStatus
{
    Missing,
    Available,
    Unreadable
}

public sealed class AccountSecretReadResult
{
    private AccountSecretReadResult(AccountSecretReadStatus status, AccountSecretEnvelope? secret)
    {
        Status = status;
        Secret = secret;
    }

    public AccountSecretReadStatus Status { get; }
    public AccountSecretEnvelope? Secret { get; }

    public static AccountSecretReadResult Missing() => new(AccountSecretReadStatus.Missing, null);
    public static AccountSecretReadResult Available(AccountSecretEnvelope secret) => new(AccountSecretReadStatus.Available, secret);
    public static AccountSecretReadResult Unreadable() => new(AccountSecretReadStatus.Unreadable, null);
}

public interface IAccountSecretStore
{
    Task<AccountSecretReadResult> ReadAsync(CancellationToken cancellationToken = default);
    Task WriteAsync(AccountSecretEnvelope secret, CancellationToken cancellationToken = default);
    Task DeleteAsync(CancellationToken cancellationToken = default);
}

public sealed class DpapiAccountSecretStore : IAccountSecretStore
{
    private const int ContainerVersion = 1;
    private const int VersionFieldLength = sizeof(int);
    private static readonly byte[] Magic = "SPLITOS1"u8.ToArray();
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly string _secretPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DpapiAccountSecretStore()
        : this(StoragePaths.UserAccountSecret)
    {
    }

    public DpapiAccountSecretStore(string secretPath)
    {
        if (string.IsNullOrWhiteSpace(secretPath)) throw new ArgumentException("Secret path is required.", nameof(secretPath));
        _secretPath = Path.GetFullPath(secretPath);
        SecretStorageAcl.EnsurePrivatePath(_secretPath);
    }

    public async Task<AccountSecretReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SecretStorageAcl.EnsurePrivatePath(_secretPath);
            if (!File.Exists(_secretPath)) return AccountSecretReadResult.Missing();

            var container = await File.ReadAllBytesAsync(_secretPath, cancellationToken).ConfigureAwait(false);
            byte[]? protectedPayload = null;
            byte[]? plaintext = null;
            try
            {
                var headerLength = Magic.Length + VersionFieldLength;
                if (container.Length <= headerLength) return AccountSecretReadResult.Unreadable();
                if (!container.AsSpan(0, Magic.Length).SequenceEqual(Magic)) return AccountSecretReadResult.Unreadable();

                var version = BinaryPrimitives.ReadInt32LittleEndian(container.AsSpan(Magic.Length, VersionFieldLength));
                if (version != ContainerVersion) return AccountSecretReadResult.Unreadable();

                protectedPayload = container.AsSpan(headerLength).ToArray();
                try
                {
                    plaintext = ProtectedData.Unprotect(protectedPayload, optionalEntropy: null, DataProtectionScope.CurrentUser);
                }
                catch (CryptographicException)
                {
                    return AccountSecretReadResult.Unreadable();
                }

                AccountSecretEnvelope? secret;
                try
                {
                    secret = JsonSerializer.Deserialize<AccountSecretEnvelope>(plaintext, SerializerOptions);
                }
                catch (JsonException)
                {
                    return AccountSecretReadResult.Unreadable();
                }

                if (!IsValid(secret)) return AccountSecretReadResult.Unreadable();
                return AccountSecretReadResult.Available(secret!);
            }
            finally
            {
                if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
                if (protectedPayload is not null) CryptographicOperations.ZeroMemory(protectedPayload);
                CryptographicOperations.ZeroMemory(container);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task WriteAsync(AccountSecretEnvelope secret, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ValidateForWrite(secret);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        byte[]? plaintext = null;
        byte[]? protectedPayload = null;
        byte[]? container = null;
        var temporaryPath = _secretPath + ".new";
        try
        {
            SecretStorageAcl.EnsurePrivatePath(_secretPath);
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);

            plaintext = JsonSerializer.SerializeToUtf8Bytes(secret, SerializerOptions);
            protectedPayload = ProtectedData.Protect(plaintext, optionalEntropy: null, DataProtectionScope.CurrentUser);

            var headerLength = Magic.Length + VersionFieldLength;
            container = GC.AllocateUninitializedArray<byte>(headerLength + protectedPayload.Length);
            Magic.AsSpan().CopyTo(container.AsSpan(0, Magic.Length));
            BinaryPrimitives.WriteInt32LittleEndian(container.AsSpan(Magic.Length, VersionFieldLength), ContainerVersion);
            protectedPayload.AsSpan().CopyTo(container.AsSpan(headerLength));

            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(container, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _secretPath, overwrite: true);
            SecretStorageAcl.EnsurePrivatePath(_secretPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
            if (protectedPayload is not null) CryptographicOperations.ZeroMemory(protectedPayload);
            if (container is not null) CryptographicOperations.ZeroMemory(container);
            _gate.Release();
        }
    }

    public async Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SecretStorageAcl.EnsurePrivatePath(_secretPath);
            var temporaryPath = _secretPath + ".new";
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            if (File.Exists(_secretPath)) File.Delete(_secretPath);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void ValidateForWrite(AccountSecretEnvelope secret)
    {
        if (!IsValid(secret))
        {
            throw new ArgumentException("Account secret envelope is incomplete or inconsistent.", nameof(secret));
        }
    }

    private static bool IsValid(AccountSecretEnvelope? secret)
        => secret is not null
           && secret.FormatVersion == AccountSecretEnvelope.CurrentFormatVersion
           && !string.IsNullOrWhiteSpace(secret.AccountId)
           && !string.IsNullOrWhiteSpace(secret.RefreshToken)
           && secret.RefreshIssuedUtc != default
           && secret.RefreshAbsoluteExpiryUtc > secret.RefreshIssuedUtc
           && (secret.LastTrustedServerObservationLocalUtc is null || secret.LastTrustedServerUtc is not null)
           && (secret.LastValidAssertionJti is null ||
               (!string.IsNullOrWhiteSpace(secret.LastValidAssertionJti) &&
                secret.LastValidAssertionJti.Length <= 256 &&
                !secret.LastValidAssertionJti.Any(char.IsControl)))
           && (secret.OfflineEntitlementAssertion is null || secret.OfflineAssertionStoredUtc is not null);
}
