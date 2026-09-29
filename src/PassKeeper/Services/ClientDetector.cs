using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using PassKeeper.Core.AutoType;
using PassKeeper.Core.Models;

namespace PassKeeper.Services;

/// <summary>An application window (or a running known client without a visible window) the user may sign in to.</summary>
public sealed class DetectedWindow
{
    public IntPtr Handle { get; init; }
    public string Title { get; init; } = "";
    public string ProcessName { get; init; } = "";
    public KnownApp? App { get; init; }

    /// <summary>Name to show and to use as the entry title.</summary>
    public string DisplayName => App?.Name ?? (Title.Length > 0 ? Title : ProcessName);

    /// <summary>Window patterns for an entry: the client's catalog patterns, otherwise the executable name.</summary>
    public List<string> Patterns => App != null ? [.. App.Patterns] : [ProcessName + ".exe"];

    public EntryCategory Category => App?.Category ?? EntryCategory.App;
}

/// <summary>Which input fields an application's login form has (found through UI Automation).</summary>
public sealed record SignInForm(bool HasLogin, bool HasPassword, bool HasPin, bool HasOtp)
{
    public static readonly SignInForm None = new(false, false, false, false);
    public bool Any => HasLogin || HasPassword || HasPin || HasOtp;
}

/// <summary>Finds open application windows and running sign-in clients (VPN, VDI, RDP) without manual input.</summary>
public static class ClientDetector
{
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

    private const uint GwOwner = 4;
    private const int DwmwaCloaked = 14;
    private const long WsExToolWindow = 0x00000080;

    /// <summary>Visible top-level windows of other applications, known clients first.</summary>
    public static List<DetectedWindow> OpenWindows()
    {
        var own = (uint)Environment.ProcessId;
        var handles = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            if (IsWindowVisible(hwnd) && GetWindow(hwnd, GwOwner) == IntPtr.Zero && !IsCloaked(hwnd) &&
                (Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64() & WsExToolWindow) == 0 &&
                Native.GetWindowTextLength(hwnd) > 0)
                handles.Add(hwnd);
            return true;
        }, IntPtr.Zero);

        var result = new List<DetectedWindow>();
        foreach (var hwnd in handles)
        {
            Native.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == own) continue;
            var process = ProcessName(pid);
            if (process.Length == 0 || process.Equals("explorer", StringComparison.OrdinalIgnoreCase) && Native.GetWindowClass(hwnd) == "Progman") continue;
            var title = Native.GetWindowTitle(hwnd);
            result.Add(new DetectedWindow { Handle = hwnd, Title = title, ProcessName = process, App = KnownApps.Match(process, title) });
        }
        return result.OrderBy(w => w.App == null).ThenBy(w => w.ProcessName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Known sign-in clients that are running now: their windows, plus clients sitting in the tray without a window.
    /// </summary>
    public static List<DetectedWindow> RunningClients()
    {
        var found = OpenWindows().Where(w => w.App != null).GroupBy(w => w.App!.Id).Select(g => g.First()).ToList();
        foreach (var app in KnownApps.All.Where(a => found.All(f => f.App!.Id != a.Id)))
        {
            foreach (var name in app.ProcessNames)
            {
                var processes = Process.GetProcessesByName(name);
                try
                {
                    if (processes.Length == 0) continue;
                    found.Add(new DetectedWindow { App = app, ProcessName = processes[0].ProcessName });
                    break;
                }
                finally
                {
                    foreach (var p in processes) p.Dispose();
                }
            }
        }
        return found;
    }

    /// <summary>Names under which the clients appear in "Settings → Apps" (uninstall registry entries).</summary>
    private static readonly Dictionary<string, string[]> InstallNames = new()
    {
        ["cisco"] = ["Cisco Secure Client", "Cisco AnyConnect"],
        ["checkpoint"] = ["Check Point Endpoint Security", "Endpoint Security VPN", "Check Point Mobile"],
        ["forticlient"] = ["FortiClient"],
        ["globalprotect"] = ["GlobalProtect"],
        ["openvpn"] = ["OpenVPN"],
        ["continent"] = ["Континент", "Continent"],
        ["vipnet"] = ["ViPNet Client"],
        ["basis"] = ["Basis Workplace", "Basis.Workplace", "Базис.WorkPlace", "Базис.Workplace"],
        ["citrix"] = ["Citrix Workspace"],
        ["horizon"] = ["Horizon Client"],
        ["termidesk"] = ["Termidesk"],
        ["rutoken"] = ["Рутокен", "Rutoken"],
        ["cryptopro"] = ["КриптоПро CSP", "CryptoPro CSP"],
        ["1c"] = ["1C:Enterprise", "1С:Предприятие"],
        ["sap"] = ["SAP GUI"],
        ["putty"] = ["PuTTY"],
        ["winscp"] = ["WinSCP"],
    };

    /// <summary>Known clients installed on this computer (per-machine and per-user installations).</summary>
    public static List<KnownApp> InstalledClients()
    {
        var names = new List<string>();
        foreach (var (hive, view) in new[]
                 {
                     (Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64),
                     (Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry32),
                     (Microsoft.Win32.RegistryHive.CurrentUser, Microsoft.Win32.RegistryView.Default),
                 })
        {
            try
            {
                using var root = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall == null) continue;
                foreach (var sub in uninstall.GetSubKeyNames())
                {
                    using var key = uninstall.OpenSubKey(sub);
                    if (key?.GetValue("DisplayName") is string name) names.Add(name);
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        }
        return KnownApps.All
            .Where(a => InstallNames.TryGetValue(a.Id, out var keys) && names.Any(n => keys.Any(k => n.Contains(k, StringComparison.OrdinalIgnoreCase))))
            .ToList();
    }

    public static DetectedWindow Describe(IntPtr hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        var process = ProcessName(pid);
        var title = Native.GetWindowTitle(hwnd);
        return new DetectedWindow { Handle = hwnd, Title = title, ProcessName = process, App = KnownApps.Match(process, title) };
    }

    /// <summary>Which sign-in fields the window shows (bounded: UI Automation may hang on a busy application).</summary>
    public static SignInForm InspectForm(IntPtr hwnd)
    {
        var task = Task.Run(() =>
        {
            try
            {
                var root = AutomationElement.FromHandle(hwnd);
                var edits = root.FindAll(TreeScope.Descendants, new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                    new PropertyCondition(AutomationElement.IsEnabledProperty, true)));
                bool login = false, password = false, pin = false, otp = false;
                foreach (AutomationElement edit in edits)
                {
                    var info = edit.Current;
                    if (info.IsOffscreen) continue;
                    switch (FieldClassifier.Classify(info.IsPassword, info.Name, info.AutomationId, info.HelpText))
                    {
                        case FieldKind.Password: password = true; break;
                        case FieldKind.Pin: pin = true; break;
                        case FieldKind.Otp: otp = true; break;
                        case FieldKind.Login or FieldKind.Email or FieldKind.Phone: login = true; break;
                        case null when !info.IsPassword && string.IsNullOrWhiteSpace(info.Name): login = true; break;
                    }
                }
                return new SignInForm(login && (password || pin), password, pin, otp);
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException or ArgumentException)
            {
                return SignInForm.None;
            }
        });
        return task.Wait(2000) ? task.Result : SignInForm.None;
    }

    private static bool IsCloaked(IntPtr hwnd) =>
        DwmGetWindowAttribute(hwnd, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

    private static string ProcessName(uint pid)
    {
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return "";
        }
    }
}
