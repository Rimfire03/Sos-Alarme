using Avalonia.Controls;
using Avalonia.Interactivity;
using SosLan.Licensing;

namespace SosLan.Views;

/// <summary>
/// Saisie d'une clé de licence. L'activation est exécutée par la fenêtre elle-même (appel réseau asynchrone,
/// interface non bloquée) : en cas d'échec le message s'affiche et l'utilisateur peut réessayer.
/// </summary>
public partial class LicenseKeyWindow : Window
{
    private readonly Func<string, Task<ActivationResult>> _activate = _ => Task.FromResult(new ActivationResult(false, null));
    private bool _success;
    private bool _busy;

    public LicenseKeyWindow()
    {
        InitializeComponent();
    }

    public LicenseKeyWindow(string description, string cancelText, Func<string, Task<ActivationResult>> activate) : this()
    {
        _activate = activate;
        DescriptionText.Text = description;
        CancelButton.Content = cancelText;
    }

    private async void OnActivateClick(object? sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        ActivateButton.IsEnabled = false;
        KeyBox.IsEnabled = false;
        ErrorText.Text = "Vérification de la licence...";
        ErrorText.Foreground = Avalonia.Media.Brushes.Gray;

        ActivationResult result;
        try
        {
            result = await _activate(KeyBox.Text ?? "");
        }
        catch (Exception)
        {
            result = new ActivationResult(false, "Une erreur est survenue pendant l'activation de la licence.");
        }

        _busy = false;
        ActivateButton.IsEnabled = true;
        KeyBox.IsEnabled = true;

        if (result.Success)
        {
            _success = true;
            Close();
            return;
        }

        ErrorText.Foreground = Avalonia.Media.Brushes.Red;
        ErrorText.Text = result.Message ?? "";
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
