namespace SplitOS.RuntimeHost.ProductIdentity;

public sealed record OnlineEntitlementEvidence(
    string AssociationId,
    SplitOSEntitlementSnapshot Entitlement,
    DateTimeOffset ObservedLocalUtc);

public enum OnlineEntitlementPublishDisposition
{
    Applied,
    Refreshed,
    StaleRejected
}

public sealed record OnlineEntitlementPublishResult(
    OnlineEntitlementPublishDisposition Disposition,
    OnlineEntitlementEvidence? Current);

/// <summary>
/// RuntimeHost-owned, process-memory evidence from an authenticated product API response.
/// This state is intentionally not durable authority and disappears when RuntimeHost exits.
/// </summary>
public sealed class OnlineEntitlementEvidenceState
{
    private readonly object _gate = new();
    private OnlineEntitlementEvidence? _current;

    public OnlineEntitlementEvidence? Read()
    {
        lock (_gate)
        {
            return _current;
        }
    }

    public OnlineEntitlementPublishResult Publish(
        string associationId,
        SplitOSEntitlementSnapshot entitlement,
        DateTimeOffset observedLocalUtc)
    {
        if (string.IsNullOrWhiteSpace(associationId) || !Guid.TryParse(associationId, out _))
        {
            throw new ArgumentException("Association id must be a non-empty GUID value.", nameof(associationId));
        }

        ArgumentNullException.ThrowIfNull(entitlement);
        if (string.IsNullOrWhiteSpace(entitlement.AccountId))
        {
            throw new ArgumentException("Entitlement account id is required.", nameof(entitlement));
        }

        if (entitlement.EntitlementVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entitlement), "Entitlement version cannot be negative.");
        }

        if (observedLocalUtc == default)
        {
            throw new ArgumentException("Local observation timestamp is required.", nameof(observedLocalUtc));
        }

        var candidate = new OnlineEntitlementEvidence(associationId, entitlement, observedLocalUtc);
        lock (_gate)
        {
            if (_current is not null &&
                string.Equals(_current.AssociationId, associationId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(_current.Entitlement.AccountId, entitlement.AccountId, StringComparison.Ordinal) &&
                entitlement.EntitlementVersion < _current.Entitlement.EntitlementVersion)
            {
                return new OnlineEntitlementPublishResult(
                    OnlineEntitlementPublishDisposition.StaleRejected,
                    _current);
            }

            var disposition = _current is not null &&
                              string.Equals(_current.AssociationId, associationId, StringComparison.OrdinalIgnoreCase) &&
                              string.Equals(_current.Entitlement.AccountId, entitlement.AccountId, StringComparison.Ordinal)
                ? OnlineEntitlementPublishDisposition.Refreshed
                : OnlineEntitlementPublishDisposition.Applied;

            _current = candidate;
            return new OnlineEntitlementPublishResult(disposition, _current);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _current = null;
        }
    }

    public bool ClearIfBoundTo(string associationId, string accountId)
    {
        if (string.IsNullOrWhiteSpace(associationId)) throw new ArgumentException("Association id is required.", nameof(associationId));
        if (string.IsNullOrWhiteSpace(accountId)) throw new ArgumentException("Account id is required.", nameof(accountId));

        lock (_gate)
        {
            if (_current is null ||
                !string.Equals(_current.AssociationId, associationId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(_current.Entitlement.AccountId, accountId, StringComparison.Ordinal))
            {
                return false;
            }

            _current = null;
            return true;
        }
    }
}
