namespace SplitOS.Contracts.Protocol;

public static class ManagedServiceRollbackProtocol
{
    public const string Capability = "Machine.Service.Policy.Rollback@1";
}

// Service targets and restoration values are derived exclusively from durable pre-state.
public sealed record MachineServicePolicyRollbackRequest(
    Guid TransitionId,
    Guid ActionId,
    Guid LeaseId,
    long FenceToken,
    string ControlSessionKey,
    int ExpectedActionRevision);

public sealed record MachineServicePolicyRollbackResult(string Disposition, string ProductCode);
