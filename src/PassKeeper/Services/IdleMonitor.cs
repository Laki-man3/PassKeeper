using System.Windows.Threading;
using Microsoft.Win32;

namespace PassKeeper.Services;

/// <summary>
/// Fires <see cref="Timeout"/> when there was no keyboard/mouse input in the Windows session for the configured period.
/// Wall-clock based, so time spent in sleep/hibernation counts as inactivity too.
/// </summary>
public sealed class IdleMonitor : IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(15) };
    private DateTime _lastActivityUtc = DateTime.UtcNow;
    private uint _lastInputTick = Native.GetLastInputTick();

    public TimeSpan Period { get; set; } = TimeSpan.FromHours(8);
    public event Action? Timeout;

    public IdleMonitor()
    {
        _timer.Tick += (_, _) => Check();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public void Start()
    {
        Reset();
        _timer.Start();
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
        if (DateTime.UtcNow - _lastActivityUtc >= Period) Timeout?.Invoke();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) _timer.Dispatcher.BeginInvoke(Check);
    }

    public void Dispose()
    {
        _timer.Stop();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }
}
