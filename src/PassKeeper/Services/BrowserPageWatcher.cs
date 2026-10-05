using System.Collections.Concurrent;
using System.Windows.Automation;
using System.Windows.Threading;

namespace PassKeeper.Services;

/// <summary>
/// Notices that the browser window in front shows another page: when a browser window is switched to, and by its
/// title and address, which are read a few times a second only while a browser window is the active one. Some
/// browsers (Yandex) send no focus events from their pages, single-page sites change the page without moving the
/// cursor, and a sign-in page reached by redirect often keeps the title of the site that sent the user there.
/// </summary>
public sealed class BrowserPageWatcher
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly ConcurrentDictionary<uint, bool> _browsers = new();
    private IntPtr _window;
    private string _title = "";
    private AutomationElement? _page;
    private string? _address;
    private int _reading;

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
        if (hwnd != _window)
        {
            _page = null;
            _address = null;
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
        if (title != _title)
        {
            _title = title;
            PageChanged?.Invoke();
            return;
        }
        CheckAddress();
    }

    /// <summary>The page's address, read off the UI thread (a busy browser may take a while to answer).</summary>
    private void CheckAddress()
    {
        if (Interlocked.Exchange(ref _reading, 1) == 1) return;
        var window = _window;
        var page = _page;
        Task.Run(() =>
        {
            string? address = null;
            try
            {
                address = Address(page);
                if (address == null)
                {
                    page = FieldFinder.PageOf(window);
                    address = Address(page);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _reading, 0);
            }
            return (page, address);
        }).ContinueWith(t =>
        {
            if (t.IsFaulted || window != _window) return;
            var (found, address) = t.Result;
            _page = found;
            if (address == null || address == _address) return;
            var first = _address == null;
            _address = address;
            if (!first) PageChanged?.Invoke();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static string? Address(AutomationElement? page)
    {
        if (page == null) return null;
        try
        {
            if (!page.TryGetCurrentPattern(ValuePattern.Pattern, out var p) || ((ValuePattern)p).Current.Value is not { Length: > 0 } value) return null;
            // A change after '#' only (single-page sites, sign-in results) is not another page.
            var hash = value.IndexOf('#');
            return hash >= 0 ? value[..hash] : value;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or TimeoutException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
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
