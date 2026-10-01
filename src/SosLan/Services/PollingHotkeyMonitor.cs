using Avalonia.Threading;
using SosLan.Models;

namespace SosLan.Services;

/// <summary>
/// Implémentation commune aux deux plateformes : interroge périodiquement l'état de la
/// touche cible via un <see cref="IKeyStateProvider"/> spécifique à l'OS, et déclenche
/// l'évènement une fois la durée de maintien atteinte.
/// </summary>
public class PollingHotkeyMonitor : IHotkeyMonitor
{
    private const int PollIntervalMs = 50;

    private readonly IKeyStateProvider _keyStateProvider;
    private readonly DispatcherTimer _timer;
    private AppKey _targetKey;
    private double _holdSeconds;
    private DateTime? _pressStartedAt;
    private bool _triggered;

    public event Action? Triggered;

    public PollingHotkeyMonitor(IKeyStateProvider keyStateProvider)
    {
        _keyStateProvider = keyStateProvider;
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(PollIntervalMs)
        };
        _timer.Tick += OnTick;
    }

    public void Start(AppKey targetKey, double holdSeconds)
    {
        UpdateTarget(targetKey, holdSeconds);
        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    public void UpdateTarget(AppKey targetKey, double holdSeconds)
    {
        _targetKey = targetKey;
        _holdSeconds = holdSeconds;
        _pressStartedAt = null;
        _triggered = false;
    }

    public void Stop() => _timer.Stop();

    private void OnTick(object? sender, EventArgs e)
    {
        bool isPressed = _keyStateProvider.IsPressed(_targetKey);

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
}
