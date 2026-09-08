using SplitOS.Contracts.Protocol;

namespace SplitOS.RuntimeHost.ProductIdentity;

public interface IRuntimeSignOutCommand
{
    Task<RuntimeSignOutResult> SignOutAsync(CancellationToken cancellationToken = default);
}

public interface ILocalSignOutFlow
{
    Task<LocalSignOutResult> SignOutAsync(CancellationToken cancellationToken = default);
}

public sealed class LocalSignOutFlow(LocalSignOutCoordinator coordinator) : ILocalSignOutFlow
{
    public Task<LocalSignOutResult> SignOutAsync(CancellationToken cancellationToken = default)
        => coordinator.SignOutAsync(cancellationToken);
}

/// <summary>
/// UI-facing semantic sign-out boundary. RuntimeHost owns all identity/credential lookup and returns
/// only bounded cleanup status. No refresh token, offline assertion, account-switch target or SID is
/// accepted from or returned to Manager.
/// </summary>
public sealed class RuntimeSignOutCommand(ILocalSignOutFlow localSignOutFlow) : IRuntimeSignOutCommand
{
    public async Task<RuntimeSignOutResult> SignOutAsync(CancellationToken cancellationToken = default)
    {
        var result = await localSignOutFlow.SignOutAsync(cancellationToken).ConfigureAwait(false);
        return new RuntimeSignOutResult(
            result.Disposition.ToString(),
            result.ProductCode,
            result.LocalSecretRemoved,
            result.AssociationRemoved);
    }
}
