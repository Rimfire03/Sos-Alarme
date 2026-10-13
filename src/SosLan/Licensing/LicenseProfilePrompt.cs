using Avalonia.Controls;
using SosLan.Views;

namespace SosLan.Licensing;

/// <summary>
/// Formulaire des informations client demandées par le serveur de licences. Ne bloque jamais l'application :
/// « Plus tard » ferme simplement la fenêtre, et la demande sera redonnée au contrôle suivant (lancement ou
/// revalidation), au plus une fois par lancement. Jamais en licence gratuite (licence.ini).
/// </summary>
public static class LicenseProfilePrompt
{
    private static bool _shownThisLaunch;

    /// <summary>Retourne vrai si les informations ont été enregistrées. Sans effet si rien n'est demandé.</summary>
    public static async Task<bool> ShowIfRequiredAsync(LicenseManager license, Window? owner = null)
    {
        try
        {
            if (_shownThisLaunch || license.IsFree || license.ProfileRequest is not { } request)
            {
                return false;
            }

            _shownThisLaunch = true;
            var window = new DemoRequestWindow(request.Prefill, new ProfileFormOptions
            {
                Highlight = request.Missing,
                SubmitAsync = license.SubmitProfileAsync
            });
            return await window.ShowAndWaitAsync(owner) != null;
        }
        catch (Exception)
        {
            // La saisie des informations ne doit jamais gêner l'application.
            return false;
        }
    }
}
