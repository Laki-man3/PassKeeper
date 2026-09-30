using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace PassKeeper.Services;

/// <summary>
/// Gives memory back to Windows once PassKeeper goes to the background (hidden to the tray, minimized, locked):
/// a compacting collection returns free heap to the system and the working set is emptied. What is needed again is
/// paged back in when the window reappears.
/// </summary>
public static class MemoryTrimmer
{
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, nint minimum, nint maximum);

    private static DispatcherTimer? _timer;

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

    public static void Trim()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        using var process = Process.GetCurrentProcess();
        SetProcessWorkingSetSize(process.Handle, -1, -1);
    }
}
