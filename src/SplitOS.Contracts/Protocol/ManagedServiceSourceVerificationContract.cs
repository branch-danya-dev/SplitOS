namespace SplitOS.Contracts.Protocol;

public static class ManagedServiceSourceVerificationProtocol
{
    public const string Capability = "Machine.Service.Policy.VerifySource@1";
}

public sealed record MachineServiceSourceVerifyRequest(Guid TransitionId, Guid LeaseId, long FenceToken,
    string ControlSessionKey, int ExpectedTransitionRevision);
public sealed record MachineServiceSourceVerifyResult(string Disposition, string ProductCode);
