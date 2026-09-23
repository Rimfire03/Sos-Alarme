using System.Windows;
using SosLan.Services;

namespace SosLan.Views;

public partial class AlarmWindow : Window
{
    private readonly AlarmSoundPlayer _soundPlayer;

    public AlarmWindow(string senderName)
    {
        InitializeComponent();
        MessageText.Text = $"Alerte en provenance de {senderName}";

        _soundPlayer = new AlarmSoundPlayer();
        _soundPlayer.Start();
    }

    private void OnAcknowledgeClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _soundPlayer.Stop();
        _soundPlayer.Dispose();
        base.OnClosed(e);
    }
}
