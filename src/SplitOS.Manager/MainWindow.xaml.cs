using Microsoft.UI.Xaml;
using SplitOS.Runtime.Client;

namespace SplitOS.Manager;

public sealed partial class MainWindow : Window
{
    private readonly string _version;

    public MainWindow()
    {
        InitializeComponent();
        _version = typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await RefreshRuntimeStateAsync();
    }

    private async void OnSignInClick(object sender, RoutedEventArgs e)
    {
        SignInButton.IsEnabled = false;
        AuthText.Text = "Opening secure sign-in…";
        try
        {
            var result = await new RuntimeAuthClient("SplitOS.Manager", _version).StartAsync();
            AuthText.Text = result.ProductCode switch
            {
                "AUTH_NOT_CONFIGURED" => "Sign-in is not enabled for this release yet.",
                _ => $"Sign-in: {result.Disposition} · {result.ProductCode}"
            };

            await RefreshRuntimeStateAsync();
        }
        catch (Exception ex)
        {
            AuthText.Text = $"Sign-in unavailable: {ex.Message}";
        }
        finally
        {
            SignInButton.IsEnabled = true;
        }
    }

    private async Task RefreshRuntimeStateAsync()
    {
        try
        {
            var health = await new RuntimeHealthClient("SplitOS.Manager", _version).ReadAsync();
            StatusText.Text = $"Runtime: {health.Status} · PID {health.ProcessId} · Session {health.SessionId}";

            var state = await new RuntimeStateClient("SplitOS.Manager", _version).ReadAsync();
            StateText.Text = $"State: {state.Status} · Managed runtime {state.ManagedRuntimeAccess} · Mode {state.OperationalMode} · Account {state.UserAssociationState}\nSchemas: machine v{state.MachineSchemaVersion} · user v{state.UserSchemaVersion} · projection v{state.ProjectionSchemaVersion}";

            var signInRelevant = string.Equals(state.UserAssociationState, "UNASSOCIATED", StringComparison.Ordinal) ||
                                 string.Equals(state.UserAssociationState, "REAUTH_REQUIRED", StringComparison.Ordinal);
            SignInButton.Visibility = signInRelevant ? Visibility.Visible : Visibility.Collapsed;
            SignInButton.Content = string.Equals(state.UserAssociationState, "REAUTH_REQUIRED", StringComparison.Ordinal)
                ? "Sign in again"
                : "Sign in to SplitOS";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Runtime unavailable: {ex.Message}";
            StateText.Text = string.Empty;
            SignInButton.Visibility = Visibility.Collapsed;
        }
    }
}
