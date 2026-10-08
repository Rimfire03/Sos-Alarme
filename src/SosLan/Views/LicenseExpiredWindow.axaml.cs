using Avalonia.Controls;
using Avalonia.Interactivity;
using SosLan.Licensing;

namespace SosLan.Views;

public enum ExpiredOutcome
{
    Quit,
    Resolved,
    EnterOtherLicense
}

/// <summary>Écran « Licence expirée » : la clé stockée est conservée (une prolongation côté serveur la rend valide).</summary>
public partial class LicenseExpiredWindow : Window
{
    private readonly Func<Task<ActivationResult>> _retry = () => Task.FromResult(new ActivationResult(false, null));
    private ExpiredOutcome _outcome = ExpiredOutcome.Quit;
    private bool _busy;

    public LicenseExpiredWindow()
    {
        InitializeComponent();
    }

    public LicenseExpiredWindow(Func<Task<ActivationResult>> retry) : this()
    {
        _retry = retry;
    }

    private async void OnRetryClick(object? sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        SetBusy(true);
        MessageText.Foreground = Avalonia.Media.Brushes.Gray;
        MessageText.Text = "Vérification de la licence...";

        ActivationResult result;
        try
        {
            result = await _retry();
        }
        catch (Exception)
        {
            result = new ActivationResult(false, "Une erreur est survenue pendant la vérification de la licence.");
        }

        if (result.Success)
        {
            _outcome = ExpiredOutcome.Resolved;
            Close();
            return;
        }

        SetBusy(false);
        MessageText.Foreground = Avalonia.Media.Brushes.Red;
        MessageText.Text = result.Message ?? "";
    }

    private void OnOtherClick(object? sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        _outcome = ExpiredOutcome.EnterOtherLicense;
        Close();
    }

    private void OnQuitClick(object? sender, RoutedEventArgs e) => Close();

    private void SetBusy(bool busy)
    {
        _busy = busy;
        RetryButton.IsEnabled = !busy;
        OtherButton.IsEnabled = !busy;
    }

    public Task<ExpiredOutcome> ShowAndWaitAsync()
    {
        var tcs = new TaskCompletionSource<ExpiredOutcome>();
        Closed += (_, _) => tcs.TrySetResult(_outcome);
        Topmost = true;
        Show();
        Activate();
        return tcs.Task;
    }
}
