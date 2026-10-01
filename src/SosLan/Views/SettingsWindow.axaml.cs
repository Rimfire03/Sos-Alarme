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

    public AppSettings? Result { get; private set; }

    public SettingsWindow() : this(new AppSettings())
    {
        // Constructeur sans paramètre requis par le chargeur XAML Avalonia (prévisualiseur).
    }

    public SettingsWindow(AppSettings currentSettings)
    {
        InitializeComponent();

        _selectedKey = currentSettings.HotKey;
        DisplayNameBox.Text = currentSettings.DisplayName;
        HotKeyBox.Text = _selectedKey.ToString();
        DurationBox.Text = currentSettings.HoldDurationSeconds.ToString();
        PortBox.Text = currentSettings.Port.ToString();
        MaxAlertDurationBox.Text = currentSettings.MaxAlertDurationSeconds.ToString();
        StartWithWindowsCheckBox.IsChecked = PlatformServices.CreateStartupService().IsEnabled();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"SOS-LAN v{version?.Major}.{version?.Minor}.{version?.Build} — © Tomline Prod&Co";
    }

    private void OnHotKeyKeyDown(object? sender, KeyEventArgs e)
    {
        e.Handled = true;

        var mapped = AvaloniaKeyMapping.FromAvaloniaKey(e.Key);
        if (mapped == null)
        {
            return;
        }

        _selectedKey = mapped.Value;
        HotKeyBox.Text = _selectedKey.ToString();
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
