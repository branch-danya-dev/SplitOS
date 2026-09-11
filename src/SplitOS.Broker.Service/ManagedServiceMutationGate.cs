namespace SplitOS.Broker.Service;

// One Broker process must finish an old native call before a new fenced recovery observes state.
internal static class ManagedServiceMutationGate
{
    internal static readonly SemaphoreSlim Instance = new(1, 1);
}
