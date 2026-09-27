using System.Text.Json;

namespace PassKeeper.Core.Interop.Browsers;

public enum BrowserKind { Chromium, Yandex, Firefox }

public sealed class BrowserProfile
{
    public required string Browser { get; init; }
    public required string ProfileName { get; init; }
    public required BrowserKind Kind { get; init; }
    /// <summary>Directory holding the login database.</summary>
    public required string ProfilePath { get; init; }
    /// <summary>Chromium "User Data" directory (with "Local State").</summary>
    public string UserDataPath { get; init; } = "";
    public string DisplayName => ProfileName.Length == 0 ? Browser : $"{Browser} — {ProfileName}";
    public override string ToString() => DisplayName;
}

public static class BrowserDetector
{
    private static readonly (string Root, string Path, string Name, BrowserKind Kind)[] Chromium =
    [
        ("L", @"Google\Chrome\User Data", "Google Chrome", BrowserKind.Chromium),
        ("L", @"Google\Chrome Beta\User Data", "Google Chrome Beta", BrowserKind.Chromium),
        ("L", @"Google\Chrome Dev\User Data", "Google Chrome Dev", BrowserKind.Chromium),
        ("L", @"Google\Chrome SxS\User Data", "Google Chrome Canary", BrowserKind.Chromium),
        ("L", @"Chromium\User Data", "Chromium", BrowserKind.Chromium),
        ("L", @"Microsoft\Edge\User Data", "Microsoft Edge", BrowserKind.Chromium),
        ("L", @"Microsoft\Edge Beta\User Data", "Microsoft Edge Beta", BrowserKind.Chromium),
        ("L", @"Microsoft\Edge Dev\User Data", "Microsoft Edge Dev", BrowserKind.Chromium),
        ("L", @"Microsoft\Edge SxS\User Data", "Microsoft Edge Canary", BrowserKind.Chromium),
        ("L", @"Yandex\YandexBrowser\User Data", "Yandex Browser", BrowserKind.Yandex),
        ("L", @"BraveSoftware\Brave-Browser\User Data", "Brave", BrowserKind.Chromium),
        ("L", @"Vivaldi\User Data", "Vivaldi", BrowserKind.Chromium),
        ("L", @"Mail.Ru\Atom\User Data", "Atom", BrowserKind.Chromium),
        ("L", @"CentBrowser\User Data", "Cent Browser", BrowserKind.Chromium),
        ("L", @"Thorium\User Data", "Thorium", BrowserKind.Chromium),
        ("L", @"Comodo\Dragon\User Data", "Comodo Dragon", BrowserKind.Chromium),
        ("L", @"Epic Privacy Browser\User Data", "Epic Privacy Browser", BrowserKind.Chromium),
        ("L", @"Iridium\User Data", "Iridium", BrowserKind.Chromium),
        ("L", @"Chromium GOST\User Data", "Chromium GOST", BrowserKind.Chromium),
        ("L", @"Sputnik\Sputnik\User Data", "Sputnik", BrowserKind.Chromium),
        ("L", @"Coowon\Coowon\User Data", "Coowon", BrowserKind.Chromium),
        ("R", @"Opera Software\Opera Stable", "Opera", BrowserKind.Chromium),
        ("R", @"Opera Software\Opera GX Stable", "Opera GX", BrowserKind.Chromium),
        ("R", @"Opera Software\Opera Air Stable", "Opera Air", BrowserKind.Chromium),
    ];

    private static readonly (string Path, string Name)[] Gecko =
    [
        (@"Mozilla\Firefox", "Mozilla Firefox"),
        (@"Waterfox", "Waterfox"),
        (@"librewolf", "LibreWolf"),
        (@"Floorp", "Floorp"),
        (@"zen", "Zen Browser"),
        (@"Mozilla\SeaMonkey", "SeaMonkey"),
        (@"Thunderbird", "Thunderbird"),
    ];

    public static List<BrowserProfile> Detect()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var result = new List<BrowserProfile>();

        foreach (var (root, rel, name, kind) in Chromium)
        {
            var userData = Path.Combine(root == "L" ? local : roaming, rel);
            if (!Directory.Exists(userData)) continue;
            var names = ReadChromiumProfileNames(userData);
            var dbName = kind == BrowserKind.Yandex ? "Ya Passman Data" : "Login Data";

            if (File.Exists(Path.Combine(userData, dbName)))
                result.Add(new BrowserProfile { Browser = name, ProfileName = "", Kind = kind, ProfilePath = userData, UserDataPath = userData });

            IEnumerable<string> dirs;
            try { dirs = Directory.EnumerateDirectories(userData); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            foreach (var dir in dirs.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var dirName = Path.GetFileName(dir);
                if (dirName.Equals("System Profile", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("Guest Profile", StringComparison.OrdinalIgnoreCase)) continue;
                if (!File.Exists(Path.Combine(dir, dbName))) continue;
                result.Add(new BrowserProfile
                {
                    Browser = name,
                    ProfileName = names.GetValueOrDefault(dirName) is { Length: > 0 } n ? n : dirName,
                    Kind = kind,
                    ProfilePath = dir,
                    UserDataPath = userData,
                });
            }
        }

        foreach (var (rel, name) in Gecko)
        {
            var profilesDir = Path.Combine(roaming, rel, "Profiles");
            if (!Directory.Exists(profilesDir)) continue;
            foreach (var dir in Directory.EnumerateDirectories(profilesDir))
            {
                if (!File.Exists(Path.Combine(dir, "logins.json")) || !File.Exists(Path.Combine(dir, "key4.db"))) continue;
                var dirName = Path.GetFileName(dir);
                var dot = dirName.IndexOf('.');
                result.Add(new BrowserProfile
                {
                    Browser = name,
                    ProfileName = dot >= 0 ? dirName[(dot + 1)..] : dirName,
                    Kind = BrowserKind.Firefox,
                    ProfilePath = dir,
                });
            }
        }
        return result;
    }

    private static Dictionary<string, string> ReadChromiumProfileNames(string userData)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var path = Path.Combine(userData, "Local State");
            if (!File.Exists(path)) return result;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("profile", out var profile) &&
                profile.TryGetProperty("info_cache", out var cache) && cache.ValueKind == JsonValueKind.Object)
                foreach (var p in cache.EnumerateObject())
                    if (p.Value.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                        result[p.Name] = n.GetString() ?? p.Name;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return result;
    }

    public static ImportResult Read(BrowserProfile profile, string? primaryPassword = null) => profile.Kind switch
    {
        BrowserKind.Firefox => FirefoxReader.Read(profile, primaryPassword),
        _ => ChromiumReader.Read(profile),
    };
}
