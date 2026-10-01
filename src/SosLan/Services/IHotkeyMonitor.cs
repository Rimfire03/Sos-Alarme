using SosLan.Models;

namespace SosLan.Services;

/// <summary>
/// Surveille en permanence l'état d'une touche au niveau du système d'exploitation,
/// quel que soit le programme qui a le focus, et notifie lorsqu'elle est maintenue
/// enfoncée pendant la durée configurée.
/// </summary>
public interface IHotkeyMonitor : IDisposable
{
    event Action? Triggered;

    void Start(AppKey targetKey, double holdSeconds);

    void UpdateTarget(AppKey targetKey, double holdSeconds);

    void Stop();
}
