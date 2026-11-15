using System.Text.Json;

namespace PassKeeper.Desktop;

public sealed class DesktopSettings
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>"ru", "en" or empty (the system's language).</summary>
    public string Language { get; set; } = "";
    /// <summary>"system", "light" or "dark".</summary>
    public string Theme { get; set; } = "system";
    /// <summary>Lock after this many minutes without input; 0: never.</summary>
    public int AutoLockMinutes { get; set; } = 30;
    /// <summary>Clear a copied secret from the clipboard after this many seconds; 0: never.</summary>
    public int ClipboardSeconds { get; set; } = 30;
    public string? ActiveProfile { get; set; }
    public bool AutoTypeEnabled { get; set; } = true;
    /// <summary>"Ctrl+Alt+A"; Meta is Cmd on macOS, Super elsewhere.</summary>
    public string AutoTypeHotkey { get; set; } = Platform.Hotkey.Default.ToString();
    /// <summary>
    /// Closing the window keeps PassKeeper in the tray (menu bar). Off by default on Linux, where a desktop without a
    /// tray (GNOME without its extension) would leave no way back to the window.
    /// </summary>
    public bool CloseToTray { get; set; } = !OperatingSystem.IsLinux();
    public double WindowWidth { get; set; } = 1120;
    public double WindowHeight { get; set; } = 720;

    private string _path = "";

    public static DesktopSettings Load(string directory)
    {
        var path = Path.Combine(directory, "settings.json");
        DesktopSettings settings;
        try
        {
            settings = File.Exists(path) ? JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(path), Json) ?? new() : new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            settings = new();
        }
        settings._path = path;
        return settings;
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
