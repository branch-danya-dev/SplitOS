using System.Security.Cryptography;
using SplitOS.Persistence.ProtectedSecrets;
using SplitOS.Persistence.User;

namespace SplitOS.RuntimeHost.ProductIdentity;

public enum LocalSignOutDisposition
{
    SignedOut,
    AlreadySignedOut,
    AlreadyInProgress,
    ContextMismatch,
    RecoveryRequired,
    PersistenceFailed
}

public sealed record LocalSignOutResult(
    LocalSignOutDisposition Disposition,
    string ProductCode,
    bool LocalSecretRemoved,
    bool AssociationRemoved);

/// <summary>
/// Performs the local half of explicit SplitOS sign-out. Premium authority is revoked locally first by
/// clearing online evidence and converging the canonical association away from ACTIVE. Destructive cleanup
/// then removes the DPAPI credential blob and exact canonical association. Server-side token revocation and
/// managed-mode convergence remain separate semantic operations.
/// </summary>
public sealed class LocalSignOutCoordinator(
    IUserAccountAssociationStore associationStore,
    IUserAccountAssociationSignOutStore signOutStore,
    IAccountSecretStore secretStore,
    IWindowsUserContext windowsUserContext,
    OnlineEntitlementEvidenceState onlineEvidence,
    RuntimeStateRefreshSignal refreshSignal)
{
    private int _signOutInProgress;

    public async Task<LocalSignOutResult> SignOutAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _signOutInProgress, 1, 0) != 0)
        {
            return new LocalSignOutResult(
                LocalSignOutDisposition.AlreadyInProgress,
                "SIGN_OUT_ALREADY_IN_PROGRESS",
                false,
                false);
        }

        try
        {
            return await SignOutCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _signOutInProgress, 0);
        }
    }

    private async Task<LocalSignOutResult> SignOutCoreAsync(CancellationToken cancellationToken)
    {
        UserAccountAssociationRecord? association;
        try
        {
            association = await associationStore.GetAccountAssociationAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsAssociationPersistenceFailure(exception))
        {
            onlineEvidence.Clear();
            refreshSignal.RequestRefresh();
            return new LocalSignOutResult(
                LocalSignOutDisposition.PersistenceFailed,
                "LOCAL_ASSOCIATION_STORE_FAILED",
                false,
                false);
        }

        if (association is null)
        {
            onlineEvidence.Clear();
            var orphanRemoved = await TryDeleteSecretAsync().ConfigureAwait(false);
            refreshSignal.RequestRefresh();
            return orphanRemoved
                ? new LocalSignOutResult(
                    LocalSignOutDisposition.AlreadySignedOut,
                    "ACCOUNT_ALREADY_SIGNED_OUT",
                    true,
                    true)
                : new LocalSignOutResult(
                    LocalSignOutDisposition.RecoveryRequired,
                    "LOCAL_SECRET_RECONCILIATION_REQUIRED",
                    false,
                    true);
        }

        var currentSid = windowsUserContext.GetCurrentUserSid();
        if (!string.Equals(currentSid, association.WindowsUserSid, StringComparison.OrdinalIgnoreCase))
        {
            onlineEvidence.ClearIfBoundTo(association.AssociationId, association.AccountId);
            refreshSignal.RequestRefresh();
            return new LocalSignOutResult(
                LocalSignOutDisposition.ContextMismatch,
                "LOCAL_ASSOCIATION_CONTEXT_MISMATCH",
                false,
                false);
        }

        // In-memory online authority must disappear before any cleanup attempt. Durable offline authority
        // is then disabled by moving the canonical association away from ACTIVE.
        onlineEvidence.ClearIfBoundTo(association.AssociationId, association.AccountId);

        var convergence = await ConvergeToReauthAsync(association).ConfigureAwait(false);
        refreshSignal.RequestRefresh();
        if (!convergence.Converged)
        {
            return new LocalSignOutResult(
                LocalSignOutDisposition.RecoveryRequired,
                convergence.ProductCode,
                false,
                false);
        }

        if (convergence.Record is null)
        {
            var orphanRemoved = await TryDeleteSecretAsync().ConfigureAwait(false);
            refreshSignal.RequestRefresh();
            return orphanRemoved
                ? new LocalSignOutResult(
                    LocalSignOutDisposition.AlreadySignedOut,
                    "ACCOUNT_ALREADY_SIGNED_OUT",
                    true,
                    true)
                : new LocalSignOutResult(
                    LocalSignOutDisposition.RecoveryRequired,
                    "LOCAL_SECRET_RECONCILIATION_REQUIRED",
                    false,
                    true);
        }

        var reauthAssociation = convergence.Record;
        var secretRemoved = await TryDeleteSecretAsync().ConfigureAwait(false);

        UserAssociationRemovalOutcome removal;
        try
        {
            removal = await signOutStore.RemoveReauthAssociationAsync(
                reauthAssociation.AssociationId,
                reauthAssociation.WindowsUserSid,
                reauthAssociation.AccountId,
                reauthAssociation.Revision,
                Guid.NewGuid(),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsAssociationPersistenceFailure(exception))
        {
            refreshSignal.RequestRefresh();
            return new LocalSignOutResult(
                LocalSignOutDisposition.RecoveryRequired,
                "LOCAL_SIGN_OUT_RECONCILIATION_REQUIRED",
                secretRemoved,
                false);
        }

        var associationRemoved = removal.Disposition is
            UserAssociationRemovalDisposition.Removed or
            UserAssociationRemovalDisposition.Missing;

        refreshSignal.RequestRefresh();
        if (!secretRemoved || !associationRemoved)
        {
            return new LocalSignOutResult(
                LocalSignOutDisposition.RecoveryRequired,
                !secretRemoved
                    ? "LOCAL_SECRET_RECONCILIATION_REQUIRED"
                    : "LOCAL_SIGN_OUT_RECONCILIATION_REQUIRED",
                secretRemoved,
                associationRemoved);
        }

        return new LocalSignOutResult(
            LocalSignOutDisposition.SignedOut,
            "ACCOUNT_SIGNED_OUT",
            true,
            true);
    }

    private async Task<(bool Converged, string ProductCode, UserAccountAssociationRecord? Record)> ConvergeToReauthAsync(
        UserAccountAssociationRecord association)
    {
        if (string.Equals(association.AssociationState, "REAUTH_REQUIRED", StringComparison.Ordinal))
        {
            return (true, "OK", association);
        }

        try
        {
            var outcome = await associationStore.MarkReauthRequiredAsync(
                association.Revision,
                Guid.NewGuid(),
                CancellationToken.None).ConfigureAwait(false);

            if (outcome.Disposition is UserAssociationWriteDisposition.Applied or UserAssociationWriteDisposition.Unchanged)
            {
                return outcome.Record is null
                    ? (false, "LOCAL_SIGN_OUT_RECONCILIATION_REQUIRED", null)
                    : (true, "OK", outcome.Record);
            }

            if (outcome.Disposition == UserAssociationWriteDisposition.Missing)
            {
                return (true, "OK", null);
            }

            if (outcome.Disposition == UserAssociationWriteDisposition.RevisionConflict && outcome.Record is not null)
            {
                var current = outcome.Record;
                if (!string.Equals(current.AssociationId, association.AssociationId, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(current.AccountId, association.AccountId, StringComparison.Ordinal) ||
                    !string.Equals(current.WindowsUserSid, association.WindowsUserSid, StringComparison.OrdinalIgnoreCase))
                {
                    return (false, "LOCAL_ASSOCIATION_CHANGED", current);
                }

                if (string.Equals(current.AssociationState, "REAUTH_REQUIRED", StringComparison.Ordinal))
                {
                    return (true, "OK", current);
                }

                var retry = await associationStore.MarkReauthRequiredAsync(
                    current.Revision,
                    Guid.NewGuid(),
                    CancellationToken.None).ConfigureAwait(false);
                if (retry.Disposition is UserAssociationWriteDisposition.Applied or UserAssociationWriteDisposition.Unchanged &&
                    retry.Record is not null)
                {
                    return (true, "OK", retry.Record);
                }
                if (retry.Disposition == UserAssociationWriteDisposition.Missing)
                {
                    return (true, "OK", null);
                }
            }
        }
        catch (Exception exception) when (IsAssociationPersistenceFailure(exception))
        {
            return (false, "LOCAL_ASSOCIATION_STORE_FAILED", null);
        }

        return (false, "LOCAL_SIGN_OUT_RECONCILIATION_REQUIRED", null);
    }

    private async Task<bool> TryDeleteSecretAsync()
    {
        try
        {
            await secretStore.DeleteAsync(CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (IsSecretPersistenceFailure(exception))
        {
            return false;
        }
    }

    private static bool IsAssociationPersistenceFailure(Exception exception)
        => exception is IOException or UnauthorizedAccessException or InvalidDataException;

    private static bool IsSecretPersistenceFailure(Exception exception)
        => exception is IOException or UnauthorizedAccessException or InvalidDataException or CryptographicException;
}
