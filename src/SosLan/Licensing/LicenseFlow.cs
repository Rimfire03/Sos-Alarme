using SosLan.Views;

namespace SosLan.Licensing;

/// <summary>Enchaînement des écrans de licence au démarrage et lors des revalidations périodiques.</summary>
public static class LicenseFlow
{
    private const string Title = "Licence SOS-LAN";

    /// <summary>
    /// Renvoie true si l'application peut démarrer (licence gratuite, valide, ou en grâce hors-ligne).
    /// Renvoie false si l'utilisateur renonce : l'application doit alors se fermer.
    /// </summary>
    public static async Task<bool> EnsureLicensedAsync(LicenseManager license)
    {
        if (license.IsFree)
        {
            return true;
        }

        if (license.HasStoredLicense)
        {
            var result = await license.CheckStoredAsync();
            if (result.Valid)
            {
                return true;
            }

            if (result.Reason == "license_expired")
            {
                // La clé stockée est conservée : une prolongation côté serveur la rend à nouveau valide.
                return await ResolveExpiredAsync(license);
            }

            if (result.Offline)
            {
                // Serveur injoignable sans grâce restante : la licence stockée est conservée (pas un refus).
                await MessageWindow.ShowInfo(Title, LicenseMessages.ForReason(result.Reason));
            }
            else
            {
                await MessageWindow.ShowInfo(Title, LicenseMessages.ForReason(result.Reason) + "\n\nSaisissez une clé de licence valide pour continuer.");
                license.ClearStorage();
            }
        }

        // Licence gratuite exclue plus haut : le bouton de démo n'est proposé qu'ici (aucune licence valide).
        var window = new LicenseKeyWindow(
            "Saisissez votre clé de licence pour utiliser SOS-LAN.",
            "Quitter",
            license.ActivateAsync,
            license.RequestDemoAsync);

        return await window.ShowAndWaitAsync();
    }

    /// <summary>
    /// Ordre « remove_bypass » du serveur : supprime licence.ini puis relance le flux normal (sans fermer brutalement).
    /// Renvoie false si l'application doit se fermer (aucune licence valide). Si la suppression échoue : true, rien ne change.
    /// </summary>
    public static async Task<bool> HandleRemoveBypassAsync(LicenseManager license)
    {
        if (!await license.RemoveBypassAsync())
        {
            return true;
        }

        await MessageWindow.ShowInfo(Title, "Le mode licence gratuite a été désactivé par l'administrateur.");
        return await EnsureLicensedAsync(license);
    }

    /// <summary>Revalidation périodique. Renvoie false si l'application doit se fermer (licence refusée / grâce dépassée).</summary>
    public static async Task<bool> RevalidateAsync(LicenseManager license)
    {
        if (license.IsFree)
        {
            return true;
        }

        var result = await license.CheckStoredAsync();
        if (result.Valid)
        {
            return true;
        }

        if (result.Reason == "license_expired")
        {
            return await ResolveExpiredAsync(license);
        }

        if (!result.Offline)
        {
            license.ClearStorage();
        }

        await MessageWindow.ShowInfo(Title, LicenseMessages.ForReason(result.Reason) + "\n\nL'application va se fermer.");
        return false;
    }

    /// <summary>
    /// Écran « Licence expirée » (Réessayer / Saisir une autre licence / Quitter). L'accès reste bloqué tant que
    /// la licence n'est pas valide ; renvoie false si l'utilisateur quitte.
    /// </summary>
    private static async Task<bool> ResolveExpiredAsync(LicenseManager license)
    {
        while (true)
        {
            var outcome = await new LicenseExpiredWindow(license.RetryStoredAsync).ShowAndWaitAsync();

            switch (outcome)
            {
                case ExpiredOutcome.Resolved:
                    return true;

                case ExpiredOutcome.EnterOtherLicense:
                    var keyWindow = new LicenseKeyWindow(
                        "Saisissez une autre clé de licence.",
                        "Retour",
                        license.ChangeAsync,
                        license.RequestDemoAsync);
                    if (await keyWindow.ShowAndWaitAsync())
                    {
                        return true;
                    }

                    break;

                default:
                    return false;
            }
        }
    }
}
