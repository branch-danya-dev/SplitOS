using SplitOS.Contracts.Protocol;
using SplitOS.RuntimeHost.ProductIdentity;

namespace SplitOS.RuntimeHost.Authentication;

public interface IRuntimeAuthStartCommand
{
    Task<RuntimeAuthStartResult> StartAsync(
        Guid correlationId,
        Guid operationId,
        CancellationToken cancellationToken = default);
}

public enum InteractiveIdentityAuthenticationDisposition
{
    Accepted,
    AlreadyInProgress,
    Cancelled,
    BackendUnavailable,
    Rejected
}

public sealed record InteractiveIdentityAuthenticationResult(
    InteractiveIdentityAuthenticationDisposition Disposition,
    string ProductCode,
    Guid? AuthTransactionId,
    ValidatedNativeAuthSession? Session);

public interface IInteractiveIdentityAuthenticationFlow
{
    Task<InteractiveIdentityAuthenticationResult> AuthenticateAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Keeps loopback callback material and OAuth/OIDC token handling entirely inside RuntimeHost.
/// The UI-facing semantic command receives only the final disposition and canonical account outcome.
/// </summary>
public sealed class InteractiveIdentityAuthenticationFlow(
    NativeAuthInteractiveFlow interactiveFlow,
    NativeAuthTokenExchangeService tokenExchange) : IInteractiveIdentityAuthenticationFlow
{
    public async Task<InteractiveIdentityAuthenticationResult> AuthenticateAsync(
        CancellationToken cancellationToken = default)
    {
        var interactive = await interactiveFlow.RunAsync(cancellationToken).ConfigureAwait(false);
        if (interactive.Disposition == NativeAuthInteractiveDisposition.AlreadyInProgress)
        {
            return Result(
                InteractiveIdentityAuthenticationDisposition.AlreadyInProgress,
                interactive.ProductCode,
                interactive.AuthTransactionId);
        }

        if (interactive.Disposition != NativeAuthInteractiveDisposition.CallbackReceived ||
            interactive.CallbackResult is null)
        {
            return Result(
                interactive.Disposition == NativeAuthInteractiveDisposition.Cancelled
                    ? InteractiveIdentityAuthenticationDisposition.Cancelled
                    : InteractiveIdentityAuthenticationDisposition.Rejected,
                interactive.ProductCode,
                interactive.AuthTransactionId);
        }

        var callback = interactive.CallbackResult;
        if (callback.Disposition == NativeAuthCallbackDisposition.Cancelled)
        {
            return Result(
                InteractiveIdentityAuthenticationDisposition.Cancelled,
                callback.ProductCode,
                interactive.AuthTransactionId);
        }

        if (callback.Disposition != NativeAuthCallbackDisposition.Accepted || callback.ExchangeContext is null)
        {
            return Result(
                InteractiveIdentityAuthenticationDisposition.Rejected,
                callback.ProductCode,
                interactive.AuthTransactionId);
        }

        var exchange = await tokenExchange.ExchangeAsync(callback.ExchangeContext, cancellationToken).ConfigureAwait(false);
        if (exchange.Disposition == NativeAuthTokenExchangeDisposition.BackendUnavailable)
        {
            return Result(
                InteractiveIdentityAuthenticationDisposition.BackendUnavailable,
                exchange.ProductCode,
                interactive.AuthTransactionId);
        }

        if (exchange.Disposition != NativeAuthTokenExchangeDisposition.Accepted || exchange.Session is null)
        {
            return Result(
                InteractiveIdentityAuthenticationDisposition.Rejected,
                exchange.ProductCode,
                interactive.AuthTransactionId);
        }

        return new InteractiveIdentityAuthenticationResult(
            InteractiveIdentityAuthenticationDisposition.Accepted,
            exchange.ProductCode,
            interactive.AuthTransactionId,
            exchange.Session);
    }

    private static InteractiveIdentityAuthenticationResult Result(
        InteractiveIdentityAuthenticationDisposition disposition,
        string productCode,
        Guid? authTransactionId)
        => new(disposition, productCode, authTransactionId, null);
}

public interface IInteractiveSessionLifecycleCompletionFlow
{
    Task<InteractiveSessionLifecycleResult> CompleteAsync(
        ValidatedNativeAuthSession session,
        InteractiveSessionLifecycleContext context,
        CancellationToken cancellationToken = default);
}

public sealed class InteractiveSessionLifecycleCompletionFlow(
    InteractiveSessionEntitlementLifecycleCoordinator coordinator)
    : IInteractiveSessionLifecycleCompletionFlow
{
    public Task<InteractiveSessionLifecycleResult> CompleteAsync(
        ValidatedNativeAuthSession session,
        InteractiveSessionLifecycleContext context,
        CancellationToken cancellationToken = default)
        => coordinator.CompleteAsync(session, context, cancellationToken);
}

/// <summary>
/// Semantic Auth.Start owner. Correlation/operation IDs come from the authenticated IPC envelope;
/// Manager supplies no auth authority, redirect, PKCE, token, account, installation or entitlement data.
/// </summary>
public sealed class RuntimeAuthStartCoordinator(
    IInteractiveIdentityAuthenticationFlow authenticationFlow,
    IInteractiveSessionLifecycleCompletionFlow lifecycleFlow,
    IInstallationIdentityProvider installationIdentityProvider) : IRuntimeAuthStartCommand
{
    public async Task<RuntimeAuthStartResult> StartAsync(
        Guid correlationId,
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        if (correlationId == Guid.Empty || operationId == Guid.Empty)
        {
            return Rejected("AUTH_REQUEST_ID_INVALID");
        }

        var authentication = await authenticationFlow.AuthenticateAsync(cancellationToken).ConfigureAwait(false);
        if (authentication.Disposition != InteractiveIdentityAuthenticationDisposition.Accepted ||
            authentication.Session is null)
        {
            return new RuntimeAuthStartResult(
                authentication.Disposition.ToString(),
                authentication.ProductCode,
                authentication.AuthTransactionId,
                null,
                null,
                false);
        }

        InstallationIdentityReadResult installation;
        try
        {
            installation = await installationIdentityProvider.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Security.SecurityException)
        {
            return new RuntimeAuthStartResult(
                "Rejected",
                "INSTALLATION_ID_UNREADABLE",
                authentication.AuthTransactionId,
                null,
                null,
                false);
        }

        if (!installation.IsAvailable || string.IsNullOrWhiteSpace(installation.InstallationId))
        {
            return new RuntimeAuthStartResult(
                "Rejected",
                installation.ProductCode,
                authentication.AuthTransactionId,
                null,
                null,
                false);
        }

        var lifecycle = await lifecycleFlow.CompleteAsync(
            authentication.Session,
            new InteractiveSessionLifecycleContext(
                ComponentIdentity.Version,
                installation.InstallationId,
                correlationId,
                operationId),
            cancellationToken).ConfigureAwait(false);

        return new RuntimeAuthStartResult(
            lifecycle.Disposition.ToString(),
            lifecycle.ProductCode,
            authentication.AuthTransactionId,
            lifecycle.AccountId,
            lifecycle.AssociationId,
            lifecycle.HasFreshOnlineEntitlement);
    }

    private static RuntimeAuthStartResult Rejected(string productCode)
        => new("Rejected", productCode, null, null, null, false);
}

/// <summary>
/// Production fail-closed placeholder used until release-owned OAuth authority configuration is provisioned.
/// This keeps Auth.Start semantically available without ever accepting endpoints or secrets from Manager.
/// </summary>
public sealed class UnavailableRuntimeAuthStartCommand : IRuntimeAuthStartCommand
{
    public Task<RuntimeAuthStartResult> StartAsync(
        Guid correlationId,
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new RuntimeAuthStartResult(
            "Unavailable",
            "AUTH_NOT_CONFIGURED",
            null,
            null,
            null,
            false));
    }
}
