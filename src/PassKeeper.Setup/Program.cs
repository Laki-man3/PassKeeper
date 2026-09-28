using System;
using System.Windows;
using PassKeeper.Shared;

namespace PassKeeper.Setup
{
    /// <summary>
    /// PassKeeper-Setup.exe [/S] [/allusers | /currentuser] [/desktop] [/autostart | /noautostart] [/nolaunch]
    ///                      ["/dir=C:\Path"] [/lang=ru|en]
    /// Uninstall.exe [/S] [/removedata] [/lang=ru|en]  — the same program built without the payload.
    /// Exit codes: 0 ok, 1 failure, 740 elevation required (silent all-users run without admin), 1602 cancelled.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            if (UninstallOptions.IsUninstallMode(args)) return Uninstall(args);

            var options = InstallOptions.Parse(args);
            if (!string.IsNullOrEmpty(options.Language)) Texts.Language = options.Language == "ru" ? "ru" : "en";

            if (options.Silent)
            {
                if (options.AllUsers && !InstallLayout.IsAdministrator()) return InstallerEngine.ExitElevationRequired;
                try
                {
                    InstallerEngine.Install(options, (p, s) => { });
                    if (options.Launch && !options.Elevated) InstallerEngine.LaunchApp(options.AllUsers, options.Directory);
                    return InstallerEngine.ExitOk;
                }
                catch (Exception ex)
                {
                    Log(ex);
                    return InstallerEngine.ExitFailed;
                }
            }

            CreateApplication();
            var window = new SetupWindow(options);
            return RunWindow(window, args, window.RenderPagesAsync);
        }

        private static int Uninstall(string[] args)
        {
            var options = UninstallOptions.Parse(args);
            var scope = UninstallEngine.DetectScope();
            var render = RenderDirectory(args);
            var machine = scope == true;
            UninstallEngine.ApplyLanguage(machine);
            var lang = Array.Find(args, a => a.StartsWith("/lang=", StringComparison.OrdinalIgnoreCase));
            if (lang != null) Texts.Language = lang.Substring(6).Trim('"').ToLowerInvariant() == "ru" ? "ru" : "en";

            if (scope == null && render == null)
            {
                if (!options.Silent) MessageBox.Show(Texts.T("UnNotInstalled"), Texts.T("UnTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                return InstallerEngine.ExitFailed;
            }

            if (options.Silent)
            {
                try
                {
                    string dataError;
                    return UninstallEngine.Run(machine, options, (p, s) => { }, out dataError);
                }
                catch (Exception ex)
                {
                    Log(ex);
                    return InstallerEngine.ExitFailed;
                }
            }

            CreateApplication();
            var window = new UninstallWindow(machine, options);
            return RunWindow(window, args, window.RenderPagesAsync);
        }

        private static void CreateApplication()
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/PassKeeper-Setup;component/SetupTheme.xaml"),
            });
        }

        private static int RunWindow(WizardWindow window, string[] args, Func<string, System.Threading.Tasks.Task> renderPages)
        {
            var app = Application.Current;
            var render = RenderDirectory(args);
            if (render != null)
            {
                window.Left = -30000;
                window.ShowActivated = false;
                window.Loaded += async (s, e) =>
                {
                    await renderPages(render);
                    window.Close();
                };
            }
            app.Run(window);
            return window.ExitCode;
        }

        /// <summary>"/render=dir": saves the pages as PNG files (documentation screenshots).</summary>
        private static string RenderDirectory(string[] args)
        {
            var render = Array.Find(args, a => a.StartsWith("/render=", StringComparison.OrdinalIgnoreCase));
            return render == null ? null : render.Substring(8).Trim('"');
        }

        private static void Log(Exception ex)
        {
            try
            {
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PassKeeper-Setup.log"),
                    DateTime.Now.ToString("s") + " " + ex + Environment.NewLine);
            }
            catch (Exception) { }
        }
    }
}
