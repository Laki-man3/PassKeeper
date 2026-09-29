using System.Text.Json;
using System.Text.Json.Serialization;
using PassKeeper.Core.Models;
using PassKeeper.Core.Storage;

namespace PassKeeper.Services;

/// <summary>Non-secret user preferences (settings.json next to the vault).</summary>
public sealed class AppSettings
{
    public string Language { get; set; } = "";
    /// <summary>"dark", "light" or "system".</summary>
    public string Theme { get; set; } = "dark";
    /// <summary>Inactivity period after which the PIN is required, in seconds.</summary>
    public int AutoLockSeconds { get; set; } = DefaultAutoLockSeconds;
    /// <summary>Written by version 1.0.0; converted to <see cref="AutoLockSeconds"/> on load.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? AutoLockMinutes { get; set; }
    public bool LockOnWindowsLock { get; set; }
    public int ClipboardClearSeconds { get; set; } = 30;
    public string AutoTypeHotkey { get; set; } = "Ctrl+Alt+A";
    public bool SmartSuggestions { get; set; } = true;
    /// <summary>Sign in automatically to clients whose entries have auto sign-in enabled.</summary>
    public bool AutoLogin { get; set; } = true;
    /// <summary>Known client ids / executable names that have auto sign-in entries (read while the vault is locked).</summary>
    public List<string> AutoLoginTriggers { get; set; } = [];
    public bool SubmitAfterFill { get; set; }
    public int KeystrokeDelayMs { get; set; } = 8;
    public bool CompatibleTyping { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool TrayHintShown { get; set; }
    public string SortOrder { get; set; } = "name";
    public double WindowWidth { get; set; } = 1180;
    public double WindowHeight { get; set; } = 740;

    public const int DefaultAutoLockSeconds = 8 * 3600;
    public const int MinAutoLockSeconds = 10;
    public const int MaxAutoLockSeconds = 24 * 3600;
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
        if (settings.AutoLockMinutes is int minutes && minutes > 0) settings.AutoLockSeconds = minutes * 60;
        settings.AutoLockMinutes = null;
        settings.AutoLockSeconds = settings.AutoLockSeconds <= 0
            ? DefaultAutoLockSeconds
            : Math.Clamp(settings.AutoLockSeconds, MinAutoLockSeconds, MaxAutoLockSeconds);
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
