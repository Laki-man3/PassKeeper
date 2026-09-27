// Shared between the application (net10.0) and the installer (net48): keep to C# features available on both.
#nullable disable
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using Microsoft.Win32;

namespace PassKeeper.Shared
{
    internal static class InstallLayout
    {
        public const string AppName = "PassKeeper";
        public const string ExeName = "PassKeeper.exe";
        public const string Publisher = "PassKeeper";
        public const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\PassKeeper";
        public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string RunValueName = "PassKeeper";
        public const string ManifestFileName = "install.manifest";
        public const string PortableMarker = "portable.txt";

        public static string DefaultInstallDir(bool allUsers)
        {
            return allUsers
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);
        }

        public static string StartMenuShortcut(bool allUsers)
        {
            var folder = Environment.GetFolderPath(allUsers ? Environment.SpecialFolder.CommonPrograms : Environment.SpecialFolder.Programs);
            return Path.Combine(folder, AppName + ".lnk");
        }

        public static string DesktopShortcut(bool allUsers)
        {
            var folder = Environment.GetFolderPath(allUsers ? Environment.SpecialFolder.CommonDesktopDirectory : Environment.SpecialFolder.DesktopDirectory);
            return Path.Combine(folder, AppName + ".lnk");
        }

        public static RegistryKey OpenRoot(bool allUsers)
        {
            return allUsers
                ? RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                : RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
        }

        /// <summary>Install directory recorded in the Uninstall key of the given scope, or null.</summary>
        public static string ReadInstallLocation(bool allUsers)
        {
            try
            {
                using (var root = OpenRoot(allUsers))
                using (var key = root.OpenSubKey(UninstallKeyPath))
                {
                    return key == null ? null : key.GetValue("InstallLocation") as string;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static bool IsAdministrator()
        {
            using (var id = WindowsIdentity.GetCurrent())
            {
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        /// <summary>Per-user, per-session name of the single-instance pipe.</summary>
        public static string PipeName()
        {
            string sid;
            using (var id = WindowsIdentity.GetCurrent())
            {
                sid = id.User != null ? id.User.Value : Environment.UserName;
            }
            using (var p = Process.GetCurrentProcess())
            {
                return "PassKeeper-" + sid + "-" + p.SessionId;
            }
        }

        public static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return string.Equals(
                Path.GetFullPath(a).TrimEnd('\\'),
                Path.GetFullPath(b).TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
