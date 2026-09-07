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
        if (Environment.GetCommandLineArgs().Any(
                static argument => string.Equals(argument, "--health-probe", StringComparison.OrdinalIgnoreCase)))
        {
            _ = RunHealthProbeAndExitAsync();
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
            var client = new RuntimeHealthClient("SplitOS.Manager", version);
            var health = await client.ReadAsync().ConfigureAwait(false);
            Environment.Exit(string.Equals(health.Status, "HEALTHY", StringComparison.Ordinal) ? 0 : 3);
        }
        catch
        {
            Environment.Exit(2);
        }
    }
}
