using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace SosLan.Services;

/// <summary>
/// Suivi de l'acceptation du CLUF. L'installateur MSI n'affiche le CLUF qu'à l'installation : une mise à jour
/// automatique (Velopack) ne le montre jamais. L'application le présente donc elle-même dès que son contenu
/// diffère de la version déjà acceptée sur ce poste (empreinte du texte).
/// </summary>
public static class EulaService
{
    private const string ResourceName = "SosLan.CLUF.md";

    private static string AcceptedFilePath => Path.Combine(SettingsService.FolderPath, "cluf-accepted.txt");

    /// <summary>Texte du CLUF embarqué dans l'application (source : installer/CLUF.md).</summary>
    public static string LoadText()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream == null)
        {
            return "";
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>Empreinte du texte (fins de ligne normalisées : stable d'un build à l'autre).</summary>
    public static string ComputeHash(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Trim();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    /// <summary>Vrai si la version courante du CLUF a déjà été acceptée sur ce poste.</summary>
    public static bool IsAccepted(string hash)
    {
        try
        {
            return File.Exists(AcceptedFilePath) && File.ReadAllText(AcceptedFilePath).Trim() == hash;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void MarkAccepted(string hash)
    {
        try
        {
            Directory.CreateDirectory(SettingsService.FolderPath);
            File.WriteAllText(AcceptedFilePath, hash);
        }
        catch (Exception)
        {
            // Non enregistré : le CLUF sera simplement représenté au prochain lancement.
        }
    }
}
