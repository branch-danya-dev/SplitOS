namespace SplitOS.Ipc;

public static class NamedPipeNames
{
    public static string RuntimeForSession(int sessionId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sessionId);
        return $"SplitOS.Runtime.v1.S{sessionId}";
    }

    public static string BrokerForSession(int sessionId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sessionId);
        return $"SplitOS.Broker.v1.S{sessionId}";
    }
}
