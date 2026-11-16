using Avalonia;

namespace PassKeeper.Desktop;

public static class Program
{
    /// <summary>Folder given with <c>--data &lt;dir&gt;</c>; otherwise the platform's place for application data.</summary>
    public static string? DataOverride { get; private set; }

    /// <summary><c>--minimized</c>: start in the tray (menu bar) without showing the window, as when starting with the system.</summary>
    public static bool StartMinimized { get; private set; }

    /// <summary>Opt-in package smoke test; uses its own temporary vault, never the user's data.</summary>
    public static string? SmokeTestOutput { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] is "--data" or "-d") DataOverride = args[i + 1];
            if (args[i] == "--smoke-test") SmokeTestOutput = Path.GetFullPath(args[i + 1]);
        }
        if (SmokeTestOutput != null)
        {
            // Isolate all application state even if --data was also supplied.
            DataOverride = Path.Combine(Path.GetTempPath(), "PassKeeper-smoke-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(SmokeTestOutput);
        }
        StartMinimized = SmokeTestOutput == null && args.Contains("--minimized");
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            if (SmokeTestOutput != null && Directory.Exists(DataOverride)) Directory.Delete(DataOverride, recursive: true);
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
