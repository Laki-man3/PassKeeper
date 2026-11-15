using System.Security.Cryptography;
using PassKeeper.Core.Models;
using PassKeeper.Core.Storage;

// seed <dataDir> <scenario>: a fresh test profile (legacy layout, migrated on first start). The generated master
// password, PIN and test passwords go next to it: <dataDir>.master, .pin, .vpnpw, .passwords.json.
var dir = args[0];
var scenario = args[1];
var prefix = Path.TrimEndingDirectorySeparator(dir);
if (Directory.Exists(dir)) Directory.Delete(dir, true);
Directory.CreateDirectory(dir);
var master = "T-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(9));
File.WriteAllText(prefix + ".master", master);
var vault = new VaultService(dir);
vault.Create("Тест", master);
var entries = new List<VaultEntry>();
if (scenario == "tray")
{
    for (var i = 1; i <= 200; i++)
        entries.Add(new VaultEntry { Title = "Запись " + i, Url = $"https://site{i}.example.org", Username = "user" + i, Password = "pw-" + i, Folder = i % 3 == 0 ? "Работа" : "" });
}
else if (scenario == "multi")
{
    // One entry per local site, all with automatic sign-in; passwords.json for the server.
    var sites = new (string Host, string Path, string Title, string Login)[]
    {
        ("sso-corp.localhost", "/auth", "Corp SSO", "alice"),
        ("mail-box.localhost", "/signin", "MailBox", "bob@mail-box.test"),
        ("shop-spa.localhost", "/", "Shop", "carol"),
        ("bank-big.localhost", "/login", "Банк", "dave"),
        ("frame-site.localhost", "/", "Frame Site", "erin"),
    };
    var passwords = new Dictionary<string, string>();
    foreach (var (host, path, title, login) in sites)
    {
        var password = "pw-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
        passwords[host] = password;
        entries.Add(new VaultEntry { Title = title, Url = $"http://{host}:8772{path}", Username = login, Password = password, AutoLogin = true, Category = EntryCategory.Web });
    }
    File.WriteAllText(prefix + ".passwords.json", System.Text.Json.JsonSerializer.Serialize(passwords));
}
else if (scenario == "vpn")
{
    var password = "pw-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
    File.WriteAllText(prefix + ".vpnpw", password);
    entries.Add(new VaultEntry { Title = "FakeVPN", Category = EntryCategory.Remote, Username = "jdoe", Password = password, AutoLogin = true, WindowPatterns = ["fakevpn.exe"] });
}
vault.AddRange(entries);
var pin = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
File.WriteAllText(prefix + ".pin", pin);
vault.SetPin(pin);
vault.Lock();
File.WriteAllText(Path.Combine(dir, "settings.json"), """{ "language": "ru", "smartSuggestions": true, "autoFillWeb": true, "autoFillLog": true, "autoLogin": true, "autoLoginWeb": true, "minimizeToTray": true, "trayHintShown": true, "autoTypeHotkey": "Ctrl+Alt+Shift+F11" }""");
Console.WriteLine("seeded " + scenario + ": " + entries.Count + " entries");
