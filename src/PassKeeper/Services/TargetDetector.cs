using System.Diagnostics;
using System.Windows.Automation;
using PassKeeper.Core.Matching;

namespace PassKeeper.Services;

/// <summary>The window the user is working in (auto-type / autofill target).</summary>
public sealed class TargetWindow
{
    public IntPtr Handle { get; init; }
    public string Title { get; init; } = "";
    public string ProcessName { get; init; } = "";
    public string? Url { get; set; }
    public bool IsBrowser { get; init; }

    public TargetContext ToContext() => new() { WindowTitle = Title, ProcessName = ProcessName, Url = Url, IsBrowser = IsBrowser };

    public string Describe()
    {
        var host = DomainUtil.GetHost(Url);
        if (host != null) return host;
        return Title.Length > 60 ? Title[..57] + "…" : Title;
    }
}

public static class TargetDetector
{
    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "opera", "opera_gx", "brave", "vivaldi", "browser", "yandex", "chromium", "waterfox",
        "librewolf", "floorp", "zen", "thorium", "iexplore", "atom", "centbrowser", "dragon", "epic", "iridium", "sputnik",
        "palemoon", "seamonkey", "maxthon", "360chrome", "arc", "coowon", "chromium-gost",
    };

    private static readonly HashSet<string> GeckoBrowsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "firefox", "waterfox", "librewolf", "floorp", "zen", "palemoon", "seamonkey",
    };

    public static bool IsBrowserProcess(string processName) => Browsers.Contains(processName);

    public static TargetWindow Capture(IntPtr hwnd, bool detectUrl, AutomationElement? focused = null)
    {
        var title = Native.GetWindowTitle(hwnd);
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        var processName = "";
        try
        {
            using var p = Process.GetProcessById((int)pid);
            processName = p.ProcessName;
        }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }

        var isBrowser = IsBrowserProcess(processName);
        var target = new TargetWindow { Handle = hwnd, Title = title, ProcessName = processName, IsBrowser = isBrowser };
        if (isBrowser && detectUrl) target.Url = GetBrowserUrl(hwnd, processName, focused);
        return target;
    }

    /// <summary>Reads the page URL through UI Automation (document value or address bar). Bounded to ~1.5 s.</summary>
    public static string? GetBrowserUrl(IntPtr hwnd, string processName, AutomationElement? focused)
    {
        var task = Task.Run(() =>
        {
            try
            {
                focused ??= FocusedIn(hwnd);
                if (focused != null)
                {
                    if (FromDocument(focused) is { } fromDoc) return fromDoc;
                    if (Msaa.DocumentUrlAt(focused.Current.BoundingRectangle) is { } fromMsaa && NormalizeUrl(fromMsaa) is { } msaaUrl) return msaaUrl;
                }
                var root = AutomationElement.FromHandle(hwnd);
                AutomationElement? bar;
                if (GeckoBrowsers.Contains(processName))
                {
                    bar = root.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.AutomationIdProperty, "urlbar-input"));
                }
                else
                {
                    bar = root.FindFirst(TreeScope.Descendants, new AndCondition(
                              new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                              new PropertyCondition(AutomationElement.ClassNameProperty, "OmniboxViewViews")))
                          ?? root.FindFirst(TreeScope.Descendants,
                              new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
                }
                if (bar != null && bar.TryGetCurrentPattern(ValuePattern.Pattern, out var p))
                    return NormalizeUrl(((ValuePattern)p).Current.Value);
            }
            catch (ElementNotAvailableException) { }
            catch (System.Runtime.InteropServices.COMException) { }
            catch (InvalidOperationException) { }
            catch (ArgumentException) { }
            return null;
        });
        return task.Wait(1500) ? task.Result : null;
    }

    /// <summary>The element with keyboard focus, if it belongs to the given window's process.</summary>
    private static AutomationElement? FocusedIn(IntPtr hwnd)
    {
        try
        {
            Native.GetWindowThreadProcessId(hwnd, out var pid);
            var focused = AutomationElement.FocusedElement;
            return focused != null && focused.Current.ProcessId == pid ? focused : null;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? FromDocument(AutomationElement element)
    {
        var walker = TreeWalker.ControlViewWalker;
        var current = element;
        for (var depth = 0; current != null && depth < 40; depth++)
        {
            if (current.Current.ControlType == ControlType.Document &&
                current.TryGetCurrentPattern(ValuePattern.Pattern, out var p) &&
                NormalizeUrl(((ValuePattern)p).Current.Value) is { } url)
                return url;
            current = walker.GetParent(current);
        }
        return null;
    }

    private static string? NormalizeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value.Contains(' ') || !value.Contains('.') && !value.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase)) return null;
        if (value.Length > 2 && value[1] == ':' && value[2] is '/' or '\\') return null; // local file path shown by the address bar
        if (value.StartsWith("about:", StringComparison.OrdinalIgnoreCase) || value.StartsWith("chrome", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("edge:", StringComparison.OrdinalIgnoreCase) || value.StartsWith("browser:", StringComparison.OrdinalIgnoreCase))
            return null;
        return DomainUtil.GetHost(value) != null ? value : null;
    }
}
