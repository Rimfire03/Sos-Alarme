using System.Drawing;
using System.Reflection;
using System.Windows;
using Application = System.Windows.Application;
using SosLan.Models;
using SosLan.Services;
using SosLan.Views;
using Velopack;
using Forms = System.Windows.Forms;

namespace SosLan;

public partial class App : Application
{
    private readonly Guid _instanceId = Guid.NewGuid();
    private readonly UpdateService _updateService = new();

    private AppSettings _settings = new();
    private Forms.NotifyIcon? _notifyIcon;
    private HotkeyMonitor? _hotkeyMonitor;
    private NetworkService? _networkService;

    [STAThread]
    public static void Main(string[] args)
    {
        // Doit s'exécuter avant tout le reste : gère les évènements d'installation/désinstallation
        // Velopack (création de raccourcis, etc.) lors du premier lancement post-installation.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var isFirstRun = SettingsService.IsFirstRun;
        _settings = SettingsService.Load();

        if (isFirstRun)
        {
            // Démarrage automatique avec Windows activé par défaut au tout premier lancement.
            StartupService.SetEnabled(true);
            SettingsService.Save(_settings);
        }

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

        _ = CheckForUpdatesAsync(silent: true);
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
        menu.Items.Add("Vérifier les mises à jour", null, async (_, _) => await CheckForUpdatesAsync(silent: false));
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
            var alarmWindow = new AlarmWindow(senderName, _settings.MaxAlertDurationSeconds);
            alarmWindow.Show();
            alarmWindow.Activate();
        });
    }

    private async Task CheckForUpdatesAsync(bool silent)
    {
        var (outcome, version) = await _updateService.CheckAsync();

        switch (outcome)
        {
            case UpdateService.CheckOutcome.NotInstalled:
                if (!silent)
                {
                    System.Windows.MessageBox.Show(
                        "Mise à jour indisponible : l'application ne semble pas provenir d'une installation officielle (SosLan-win.msi).",
                        "Mise à jour",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                break;

            case UpdateService.CheckOutcome.UpToDate:
                if (!silent)
                {
                    System.Windows.MessageBox.Show(
                        "Vous possédez déjà la dernière version de SOS-LAN.",
                        "Mise à jour",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                break;

            case UpdateService.CheckOutcome.UpdateAvailable:
                var result = System.Windows.MessageBox.Show(
                    $"Une nouvelle version de SOS-LAN est disponible (v{version}).\n\nInstaller la mise à jour maintenant ? L'application redémarrera automatiquement.",
                    "Mise à jour disponible",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    _notifyIcon?.ShowBalloonTip(3000, "SOS-LAN", "Téléchargement de la mise à jour en cours...", Forms.ToolTipIcon.Info);
                    var applied = await _updateService.DownloadAndApplyAsync();
                    if (!applied)
                    {
                        _notifyIcon?.ShowBalloonTip(3000, "SOS-LAN", "Échec de l'installation de la mise à jour.", Forms.ToolTipIcon.Error);
                    }
                }
                break;

            case UpdateService.CheckOutcome.Failed:
                if (!silent)
                {
                    System.Windows.MessageBox.Show(
                        "Échec de la vérification des mises à jour. Vérifiez votre connexion réseau et réessayez.",
                        "Mise à jour",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                break;
        }
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
