using System.Text.Json;
using PassKeeper.Core.Models;
using PassKeeper.Core.Storage;

namespace PassKeeper.Services;

/// <summary>Non-secret user preferences (settings.json next to the vault).</summary>
public sealed class AppSettings
{
    public string Language { get; set; } = "";
    /// <summary>"dark", "light" or "system".</summary>
    public string Theme { get; set; } = "dark";
    public int AutoLockMinutes { get; set; } = 480;
    public bool LockOnWindowsLock { get; set; }
    public int ClipboardClearSeconds { get; set; } = 30;
    public string AutoTypeHotkey { get; set; } = "Ctrl+Alt+A";
    public bool SmartSuggestions { get; set; } = true;
    public bool SubmitAfterFill { get; set; }
    public int KeystrokeDelayMs { get; set; } = 8;
    public bool CompatibleTyping { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool TrayHintShown { get; set; }
    public string SortOrder { get; set; } = "name";
    public double WindowWidth { get; set; } = 1180;
    public double WindowHeight { get; set; } = 740;

    public static readonly int[] AutoLockChoices = [5, 15, 30, 60, 240, 480, 1440];
    public static readonly int[] ClipboardChoices = [10, 20, 30, 60, 0];

    private string _path = "";

    public static AppSettings Load(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, "settings.json");
        AppSettings settings;
        try
        {
            settings = File.Exists(path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), VaultJson.Options) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            settings = new AppSettings();
        }
        settings._path = path;
        if (settings.AutoLockMinutes <= 0) settings.AutoLockMinutes = 480;
        settings.KeystrokeDelayMs = Math.Clamp(settings.KeystrokeDelayMs, 0, 200);
        return settings;
    }

    public void Save()
    {
        try
        {
            FileUtil.WriteAtomic(_path, JsonSerializer.SerializeToUtf8Bytes(this, VaultJson.Indented));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
