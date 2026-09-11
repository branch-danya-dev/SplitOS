namespace SplitOS.Contracts.Protocol;

public static class ModeBasePolicyProtocol
{
    public const string Capability = "Machine.Mode.BasePolicy.Read@1";
}
public sealed record MachineModeBasePolicyRequest(int SourceModeRevision, string ControlSessionKey);
public sealed record MachineModeBasePolicyResult(string Disposition, string ProductCode,
    IReadOnlyList<ManagedServicePolicyEntry> Entries, string? DesiredDigest);
