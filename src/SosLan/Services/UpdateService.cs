using Velopack;
using Velopack.Sources;

namespace SosLan.Services;

/// <summary>
/// Vérifie, télécharge et applique les mises à jour publiées sur les releases GitHub du dépôt,
/// via Velopack. Sans effet si l'application ne tourne pas depuis une installation Velopack
/// (ex. lancement en développement via dotnet run). Le téléchargement et l'application de la
/// mise à jour ne se font qu'après confirmation explicite de l'appelant (voir DownloadAndApplyAsync).
/// </summary>
public class UpdateService
{
    private const string RepoUrl = "https://github.com/Rimfire03/Sos-Alarme";

    private UpdateManager? _manager;
    private UpdateInfo? _pendingUpdate;

    public enum CheckOutcome
    {
        NotInstalled,
        UpToDate,
        UpdateAvailable,
        Failed
    }

    public async Task<(CheckOutcome Outcome, string? Version)> CheckAsync()
    {
        _manager = new UpdateManager(new GithubSource(RepoUrl, null, false));

        if (!_manager.IsInstalled)
        {
            return (CheckOutcome.NotInstalled, null);
        }

        try
        {
            var newVersion = await _manager.CheckForUpdatesAsync();
            if (newVersion == null)
            {
                return (CheckOutcome.UpToDate, null);
            }

            _pendingUpdate = newVersion;
            return (CheckOutcome.UpdateAvailable, newVersion.TargetFullRelease.Version.ToString());
        }
        catch
        {
            return (CheckOutcome.Failed, null);
        }
    }

    /// <summary>
    /// Télécharge et applique la mise à jour détectée par le dernier appel à CheckAsync, puis
    /// redémarre l'application. Ne doit être appelé qu'après confirmation de l'utilisateur.
    /// </summary>
    public async Task<bool> DownloadAndApplyAsync()
    {
        if (_manager == null || _pendingUpdate == null)
        {
            return false;
        }

        try
        {
            await _manager.DownloadUpdatesAsync(_pendingUpdate);
            _manager.ApplyUpdatesAndRestart(_pendingUpdate);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
