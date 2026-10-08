using System.Runtime.InteropServices;
using SosLan.Licensing.MacOS;
using SosLan.Licensing.Windows;

namespace SosLan.Licensing;

/// <summary>Stockage sécurisé de la licence : un seul élément opaque, chiffré, jamais dans un fichier.</summary>
public interface ILicenseStorage
{
    /// <summary>Renvoie l'élément stocké (texte opaque) ou null s'il n'existe pas / est illisible.</summary>
    string? Load();

    void Save(string value);

    void Delete();
}

/// <summary>Identifiant brut et stable du poste (même valeur pour tous les utilisateurs du poste).</summary>
public interface IMachineIdProvider
{
    string GetRawMachineId();
}

public static class LicensePlatform
{
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public static ILicenseStorage CreateStorage() =>
        OperatingSystem.IsWindows() ? new WindowsLicenseStorage() : new MacLicenseStorage();

    public static IMachineIdProvider CreateMachineIdProvider() =>
        OperatingSystem.IsWindows() ? new WindowsMachineIdProvider() : new MacMachineIdProvider();

    /// <summary>
    /// Dossier d'installation où chercher licence.ini (un seul emplacement par plateforme) :
    /// - Windows : racine d'installation Velopack (dossier contenant Update.exe, parent de « current »),
    ///   qui survit aux mises à jour ; sinon le dossier de l'exécutable ;
    /// - macOS : le dossier qui contient le bundle .app (à côté de SOS-LAN.app) ; sinon le dossier de l'exécutable.
    /// </summary>
    public static string GetInstallDirectory()
    {
#if DEBUG
        // Uniquement dans les builds de développement : permet de tester avec un dossier dédié (ex. lecture seule).
        var overrideDir = Environment.GetEnvironmentVariable("SOSLAN_INSTALL_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            return overrideDir;
        }
#endif
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (IsWindows)
        {
            var parent = Path.GetDirectoryName(baseDir);
            if (parent != null && File.Exists(Path.Combine(parent, "Update.exe")))
            {
                return parent;
            }

            return baseDir;
        }

        var dir = new DirectoryInfo(baseDir);
        while (dir != null)
        {
            if (dir.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            {
                return dir.Parent?.FullName ?? baseDir;
            }

            dir = dir.Parent;
        }

        return baseDir;
    }
}
