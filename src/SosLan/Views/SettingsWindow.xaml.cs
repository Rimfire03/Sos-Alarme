using System.Windows;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using SosLan.Models;
using Forms = System.Windows.Forms;

namespace SosLan.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _workingCopy;
    private Forms.Keys _selectedKey;

    public AppSettings? Result { get; private set; }

    public SettingsWindow(AppSettings currentSettings)
    {
        InitializeComponent();

        _workingCopy = new AppSettings
        {
            DisplayName = currentSettings.DisplayName,
            HotKey = currentSettings.HotKey,
            HoldDurationSeconds = currentSettings.HoldDurationSeconds,
            Port = currentSettings.Port
        };

        _selectedKey = _workingCopy.HotKey;
        DisplayNameBox.Text = _workingCopy.DisplayName;
        HotKeyBox.Text = _selectedKey.ToString();
        DurationBox.Text = _workingCopy.HoldDurationSeconds.ToString();
        PortBox.Text = _workingCopy.Port.ToString();
    }

    private void OnHotKeyPreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var vkCode = KeyInterop.VirtualKeyFromKey(key);
        _selectedKey = (Forms.Keys)vkCode;
        HotKeyBox.Text = _selectedKey.ToString();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;

        var displayName = DisplayNameBox.Text.Trim();
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

        Result = new AppSettings
        {
            DisplayName = displayName,
            HotKey = _selectedKey,
            HoldDurationSeconds = duration,
            Port = port
        };

        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
