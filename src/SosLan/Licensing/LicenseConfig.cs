namespace SosLan.Licensing;

/// <summary>
/// Réglages centralisés du système de licence (communs à Windows et macOS).
/// </summary>
public static class LicenseConfig
{
    /// <summary>
    /// INTERRUPTEUR GLOBAL : mettre à <c>false</c> avant de publier une release sans licence.
    /// Désactivé : aucun contrôle, aucun appel réseau, tout est accessible et les Paramètres
    /// affichent « Licence gratuite ». Indépendant de licence.ini.
    /// </summary>
    public const bool Enabled = true;

    public const string ProductSlug = "sos-alarme";

    /// <summary>Nom du fichier de bypass « licence gratuite » (voir <see cref="LicenseLocations"/>).</summary>
    public const string FreeLicenseFileName = "licence.ini";

    public const int GraceDays = 30;

    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan RevalidationInterval = TimeSpan.FromHours(24);

    public static string ServerUrl
    {
        get
        {
#if DEBUG
            // Uniquement dans les builds de développement : permet de tester contre un faux serveur.
            var overrideUrl = Environment.GetEnvironmentVariable("SOSLAN_LICENSE_URL");
            if (!string.IsNullOrWhiteSpace(overrideUrl))
            {
                return overrideUrl.TrimEnd('/');
            }
#endif
            return "https://licences.tlpc.fr";
        }
    }
}
