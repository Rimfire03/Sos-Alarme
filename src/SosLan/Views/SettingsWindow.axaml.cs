using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SosLan.Licensing;
using SosLan.Models;
using SosLan.Services;

namespace SosLan.Views;

public partial class SettingsWindow : Window
{
    private AppKey _selectedKey;
    private bool _capturingKey;

    private readonly LicenseManager? _license;

    public AppSettings? Result { get; private set; }

    /// <summary>Vrai si la licence a été supprimée depuis cette fenêtre : l'application doit se fermer.</summary>
    public bool LicenseRemoved { get; private set; }

    public SettingsWindow() : this(new AppSettings(), null)
    {
        // Constructeur sans paramètre requis par le chargeur XAML Avalonia (prévisualiseur).
    }

    public SettingsWindow(AppSettings currentSettings, LicenseManager? license)
    {
        InitializeComponent();

        _license = license;
        RefreshLicenseSection();

        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);

        _selectedKey = currentSettings.HotKey;
        DisplayNameBox.Text = currentSettings.DisplayName;
        HotKeyText.Text = _selectedKey.ToString();
        DurationBox.Text = currentSettings.HoldDurationSeconds.ToString();
        PortBox.Text = currentSettings.Port.ToString();
        MaxAlertDurationBox.Text = currentSettings.MaxAlertDurationSeconds.ToString();
        StartWithWindowsCheckBox.IsChecked = PlatformServices.CreateStartupService().IsEnabled();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"SOS-LAN v{version?.Major}.{version?.Minor}.{version?.Build} — © Tomline Prod&Co";
    }

    private void RefreshLicenseSection()
    {
        LicenseErrorText.Text = "";
        LicenseOfflineText.Text = "";
        LicenseDetailText.Text = "";

        if (_license == null || _license.IsFree)
        {
            LicenseStatusText.Text = "Licence gratuite";
            LicenseButtons.IsVisible = false;
            return;
        }

        LicenseButtons.IsVisible = true;
        var info = _license.CurrentLicense;
        LicenseStatusText.Text = string.IsNullOrWhiteSpace(info?.CustomerName)
            ? "Licence active"
            : $"Licence accordée à {info.CustomerName}";

        // Démo : date et jours restants (couleur d'alerte à 2 jours ou moins) ; expiring : date seule ;
        // perpetual / free : pas de date. La clé n'est jamais affichée.
        LicenseDetailText.Foreground = Avalonia.Media.Brushes.Gray;
        if (info is { IsTimeLimited: true, ExpiresAt: { } expiresAt })
        {
            var date = expiresAt.LocalDateTime.ToString("dd/MM/yyyy");
            if (info.IsDemo)
            {
                var daysLeft = Math.Max(0, (int)Math.Ceiling((expiresAt - DateTimeOffset.Now).TotalDays));
                LicenseDetailText.Text = $"Démo — expire le {date} (dans {daysLeft} jour{(daysLeft > 1 ? "s" : "")})";
                if (daysLeft <= 2)
                {
                    LicenseDetailText.Foreground = Avalonia.Media.Brushes.Red;
                }
            }
            else
            {
                LicenseDetailText.Text = $"Expire le {date}";
            }
        }

        if (_license.IsOffline && _license.GraceUntil is { } graceUntil)
        {
            LicenseOfflineText.Text = $"Mode hors-ligne : serveur de licences injoignable, utilisation autorisée jusqu'au {graceUntil.LocalDateTime:dd/MM/yyyy}.";
        }
    }

    private async void OnChangeLicenseClick(object? sender, RoutedEventArgs e)
    {
        if (_license == null)
        {
            return;
        }

        var window = new LicenseKeyWindow(
            "Saisissez la nouvelle clé de licence. L'ancienne licence est conservée tant que la nouvelle n'est pas activée.",
            "Annuler",
            _license.ChangeAsync);

        if (await window.ShowAndWaitAsync(this))
        {
            RefreshLicenseSection();
        }
    }

    private async void OnRemoveLicenseClick(object? sender, RoutedEventArgs e)
    {
        if (_license == null)
        {
            return;
        }

        LicenseErrorText.Text = "";
        var confirmed = await MessageWindow.ShowConfirm("Supprimer la licence",
            "Supprimer la licence de ce poste ? SOS-LAN se fermera et ne pourra plus être utilisé sans nouvelle licence.");
        if (!confirmed)
        {
            return;
        }

        var removed = await _license.RemoveAsync(deleteEvenIfUnreachable: false);
        if (!removed)
        {
            var force = await MessageWindow.ShowConfirm("Serveur injoignable",
                "Le serveur de licences est injoignable : le poste ne pourra pas être libéré côté serveur. Supprimer quand même la licence de ce poste ?");
            if (!force)
            {
                return;
            }

            await _license.RemoveAsync(deleteEvenIfUnreachable: true);
        }

        LicenseRemoved = true;
        Close();
    }

    private void OnChangeKeyClick(object? sender, RoutedEventArgs e)
    {
        if (_capturingKey)
        {
            StopCapture();
            return;
        }

        _capturingKey = true;
        ErrorText.Text = string.Empty;
        HotKeyText.Text = "Appuyez sur la touche souhaitée...";
        ChangeKeyButton.Content = "Annuler";
    }

    // Écouté en phase "tunnel" au niveau de la fenêtre : pendant la capture, toutes les
    // touches (Tab, Espace, Entrée...) sont interceptées avant que l'interface ne les
    // traite (changement de focus, clic sur un bouton, validation du formulaire).
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_capturingKey)
        {
            return;
        }

        e.Handled = true;

        var mapped = AvaloniaKeyMapping.FromAvaloniaKey(e.Key);
        if (mapped == null)
        {
            ErrorText.Text = "Cette touche n'est pas prise en charge (lettres, chiffres, F1-F12, flèches, Espace, Tab, Échap, etc.). Essayez-en une autre ou cliquez sur Annuler.";
            return;
        }

        _selectedKey = mapped.Value;
        StopCapture();
    }

    private void StopCapture()
    {
        _capturingKey = false;
        HotKeyText.Text = _selectedKey.ToString();
        ChangeKeyButton.Content = "Modifier";
        ErrorText.Text = string.Empty;
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;

        var displayName = DisplayNameBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(displayName))
        {
            ErrorText.Text = "Le nom affiché ne peut pas être vide.";
            return;
        }

        if (!int.TryParse(DurationBox.Text, out var duration) || duration <= 0)
        {
            ErrorText.Text = "La durée doit être un nombre entier positif de secondes.";
            return;
        }

        if (!int.TryParse(PortBox.Text, out var port) || port is < 1024 or > 65535)
        {
            ErrorText.Text = "Le port doit être compris entre 1024 et 65535.";
            return;
        }

        if (!int.TryParse(MaxAlertDurationBox.Text, out var maxAlertDuration) || maxAlertDuration <= 0)
        {
            ErrorText.Text = "La durée maximale du signal sonore doit être un nombre entier positif de secondes.";
            return;
        }

        PlatformServices.CreateStartupService().SetEnabled(StartWithWindowsCheckBox.IsChecked == true);

        Result = new AppSettings
        {
            DisplayName = displayName,
            HotKey = _selectedKey,
            HoldDurationSeconds = duration,
            Port = port,
            MaxAlertDurationSeconds = maxAlertDuration
        };

        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
