using System.Runtime.InteropServices;

namespace PassKeeper.Services;

/// <summary>
/// Reports which window the user switched to (other programs only). One system hook for the whole application;
/// the event arrives on the thread that created the watcher (the UI thread).
/// </summary>
public sealed class ForegroundWatcher : IDisposable
{
    private delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventDelegate callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);

    private const uint EventSystemForeground = 0x0003;
    private const uint WinEventOutOfContext = 0x0000;
    private const uint WinEventSkipOwnProcess = 0x0002;
    private const uint GaRoot = 2;

    private readonly WinEventDelegate _callback;
    private IntPtr _hook;

    public event Action<IntPtr>? Changed;

    public ForegroundWatcher()
    {
        _callback = (_, _, hwnd, idObject, _, _, _) =>
        {
            if (idObject != 0 || hwnd == IntPtr.Zero) return;
            // Some browsers report an inner child window (Yandex: "Chrome Legacy Window"): the top-level window is meant.
            var root = Native.GetAncestor(hwnd, GaRoot);
            Changed?.Invoke(root != IntPtr.Zero ? root : hwnd);
        };
        _hook = SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, _callback, 0, 0, WinEventOutOfContext | WinEventSkipOwnProcess);
    }

    /// <summary>
    /// Reports the window in front now, as if the user had just switched to it: a sign-in window or page already open
    /// when PassKeeper starts sends no event of its own. PassKeeper's own windows are left out, as by the hook.
    /// </summary>
    public void ReportCurrent()
    {
        var hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == (uint)Environment.ProcessId) return;
        var root = Native.GetAncestor(hwnd, GaRoot);
        Changed?.Invoke(root != IntPtr.Zero ? root : hwnd);
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }
}
