using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SosLan.Models;
using SosLan.Services;

namespace SosLan.Views;

public partial class SettingsWindow : Window
{
    private AppKey _selectedKey;
    private bool _capturingKey;

    public AppSettings? Result { get; private set; }

    public SettingsWindow() : this(new AppSettings())
    {
        // Constructeur sans paramètre requis par le chargeur XAML Avalonia (prévisualiseur).
    }

    public SettingsWindow(AppSettings currentSettings)
    {
        InitializeComponent();

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
