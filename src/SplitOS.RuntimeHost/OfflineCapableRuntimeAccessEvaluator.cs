using System.Security.Cryptography;
using SplitOS.Persistence.ProtectedSecrets;
using SplitOS.RuntimeHost.ProductIdentity;

namespace SplitOS.RuntimeHost;

/// <summary>
/// Extends the online entitlement evaluator with a narrowly bounded offline fallback.
/// Fresh online evidence remains authoritative: local offline proof is consulted only when
/// online evidence is absent or has aged beyond the bounded freshness window.
/// </summary>
public sealed class OfflineCapableRuntimeAccessEvaluator : IRuntimeAccessEvaluator
{
    private const string ManagedRuntimeCapability = "runtime.managed_modes";

    private readonly OnlineEntitlementRuntimeAccessEvaluator _onlineEvaluator;
    private readonly IAccountSecretStore _secretStore;
    private readonly OfflineEntitlementAssertionValidator _offlineValidator;
    private readonly string _installationId;

    public OfflineCapableRuntimeAccessEvaluator(
        OnlineEntitlementRuntimeAccessEvaluator onlineEvaluator,
        IAccountSecretStore secretStore,
        OfflineEntitlementAssertionValidator offlineValidator,
        string installationId)
    {
        _onlineEvaluator = onlineEvaluator ?? throw new ArgumentNullException(nameof(onlineEvaluator));
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        _offlineValidator = offlineValidator ?? throw new ArgumentNullException(nameof(offlineValidator));
        _installationId = ValidateInstallationId(installationId);
    }

    public async ValueTask<RuntimeAccessEvaluation> EvaluateAsync(
        AccountAssociationEvaluation association,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(association);

        var online = await _onlineEvaluator.EvaluateAsync(association, cancellationToken).ConfigureAwait(false);
        if (online.IsEnabled)
        {
            return online;
        }

        // Fresh negative online authority (FREE, suspended, missing capability, context mismatch,
        // clock anomaly, etc.) must never be overridden by an older offline PRO assertion.
        if (!CanAttemptOfflineFallback(online.Reason))
        {
            return online;
        }

        if (!string.Equals(association.AssociationState, "ACTIVE", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(association.AccountId) ||
            string.IsNullOrWhiteSpace(association.AssociationId))
        {
            return online;
        }

        AccountSecretReadResult secretRead;
        try
        {
            secretRead = await _secretStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsSecretReadFailure(exception))
        {
            return Disabled("LOCAL_SECRET_UNREADABLE", online.EntitlementVersion);
        }

        if (secretRead.Status == AccountSecretReadStatus.Missing)
        {
            return Disabled("LOCAL_SECRET_MISSING", online.EntitlementVersion);
        }

        if (secretRead.Status != AccountSecretReadStatus.Available || secretRead.Secret is null)
        {
            return Disabled("LOCAL_SECRET_UNREADABLE", online.EntitlementVersion);
        }

        var secret = secretRead.Secret;
        if (!string.Equals(secret.AccountId, association.AccountId, StringComparison.Ordinal))
        {
            return Disabled("LOCAL_SECRET_ACCOUNT_MISMATCH", online.EntitlementVersion);
        }

        var validation = await _offlineValidator.ValidateAsync(
            secret.OfflineEntitlementAssertion,
            new OfflineEntitlementValidationContext(
                association.AccountId,
                association.AssociationId,
                _installationId,
                ManagedRuntimeCapability,
                online.EntitlementVersion,
                secret.LastTrustedServerUtc,
                secret.LastTrustedServerObservationLocalUtc),
            cancellationToken).ConfigureAwait(false);

        if (!validation.IsAccepted || validation.Claims is null)
        {
            return Disabled(validation.ProductCode, online.EntitlementVersion);
        }

        return new RuntimeAccessEvaluation(
            "ENABLED",
            validation.ProductCode,
            validation.Claims.EntitlementVersion);
    }

    private static bool CanAttemptOfflineFallback(string reason)
        => string.Equals(reason, "ONLINE_ENTITLEMENT_MISSING", StringComparison.Ordinal) ||
           string.Equals(reason, "ONLINE_ENTITLEMENT_STALE", StringComparison.Ordinal);

    private static string ValidateInstallationId(string installationId)
    {
        if (string.IsNullOrWhiteSpace(installationId) ||
            installationId.Length > 256 ||
            installationId.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Installation id is missing or outside supported bounds.",
                nameof(installationId));
        }

        return installationId;
    }

    private static bool IsSecretReadFailure(Exception exception)
        => exception is IOException or UnauthorizedAccessException or InvalidDataException or CryptographicException;

    private static RuntimeAccessEvaluation Disabled(string reason, long? entitlementVersion)
        => new("DISABLED", reason, entitlementVersion);
}
