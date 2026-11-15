param([int]$ProcessId, [string]$Out)
# Picture of one window of the process (PrintWindow: the window draws itself, whatever covers it on the screen).
Add-Type -AssemblyName System.Drawing
if (-not ("PkCapture" -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
public static class PkCapture
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    public static Bitmap Capture(int pid)
    {
        SetProcessDPIAware();
        IntPtr best = IntPtr.Zero; long area = 0;
        EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); RECT r; if (p == pid && IsWindowVisible(h) && GetWindowRect(h, out r)) { long a = (long)(r.Right - r.Left) * (r.Bottom - r.Top); if (a > area) { area = a; best = h; } } return true; }, IntPtr.Zero);
        if (best == IntPtr.Zero) return null;
        RECT rect; GetWindowRect(best, out rect);
        var bmp = new Bitmap(rect.Right - rect.Left, rect.Bottom - rect.Top);
        using (var g = Graphics.FromImage(bmp)) { var hdc = g.GetHdc(); PrintWindow(best, hdc, 2); g.ReleaseHdc(hdc); }
        return bmp;
    }
}
"@ -ReferencedAssemblies System.Drawing
}
$bmp = [PkCapture]::Capture($ProcessId)
if ($bmp) { $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose(); "saved $Out" } else { "no window" }
