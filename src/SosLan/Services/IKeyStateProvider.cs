using SosLan.Models;

namespace SosLan.Services;

/// <summary>
/// Indique si une touche est actuellement enfoncée au niveau matériel/système,
/// indépendamment de l'application qui a le focus. Une implémentation par plateforme.
/// </summary>
public interface IKeyStateProvider
{
    bool IsPressed(AppKey key);
}
