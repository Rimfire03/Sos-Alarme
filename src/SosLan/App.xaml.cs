using System.Drawing;
using System.Reflection;
using System.Windows;
using Application = System.Windows.Application;
using SosLan.Models;
using SosLan.Services;
using SosLan.Views;
using Forms = System.Windows.Forms;

namespace SosLan;

public partial class App : Application
{
    private readonly Guid _instanceId = Guid.NewGuid();

    private AppSettings _settings = new();
    private Forms.NotifyIcon? _notifyIcon;
    private HotkeyMonitor? _hotkeyMonitor;
    private NetworkService? _networkService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settings = SettingsService.Load();

        _networkService = new NetworkService(_instanceId);
        _networkService.AlarmReceived += OnAlarmReceived;
        _networkService.Start(_settings.Port);

        _hotkeyMonitor = new HotkeyMonitor();
        _hotkeyMonitor.Triggered += OnHotkeyTriggered;
        _hotkeyMonitor.Start(_settings.HotKey, _settings.HoldDurationSeconds);

        SetupNotifyIcon();
    }

    private void SetupNotifyIcon()
    {
        Icon icon;
        try
        {
            icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location) ?? SystemIcons.Application;
        }
        catch
        {
            icon = SystemIcons.Application;
        }

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Paramètres...", null, (_, _) => OpenSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quitter", null, (_, _) => Shutdown());

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = icon,
            Visible = true,
            Text = "SosLan",
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick += (_, _) => OpenSettings();
    }

    private void OpenSettings()
    {
        var window = new SettingsWindow(_settings)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };

        if (window.ShowDialog() == true && window.Result != null)
        {
            _settings = window.Result;
            SettingsService.Save(_settings);

            _hotkeyMonitor?.UpdateTarget(_settings.HotKey, _settings.HoldDurationSeconds);
            _networkService?.Start(_settings.Port);
        }
    }

    private void OnHotkeyTriggered()
    {
        Dispatcher.Invoke(() =>
        {
            _networkService?.BroadcastAlarm(_settings.DisplayName);
            _notifyIcon?.ShowBalloonTip(3000, "SOS-LAN", "Alerte envoyée à tout le réseau local.", Forms.ToolTipIcon.Info);
        });
    }

    private void OnAlarmReceived(string senderName)
    {
        Dispatcher.Invoke(() =>
        {
            var alarmWindow = new AlarmWindow(senderName);
            alarmWindow.Show();
            alarmWindow.Activate();
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyMonitor?.Dispose();
        _networkService?.Dispose();
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }

        base.OnExit(e);
    }
}
