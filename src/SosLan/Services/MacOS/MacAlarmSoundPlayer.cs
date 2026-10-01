using System.Diagnostics;

namespace SosLan.Services.MacOS;

/// <summary>
/// Joue le son d'alerte en boucle sous macOS en relançant l'utilitaire système "afplay"
/// (aucune lecture audio en boucle native en .NET multiplateforme).
/// </summary>
public class MacAlarmSoundPlayer : IAlarmSoundPlayer
{
    private readonly string _tempFilePath;
    private CancellationTokenSource? _cts;
    private Process? _currentProcess;
    private Task? _loopTask;

    public MacAlarmSoundPlayer()
    {
        _tempFilePath = Path.Combine(Path.GetTempPath(), $"soslan-alert-{Guid.NewGuid():N}.wav");
        File.WriteAllBytes(_tempFilePath, AlertToneWavGenerator.Generate());
    }

    public void Start()
    {
        if (_loopTask != null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => LoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();

        try
        {
            _currentProcess?.Kill();
        }
        catch
        {
            // Processus déjà terminé.
        }

        _loopTask = null;
    }

    private void LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var process = new Process();
                _currentProcess = process;
                process.StartInfo.FileName = "afplay";
                process.StartInfo.ArgumentList.Add(_tempFilePath);
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.Start();
                process.WaitForExit();
            }
            catch
            {
                // "afplay" indisponible : on arrête la boucle plutôt que de tourner à vide.
                break;
            }
        }
    }

    public void Dispose()
    {
        Stop();
        try
        {
            File.Delete(_tempFilePath);
        }
        catch
        {
            // Nettoyage best-effort.
        }
    }
}
