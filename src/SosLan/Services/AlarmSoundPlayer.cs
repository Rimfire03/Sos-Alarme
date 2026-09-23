using System.IO;
using System.Media;

namespace SosLan.Services;

/// <summary>
/// Joue en boucle une tonalité d'alerte générée en mémoire (aucun fichier audio requis).
/// </summary>
public class AlarmSoundPlayer : IDisposable
{
    private readonly SoundPlayer _player;
    private readonly MemoryStream _stream;

    public AlarmSoundPlayer()
    {
        _stream = GenerateAlertToneWav();
        _player = new SoundPlayer(_stream);
        _player.Load();
    }

    public void Start() => _player.PlayLooping();

    public void Stop() => _player.Stop();

    public void Dispose()
    {
        _player.Dispose();
        _stream.Dispose();
    }

    /// <summary>
    /// Génère un court motif de deux bips (WAV PCM 16 bits mono) utilisé en boucle.
    /// </summary>
    private static MemoryStream GenerateAlertToneWav()
    {
        const int sampleRate = 44100;
        const double beepDuration = 0.18;
        const double silenceDuration = 0.12;
        const double pauseDuration = 0.35;
        const double frequency = 950;

        var samples = new List<short>();
        AppendTone(samples, sampleRate, frequency, beepDuration);
        AppendSilence(samples, sampleRate, silenceDuration);
        AppendTone(samples, sampleRate, frequency, beepDuration);
        AppendSilence(samples, sampleRate, pauseDuration);

        var stream = new MemoryStream();
        WriteWavHeader(stream, sampleRate, samples.Count);
        foreach (var sample in samples)
        {
            stream.Write(BitConverter.GetBytes(sample));
        }

        stream.Position = 0;
        return stream;
    }

    private static void AppendTone(List<short> samples, int sampleRate, double frequency, double durationSeconds)
    {
        int count = (int)(sampleRate * durationSeconds);
        for (int i = 0; i < count; i++)
        {
            double t = i / (double)sampleRate;
            double envelope = Math.Sin(Math.PI * i / count); // évite les clics de début/fin
            double value = Math.Sin(2 * Math.PI * frequency * t) * envelope;
            samples.Add((short)(value * short.MaxValue * 0.8));
        }
    }

    private static void AppendSilence(List<short> samples, int sampleRate, double durationSeconds)
    {
        int count = (int)(sampleRate * durationSeconds);
        for (int i = 0; i < count; i++)
        {
            samples.Add(0);
        }
    }

    private static void WriteWavHeader(Stream stream, int sampleRate, int sampleCount)
    {
        const short bitsPerSample = 16;
        const short channels = 1;
        int dataSize = sampleCount * (bitsPerSample / 8) * channels;
        int byteRate = sampleRate * channels * (bitsPerSample / 8);

        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write((short)(channels * (bitsPerSample / 8)));
        writer.Write(bitsPerSample);
        writer.Write("data"u8.ToArray());
        writer.Write(dataSize);
    }
}
