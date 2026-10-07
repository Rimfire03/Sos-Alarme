using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace SosLan.Licensing.Windows;

/// <summary>
/// Licence stockée dans le registre (HKCU\Software\TomLine prod&amp;co\SosLan), valeur unique chiffrée par DPAPI
/// (portée utilisateur). Chaque session Windows a donc son stockage, mais toutes partagent le même deviceId.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsLicenseStorage : ILicenseStorage
{
    private const string KeyPath = @"Software\TomLine prod&co\SosLan";
    private const string ValueName = "License";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SosLan.License.v1");

    public string? Load()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            if (key?.GetValue(ValueName) is not byte[] protectedBytes)
            {
                return null;
            }

            var bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or IOException)
        {
            return null;
        }
    }

    public void Save(string value)
    {
        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser);
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue(ValueName, protectedBytes, RegistryValueKind.Binary);
    }

    public void Delete()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);
        }
        catch (IOException)
        {
            // Rien à supprimer.
        }
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsMachineIdProvider : IMachineIdProvider
{
    public string GetRawMachineId()
    {
        // Vue 64 bits explicite : la valeur est la même quelle que soit l'architecture du processus.
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        var guid = key?.GetValue("MachineGuid") as string;
        if (string.IsNullOrWhiteSpace(guid))
        {
            throw new InvalidOperationException("MachineGuid introuvable.");
        }

        return guid;
    }
}
