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
        public const string UninstallerName = "Uninstall.exe";
        /// <summary>Choices made in the installer (HKCU or HKLM, by installation scope).</summary>
        public const string AppKeyPath = @"Software\PassKeeper";
        public const string AutostartPreferenceValue = "Autostart";
        public const string LanguageValue = "Language";

        public static string DefaultInstallDir(bool allUsers)
        {
            return allUsers
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);
        }

        /// <summary>Start menu folder with the application and uninstaller shortcuts.</summary>
        public static string StartMenuFolder(bool allUsers)
        {
            var programs = Environment.GetFolderPath(allUsers ? Environment.SpecialFolder.CommonPrograms : Environment.SpecialFolder.Programs);
            return Path.Combine(programs, AppName);
        }

        public static string StartMenuShortcut(bool allUsers)
        {
            return Path.Combine(StartMenuFolder(allUsers), AppName + ".lnk");
        }

        /// <summary>Shortcut placed directly in "Programs" by version 1.0.0.</summary>
        public static string LegacyStartMenuShortcut(bool allUsers)
        {
            var programs = Environment.GetFolderPath(allUsers ? Environment.SpecialFolder.CommonPrograms : Environment.SpecialFolder.Programs);
            return Path.Combine(programs, AppName + ".lnk");
        }

        /// <summary>%APPDATA%\PassKeeper of the current Windows user: vault, PIN, settings, backups.</summary>
        public static string UserDataDirectory()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);
        }

        /// <summary>Value stored by the installer under <see cref="AppKeyPath"/>, or null.</summary>
        public static object ReadInstallerValue(bool allUsers, string name)
        {
            try
            {
                using (var root = OpenRoot(allUsers))
                using (var key = root.OpenSubKey(AppKeyPath))
                {
                    return key == null ? null : key.GetValue(name);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>"Start when signing in" as chosen in the installer; null when the installer did not record it.</summary>
        public static bool? ReadAutostartPreference(bool allUsers)
        {
            var value = ReadInstallerValue(allUsers, AutostartPreferenceValue);
            if (value is int) return (int)value != 0;
            return null;
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
