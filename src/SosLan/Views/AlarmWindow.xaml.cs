using System.Windows;
using System.Windows.Threading;
using SosLan.Services;

namespace SosLan.Views;

public partial class AlarmWindow : Window
{
    private readonly AlarmSoundPlayer _soundPlayer;
    private readonly DispatcherTimer _maxDurationTimer;

    public AlarmWindow(string senderName, int maxDurationSeconds)
    {
        InitializeComponent();
        MessageText.Text = $"Alerte en provenance de {senderName}";

        _soundPlayer = new AlarmSoundPlayer();
        _soundPlayer.Start();

        _maxDurationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Max(1, maxDurationSeconds))
        };
        _maxDurationTimer.Tick += (_, _) => Close();
        _maxDurationTimer.Start();
    }

    private void OnAcknowledgeClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _maxDurationTimer.Stop();
        _soundPlayer.Stop();
        _soundPlayer.Dispose();
        base.OnClosed(e);
    }
}
