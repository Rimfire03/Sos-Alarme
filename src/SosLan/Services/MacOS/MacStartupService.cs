using System.Diagnostics;

namespace SosLan.Services.MacOS;

/// <summary>
/// Démarrage automatique via un LaunchAgent utilisateur (~/Library/LaunchAgents),
/// l'équivalent macOS de la clé Run du registre Windows.
/// </summary>
public class MacStartupService : IStartupService
{
    private const string Label = "co.tomlineprodco.soslan";

    private static string PlistPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Personal),
        "Library", "LaunchAgents", $"{Label}.plist");

    public bool IsEnabled() => File.Exists(PlistPath);

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            var executablePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName!;
            var plist = $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0">
                <dict>
                    <key>Label</key>
                    <string>{Label}</string>
                    <key>ProgramArguments</key>
                    <array>
                        <string>{executablePath}</string>
                    </array>
                    <key>RunAtLoad</key>
                    <true/>
                </dict>
                </plist>
                """;

            Directory.CreateDirectory(Path.GetDirectoryName(PlistPath)!);
            File.WriteAllText(PlistPath, plist);
            RunLaunchctl("load", "-w", PlistPath);
        }
        else
        {
            if (File.Exists(PlistPath))
            {
                RunLaunchctl("unload", "-w", PlistPath);
                File.Delete(PlistPath);
            }
        }
    }

    private static void RunLaunchctl(params string[] arguments)
    {
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = "launchctl";
            foreach (var arg in arguments)
            {
                process.StartInfo.ArgumentList.Add(arg);
            }
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            process.WaitForExit(5000);
        }
        catch
        {
            // launchctl indisponible ou échec : le fichier plist reste la source de vérité
            // pour IsEnabled(), la session suivante rechargera le LaunchAgent normalement.
        }
    }
}
