using Avalonia.Controls;
using Avalonia.Interactivity;
using SosLan.Licensing;

namespace SosLan.Views;

/// <summary>
/// Saisie d'une clé de licence. L'activation est exécutée par la fenêtre elle-même (appel réseau asynchrone,
/// interface non bloquée) : en cas d'échec le message s'affiche et l'utilisateur peut réessayer.
/// Le bouton « Demander une démo » n'apparaît que si une action de démo est fournie.
/// </summary>
public partial class LicenseKeyWindow : Window
{
    private readonly Func<string, Task<ActivationResult>> _activate = _ => Task.FromResult(new ActivationResult(false, null));
    private readonly Func<Task<ActivationResult>>? _requestDemo;
    private bool _success;
    private bool _busy;

    public LicenseKeyWindow()
    {
        InitializeComponent();
    }

    public LicenseKeyWindow(string description, string cancelText, Func<string, Task<ActivationResult>> activate,
        Func<Task<ActivationResult>>? requestDemo = null) : this()
    {
        _activate = activate;
        _requestDemo = requestDemo;
        DescriptionText.Text = description;
        CancelButton.Content = cancelText;
        DemoButton.IsVisible = requestDemo != null;
    }

    private async void OnActivateClick(object? sender, RoutedEventArgs e) =>
        await RunAsync(() => _activate(KeyBox.Text ?? ""));

    private async void OnDemoClick(object? sender, RoutedEventArgs e)
    {
        if (_requestDemo != null)
        {
            await RunAsync(_requestDemo);
        }
    }

    private async Task RunAsync(Func<Task<ActivationResult>> action)
    {
        if (_busy)
        {
            return;
        }

        SetBusy(true);
        ErrorText.Text = "Vérification de la licence...";
        ErrorText.Foreground = Avalonia.Media.Brushes.Gray;

        ActivationResult result;
        try
        {
            result = await action();
        }
        catch (Exception)
        {
            result = new ActivationResult(false, "Une erreur est survenue pendant l'activation de la licence.");
        }

        if (result.Success)
        {
            if (!string.IsNullOrEmpty(result.Message))
            {
                await MessageWindow.ShowInfo("Licence SOS-LAN", result.Message);
            }

            _success = true;
            Close();
            return;
        }

        SetBusy(false);
        ErrorText.Foreground = Avalonia.Media.Brushes.Red;
        ErrorText.Text = result.Message ?? "";
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ActivateButton.IsEnabled = !busy;
        DemoButton.IsEnabled = !busy;
        KeyBox.IsEnabled = !busy;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();

    /// <summary>Affiche la fenêtre et renvoie true si une licence a été activée.</summary>
    public Task<bool> ShowAndWaitAsync(Window? owner = null)
    {
        var tcs = new TaskCompletionSource<bool>();
        Closed += (_, _) => tcs.TrySetResult(_success);

        if (owner != null)
        {
            ShowDialog(owner);
        }
        else
        {
            Topmost = true;
            Show();
            Activate();
        }

        return tcs.Task;
    }
}
