namespace SosLan.Services;

/// <summary>
/// Joue ou arrête le signal sonore d'alerte en boucle. Une implémentation par plateforme.
/// </summary>
public interface IAlarmSoundPlayer : IDisposable
{
    void Start();

    void Stop();
}
