using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace PassKeeper.Services;

/// <summary>
/// A new page changes the browser window's title. Some browsers (Firefox right after start, some tab switches)
/// send no focus event for a login field the page focuses by itself, so a title change of the active browser window
/// triggers a short check of where the cursor is. The hook is installed per browser process only — nothing is
/// received from other applications.
/// </summary>
public sealed class BrowserTitleWatcher : IDisposable
{
    private delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventDelegate callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);

    private const uint EventObjectNameChange = 0x800C;
    private const uint WinEventOutOfContext = 0x0000;

    private readonly Dictionary<int, IntPtr> _hooks = [];
    private readonly WinEventDelegate _callback;
    private readonly DispatcherTimer _debounce = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(350) };

    public event Action? PageChanged;

    public BrowserTitleWatcher()
    {
        _callback = OnNameChange;
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            PageChanged?.Invoke();
        };
    }

    /// <summary>Starts watching the windows of a browser process (call on the UI thread).</summary>
    public void Watch(int processId)
    {
        if (_hooks.ContainsKey(processId)) return;
        foreach (var dead in _hooks.Keys.Where(pid => !IsAlive(pid)).ToList())
        {
            UnhookWinEvent(_hooks[dead]);
            _hooks.Remove(dead);
        }
        var hook = SetWinEventHook(EventObjectNameChange, EventObjectNameChange, IntPtr.Zero, _callback, (uint)processId, 0, WinEventOutOfContext);
        if (hook != IntPtr.Zero) _hooks[processId] = hook;
    }

    private void OnNameChange(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        // The window itself (not an element inside it), and only the one the user works in.
        if (idObject != 0 || idChild != 0 || hwnd != Native.GetForegroundWindow()) return;
        _debounce.Stop();
        _debounce.Start();
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _debounce.Stop();
        foreach (var hook in _hooks.Values) UnhookWinEvent(hook);
        _hooks.Clear();
    }
}
