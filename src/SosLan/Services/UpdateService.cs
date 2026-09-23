using Velopack;
using Velopack.Sources;

namespace SosLan.Services;

/// <summary>
/// Vérifie, télécharge et applique les mises à jour publiées sur les releases GitHub du dépôt,
/// via Velopack. Sans effet si l'application ne tourne pas depuis une installation Velopack
/// (ex. lancement en développement via dotnet run).
/// </summary>
public class UpdateService
{
    private const string RepoUrl = "https://github.com/Rimfire03/Sos-Alarme";

    public enum Outcome
    {
        NotInstalled,
        UpToDate,
        Updated,
        Failed
    }

    public async Task<Outcome> CheckAndApplyAsync()
    {
        var manager = new UpdateManager(new GithubSource(RepoUrl, null, false));

        if (!manager.IsInstalled)
        {
            return Outcome.NotInstalled;
        }

        try
        {
            var newVersion = await manager.CheckForUpdatesAsync();
            if (newVersion == null)
            {
                return Outcome.UpToDate;
            }

            await manager.DownloadUpdatesAsync(newVersion);
            manager.ApplyUpdatesAndRestart(newVersion);
            return Outcome.Updated;
        }
        catch
        {
            return Outcome.Failed;
        }
    }
}
