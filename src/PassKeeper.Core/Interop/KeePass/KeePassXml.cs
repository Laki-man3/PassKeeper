using System.Globalization;
using System.Xml.Linq;
using PassKeeper.Core.Models;
using PassKeeper.Core.Security;

namespace PassKeeper.Core.Interop.KeePass;

/// <summary>Maps the KeePass 2 XML document (inside KDBX or as a plain XML export) to vault entries and back.</summary>
public static class KeePassXml
{
    private const string FavoriteTag = "Favorite";
    private static readonly HashSet<string> Standard = new(StringComparer.Ordinal) { "Title", "UserName", "Password", "URL", "Notes" };

    public static ImportResult ToEntries(XDocument doc, string source)
    {
        var result = new ImportResult { Source = source };
        var root = doc.Root?.Element("Root")?.Element("Group");
        if (root == null)
        {
            result.Warn(WarningCodes.Error, source, "KeePass XML has no root group");
            return result;
        }
        var recycleBin = doc.Root?.Element("Meta")?.Element("RecycleBinUUID")?.Value;
        Walk(root, "", isRoot: true);
        return result;

        void Walk(XElement group, string path, bool isRoot)
        {
            var uuid = group.Element("UUID")?.Value;
            if (!isRoot && !string.IsNullOrEmpty(recycleBin) && uuid == recycleBin && recycleBin != "AAAAAAAAAAAAAAAAAAAAAA==")
                return;
            foreach (var entry in group.Elements("Entry"))
            {
                var e = ToEntry(entry, path);
                if (e != null) result.Entries.Add(e);
            }
            foreach (var child in group.Elements("Group"))
            {
                var name = child.Element("Name")?.Value.Trim() ?? "";
                Walk(child, path.Length == 0 ? name : path + "/" + name, isRoot: false);
            }
        }
    }

    private static VaultEntry? ToEntry(XElement entry, string folder)
    {
        var e = new VaultEntry { Folder = folder };
        string? otpSecretBase32 = null, otpLength = null, otpPeriod = null, otpAlgorithm = null;

        foreach (var s in entry.Elements("String"))
        {
            var key = s.Element("Key")?.Value ?? "";
            var valueEl = s.Element("Value");
            var value = valueEl?.Value ?? "";
            var isProtected = string.Equals(valueEl?.Attribute("Protected")?.Value, "True", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(valueEl?.Attribute("ProtectInMemory")?.Value, "True", StringComparison.OrdinalIgnoreCase);
            switch (key)
            {
                case "Title": e.Title = value; break;
                case "UserName": e.Username = value; break;
                case "Password": e.Password = value; break;
                case "URL": e.Url = value; break;
                case "Notes": e.Notes = value; break;
                case "otp": e.Totp = value; break;
                case "TimeOtp-Secret-Base32": otpSecretBase32 = value; break;
                case "TimeOtp-Length": otpLength = value; break;
                case "TimeOtp-Period": otpPeriod = value; break;
                case "TimeOtp-Algorithm": otpAlgorithm = value; break;
                default:
                    if (value.Length == 0) break;
                    var lower = key.ToLowerInvariant();
                    if (lower is "email" or "e-mail" or "почта" or "электронная почта" && e.Email.Length == 0) e.Email = value;
                    else if (lower is "phone" or "телефон" or "phone number" && e.Phone.Length == 0) e.Phone = value;
                    else if (lower is "key" or "api key" or "ключ" && e.SecretKey.Length == 0) e.SecretKey = value;
                    else if (lower.StartsWith("kp2a_url") || lower.StartsWith("url") && lower.Length <= 5 || lower == "additional url")
                        e.ExtraUrls.Add(value);
                    else e.CustomFields.Add(new CustomField { Name = key, Value = value, Protected = isProtected });
                    break;
            }
        }

        if (e.Totp.Length == 0 && !string.IsNullOrWhiteSpace(otpSecretBase32))
        {
            var secret = otpSecretBase32.Replace(" ", "");
            var digits = int.TryParse(otpLength, out var d) ? d : 6;
            var period = int.TryParse(otpPeriod, out var p) ? p : 30;
            var algorithm = otpAlgorithm switch { "HMAC-SHA-256" => "SHA256", "HMAC-SHA-512" => "SHA512", _ => "SHA1" };
            e.Totp = digits == 6 && period == 30 && algorithm == "SHA1"
                ? secret
                : $"otpauth://totp/{Uri.EscapeDataString(e.Title)}?secret={secret}&digits={digits}&period={period}&algorithm={algorithm}";
        }

        var tags = entry.Element("Tags")?.Value;
        if (!string.IsNullOrWhiteSpace(tags))
        {
            foreach (var t in tags.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (t.Equals(FavoriteTag, StringComparison.OrdinalIgnoreCase)) e.Favorite = true;
                else e.Tags.Add(t);
            }
        }

        var times = entry.Element("Times");
        if (ParseTime(times?.Element("CreationTime")?.Value) is { } created) e.CreatedUtc = created;
        if (ParseTime(times?.Element("LastModificationTime")?.Value) is { } modified) e.ModifiedUtc = modified;

        var autoType = entry.Element("AutoType");
        var seq = autoType?.Element("DefaultSequence")?.Value;
        if (!string.IsNullOrWhiteSpace(seq) && AutoType.AutoTypeSequence.Validate(seq) == null) e.AutoTypeSequence = seq;
        foreach (var assoc in autoType?.Elements("Association") ?? [])
        {
            var window = assoc.Element("Window")?.Value;
            if (!string.IsNullOrWhiteSpace(window) && !window.StartsWith("//", StringComparison.Ordinal))
                e.WindowPatterns.Add(window.Trim());
        }

        if (ImportHelpers.IsEmpty(e) && string.IsNullOrWhiteSpace(e.Title)) return null;
        e.Title = ImportHelpers.MakeTitle(e.Title, e.Url, e.Username);
        return e;
    }

    internal static DateTime? ParseTime(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (s.Contains('-') && DateTime.TryParse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt))
            return dt;
        try
        {
            var b = Convert.FromBase64String(s.Trim());
            if (b.Length == 8)
                return DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc).AddSeconds(BitConverter.ToInt64(b));
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException) { }
        return null;
    }

    // ------------------------------------------------------------------------------------------------ export

    /// <param name="kdbx4">true: binary (base64) times and Protected="True" markers for the KDBX writer;
    /// false: plain KeePass 2 XML export.</param>
    public static XDocument FromEntries(IEnumerable<VaultEntry> entries, bool kdbx4, string databaseName = "PassKeeper")
    {
        var now = DateTime.UtcNow;
        string Time(DateTime t) => kdbx4
            ? Convert.ToBase64String(BitConverter.GetBytes((long)(DateTime.SpecifyKind(t, DateTimeKind.Utc) - DateTime.MinValue).TotalSeconds))
            : DateTime.SpecifyKind(t, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        static string NewUuid() => Convert.ToBase64String(Guid.NewGuid().ToByteArray());

        XElement Times(DateTime created, DateTime modified) => new("Times",
            new XElement("CreationTime", Time(created)),
            new XElement("LastModificationTime", Time(modified)),
            new XElement("LastAccessTime", Time(modified)),
            new XElement("ExpiryTime", Time(now)),
            new XElement("Expires", "False"),
            new XElement("UsageCount", "0"),
            new XElement("LocationChanged", Time(modified)));

        XElement NewGroup(string name) => new("Group",
            new XElement("UUID", NewUuid()),
            new XElement("Name", name),
            new XElement("Notes", ""),
            new XElement("IconID", "48"),
            Times(now, now),
            new XElement("IsExpanded", "True"),
            new XElement("EnableAutoType", "null"),
            new XElement("EnableSearching", "null"));

        var root = NewGroup(databaseName);
        root.Element("IconID")!.Value = "49";
        var groups = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase) { [""] = root };

        XElement GetGroup(string path)
        {
            path = path.Trim().Trim('/');
            if (groups.TryGetValue(path, out var g)) return g;
            var slash = path.LastIndexOf('/');
            var parent = GetGroup(slash < 0 ? "" : path[..slash]);
            g = NewGroup(slash < 0 ? path : path[(slash + 1)..]);
            parent.Add(g);
            groups[path] = g;
            return g;
        }

        XElement Str(string key, string value, bool isProtected = false)
        {
            var v = new XElement("Value", value);
            if (isProtected && kdbx4) v.SetAttributeValue("Protected", "True");
            else if (isProtected) v.SetAttributeValue("ProtectInMemory", "True");
            return new XElement("String", new XElement("Key", key), v);
        }

        foreach (var e in entries.Where(x => !x.IsDeleted))
        {
            var tags = e.Tags.ToList();
            if (e.Favorite) tags.Add(FavoriteTag);
            var el = new XElement("Entry",
                new XElement("UUID", Convert.ToBase64String(e.Id.ToByteArray())),
                new XElement("IconID", "0"),
                new XElement("ForegroundColor"),
                new XElement("BackgroundColor"),
                new XElement("OverrideURL"),
                new XElement("Tags", string.Join(";", tags)),
                Times(e.CreatedUtc, e.ModifiedUtc),
                Str("Title", e.Title),
                Str("UserName", e.Username),
                Str("Password", e.Password, true),
                Str("URL", e.Url),
                Str("Notes", e.Notes));
            if (e.Email.Length > 0) el.Add(Str("Email", e.Email));
            if (e.Phone.Length > 0) el.Add(Str("Phone", e.Phone));
            if (e.SecretKey.Length > 0) el.Add(Str("Key", e.SecretKey, true));
            if (e.Totp.Length > 0)
            {
                var cfg = Totp.Parse(e.Totp);
                el.Add(Str("otp", e.Totp.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase) || cfg == null
                    ? e.Totp
                    : Totp.BuildUri(Base32.Encode(cfg.Secret), string.IsNullOrEmpty(e.Title) ? "PassKeeper" : e.Title, e.Username), true));
                if (cfg != null) el.Add(Str("TimeOtp-Secret-Base32", Base32.Encode(cfg.Secret), true));
            }
            for (var i = 0; i < e.ExtraUrls.Count; i++) el.Add(Str($"KP2A_URL_{i + 1}", e.ExtraUrls[i]));
            var used = new HashSet<string>(Standard.Concat(["Email", "Phone", "Key", "otp", "TimeOtp-Secret-Base32"]));
            foreach (var f in e.CustomFields)
            {
                var name = string.IsNullOrWhiteSpace(f.Name) ? "Field" : f.Name.Trim();
                var unique = name;
                for (var n = 2; !used.Add(unique); n++) unique = $"{name} ({n})";
                el.Add(Str(unique, f.Value, f.Protected));
            }
            var autoType = new XElement("AutoType",
                new XElement("Enabled", "True"),
                new XElement("DataTransferObfuscation", "0"));
            if (e.AutoTypeSequence.Length > 0) autoType.Add(new XElement("DefaultSequence", e.AutoTypeSequence));
            foreach (var w in e.WindowPatterns.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                var pattern = w.Contains('*') ? w : "*" + w + "*";
                autoType.Add(new XElement("Association", new XElement("Window", pattern), new XElement("KeystrokeSequence")));
            }
            el.Add(autoType);
            GetGroup(e.Folder).Add(el);
        }

        return new XDocument(new XDeclaration("1.0", "utf-8", "yes"),
            new XElement("KeePassFile",
                new XElement("Meta",
                    new XElement("Generator", "PassKeeper"),
                    new XElement("DatabaseName", databaseName),
                    new XElement("DatabaseNameChanged", Time(now)),
                    new XElement("DefaultUserName"),
                    new XElement("RecycleBinEnabled", "True"),
                    new XElement("RecycleBinUUID", "AAAAAAAAAAAAAAAAAAAAAA=="),
                    new XElement("MemoryProtection",
                        new XElement("ProtectTitle", "False"),
                        new XElement("ProtectUserName", "False"),
                        new XElement("ProtectPassword", "True"),
                        new XElement("ProtectURL", "False"),
                        new XElement("ProtectNotes", "False"))),
                new XElement("Root", root, new XElement("DeletedObjects"))));
    }
}
