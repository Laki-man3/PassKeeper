using System.Collections.Concurrent;
using System.Windows.Threading;

namespace PassKeeper.Services;

/// <summary>
/// Notices that the browser window in front shows another page: when a browser window is switched to, and by its
/// title, which is read twice a second only while a browser window is the active one. Some browsers (Yandex) send no
/// focus events from their pages, and single-page sites change the page without moving the cursor.
/// </summary>
public sealed class BrowserPageWatcher
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly ConcurrentDictionary<uint, bool> _browsers = new();
    private IntPtr _window;
    private string _title = "";

    public event Action? PageChanged;

    public BrowserPageWatcher(ForegroundWatcher foreground)
    {
        foreground.Changed += OnForeground;
        _timer.Tick += (_, _) => Check();
    }

    private void OnForeground(IntPtr hwnd)
    {
        if (!IsBrowserWindow(hwnd))
        {
            _window = IntPtr.Zero;
            _timer.Stop();
            return;
        }
        _window = hwnd;
        _title = Native.GetWindowTitle(hwnd);
        _timer.Start();
        PageChanged?.Invoke();
    }

    private void Check()
    {
        if (Native.GetForegroundWindow() != _window)
        {
            // PassKeeper itself or another program is in front (switching back to a browser starts the check again).
            _timer.Stop();
            return;
        }
        var title = Native.GetWindowTitle(_window);
        if (title == _title) return;
        _title = title;
        PageChanged?.Invoke();
    }

    private bool IsBrowserWindow(IntPtr hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        return pid != 0 && _browsers.GetOrAdd(pid, id =>
        {
            try
            {
                using var p = System.Diagnostics.Process.GetProcessById((int)id);
                return TargetDetector.IsBrowserProcess(p.ProcessName);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return false;
            }
        });
    }
}
