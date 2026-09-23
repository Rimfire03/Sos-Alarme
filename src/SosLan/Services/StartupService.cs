using System.Diagnostics;
using Microsoft.Win32;

namespace SosLan.Services;

/// <summary>
/// Active ou désactive le démarrage automatique de l'application avec Windows,
/// via la clé Run du registre de l'utilisateur courant.
/// </summary>
public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SOS-LAN";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        var existing = key?.GetValue(ValueName) as string;
        return existing != null && existing.Trim('"').Equals(GetExecutablePath(), StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

        if (enabled)
        {
            key.SetValue(ValueName, $"\"{GetExecutablePath()}\"");
        }
        else
        {
            key.DeleteValue(ValueName, false);
        }
    }

    private static string GetExecutablePath()
    {
        return Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName!;
    }
}
