using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace PassKeeper.Services;

/// <summary>
/// Gives memory back to Windows once PassKeeper goes to the background (hidden to the tray, minimized, locked):
/// a compacting collection returns free heap to the system and the working set is emptied. What is needed again is
/// paged back in when the window reappears. Work done while hidden (autofill, a suggestion, a new page in the browser)
/// brings memory back in, so it is given back again a little later; with nothing going on there is no timer at all.
/// </summary>
public static class MemoryTrimmer
{
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, nint minimum, nint maximum);

    /// <summary>A working set below this is left alone: trimming it again would only cost page faults.</summary>
    private const long IdleWorkingSet = 24 * 1024 * 1024;

    /// <summary>Managed memory grown by this much since the last compaction is worth another one.</summary>
    private const long HeapGrowth = 6 * 1024 * 1024;

    private static DispatcherTimer? _timer;
    private static DispatcherTimer? _activityTimer;
    private static long _heapAfterTrim;

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
    /// Something was done in the background: memory is looked at ten seconds later (one timer for all that happens
    /// meanwhile). Does nothing while the window is on the screen.
    /// </summary>
    public static void AfterActivity()
    {
        if (App.Instance.IsExiting || App.Instance.Main is { IsVisible: true, WindowState: not System.Windows.WindowState.Minimized }) return;
        _activityTimer ??= CreateActivityTimer();
        if (!_activityTimer.IsEnabled) _activityTimer.Start();
    }

    private static DispatcherTimer CreateActivityTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromSeconds(10) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (Environment.WorkingSet < IdleWorkingSet) return;
            if (GC.GetTotalMemory(false) - _heapAfterTrim > HeapGrowth) Trim();
            else TrimWorkingSet();
        };
        return timer;
    }

    public static void Trim()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        _heapAfterTrim = GC.GetTotalMemory(false);
        TrimWorkingSet();
    }

    private static void TrimWorkingSet()
    {
        using var process = Process.GetCurrentProcess();
        SetProcessWorkingSetSize(process.Handle, -1, -1);
    }
}
