using System;
using System.Windows;
using PassKeeper.Shared;

namespace PassKeeper.Setup
{
    /// <summary>
    /// PassKeeper-Setup.exe [/S] [/allusers | /currentuser] [/desktop] [/autostart | /noautostart] [/nolaunch]
    ///                      ["/dir=C:\Path"] [/lang=ru|en]
    /// Exit codes: 0 ok, 1 failure, 740 elevation required (silent all-users run without admin), 1602 cancelled.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
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
                    try
                    {
                        System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PassKeeper-Setup.log"),
                            DateTime.Now.ToString("s") + " " + ex + Environment.NewLine);
                    }
                    catch (Exception) { }
                    return InstallerEngine.ExitFailed;
                }
            }

            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/PassKeeper-Setup;component/SetupTheme.xaml"),
            });
            var window = new SetupWindow(options);
            var render = Array.Find(args, a => a.StartsWith("/render=", StringComparison.OrdinalIgnoreCase));
            if (render != null)
            {
                window.Left = -30000;
                window.ShowActivated = false;
                window.Loaded += async (s, e) =>
                {
                    await window.RenderPagesAsync(render.Substring(8).Trim('"'));
                    window.Close();
                };
            }
            app.Run(window);
            return window.ExitCode;
        }
    }
}
