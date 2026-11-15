namespace PassKeeper.Desktop;

/// <summary>Where the data lives on each system, readable by the user only.</summary>
public static class AppPaths
{
    /// <summary>
    /// macOS: ~/Library/Application Support/PassKeeper; Linux: $XDG_DATA_HOME/PassKeeper (~/.local/share/PassKeeper).
    /// On Windows (development only) a folder of its own, apart from the Windows version's data.
    /// </summary>
    public static string DataDirectory()
    {
        if (!string.IsNullOrWhiteSpace(Program.DataOverride)) return Path.GetFullPath(Program.DataOverride);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS()) return Path.Combine(home, "Library", "Application Support", "PassKeeper");
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PassKeeper Desktop");
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        return Path.Combine(string.IsNullOrWhiteSpace(xdg) ? Path.Combine(home, ".local", "share") : xdg, "PassKeeper");
    }

    /// <summary>Creates the folder and, on macOS and Linux, lets only its owner in (the vault, backups and settings are inside).</summary>
    public static void Prepare(string directory)
    {
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
