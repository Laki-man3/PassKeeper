using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PassKeeper.Core.Interop.KeePass;
using PassKeeper.Core.Models;
using PassKeeper.Core.Security;

namespace PassKeeper.Core.Interop;

public enum ExportFormat
{
    PassKeeperEncrypted,
    Kdbx,
    CsvChrome,
    CsvFirefox,
    CsvBitwarden,
    CsvKeePassXc,
    CsvOnePassword,
    CsvLastPass,
    CsvPassKeeper,
    JsonBitwarden,
    JsonPassKeeper,
    XmlKeePass,
    Html,
}

public static class Exporter
{
    public static bool IsEncrypted(ExportFormat f) => f is ExportFormat.PassKeeperEncrypted or ExportFormat.Kdbx;

    public static string Extension(ExportFormat f) => f switch
    {
        ExportFormat.PassKeeperEncrypted => ".pkx",
        ExportFormat.Kdbx => ".kdbx",
        ExportFormat.JsonBitwarden or ExportFormat.JsonPassKeeper => ".json",
        ExportFormat.XmlKeePass => ".xml",
        ExportFormat.Html => ".html",
        _ => ".csv",
    };

    public static string DefaultFileName(ExportFormat f)
    {
        var suffix = f switch
        {
            ExportFormat.CsvChrome => "-chrome",
            ExportFormat.CsvFirefox => "-firefox",
            ExportFormat.CsvBitwarden or ExportFormat.JsonBitwarden => "-bitwarden",
            ExportFormat.CsvKeePassXc => "-keepassxc",
            ExportFormat.CsvOnePassword => "-1password",
            ExportFormat.CsvLastPass => "-lastpass",
            ExportFormat.XmlKeePass => "-keepass",
            _ => "",
        };
        return $"PassKeeper-{DateTime.Now:yyyy-MM-dd}{suffix}{Extension(f)}";
    }

    /// <param name="password">Required for encrypted formats.</param>
    public static byte[] Export(IEnumerable<VaultEntry> source, ExportFormat format, string? password = null,
        KdbxWriteOptions? kdbxOptions = null)
    {
        var entries = source.Where(e => !e.IsDeleted).ToList();
        return format switch
        {
            ExportFormat.PassKeeperEncrypted => PassKeeperFormat.Encrypt(entries, Require(password)),
            ExportFormat.Kdbx => Kdbx.Encrypt(KeePassXml.FromEntries(entries, kdbx4: true), Require(password), null, kdbxOptions),
            ExportFormat.JsonPassKeeper => Utf8(PassKeeperFormat.ToJson(entries)),
            ExportFormat.JsonBitwarden => Utf8(BitwardenJson(entries)),
            ExportFormat.XmlKeePass => KeePassXmlBytes(entries),
            ExportFormat.Html => Utf8(Html(entries)),
            _ => Utf8(CsvText(entries, format), bom: format is ExportFormat.CsvPassKeeper),
        };
    }

    private static string Require(string? password) =>
        string.IsNullOrEmpty(password) ? throw new ArgumentException("A password is required for encrypted export.") : password;

    private static byte[] Utf8(string text, bool bom = false)
    {
        var body = Encoding.UTF8.GetBytes(text);
        return bom ? [0xEF, 0xBB, 0xBF, .. body] : body;
    }

    public static string CsvText(IReadOnlyList<VaultEntry> entries, ExportFormat format)
    {
        var rows = new List<string?[]>();
        static string Ts(DateTime? t) => t.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(t.Value, DateTimeKind.Utc)).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) : "";

        switch (format)
        {
            case ExportFormat.CsvChrome:
                rows.Add(["name", "url", "username", "password", "note"]);
                rows.AddRange(entries.Select(e => new[] { e.Title, e.Url, Login(e), e.Password, e.Notes }));
                break;
            case ExportFormat.CsvFirefox:
                rows.Add(["url", "username", "password", "httpRealm", "formActionOrigin", "guid", "timeCreated", "timeLastUsed", "timePasswordChanged"]);
                rows.AddRange(entries.Where(e => e.Url.Length > 0).Select(e => new[]
                {
                    Origin(e.Url), Login(e), e.Password, "", "", "{" + e.Id + "}", Ts(e.CreatedUtc), Ts(e.LastUsedUtc ?? e.ModifiedUtc), Ts(e.ModifiedUtc),
                }));
                break;
            case ExportFormat.CsvBitwarden:
                rows.Add(["folder", "favorite", "type", "name", "notes", "fields", "reprompt", "login_uri", "login_username", "login_password", "login_totp"]);
                rows.AddRange(entries.Select(e => new[]
                {
                    e.Folder, e.Favorite ? "1" : "", IsNote(e) ? "note" : "login", e.Title, e.Notes, FieldsBlob(e), "0",
                    string.Join(",", e.AllUrls()), Login(e), e.Password, e.Totp,
                }));
                break;
            case ExportFormat.CsvKeePassXc:
                rows.Add(["Group", "Title", "Username", "Password", "URL", "Notes", "TOTP", "Icon", "Last Modified", "Created"]);
                rows.AddRange(entries.Select(e => new[]
                {
                    "Root" + (e.Folder.Length > 0 ? "/" + e.Folder : ""), e.Title, Login(e), e.Password, e.Url, NotesWithExtras(e), e.Totp, "0",
                    e.ModifiedUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                    e.CreatedUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                }));
                break;
            case ExportFormat.CsvOnePassword:
                rows.Add(["Title", "Url", "Username", "Password", "OTPAuth", "Favorite", "Archived", "Tags", "Notes"]);
                rows.AddRange(entries.Select(e => new[]
                {
                    e.Title, e.Url, Login(e), e.Password, e.Totp, e.Favorite ? "true" : "false", "false", string.Join(",", e.Tags), NotesWithExtras(e),
                }));
                break;
            case ExportFormat.CsvLastPass:
                rows.Add(["url", "username", "password", "totp", "extra", "name", "grouping", "fav"]);
                rows.AddRange(entries.Select(e => new[]
                {
                    IsNote(e) ? "http://sn" : e.Url, Login(e), e.Password, e.Totp, NotesWithExtras(e), e.Title, e.Folder, e.Favorite ? "1" : "0",
                }));
                break;
            default:
                rows.Add(["title", "url", "username", "password", "email", "phone", "key", "totp", "notes", "folder", "favorite", "tags", "additional_urls"]);
                rows.AddRange(entries.Select(e => new[]
                {
                    e.Title, e.Url, e.Username, e.Password, e.Email, e.Phone, e.SecretKey, e.Totp, e.Notes, e.Folder,
                    e.Favorite ? "1" : "0", string.Join(";", e.Tags), string.Join(" ", e.ExtraUrls),
                }));
                break;
        }
        return Csv.Write(rows, ',', quoteAll: format == ExportFormat.CsvFirefox);
    }

    private static string Login(VaultEntry e) => e.Username.Length > 0 ? e.Username : e.Email;

    private static bool IsNote(VaultEntry e) => e.Password.Length == 0 && e.Username.Length == 0 && e.Url.Length == 0;

    private static string Origin(string url)
    {
        var normalized = url.Contains("://", StringComparison.Ordinal) ? url : "https://" + url;
        return Uri.TryCreate(normalized, UriKind.Absolute, out var u) ? u.GetLeftPart(UriPartial.Authority) : url;
    }

    private static string FieldsBlob(VaultEntry e)
    {
        var lines = new List<string>();
        if (e.Email.Length > 0 && e.Username.Length > 0) lines.Add("Email: " + e.Email);
        if (e.Phone.Length > 0) lines.Add("Phone: " + e.Phone);
        if (e.SecretKey.Length > 0) lines.Add("Key: " + e.SecretKey);
        lines.AddRange(e.CustomFields.Select(f => $"{f.Name}: {f.Value}"));
        return string.Join("\n", lines);
    }

    /// <summary>For formats without extra fields, keep e-mail/phone/key/custom fields in the notes.</summary>
    private static string NotesWithExtras(VaultEntry e)
    {
        var extras = FieldsBlob(e);
        if (extras.Length == 0) return e.Notes;
        return e.Notes.Length == 0 ? extras : e.Notes + "\n\n" + extras;
    }

    private static string BitwardenJson(IReadOnlyList<VaultEntry> entries)
    {
        var folders = entries.Select(e => e.Folder).Where(f => f.Length > 0).Distinct().ToDictionary(f => f, _ => Guid.NewGuid().ToString());
        var items = new JsonArray();
        foreach (var e in entries)
        {
            var fields = new JsonArray();
            void F(string name, string value, int type)
            {
                if (value.Length > 0) fields.Add(new JsonObject { ["name"] = name, ["value"] = value, ["type"] = type, ["linkedId"] = null });
            }
            if (e.Username.Length > 0) F("Email", e.Email, 0);
            F("Phone", e.Phone, 0);
            F("Key", e.SecretKey, 1);
            foreach (var cf in e.CustomFields) F(cf.Name, cf.Value, cf.Protected ? 1 : 0);

            var note = IsNote(e);
            var item = new JsonObject
            {
                ["id"] = e.Id.ToString(),
                ["organizationId"] = null,
                ["folderId"] = e.Folder.Length > 0 ? folders[e.Folder] : null,
                ["type"] = note ? 2 : 1,
                ["reprompt"] = 0,
                ["name"] = e.Title,
                ["notes"] = e.Notes.Length > 0 ? e.Notes : null,
                ["favorite"] = e.Favorite,
                ["fields"] = fields,
                ["collectionIds"] = null,
            };
            if (note) item["secureNote"] = new JsonObject { ["type"] = 0 };
            else
            {
                var uris = new JsonArray();
                foreach (var u in e.AllUrls()) uris.Add(new JsonObject { ["match"] = null, ["uri"] = u });
                item["login"] = new JsonObject
                {
                    ["uris"] = uris,
                    ["username"] = Login(e),
                    ["password"] = e.Password,
                    ["totp"] = e.Totp.Length > 0 ? e.Totp : null,
                };
            }
            items.Add(item);
        }
        var root = new JsonObject
        {
            ["encrypted"] = false,
            ["folders"] = new JsonArray(folders.Select(f => (JsonNode)new JsonObject { ["id"] = f.Value, ["name"] = f.Key }).ToArray()),
            ["items"] = items,
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    private static byte[] KeePassXmlBytes(IReadOnlyList<VaultEntry> entries)
    {
        var doc = KeePassXml.FromEntries(entries, kdbx4: false);
        using var ms = new MemoryStream();
        using (var w = System.Xml.XmlWriter.Create(ms, new System.Xml.XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }))
            doc.Save(w);
        return ms.ToArray();
    }

    private static string Html(IReadOnlyList<VaultEntry> entries)
    {
        static string H(string s) => WebUtility.HtmlEncode(s).Replace("\n", "<br>");
        var sb = new StringBuilder();
        sb.Append("""
            <!DOCTYPE html><html lang="ru"><head><meta charset="utf-8"><title>PassKeeper</title>
            <style>
            body{font-family:"Segoe UI",Arial,sans-serif;margin:32px;color:#1b1f2a}
            h1{font-size:22px;margin:0 0 4px}p.meta{color:#6b7285;margin:0 0 20px;font-size:13px}
            table{border-collapse:collapse;width:100%;font-size:13px}
            th{text-align:left;background:#f1f2f7;padding:8px;border-bottom:2px solid #d9dce5}
            td{padding:8px;border-bottom:1px solid #e4e7ee;vertical-align:top;word-break:break-word}
            td.pw{font-family:Consolas,monospace}tr:nth-child(even) td{background:#fafbfd}
            @media print{body{margin:0}th{background:#eee}}
            </style></head><body>
            """);
        sb.Append($"<h1>PassKeeper</h1><p class=\"meta\">{DateTime.Now:yyyy-MM-dd HH:mm} · {entries.Count}</p>");
        sb.Append("<table><thead><tr><th>Название / Title</th><th>Логин / Login</th><th>Пароль / Password</th><th>Сайт / URL</th><th>Папка / Folder</th><th>Заметки / Notes</th></tr></thead><tbody>");
        foreach (var e in entries.OrderBy(x => x.Folder).ThenBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase))
        {
            var extras = new List<string>();
            if (e.Email.Length > 0) extras.Add("E-mail: " + e.Email);
            if (e.Phone.Length > 0) extras.Add("Тел./Phone: " + e.Phone);
            if (e.SecretKey.Length > 0) extras.Add("Ключ/Key: " + e.SecretKey);
            if (e.Totp.Length > 0) extras.Add("2FA: " + e.Totp);
            extras.AddRange(e.CustomFields.Select(f => f.Name + ": " + f.Value));
            var notes = string.Join("\n", new[] { e.Notes }.Concat(extras).Where(s => s.Length > 0));
            sb.Append($"<tr><td>{H(e.Title)}</td><td>{H(e.Username)}</td><td class=\"pw\">{H(e.Password)}</td><td>{H(e.Url)}</td><td>{H(e.Folder)}</td><td>{H(notes)}</td></tr>");
        }
        sb.Append("</tbody></table></body></html>");
        return sb.ToString();
    }
}
