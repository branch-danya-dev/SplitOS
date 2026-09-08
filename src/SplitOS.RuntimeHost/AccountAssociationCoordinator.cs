using System.Security.Principal;
using SplitOS.Persistence.ProtectedSecrets;
using SplitOS.Persistence.User;

namespace SplitOS.RuntimeHost;

public sealed record AccountAssociationEvaluation(
    string AssociationState,
    string? AccountId,
    string? Reason,
    int? Revision);

public interface IWindowsUserContext
{
    string GetCurrentUserSid();
}

public sealed class WindowsUserContext : IWindowsUserContext
{
    public string GetCurrentUserSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value
            ?? throw new InvalidOperationException("Current Windows user SID is unavailable.");
    }
}

public sealed class AccountAssociationCoordinator(
    IUserAccountAssociationStore associationStore,
    IAccountSecretStore secretStore,
    IWindowsUserContext windowsUserContext)
{
    private const int MaximumRevisionConflictRetries = 1;

    public Task<AccountAssociationEvaluation> EvaluateAsync(CancellationToken cancellationToken = default)
        => EvaluateAsyncCore(0, cancellationToken);

    private async Task<AccountAssociationEvaluation> EvaluateAsyncCore(
        int revisionConflictRetries,
        CancellationToken cancellationToken)
    {
        var association = await associationStore.GetAccountAssociationAsync(cancellationToken).ConfigureAwait(false);
        if (association is null)
        {
            return new AccountAssociationEvaluation("UNASSOCIATED", null, null, null);
        }

        var currentSid = windowsUserContext.GetCurrentUserSid();
        if (!string.Equals(currentSid, association.WindowsUserSid, StringComparison.OrdinalIgnoreCase))
        {
            return new AccountAssociationEvaluation(
                "REAUTH_REQUIRED",
                association.AccountId,
                "LOCAL_ASSOCIATION_CONTEXT_MISMATCH",
                association.Revision);
        }

        var secret = await secretStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (secret.Status == AccountSecretReadStatus.Missing)
        {
            return await ConvergeToReauthAsync(
                association,
                "LOCAL_SECRET_MISSING",
                revisionConflictRetries,
                cancellationToken).ConfigureAwait(false);
        }

        if (secret.Status == AccountSecretReadStatus.Unreadable || secret.Secret is null)
        {
            return await ConvergeToReauthAsync(
                association,
                "LOCAL_SECRET_UNREADABLE",
                revisionConflictRetries,
                cancellationToken).ConfigureAwait(false);
        }

        if (!string.Equals(secret.Secret.AccountId, association.AccountId, StringComparison.Ordinal))
        {
            return await ConvergeToReauthAsync(
                association,
                "LOCAL_SECRET_ACCOUNT_MISMATCH",
                revisionConflictRetries,
                cancellationToken).ConfigureAwait(false);
        }

        return new AccountAssociationEvaluation(
            association.AssociationState,
            association.AccountId,
            association.AssociationState == "REAUTH_REQUIRED" ? "REAUTH_REQUIRED" : null,
            association.Revision);
    }

    private async Task<AccountAssociationEvaluation> ConvergeToReauthAsync(
        UserAccountAssociationRecord association,
        string reason,
        int revisionConflictRetries,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(association.AssociationState, "REAUTH_REQUIRED", StringComparison.Ordinal))
        {
            var outcome = await associationStore.MarkReauthRequiredAsync(
                association.Revision,
                Guid.NewGuid(),
                cancellationToken).ConfigureAwait(false);

            if (outcome.Disposition is UserAssociationWriteDisposition.Applied or UserAssociationWriteDisposition.Unchanged)
            {
                return new AccountAssociationEvaluation(
                    "REAUTH_REQUIRED",
                    association.AccountId,
                    reason,
                    outcome.ActualRevision);
            }

            if (outcome.Disposition == UserAssociationWriteDisposition.Missing)
            {
                return new AccountAssociationEvaluation("UNASSOCIATED", null, null, null);
            }

            if (outcome.Disposition == UserAssociationWriteDisposition.RevisionConflict)
            {
                if (revisionConflictRetries < MaximumRevisionConflictRetries)
                {
                    return await EvaluateAsyncCore(
                        revisionConflictRetries + 1,
                        cancellationToken).ConfigureAwait(false);
                }

                throw new InvalidDataException(
                    "Account association changed repeatedly while RuntimeHost was reconciling protected credentials.");
            }
        }

        return new AccountAssociationEvaluation(
            "REAUTH_REQUIRED",
            association.AccountId,
            reason,
            association.Revision);
    }
}
