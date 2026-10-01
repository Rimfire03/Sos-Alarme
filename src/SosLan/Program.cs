using Avalonia;
using Velopack;

namespace SosLan;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Doit s'exécuter avant tout le reste : gère les évènements d'installation/désinstallation
        // Velopack (création de raccourcis, etc.) lors du premier lancement post-installation.
        VelopackApp.Build().Run();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
