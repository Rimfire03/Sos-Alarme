using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace SosLan.Licensing.MacOS;

/// <summary>
/// Licence stockée dans le Keychain de l'utilisateur (élément générique, service dédié), valeur unique.
/// Le secret est transmis à l'outil « security » par l'entrée standard (mode interactif) et non en argument,
/// pour ne pas apparaître dans la liste des processus.
/// </summary>
public sealed class MacLicenseStorage : ILicenseStorage
{
    private const string Service = "SOS-LAN License";
    private const string Account = "SosLan";

    public string? Load()
    {
        var (exitCode, output) = RunSecurity(null, "find-generic-password", "-a", Account, "-s", Service, "-w");
        if (exitCode != 0)
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(output.Trim()));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public void Save(string value)
    {
        // Encodage base64 : valeur opaque sans espace ni guillemet, sûre à passer au mode interactif.
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        var (exitCode, _) = RunSecurity($"add-generic-password -U -a {Account} -s \"{Service}\" -w {encoded}\n", "-i");
        if (exitCode != 0)
        {
            throw new IOException("Écriture dans le Keychain impossible.");
        }
    }

    public void Delete()
    {
        RunSecurity(null, "delete-generic-password", "-a", Account, "-s", Service);
    }

    private static (int ExitCode, string Output) RunSecurity(string? stdin, params string[] args)
    {
        var psi = new ProcessStartInfo("/usr/bin/security")
        {
            RedirectStandardInput = stdin != null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi) ?? throw new IOException("security introuvable.");
        if (stdin != null)
        {
            process.StandardInput.Write(stdin);
            process.StandardInput.Close();
        }

        var output = process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}

public sealed class MacMachineIdProvider : IMachineIdProvider
{
    public string GetRawMachineId()
    {
        var psi = new ProcessStartInfo("/usr/sbin/ioreg", "-rd1 -c IOPlatformExpertDevice")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("ioreg introuvable.");
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        var match = Regex.Match(output, "\"IOPlatformUUID\"\\s*=\\s*\"([^\"]+)\"");
        if (!match.Success)
        {
            throw new InvalidOperationException("IOPlatformUUID introuvable.");
        }

        return match.Groups[1].Value;
    }
}
