using PassKeeper.Services;

namespace PassKeeper;

public sealed class StartupOptions
{
    public bool Minimized { get; private init; }
    public bool Uninstall { get; private init; }
    public bool Quiet { get; private init; }
    public bool Elevated { get; private init; }
    public string? DataDirectory { get; private init; }
    public string? ScreenshotsDirectory { get; private init; }
    public int? ParentPid { get; private init; }
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
            Elevated = Has("--elevated"),
            DataDirectory = Value("--data"),
            ScreenshotsDirectory = Value("--screenshots"),
            ParentPid = int.TryParse(Value("--parent-pid"), out var pp) ? pp : null,
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
        SingleInstance? instance = null;
        if (!options.Uninstall && options.ScreenshotsDirectory == null && options.SelfTestReport == null)
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
}
