using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Threading;

namespace SosLan.Services;

/// <summary>
/// Surveille en permanence l'état d'une touche via l'API Windows (GetAsyncKeyState)
/// afin de détecter un appui long, quel que soit le programme qui a le focus.
/// </summary>
public class HotkeyMonitor : IDisposable
{
    private const int PollIntervalMs = 50;

    private readonly DispatcherTimer _timer;
    private Keys _targetKey;
    private double _holdSeconds;
    private DateTime? _pressStartedAt;
    private bool _triggered;

    public event Action? Triggered;

    public HotkeyMonitor()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(PollIntervalMs)
        };
        _timer.Tick += OnTick;
    }

    public void Start(Keys targetKey, double holdSeconds)
    {
        UpdateTarget(targetKey, holdSeconds);
        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    public void UpdateTarget(Keys targetKey, double holdSeconds)
    {
        _targetKey = targetKey;
        _holdSeconds = holdSeconds;
        _pressStartedAt = null;
        _triggered = false;
    }

    public void Stop() => _timer.Stop();

    private void OnTick(object? sender, EventArgs e)
    {
        bool isPressed = (GetAsyncKeyState((int)_targetKey) & 0x8000) != 0;

        if (isPressed)
        {
            _pressStartedAt ??= DateTime.UtcNow;

            if (!_triggered && (DateTime.UtcNow - _pressStartedAt.Value).TotalSeconds >= _holdSeconds)
            {
                _triggered = true;
                Triggered?.Invoke();
            }
        }
        else
        {
            _pressStartedAt = null;
            _triggered = false;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
