using System.Media;

namespace SosLan.Services.Windows;

/// <summary>
/// Joue le son d'alerte en boucle via System.Media.SoundPlayer (API Windows native).
/// </summary>
public class WindowsAlarmSoundPlayer : IAlarmSoundPlayer
{
    private readonly SoundPlayer _player;
    private readonly MemoryStream _stream;

    public WindowsAlarmSoundPlayer()
    {
        _stream = new MemoryStream(AlertToneWavGenerator.Generate());
        _player = new SoundPlayer(_stream);
        _player.Load();
    }

    public void Start() => _player.PlayLooping();

    public void Stop() => _player.Stop();

    public void Dispose()
    {
        _player.Dispose();
        _stream.Dispose();
    }
}
