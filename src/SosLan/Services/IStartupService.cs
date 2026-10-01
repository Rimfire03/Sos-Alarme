namespace SosLan.Services;

/// <summary>
/// Active ou désactive le démarrage automatique de l'application avec la session
/// utilisateur. Une implémentation par plateforme (registre Windows, LaunchAgent macOS).
/// </summary>
public interface IStartupService
{
    bool IsEnabled();

    void SetEnabled(bool enabled);
}
