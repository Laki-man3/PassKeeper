using PassKeeper.Shared;

namespace PassKeeper.Services;

public static class AppPaths
{
    public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, InstallLayout.ExeName);
    public static string ExeDirectory => Path.GetDirectoryName(ExePath)!;

    /// <summary>
    /// --data &lt;dir&gt; wins; a "portable.txt" marker next to the exe keeps data in ".\Data" (USB stick);
    /// otherwise %APPDATA%\PassKeeper (per Windows user, also for all-users installations).
    /// </summary>
    public static string ResolveDataDirectory(string? overrideDir)
    {
        if (!string.IsNullOrWhiteSpace(overrideDir)) return Path.GetFullPath(overrideDir);
        if (IsPortable) return Path.Combine(ExeDirectory, "Data");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PassKeeper");
    }

    public static bool IsPortable => File.Exists(Path.Combine(ExeDirectory, InstallLayout.PortableMarker));

    /// <summary>Installed for all users (Program Files + HKLM uninstall entry).</summary>
    public static bool IsMachineInstall =>
        InstallLayout.SamePath(InstallLayout.ReadInstallLocation(true), ExeDirectory);

    public static bool IsUserInstall =>
        InstallLayout.SamePath(InstallLayout.ReadInstallLocation(false), ExeDirectory);
}
