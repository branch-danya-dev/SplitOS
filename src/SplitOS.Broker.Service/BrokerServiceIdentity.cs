using System.Security.Principal;

namespace SplitOS.Broker.Service;

public static class BrokerServiceIdentity
{
    public static bool IsLocalSystem(SecurityIdentifier? sid)
        => sid is not null && sid.IsWellKnown(WellKnownSidType.LocalSystemSid);

    public static void EnsureLocalSystem()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!IsLocalSystem(identity.User))
        {
            throw new InvalidOperationException(
                "SplitOS Broker must run as LocalSystem before privileged IPC is exposed.");
        }
    }
}
