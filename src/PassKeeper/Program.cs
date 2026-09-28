using System.Diagnostics;
using PassKeeper.Services;
using PassKeeper.Shared;

namespace PassKeeper;

public sealed class StartupOptions
{
    public bool Minimized { get; private init; }
    public bool Uninstall { get; private init; }
    public bool Quiet { get; private init; }
    public string? DataDirectory { get; private init; }
    public string? ScreenshotsDirectory { get; private init; }
    /// <summary>"--lang ru|en" limits the screenshots to one language.</summary>
    public string? ScreenshotsLanguage { get; private init; }
    public string? SelfTestReport { get; private init; }
    public string? SelfTestBrowser { get; private init; }

    public static StartupOptions Parse(string[] args)
    {
        bool Has(params string[] names) => args.Any(a => names.Contains(a.ToLowerInvariant()));
        string? Value(string name)
        {
            var i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        return new StartupOptions
        {
            Minimized = Has("--minimized", "/minimized", "-m"),
            Uninstall = Has("--uninstall", "/uninstall"),
            Quiet = Has("--quiet", "/quiet", "/s", "--silent"),
            DataDirectory = Value("--data"),
            ScreenshotsDirectory = Value("--screenshots"),
            ScreenshotsLanguage = Value("--lang"),
            SelfTestReport = Value("--selftest"),
            SelfTestBrowser = Value("--browser"),
        };
    }
}

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var options = StartupOptions.Parse(args);
        if (options.Uninstall) return StartUninstaller(options.Quiet);
        SingleInstance? instance = null;
        if (options.ScreenshotsDirectory == null && options.SelfTestReport == null)
        {
            instance = SingleInstance.TryAcquire();
            if (instance == null)
            {
                // Already running in this session: bring it to front (unless started by autostart).
                if (!options.Minimized) SingleInstance.Send("SHOW");
                return 0;
            }
        }

        var app = new App(options, instance);
        app.InitializeComponent();
        return app.Run();
    }

    /// <summary>
    /// "--uninstall" (the uninstall command of version 1.0.0) hands over to Uninstall.exe and exits at once,
    /// so that this executable does not keep the installation folder busy.
    /// </summary>
    public static int StartUninstaller(bool quiet)
    {
        var path = Path.Combine(AppPaths.ExeDirectory, InstallLayout.UninstallerName);
        if (!File.Exists(path)) return 1;
        Process.Start(new ProcessStartInfo(path, quiet ? "/S" : "") { UseShellExecute = true, WorkingDirectory = Path.GetTempPath() })?.Dispose();
        return 0;
    }
}
