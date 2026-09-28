using Microsoft.Win32;
using PassKeeper.Shared;

namespace PassKeeper.Services;

/// <summary>Autostart via HKCU\...\Run (no administrator rights). HKLM\...\Run is set by an all-users installation.</summary>
public static class AutostartService
{
    public static string Command => $"\"{AppPaths.ExePath}\" --minimized";

    public static bool IsEnabledForUser()
    {
        using var key = Registry.CurrentUser.OpenSubKey(InstallLayout.RunKeyPath);
        return key?.GetValue(InstallLayout.RunValueName) is string;
    }

    public static bool IsEnabledForMachine()
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = root.OpenSubKey(InstallLayout.RunKeyPath);
            return key?.GetValue(InstallLayout.RunValueName) is string;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Default for the first-run screen: what was chosen in the installer (off for portable/unregistered copies).</summary>
    public static bool DefaultForNewProfile()
    {
        if (IsEnabledForUser()) return true;
        if (AppPaths.IsMachineInstall) return InstallLayout.ReadAutostartPreference(true) == true;
        if (AppPaths.IsUserInstall) return InstallLayout.ReadAutostartPreference(false) == true;
        return false;
    }

    public static void SetForUser(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(InstallLayout.RunKeyPath);
        if (enabled) key.SetValue(InstallLayout.RunValueName, Command);
        else key.DeleteValue(InstallLayout.RunValueName, throwOnMissingValue: false);
    }

    /// <summary>Keeps the Run entry pointing at the current executable after the app was moved/updated.</summary>
    public static void RefreshPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(InstallLayout.RunKeyPath, writable: true);
            if (key?.GetValue(InstallLayout.RunValueName) is string current && current != Command)
                key.SetValue(InstallLayout.RunValueName, Command);
        }
        catch (Exception) { }
    }
}
