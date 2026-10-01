using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SosLan.Services;

namespace SosLan.Views;

public partial class AlarmWindow : Window
{
    private readonly IAlarmSoundPlayer _soundPlayer;
    private readonly DispatcherTimer _maxDurationTimer;

    public AlarmWindow() : this("", 60)
    {
        // Constructeur sans paramètre requis par le chargeur XAML Avalonia (prévisualiseur).
    }

    public AlarmWindow(string senderName, int maxDurationSeconds)
    {
        InitializeComponent();
        MessageText.Text = $"Alerte en provenance de {senderName}";
        TriggeredAtText.Text = $"Déclenchée le {DateTime.Now:dd/MM/yyyy à HH:mm:ss}";

        _soundPlayer = PlatformServices.CreateAlarmSoundPlayer();
        _soundPlayer.Start();

        _maxDurationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Max(1, maxDurationSeconds))
        };
        _maxDurationTimer.Tick += (_, _) =>
        {
            _soundPlayer.Stop();
            _maxDurationTimer.Stop();
        };
        _maxDurationTimer.Start();

        Closed += (_, _) =>
        {
            _maxDurationTimer.Stop();
            _soundPlayer.Stop();
            _soundPlayer.Dispose();
        };
    }

    private void OnAcknowledgeClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
