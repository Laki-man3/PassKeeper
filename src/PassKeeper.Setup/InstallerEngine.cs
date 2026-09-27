using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.Win32;
using PassKeeper.Shared;

namespace PassKeeper.Setup
{
    internal sealed class InstallOptions
    {
        public bool AllUsers;
        public string Directory;
        public bool DesktopShortcut;
        public bool Autostart = true;
        public bool Launch = true;
        public bool Silent;
        public bool Elevated;
        public string Language;

        public static InstallOptions Parse(string[] args)
        {
            var o = new InstallOptions();
            foreach (var raw in args)
            {
                var a = raw.Trim();
                var lower = a.ToLowerInvariant();
                if (lower == "/s" || lower == "/silent" || lower == "--silent" || lower == "/quiet") o.Silent = true;
                else if (lower == "/allusers" || lower == "--allusers") o.AllUsers = true;
                else if (lower == "/currentuser" || lower == "--currentuser") o.AllUsers = false;
                else if (lower == "/desktop") o.DesktopShortcut = true;
                else if (lower == "/nodesktop") o.DesktopShortcut = false;
                else if (lower == "/autostart") o.Autostart = true;
                else if (lower == "/noautostart") o.Autostart = false;
                else if (lower == "/nolaunch") o.Launch = false;
                else if (lower == "/elevated") o.Elevated = true;
                else if (lower.StartsWith("/dir=")) o.Directory = a.Substring(5).Trim('"');
                else if (lower.StartsWith("/lang=")) o.Language = a.Substring(6).Trim('"').ToLowerInvariant();
            }
            if (o.Silent && !args.Any(x => x.Equals("/autostart", StringComparison.OrdinalIgnoreCase))) o.Autostart = false;
            return o;
        }

        public string ToArguments()
        {
            var parts = new List<string> { "/S", AllUsers ? "/allusers" : "/currentuser", "/nolaunch", "/elevated" };
            parts.Add(DesktopShortcut ? "/desktop" : "/nodesktop");
            parts.Add(Autostart ? "/autostart" : "/noautostart");
            if (!string.IsNullOrEmpty(Directory)) parts.Add("\"/dir=" + Directory + "\"");
            if (!string.IsNullOrEmpty(Language)) parts.Add("/lang=" + Language);
            return string.Join(" ", parts);
        }
    }

    internal static class InstallerEngine
    {
        public const int ExitOk = 0;
        public const int ExitFailed = 1;
        public const int ExitCancelled = 1602;
        public const int ExitElevationRequired = 740;

        public static string Version
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return v.Major + "." + v.Minor + "." + v.Build;
            }
        }

        public static bool HasPayload()
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceInfo("payload.zip") != null;
        }

        /// <summary>Existing installation of this scope (for upgrades), or null.</summary>
        public static string ExistingDirectory(bool allUsers)
        {
            var dir = InstallLayout.ReadInstallLocation(allUsers);
            return !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, InstallLayout.ExeName)) ? dir : null;
        }

        public static void Install(InstallOptions o, Action<double, string> progress)
        {
            if (!HasPayload()) throw new InvalidOperationException(Texts.T("NoPayload"));
            var dir = string.IsNullOrWhiteSpace(o.Directory) ? (ExistingDirectory(o.AllUsers) ?? InstallLayout.DefaultInstallDir(o.AllUsers)) : o.Directory;
            dir = Path.GetFullPath(dir);
            var exe = Path.Combine(dir, InstallLayout.ExeName);

            progress(0.02, Texts.T("StepStopping"));
            StopRunningInstance(exe);

            progress(0.05, Texts.T("StepCopying"));
            Directory.CreateDirectory(dir);
            var oldManifest = ReadManifest(dir);
            var installed = ExtractPayload(dir, (p) => progress(0.05 + p * 0.8, Texts.T("StepCopying")));

            foreach (var stale in oldManifest.Except(installed, StringComparer.OrdinalIgnoreCase))
            {
                try { File.Delete(Path.Combine(dir, stale)); } catch (Exception) { }
            }
            File.WriteAllLines(Path.Combine(dir, InstallLayout.ManifestFileName), installed);

            progress(0.9, Texts.T("StepShortcuts"));
            ShellLink.Create(InstallLayout.StartMenuShortcut(o.AllUsers), exe, "", Texts.T("ShortcutDescription"));
            var desktop = InstallLayout.DesktopShortcut(o.AllUsers);
            if (o.DesktopShortcut) ShellLink.Create(desktop, exe, "", Texts.T("ShortcutDescription"));
            else if (File.Exists(desktop)) File.Delete(desktop);

            progress(0.95, Texts.T("StepRegistry"));
            RegisterUninstall(o.AllUsers, dir, exe, installed);
            using (var root = InstallLayout.OpenRoot(o.AllUsers))
            using (var run = root.CreateSubKey(InstallLayout.RunKeyPath))
            {
                if (o.Autostart) run.SetValue(InstallLayout.RunValueName, "\"" + exe + "\" --minimized");
                else if (o.AllUsers) run.DeleteValue(InstallLayout.RunValueName, false);
            }
            progress(1, Texts.T("StepDone"));
        }

        private static List<string> ReadManifest(string dir)
        {
            var path = Path.Combine(dir, InstallLayout.ManifestFileName);
            try
            {
                return File.Exists(path) ? File.ReadAllLines(path).Where(l => l.Length > 0 && !l.Contains("..")).ToList() : new List<string>();
            }
            catch (IOException)
            {
                return new List<string>();
            }
        }

        private static List<string> ExtractPayload(string dir, Action<double> progress)
        {
            var root = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
            var files = new List<string>();
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                long total = Math.Max(1, zip.Entries.Sum(e => e.Length)), done = 0;
                foreach (var entry in zip.Entries)
                {
                    var target = Path.GetFullPath(Path.Combine(dir, entry.FullName));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Invalid path in payload: " + entry.FullName);
                    if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\"))
                    {
                        Directory.CreateDirectory(target);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    WriteFile(entry, target);
                    files.Add(target.Substring(root.Length));
                    done += entry.Length;
                    progress((double)done / total);
                }
            }
            return files;
        }

        /// <summary>Writes a file; a locked file (running app of another user) is renamed to *.old first.</summary>
        private static void WriteFile(ZipArchiveEntry entry, string target)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    entry.ExtractToFile(target, true);
                    return;
                }
                catch (IOException)
                {
                    if (!File.Exists(target)) throw;
                    var old = target + "." + Guid.NewGuid().ToString("N").Substring(0, 6) + ".old";
                    try
                    {
                        File.Move(target, old);
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(500);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    if (attempt == 2) throw;
                    Thread.Sleep(500);
                }
            }
            entry.ExtractToFile(target, true);
        }

        private static void RegisterUninstall(bool allUsers, string dir, string exe, List<string> files)
        {
            long size = 0;
            foreach (var f in files)
            {
                try { size += new FileInfo(Path.Combine(dir, f)).Length; } catch (Exception) { }
            }
            using (var root = InstallLayout.OpenRoot(allUsers))
            using (var key = root.CreateSubKey(InstallLayout.UninstallKeyPath))
            {
                key.SetValue("DisplayName", "PassKeeper");
                key.SetValue("DisplayVersion", Version);
                key.SetValue("Publisher", InstallLayout.Publisher);
                key.SetValue("DisplayIcon", exe + ",0");
                key.SetValue("InstallLocation", dir);
                key.SetValue("UninstallString", "\"" + exe + "\" --uninstall");
                key.SetValue("QuietUninstallString", "\"" + exe + "\" --uninstall --quiet");
                key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                key.SetValue("EstimatedSize", (int)(size / 1024), RegistryValueKind.DWord);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("Comments", allUsers ? "All users" : "Current user");
            }
        }

        /// <summary>Asks a running PassKeeper of the current user to exit and waits for processes from the target folder.</summary>
        private static void StopRunningInstance(string exe)
        {
            try
            {
                using (var client = new NamedPipeClientStream(".", InstallLayout.PipeName(), PipeDirection.Out))
                {
                    client.Connect(800);
                    using (var w = new StreamWriter(client))
                    {
                        w.WriteLine("EXIT");
                        w.Flush();
                    }
                }
            }
            catch (Exception)
            {
                // not running
            }

            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < deadline)
            {
                var running = Process.GetProcessesByName("PassKeeper").Where(p => SameExe(p, exe)).ToList();
                if (running.Count == 0) return;
                Thread.Sleep(300);
            }
        }

        private static bool SameExe(Process p, string exe)
        {
            try
            {
                return InstallLayout.SamePath(p.MainModule.FileName, exe);
            }
            catch (Exception)
            {
                return false; // other user's process (no access) - its files are renamed if locked
            }
        }

        public static void LaunchApp(bool allUsers, string dir)
        {
            dir = string.IsNullOrWhiteSpace(dir) ? (ExistingDirectory(allUsers) ?? InstallLayout.DefaultInstallDir(allUsers)) : dir;
            var exe = Path.Combine(dir, InstallLayout.ExeName);
            if (File.Exists(exe)) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = dir });
        }

        /// <summary>Runs this setup elevated for an all-users installation and waits for it.</summary>
        public static int RunElevated(InstallOptions o)
        {
            try
            {
                var self = Process.GetCurrentProcess().MainModule.FileName;
                var p = Process.Start(new ProcessStartInfo(self, o.ToArguments()) { UseShellExecute = true, Verb = "runas" });
                p.WaitForExit();
                return p.ExitCode;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return ExitCancelled;
            }
        }
    }
}
