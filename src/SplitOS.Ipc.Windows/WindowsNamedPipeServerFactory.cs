using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace SplitOS.Ipc.Windows;

public static class WindowsNamedPipeServerFactory
{
    public static NamedPipeServerStream CreateCurrentUserOnly(string pipeName, int maxInstances = 8)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);

        return new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    }

    public static NamedPipeServerStream CreateBrokerForSession(
        string pipeName,
        uint sessionId,
        int maxInstances = 8)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);

        var userSid = WindowsSessionInfo.GetLoggedOnUserSid(sessionId);
        var security = BuildBrokerPipeSecurity(userSid);

        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            maxInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            security);
    }

    public static PipeSecurity BuildBrokerPipeSecurity(SecurityIdentifier sessionUserSid)
    {
        ArgumentNullException.ThrowIfNull(sessionUserSid);

        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        var localSystem = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var network = new SecurityIdentifier(WellKnownSidType.NetworkSid, null);

        security.AddAccessRule(new PipeAccessRule(
            network,
            PipeAccessRights.FullControl,
            AccessControlType.Deny));

        security.AddAccessRule(new PipeAccessRule(
            localSystem,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        security.AddAccessRule(new PipeAccessRule(
            sessionUserSid,
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        return security;
    }
}
