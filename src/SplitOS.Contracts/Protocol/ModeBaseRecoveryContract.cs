namespace SplitOS.Contracts.Protocol;

public static class ModeBaseRecoveryProtocol
{
    public const string Capability = "Machine.Mode.BaseRecovery.Execute@1";
}

public sealed record MachineModeBaseRecoveryRequest(Guid TransitionId, Guid LeaseId, long FenceToken,
    string ControlSessionKey, int ExpectedTransitionRevision);
public sealed record MachineModeBaseRecoveryResult(string Disposition, string ProductCode);
