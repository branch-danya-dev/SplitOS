using Microsoft.UI.Xaml;
using SplitOS.Runtime.Client;

namespace SplitOS.Manager;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var arguments = Environment.GetCommandLineArgs();
        if (arguments.Any(static argument => string.Equals(argument, "--health-probe", StringComparison.OrdinalIgnoreCase)))
        {
            _ = RunHealthProbeAndExitAsync();
            return;
        }

        if (arguments.Any(static argument => string.Equals(argument, "--state-probe", StringComparison.OrdinalIgnoreCase)))
        {
            _ = RunStateProbeAndExitAsync();
            return;
        }

        _window = new MainWindow();
        _window.Activate();
    }

    private static async Task RunHealthProbeAndExitAsync()
    {
        try
        {
            var version = typeof(App).Assembly.GetName().Version?.ToString() ?? "0.0.0";
            var health = await new RuntimeHealthClient("SplitOS.Manager", version).ReadAsync().ConfigureAwait(false);
            Environment.Exit(string.Equals(health.Status, "HEALTHY", StringComparison.Ordinal) ? 0 : 3);
        }
        catch
        {
            Environment.Exit(2);
        }
    }

    private static async Task RunStateProbeAndExitAsync()
    {
        try
        {
            var version = typeof(App).Assembly.GetName().Version?.ToString() ?? "0.0.0";
            var state = await new RuntimeStateClient("SplitOS.Manager", version).ReadAsync().ConfigureAwait(false);
            var expectedFree = string.Equals(state.Status, "READY", StringComparison.Ordinal)
                && string.Equals(state.ManagedRuntimeAccess, "DISABLED", StringComparison.Ordinal)
                && string.Equals(state.OperationalMode, "NONE", StringComparison.Ordinal);
            Environment.Exit(expectedFree ? 0 : 4);
        }
        catch
        {
            Environment.Exit(2);
        }
    }
}
