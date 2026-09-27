using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using PassKeeper.Localization;
using PassKeeper.Services;
using PassKeeper.Shared;

namespace PassKeeper.Setup;

/// <summary>
/// "PassKeeper.exe --uninstall [--quiet]" (registered as UninstallString). Removes shortcuts, registry entries and the
/// installation folder; optionally the current user's vault. Machine-wide installations are removed by an elevated copy.
/// </summary>
public static class Uninstaller
{
    public static void Run(StartupOptions options)
    {
        ThemeService.Apply("dark");
        var app = Application.Current;
        var machine = AppPaths.IsMachineInstall;
        var user = AppPaths.IsUserInstall;

        if (!machine && !user)
        {
            if (!options.Quiet) Message(Loc.T("Uninstall.NotInstalled"));
            app.Shutdown(1);
            return;
        }

        var removeData = false;
        if (!options.Quiet && !options.Elevated)
        {
            var window = new UninstallWindow();
            if (window.ShowDialog() != true)
            {
                app.Shutdown(1);
                return;
            }
            removeData = window.RemoveData;
        }

        // Per-user parts are always handled by the (non-elevated) invoking user.
        if (!options.Elevated)
        {
            SingleInstance.Send("EXIT", 1500);
            Thread.Sleep(800);
            TryDeleteValue(Registry.CurrentUser, InstallLayout.RunKeyPath, InstallLayout.RunValueName);
            if (removeData) TryDeleteDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PassKeeper"));
        }

        if (machine && !InstallLayout.IsAdministrator())
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo(AppPaths.ExePath, $"--uninstall --quiet --elevated --parent-pid {Environment.ProcessId}") { UseShellExecute = true, Verb = "runas" });
                p?.WaitForExit();
                if (!options.Quiet) Message(p?.ExitCode == 0 ? Loc.T("Uninstall.Done") : Loc.T("Uninstall.Failed"));
                app.Shutdown(p?.ExitCode ?? 1);
            }
            catch (Win32Exception)
            {
                if (!options.Quiet) Message(Loc.T("Uninstall.NeedAdmin"));
                app.Shutdown(1);
            }
            return;
        }

        try
        {
            RemoveInstallation(machine, options.ParentPid);
            if (!options.Quiet && !options.Elevated) Message(Loc.T("Uninstall.Done"));
            app.Shutdown(0);
        }
        catch (Exception ex)
        {
            if (!options.Quiet) Message(Loc.F("Common.ErrorFormat", ex.Message));
            app.Shutdown(1);
        }
    }

    private static void RemoveInstallation(bool machine, int? parentPid)
    {
        TryDeleteFile(InstallLayout.StartMenuShortcut(machine));
        TryDeleteFile(InstallLayout.DesktopShortcut(machine));
        using (var root = InstallLayout.OpenRoot(machine))
        {
            try { root.DeleteSubKeyTree(InstallLayout.UninstallKeyPath, throwOnMissingSubKey: false); } catch (Exception) { }
            TryDeleteValue(root, InstallLayout.RunKeyPath, InstallLayout.RunValueName);
        }
        ScheduleFolderRemoval(AppPaths.ExeDirectory, parentPid);
    }

    /// <summary>The running executable cannot delete itself: a hidden cmd script waits for this process to exit.</summary>
    private static void ScheduleFolderRemoval(string dir, int? parentPid)
    {
        var manifest = Path.Combine(dir, InstallLayout.ManifestFileName);
        var name = Path.GetFileName(dir.TrimEnd('\\'));
        var parent = Path.GetDirectoryName(dir.TrimEnd('\\'))!;
        var pid = Environment.ProcessId;
        var script = new StringBuilder();
        script.AppendLine("@echo off");
        script.AppendLine("chcp 65001 >nul");
        script.AppendLine(":wait");
        foreach (var p in new[] { pid, parentPid ?? 0 }.Where(p => p > 0))
            script.AppendLine($"tasklist /FI \"PID eq {p}\" 2>nul | find \" {p} \" >nul && (ping -n 2 127.0.0.1 >nul & goto wait)");
        if (name.Equals(InstallLayout.AppName, StringComparison.OrdinalIgnoreCase) && File.Exists(manifest))
        {
            script.AppendLine($"rmdir /s /q \"{name}\"");
        }
        else if (File.Exists(manifest))
        {
            foreach (var rel in File.ReadAllLines(manifest).Where(l => l.Length > 0 && !l.Contains("..")))
                script.AppendLine($"del /f /q \"{Path.Combine(dir, rel)}\"");
            script.AppendLine($"del /f /q \"{manifest}\"");
            script.AppendLine($"rmdir \"{dir}\"");
        }
        script.AppendLine("del \"%~f0\"");
        var path = Path.Combine(Path.GetTempPath(), $"passkeeper-uninstall-{pid}.cmd");
        File.WriteAllText(path, script.ToString(), new UTF8Encoding(false));
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{path}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = parent,
        });
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch (Exception) { }
    }

    private static void TryDeleteValue(RegistryKey root, string keyPath, string name)
    {
        try
        {
            using var key = root.OpenSubKey(keyPath, writable: true);
            key?.DeleteValue(name, throwOnMissingValue: false);
        }
        catch (Exception) { }
    }

    private static void Message(string text) =>
        MessageBox.Show(text, "PassKeeper", MessageBoxButton.OK, MessageBoxImage.Information);
}

/// <summary>Confirmation window of the uninstaller.</summary>
public sealed class UninstallWindow : Window
{
    private readonly CheckBox _removeData;

    public UninstallWindow()
    {
        Title = Loc.T("Uninstall.Title");
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = (FontFamily)FindResource("Font.Ui");
        SetResourceReference(BackgroundProperty, "Brush.Surface");
        SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Brush.Text");
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/PassKeeper;component/Assets/PassKeeper.ico"));
        SourceInitialized += (_, _) => ThemeService.ApplyWindowFrame(this);

        var root = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new Image { Source = (ImageSource)FindResource("LogoImage"), Width = 40, Height = 40 });
        head.Children.Add(new TextBlock { Text = Loc.T("Uninstall.Title"), Style = (Style)FindResource("Text.H2"), FontSize = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) });
        root.Children.Add(head);
        root.Children.Add(new TextBlock { Text = Loc.T("Uninstall.Text"), Style = (Style)FindResource("Text.Secondary"), Margin = new Thickness(0, 16, 0, 0) });
        _removeData = new CheckBox { Content = Loc.T("Uninstall.RemoveData"), Margin = new Thickness(0, 16, 0, 0) };
        root.Children.Add(_removeData);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        var cancel = new Button { Style = (Style)FindResource("Btn.Ghost"), Content = Loc.T("Common.Cancel"), IsCancel = true };
        cancel.Click += (_, _) => DialogResult = false;
        var ok = new Button { Style = (Style)FindResource("Btn.Danger"), Content = Loc.T("Uninstall.Button"), Margin = new Thickness(8, 0, 0, 0), MinWidth = 120 };
        ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);
        Content = root;
    }

    public bool RemoveData => _removeData.IsChecked == true;
}
