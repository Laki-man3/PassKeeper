using Avalonia;

namespace PassKeeper.Desktop;

public static class Program
{
    /// <summary>Folder given with <c>--data &lt;dir&gt;</c>; otherwise the platform's place for application data.</summary>
    public static string? DataOverride { get; private set; }

    /// <summary><c>--minimized</c>: start in the tray (menu bar) without showing the window, as when starting with the system.</summary>
    public static bool StartMinimized { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] is "--data" or "-d") DataOverride = args[i + 1];
        StartMinimized = args.Contains("--minimized");
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
