using SplitOS.Contracts.Protocol;

namespace SplitOS.RuntimeHost.Authentication;

public interface IRuntimeAuthStartCommandFactory
{
    IRuntimeAuthStartCommand Create(NativeAuthAuthorityConfiguration authority);
}

/// <summary>
/// Production-facing semantic Auth.Start gate. Release provisioning remains the only source of OAuth/OIDC
/// authority. A verified package is required before any concrete interactive authentication pipeline is
/// constructed; Manager can never substitute endpoints, client metadata or trust material.
/// </summary>
public sealed class ProvisionedRuntimeAuthStartCommand(
    INativeAuthAuthorityPackageProvider authorityProvider,
    IRuntimeAuthStartCommandFactory commandFactory) : IRuntimeAuthStartCommand
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IRuntimeAuthStartCommand? _verifiedCommand;
    private long _verifiedMetadataVersion;
    private long _verifiedSecurityEpoch;

    public async Task<RuntimeAuthStartResult> StartAsync(
        Guid correlationId,
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        if (correlationId == Guid.Empty || operationId == Guid.Empty)
        {
            return Unavailable("AUTH_REQUEST_ID_INVALID");
        }

        NativeAuthAuthorityPackageReadResult package;
        try
        {
            package = await authorityProvider.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Security.SecurityException)
        {
            return Unavailable("AUTH_AUTHORITY_PACKAGE_UNREADABLE");
        }

        if (!package.IsAvailable || package.Metadata is null)
        {
            return Unavailable(package.ProductCode);
        }

        IRuntimeAuthStartCommand command;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_verifiedCommand is null ||
                package.Metadata.SecurityEpoch > _verifiedSecurityEpoch ||
                (package.Metadata.SecurityEpoch == _verifiedSecurityEpoch &&
                 package.Metadata.Version > _verifiedMetadataVersion))
            {
                command = commandFactory.Create(package.Metadata.Authority)
                    ?? throw new InvalidOperationException("Runtime auth command factory returned no command.");
                _verifiedCommand = command;
                _verifiedMetadataVersion = package.Metadata.Version;
                _verifiedSecurityEpoch = package.Metadata.SecurityEpoch;
            }
            else if (package.Metadata.SecurityEpoch < _verifiedSecurityEpoch ||
                     package.Metadata.Version < _verifiedMetadataVersion)
            {
                return Unavailable("AUTH_AUTHORITY_ROLLBACK_REJECTED");
            }
            else
            {
                command = _verifiedCommand;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Unavailable("AUTH_NOT_CONFIGURED");
        }
        finally
        {
            _gate.Release();
        }

        return await command.StartAsync(correlationId, operationId, cancellationToken).ConfigureAwait(false);
    }

    private static RuntimeAuthStartResult Unavailable(string productCode)
        => new(
            "Unavailable",
            string.IsNullOrWhiteSpace(productCode) ? "AUTH_NOT_CONFIGURED" : productCode,
            null,
            null,
            null,
            false);
}
