using Microsoft.UI.Xaml;
using SplitOS.Runtime.Client;

namespace SplitOS.GameLauncher;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var version = typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "0.0.0";
            var client = new RuntimeHealthClient("SplitOS.GameLauncher", version);
            var health = await client.ReadAsync();
            StatusText.Text = $"Runtime: {health.Status} · PID {health.ProcessId} · Session {health.SessionId}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Runtime unavailable: {ex.Message}";
        }
    }
}
