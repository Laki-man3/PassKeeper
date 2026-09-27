using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace PassKeeper.Services;

public static class ThemeService
{
    private static string _mode = "dark";

    public static bool IsDark { get; private set; } = true;
    public static event Action? Changed;

    public static void Initialize()
    {
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (_mode == "system" && e.Category == UserPreferenceCategory.General)
                Application.Current?.Dispatcher.BeginInvoke(() => Apply(_mode));
        };
    }

    public static void Apply(string mode)
    {
        _mode = mode is "light" or "system" ? mode : "dark";
        var dark = _mode == "dark" || (_mode == "system" && IsSystemDark());
        var app = Application.Current;
        if (app == null) return;
        var dict = new ResourceDictionary { Source = new Uri($"pack://application:,,,/PassKeeper;component/Themes/{(dark ? "Dark" : "Light")}.xaml") };
        var merged = app.Resources.MergedDictionaries;
        if (merged.Count > 0) merged[0] = dict;
        else merged.Add(dict);
        IsDark = dark;
        foreach (Window w in app.Windows) ApplyWindowFrame(w);
        Changed?.Invoke();
    }

    public static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Dark title bar / rounded corners on Windows 10 2004+ and 11.</summary>
    public static void ApplyWindowFrame(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var dark = IsDark ? 1 : 0;
        Native.DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
        var round = 2;
        Native.DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int)); // DWMWA_WINDOW_CORNER_PREFERENCE = round
    }
}
