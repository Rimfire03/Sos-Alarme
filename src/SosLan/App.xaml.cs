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

        _networkService = new NetworkService(_instanceId)
        {
            DisplayName = _settings.DisplayName
        };
        _networkService.AlarmReceived += OnAlarmReceived;
        _networkService.Start(_settings.Port);

        _hotkeyMonitor = new HotkeyMonitor();
        _hotkeyMonitor.Triggered += OnHotkeyTriggered;
        _hotkeyMonitor.Start(_settings.HotKey, _settings.HoldDurationSeconds);

        SetupNotifyIcon();
    }

    private void SetupNotifyIcon()
    {
        var icon = LoadAppIcon();

        var menu = new Forms.ContextMenuStrip();

        var peersMenuItem = new Forms.ToolStripMenuItem("Postes détectés sur le réseau");
        peersMenuItem.DropDownOpening += (_, _) => RefreshPeersMenu(peersMenuItem);
        menu.Items.Add(peersMenuItem);

        menu.Items.Add(new Forms.ToolStripSeparator());
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

    private static Icon LoadAppIcon()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("SosLan.cloche.ico");
            if (stream != null)
            {
                return new Icon(stream);
            }
        }
        catch
        {
            // Retombe sur l'icône associée à l'exécutable, puis l'icône système par défaut.
        }

        try
        {
            return Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location) ?? SystemIcons.Application;
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    private void RefreshPeersMenu(Forms.ToolStripMenuItem parentItem)
    {
        parentItem.DropDownItems.Clear();

        var peers = _networkService?.GetActivePeers() ?? Array.Empty<(string Name, TimeSpan LastSeenAgo)>();

        if (peers.Count == 0)
        {
            parentItem.DropDownItems.Add(new Forms.ToolStripMenuItem("Aucun poste détecté") { Enabled = false });
            return;
        }

        foreach (var peer in peers)
        {
            var secondsAgo = (int)peer.LastSeenAgo.TotalSeconds;
            parentItem.DropDownItems.Add(new Forms.ToolStripMenuItem($"{peer.Name} (vu il y a {secondsAgo}s)") { Enabled = false });
        }
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
            if (_networkService != null)
            {
                _networkService.DisplayName = _settings.DisplayName;
                _networkService.Start(_settings.Port);
            }
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
