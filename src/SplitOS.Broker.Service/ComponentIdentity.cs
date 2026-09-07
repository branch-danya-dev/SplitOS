namespace SplitOS.Broker.Service;

internal static class ComponentIdentity
{
    public const string Name = "SplitOS.Broker.Service";

    public static string Version { get; } =
        typeof(ComponentIdentity).Assembly.GetName().Version?.ToString() ?? "0.0.0";
}
