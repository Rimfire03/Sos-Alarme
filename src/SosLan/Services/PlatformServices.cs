using System.Runtime.InteropServices;
using SosLan.Services.MacOS;
using SosLan.Services.Windows;

namespace SosLan.Services;

/// <summary>
/// Point d'entrée unique pour obtenir les implémentations spécifiques à la plateforme
/// courante (Windows ou macOS). Évite de disperser des tests RuntimeInformation partout
/// dans le reste du code.
/// </summary>
public static class PlatformServices
{
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public static bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    public static IHotkeyMonitor CreateHotkeyMonitor()
    {
        IKeyStateProvider provider = IsWindows
            ? new WindowsKeyStateProvider()
            : new MacKeyStateProvider();

        return new PollingHotkeyMonitor(provider);
    }

    public static IStartupService CreateStartupService()
    {
        return IsWindows
            ? new WindowsStartupService()
            : new MacStartupService();
    }

    public static IAlarmSoundPlayer CreateAlarmSoundPlayer()
    {
        return IsWindows
            ? new WindowsAlarmSoundPlayer()
            : new MacAlarmSoundPlayer();
    }
}
