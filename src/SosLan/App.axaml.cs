using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SosLan.Models;
using SosLan.Services;
using SosLan.Views;

namespace SosLan;

public partial class App : Application
{
    private readonly Guid _instanceId = Guid.NewGuid();
    private readonly UpdateService _updateService = new();

    private AppSettings _settings = new();
    private Window? _hiddenOwner;
    private TrayIcon? _trayIcon;
    private NativeMenu? _peersMenu;
    private DispatcherTimer? _peersRefreshTimer;
    private IHotkeyMonitor? _hotkeyMonitor;
    private NetworkService? _networkService;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) => Cleanup();

            // Fenêtre invisible nécessaire comme "owner" pour les boîtes de dialogue
            // modales (Paramètres, messages) ; l'application n'a pas de fenêtre principale.
            _hiddenOwner = new Window
            {
                Width = 1,
                Height = 1,
                ShowInTaskbar = false,
                Opacity = 0,
                Position = new PixelPoint(-10000, -10000)
            };
            _hiddenOwner.Show();

            StartApplication();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void StartApplication()
    {
        var isFirstRun = SettingsService.IsFirstRun;
        _settings = SettingsService.Load();

        if (isFirstRun)
        {
            // Démarrage automatique activé par défaut au tout premier lancement.
            PlatformServices.CreateStartupService().SetEnabled(true);
            SettingsService.Save(_settings);
        }

        _networkService = new NetworkService(_instanceId)
        {
            DisplayName = _settings.DisplayName
        };
        _networkService.AlarmReceived += OnAlarmReceived;
        _networkService.Start(_settings.Port);

        _hotkeyMonitor = PlatformServices.CreateHotkeyMonitor();
        _hotkeyMonitor.Triggered += OnHotkeyTriggered;
        _hotkeyMonitor.Start(_settings.HotKey, _settings.HoldDurationSeconds);

        SetupTrayIcon();

        _ = CheckForUpdatesAsync(silent: true);
    }

    private void SetupTrayIcon()
    {
        var icon = LoadAppIcon();

        _peersMenu = new NativeMenu();
        RefreshPeersMenu();

        _peersRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _peersRefreshTimer.Tick += (_, _) => RefreshPeersMenu();
        _peersRefreshTimer.Start();

        var peersMenuItem = new NativeMenuItem("Postes détectés sur le réseau") { Menu = _peersMenu };

        var settingsItem = new NativeMenuItem("Paramètres...");
        settingsItem.Click += async (_, _) => await OpenSettingsAsync();

        var checkUpdatesItem = new NativeMenuItem("Vérifier les mises à jour");
        checkUpdatesItem.Click += async (_, _) => await CheckForUpdatesAsync(silent: false);

        var quitItem = new NativeMenuItem("Quitter");
        quitItem.Click += (_, _) =>
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        };

        var menu = new NativeMenu
        {
            peersMenuItem,
            new NativeMenuItemSeparator(),
            settingsItem,
            checkUpdatesItem,
            new NativeMenuItemSeparator(),
            quitItem
        };

        _trayIcon = new TrayIcon
        {
            Icon = icon,
            ToolTipText = "SOS-LAN",
            Menu = menu,
            IsVisible = true
        };
    }

    private static WindowIcon? LoadAppIcon()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("SosLan.cloche.ico");
            if (stream != null)
            {
                return new WindowIcon(stream);
            }
        }
        catch
        {
            // Pas d'icône disponible : l'icône par défaut de l'OS sera utilisée.
        }

        return null;
    }

    private void RefreshPeersMenu()
    {
        if (_peersMenu == null)
        {
            return;
        }

        _peersMenu.Items.Clear();

        var peers = _networkService?.GetActivePeers() ?? Array.Empty<(string Name, TimeSpan LastSeenAgo)>();

        if (peers.Count == 0)
        {
            _peersMenu.Items.Add(new NativeMenuItem("Aucun poste détecté") { IsEnabled = false });
            return;
        }

        foreach (var peer in peers)
        {
            var secondsAgo = (int)peer.LastSeenAgo.TotalSeconds;
            _peersMenu.Items.Add(new NativeMenuItem($"{peer.Name} (vu il y a {secondsAgo}s)") { IsEnabled = false });
        }
    }

    private async Task OpenSettingsAsync()
    {
        var window = new SettingsWindow(_settings);
        var saved = await window.ShowDialog<bool>(_hiddenOwner!);

        if (saved && window.Result != null)
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
        Dispatcher.UIThread.Post(() => _networkService?.BroadcastAlarm(_settings.DisplayName));
    }

    private void OnAlarmReceived(string senderName)
    {
        Dispatcher.UIThread.Post(() =>
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
                    await MessageWindow.ShowInfo(_hiddenOwner!, "Mise à jour",
                        "Mise à jour indisponible : l'application ne semble pas provenir d'une installation officielle.");
                }
                break;

            case UpdateService.CheckOutcome.UpToDate:
                if (!silent)
                {
                    await MessageWindow.ShowInfo(_hiddenOwner!, "Mise à jour", "Vous possédez déjà la dernière version de SOS-LAN.");
                }
                break;

            case UpdateService.CheckOutcome.UpdateAvailable:
                var confirmed = await MessageWindow.ShowConfirm(
                    _hiddenOwner!,
                    "Mise à jour disponible",
                    $"Une nouvelle version de SOS-LAN est disponible (v{version}).\n\nInstaller la mise à jour maintenant ? L'application redémarrera automatiquement.");

                if (confirmed)
                {
                    var applied = await _updateService.DownloadAndApplyAsync();
                    if (!applied)
                    {
                        await MessageWindow.ShowInfo(_hiddenOwner!, "Mise à jour", "Échec de l'installation de la mise à jour.");
                    }
                }
                break;

            case UpdateService.CheckOutcome.Failed:
                if (!silent)
                {
                    await MessageWindow.ShowInfo(_hiddenOwner!, "Mise à jour",
                        "Échec de la vérification des mises à jour. Vérifiez votre connexion réseau et réessayez.");
                }
                break;
        }
    }

    private void Cleanup()
    {
        _peersRefreshTimer?.Stop();
        _hotkeyMonitor?.Dispose();
        _networkService?.Dispose();
        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
        }
    }
}
