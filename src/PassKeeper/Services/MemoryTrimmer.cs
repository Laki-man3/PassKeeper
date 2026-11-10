using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace PassKeeper.Services;

/// <summary>
/// Gives memory back to Windows once PassKeeper goes to the background (hidden to the tray, minimized, locked):
/// a compacting collection returns free heap to the system and the working set is emptied. What is needed again is
/// paged back in when the window reappears. Work done while hidden (autofill, a suggestion, a new page in the browser)
/// brings pages back in: they are given back once nothing has happened for a while, by emptying the working set only.
/// No collection runs and nothing waits on the interface thread while the user works.
/// </summary>
public static class MemoryTrimmer
{
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, nint minimum, nint maximum);

    /// <summary>A working set below this is left alone: emptying it again would only cost page faults.</summary>
    private const long IdleWorkingSet = 24 * 1024 * 1024;

    /// <summary>Quiet time after the last activity before the working set is emptied.</summary>
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(30);

    private static DispatcherTimer? _timer;
    private static DispatcherTimer? _activityTimer;

    /// <summary>Trims a few seconds later (after closing animations, when the application is idle).</summary>
    public static void TrimSoon()
    {
        _timer ??= new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromSeconds(4) };
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Stop();
        _timer.Start();
    }

    private static void OnTick(object? sender, EventArgs e)
    {
        _timer?.Stop();
        Trim();
    }

    /// <summary>
    /// Something was done in the background: the working set is emptied once nothing more has happened for
    /// <see cref="Quiet"/>. Does nothing while the window is on the screen.
    /// </summary>
    public static void AfterActivity()
    {
        if (App.Instance.IsExiting || App.Instance.Main is { IsVisible: true, WindowState: not WindowState.Minimized }) return;
        _activityTimer ??= CreateActivityTimer();
        _activityTimer.Stop();
        _activityTimer.Start();
    }

    private static DispatcherTimer CreateActivityTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = Quiet };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var app = App.Instance;
            if (app.IsExiting || Environment.WorkingSet < IdleWorkingSet) return;
            if (app.AutoType.IsBusy)
            {
                timer.Start();
                return;
            }
            // A suggestion, a PIN prompt or the window itself is on the screen: it is trimmed when hidden.
            foreach (Window window in app.Windows)
                if (window.IsVisible && window.WindowState != WindowState.Minimized) return;
            TrimWorkingSet();
        };
        return timer;
    }

    private static int _trimming;

    /// <summary>
    /// Compacts the heap and empties the working set, on a pool thread: waiting for finalizers (UI Automation
    /// elements of other programs among them) never holds up the interface thread.
    /// </summary>
    public static void Trim()
    {
        if (Interlocked.Exchange(ref _trimming, 1) == 1) return;
        Task.Run(() =>
        {
            try
            {
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                TrimWorkingSet();
            }
            finally
            {
                Interlocked.Exchange(ref _trimming, 0);
            }
        });
    }

    private static void TrimWorkingSet()
    {
        using var process = Process.GetCurrentProcess();
        SetProcessWorkingSetSize(process.Handle, -1, -1);
    }
}
