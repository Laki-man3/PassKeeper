using System.Windows.Threading;
using Microsoft.Win32;

namespace PassKeeper.Services;

/// <summary>
/// Fires <see cref="Timeout"/> when there was no keyboard/mouse input in the Windows session for the configured period.
/// Wall-clock based, so time spent in sleep/hibernation counts as inactivity too. The next check is scheduled for the
/// moment the period would run out (at most a minute ahead), so the process stays asleep in between yet locks on time.
/// </summary>
public sealed class IdleMonitor : IDisposable
{
    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(1);
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background);
    private DateTime _lastActivityUtc = DateTime.UtcNow;
    private uint _lastInputTick = Native.GetLastInputTick();
    private TimeSpan _period = TimeSpan.FromHours(8);

    public event Action? Timeout;

    public IdleMonitor()
    {
        _timer.Tick += (_, _) => Check();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public TimeSpan Period
    {
        get => _period;
        set
        {
            _period = value;
            if (_timer.IsEnabled) Schedule(TimeSpan.Zero);
        }
    }

    public void Start()
    {
        Reset();
        Schedule(_period);
    }

    public void Stop() => _timer.Stop();

    public void Reset()
    {
        _lastActivityUtc = DateTime.UtcNow;
        _lastInputTick = Native.GetLastInputTick();
    }

    private void Check()
    {
        var tick = Native.GetLastInputTick();
        if (tick != _lastInputTick)
        {
            _lastInputTick = tick;
            var idleMs = unchecked((uint)Environment.TickCount - tick);
            var lastInput = DateTime.UtcNow - TimeSpan.FromMilliseconds(Math.Min(idleMs, int.MaxValue));
            if (lastInput > _lastActivityUtc) _lastActivityUtc = lastInput;
        }
        var remaining = _period - (DateTime.UtcNow - _lastActivityUtc);
        if (remaining <= TimeSpan.Zero)
        {
            _timer.Stop();
            Timeout?.Invoke();
            return;
        }
        Schedule(remaining);
    }

    private void Schedule(TimeSpan wait)
    {
        _timer.Stop();
        _timer.Interval = wait <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : wait > MaxWait ? MaxWait : wait + TimeSpan.FromMilliseconds(20);
        _timer.Start();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume && _timer.IsEnabled) _timer.Dispatcher.BeginInvoke(Check);
    }

    public void Dispose()
    {
        _timer.Stop();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }
}
