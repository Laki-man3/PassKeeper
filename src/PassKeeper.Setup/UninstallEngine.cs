using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using PassKeeper.Shared;

namespace PassKeeper.Setup
{
    /// <summary>Uninstall.exe [/S] [/removedata] [/lang=ru|en]</summary>
    internal sealed class UninstallOptions
    {
        public bool Silent;
        public bool Elevated;
        public bool RemoveData;
        public int ParentPid;

        public static bool IsUninstallMode(string[] args)
        {
            return args.Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase) || a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase))
                   || !InstallerEngine.HasPayload();
        }

        public static UninstallOptions Parse(string[] args)
        {
            var o = new UninstallOptions();
            foreach (var raw in args)
            {
                var lower = raw.Trim().ToLowerInvariant();
                if (lower == "/s" || lower == "/silent" || lower == "/quiet" || lower == "--quiet") o.Silent = true;
                else if (lower == "/removedata") o.RemoveData = true;
                else if (lower == "/elevated") o.Elevated = true;
                else if (lower.StartsWith("/parentpid=")) int.TryParse(lower.Substring(11), out o.ParentPid);
            }
            return o;
        }
    }

    internal static class UninstallEngine
    {
        public static string OwnPath
        {
            get { using (var p = Process.GetCurrentProcess()) return p.MainModule.FileName; }
        }

        public static string InstallDirectory => Path.GetDirectoryName(OwnPath);

        /// <summary>true: all users, false: current user, null: this folder is not a registered installation.</summary>
        public static bool? DetectScope()
        {
            var dir = InstallDirectory;
            if (InstallLayout.SamePath(InstallLayout.ReadInstallLocation(true), dir)) return true;
            if (InstallLayout.SamePath(InstallLayout.ReadInstallLocation(false), dir)) return false;
            return null;
        }

        public static bool HasUserData => Directory.Exists(InstallLayout.UserDataDirectory());

        /// <summary>UI language: the one the user chose in the app, then the installer's, then Windows'.</summary>
        public static void ApplyLanguage(bool machine)
        {
            try
            {
                var settings = Path.Combine(InstallLayout.UserDataDirectory(), "settings.json");
                if (File.Exists(settings))
                {
                    var m = Regex.Match(File.ReadAllText(settings), "\"language\"\\s*:\\s*\"(ru|en)\"", RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        Texts.Language = m.Groups[1].Value.ToLowerInvariant();
                        return;
                    }
                }
            }
            catch (Exception) { }
            var lang = InstallLayout.ReadInstallerValue(machine, InstallLayout.LanguageValue) as string;
            if (lang == "ru" || lang == "en") Texts.Language = lang;
        }

        /// <summary>
        /// Full uninstall for the invoking user. A machine-wide installation is removed by an elevated copy
        /// (/elevated) first; the user's own autostart entry and, if requested, data are removed only after that succeeded.
        /// </summary>
        public static int Run(bool machine, UninstallOptions o, Action<double, string> progress, out string dataError)
        {
            dataError = null;
            if (o.Elevated)
            {
                RemoveProgram(machine, o.ParentPid);
                return InstallerEngine.ExitOk;
            }

            progress(0.1, Texts.T("UnStepStopping"));
            InstallerEngine.StopRunningInstance(Path.Combine(InstallDirectory, InstallLayout.ExeName));

            progress(0.4, Texts.T("UnStepFiles"));
            if (machine && !InstallLayout.IsAdministrator())
            {
                var code = RunElevated();
                if (code != InstallerEngine.ExitOk) return code;
            }
            else
            {
                RemoveProgram(machine, 0);
            }

            progress(0.8, Texts.T(o.RemoveData ? "UnStepData" : "UnStepFiles"));
            TryDeleteValue(Registry.CurrentUser, InstallLayout.RunKeyPath, InstallLayout.RunValueName);
            TryDeleteValue(Registry.CurrentUser, StartupApprovedPath, InstallLayout.RunValueName);
            TryDeleteKey(Registry.CurrentUser, InstallLayout.AppKeyPath);
            if (o.RemoveData) dataError = DeleteUserData();
            progress(1, Texts.T("StepDone"));
            return InstallerEngine.ExitOk;
        }

        private static int RunElevated()
        {
            try
            {
                var args = "/uninstall /S /elevated /parentpid=" + Process.GetCurrentProcess().Id;
                using (var p = Process.Start(new ProcessStartInfo(OwnPath, args) { UseShellExecute = true, Verb = "runas" }))
                {
                    p.WaitForExit();
                    return p.ExitCode;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return InstallerEngine.ExitCancelled;
            }
        }

        /// <summary>Enabled/disabled state of autostart entries kept by Task Manager ("Startup apps").</summary>
        private const string StartupApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        private static void RemoveProgram(bool machine, int parentPid)
        {
            // Copies of other users are ended by the elevated uninstaller of an all-users installation.
            RunningCopies.Close(InstallDirectory, 3000);
            TryDeleteFile(InstallLayout.LegacyStartMenuShortcut(machine));
            TryDeleteDirectory(InstallLayout.StartMenuFolder(machine));
            TryDeleteFile(InstallLayout.DesktopShortcut(machine));
            using (var root = InstallLayout.OpenRoot(machine))
            {
                TryDeleteKey(root, InstallLayout.UninstallKeyPath);
                TryDeleteKey(root, InstallLayout.AppKeyPath);
                TryDeleteValue(root, InstallLayout.RunKeyPath, InstallLayout.RunValueName);
                TryDeleteValue(root, StartupApprovedPath, InstallLayout.RunValueName);
            }
            ScheduleFolderRemoval(InstallDirectory, parentPid);
        }

        /// <summary>Deletes %APPDATA%\PassKeeper. Returns an error description, or null.</summary>
        private static string DeleteUserData()
        {
            var dir = InstallLayout.UserDataDirectory();
            if (!Directory.Exists(dir)) return null;
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    Directory.Delete(dir, true);
                    return null;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    if (attempt == 4) return ex.Message;
                    System.Threading.Thread.Sleep(500);
                }
            }
        }

        /// <summary>
        /// A running executable cannot delete itself: a hidden cmd script waits for the uninstaller to exit and removes
        /// the folder, retrying for a while if a file is still busy (antivirus scan, a closing copy).
        /// </summary>
        private static void ScheduleFolderRemoval(string dir, int parentPid)
        {
            var manifest = Path.Combine(dir, InstallLayout.ManifestFileName);
            if (!File.Exists(manifest)) return;
            var trimmed = dir.TrimEnd('\\');
            var name = Path.GetFileName(trimmed);
            var parent = Path.GetDirectoryName(trimmed);
            var pid = Process.GetCurrentProcess().Id;
            var script = new StringBuilder();
            script.AppendLine("@echo off");
            script.AppendLine("chcp 65001 >nul");
            script.AppendLine(":wait");
            foreach (var p in new[] { pid, parentPid }.Where(p => p > 0))
                script.AppendLine("tasklist /FI \"PID eq " + p + "\" 2>nul | find \" " + p + " \" >nul && (ping -n 2 127.0.0.1 >nul & goto wait)");
            script.AppendLine("set tries=0");
            script.AppendLine(":remove");
            string check;
            if (name.Equals(InstallLayout.AppName, StringComparison.OrdinalIgnoreCase))
            {
                script.AppendLine("rmdir /s /q \"" + name + "\" 2>nul");
                check = name;
            }
            else
            {
                // Custom folder chosen by the user: remove only what the installer put there.
                foreach (var rel in File.ReadAllLines(manifest).Where(l => l.Length > 0 && !l.Contains("..")))
                    script.AppendLine("del /f /q \"" + Path.Combine(dir, rel) + "\" 2>nul");
                script.AppendLine("del /f /q \"" + manifest + "\" 2>nul");
                script.AppendLine("rmdir \"" + dir + "\" 2>nul");
                check = manifest;
            }
            script.AppendLine("if not exist \"" + check + "\" goto done");
            script.AppendLine("set /a tries+=1");
            script.AppendLine("if %tries% geq 30 goto done");
            script.AppendLine("ping -n 2 127.0.0.1 >nul");
            script.AppendLine("goto remove");
            script.AppendLine(":done");
            script.AppendLine("del \"%~f0\"");
            var path = Path.Combine(Path.GetTempPath(), "passkeeper-uninstall-" + pid + ".cmd");
            File.WriteAllText(path, script.ToString(), new UTF8Encoding(false));
            Process.Start(new ProcessStartInfo("cmd.exe", "/c \"" + path + "\"")
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

        private static void TryDeleteKey(RegistryKey root, string path)
        {
            try { root.DeleteSubKeyTree(path, false); } catch (Exception) { }
        }

        private static void TryDeleteValue(RegistryKey root, string keyPath, string name)
        {
            try
            {
                using (var key = root.OpenSubKey(keyPath, true))
                {
                    if (key != null) key.DeleteValue(name, false);
                }
            }
            catch (Exception) { }
        }
    }
}
