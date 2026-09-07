namespace SplitOS.RuntimeHost;

internal static class ComponentIdentity
{
    public const string Name = "SplitOS.RuntimeHost";

    public static string Version { get; } =
        typeof(ComponentIdentity).Assembly.GetName().Version?.ToString() ?? "0.0.0";
}
