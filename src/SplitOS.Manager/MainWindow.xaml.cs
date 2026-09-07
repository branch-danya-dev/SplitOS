using Microsoft.UI.Xaml;
using SplitOS.Runtime.Client;

namespace SplitOS.Manager;

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
            var health = await new RuntimeHealthClient("SplitOS.Manager", version).ReadAsync();
            StatusText.Text = $"Runtime: {health.Status} · PID {health.ProcessId} · Session {health.SessionId}";

            var state = await new RuntimeStateClient("SplitOS.Manager", version).ReadAsync();
            StateText.Text = $"State: {state.Status} · Managed runtime {state.ManagedRuntimeAccess} · Mode {state.OperationalMode} · Account {state.UserAssociationState}\nSchemas: machine v{state.MachineSchemaVersion} · user v{state.UserSchemaVersion} · projection v{state.ProjectionSchemaVersion}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Runtime unavailable: {ex.Message}";
            StateText.Text = string.Empty;
        }
    }
}
