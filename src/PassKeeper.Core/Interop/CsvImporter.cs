using System.Text;
using PassKeeper.Core.Models;

namespace PassKeeper.Core.Interop;

/// <summary>
/// Header-driven CSV import. Recognises exports from Chrome/Edge/Opera/Brave/Vivaldi/Yandex, Firefox, Safari,
/// Bitwarden, 1Password, LastPass, Dashlane, NordPass, Proton Pass, KeePass/KeePassXC, RoboForm, Keeper and
/// any CSV with reasonably named columns (English or Russian).
/// </summary>
public static class CsvImporter
{
    private enum F { Title, Url, ExtraUrl, Username, Username2, Password, Notes, Folder, Totp, Email, Phone, Favorite, Tags, Type, Fields, SecretKey, Ignore }

    private static readonly Dictionary<string, F> Map = BuildMap();

    private static Dictionary<string, F> BuildMap()
    {
        var m = new Dictionary<string, F>();
        void Add(F f, params string[] names)
        {
            foreach (var n in names) m[Normalize(n)] = f;
        }
        Add(F.Title, "name", "title", "item name", "account", "account name", "entry", "site name", "название", "имя записи", "наименование", "заголовок");
        Add(F.Url, "url", "website", "web site", "website address", "login_uri", "login uri", "uri", "hostname", "origin", "origin url", "address", "site", "login url", "web address", "адрес", "сайт", "ссылка", "адрес сайта", "url сайта");
        Add(F.ExtraUrl, "additional_urls", "additional urls", "matchurl", "match url");
        Add(F.Username, "username", "login", "login_username", "login username", "user", "user name", "userid", "user id", "login name", "логин", "пользователь", "имя пользователя");
        Add(F.Username2, "username2", "username3", "secondary login");
        Add(F.Password, "password", "login_password", "login password", "pass", "pwd", "пароль");
        Add(F.Notes, "note", "notes", "extra", "comment", "comments", "заметки", "заметка", "примечание", "примечания", "комментарий");
        Add(F.Folder, "folder", "group", "grouping", "category", "vault", "папка", "группа", "категория");
        Add(F.Totp, "totp", "login_totp", "otpauth", "otp", "one-time password", "otpsecret", "otp secret", "2fa", "two factor", "одноразовый пароль");
        Add(F.Email, "email", "e-mail", "mail", "email address", "почта", "электронная почта", "e-mail адрес");
        Add(F.Phone, "phone", "phone number", "phone_number", "telephone", "mobile", "телефон", "номер телефона");
        Add(F.Favorite, "favorite", "favourite", "fav", "favorite status", "избранное");
        Add(F.Tags, "tags", "labels", "метки", "теги");
        Add(F.Type, "type", "item type", "тип");
        Add(F.Fields, "fields", "custom_fields", "custom fields", "rffieldsv2");
        Add(F.SecretKey, "key", "api key", "license key", "token", "ключ", "лицензионный ключ");
        Add(F.Ignore, "guid", "id", "httprealm", "formactionorigin", "timecreated", "timelastused", "timepasswordchanged",
            "icon", "last modified", "created", "reprompt", "createtime", "modifytime", "archived status",
            "date_created", "date_last_used", "date_password_modified", "collections", "organizationid");
        return m;
    }

    private static string Normalize(string header)
    {
        var sb = new StringBuilder(header.Length);
        foreach (var c in header.Trim().Trim('"').ToLowerInvariant())
            if (char.IsLetterOrDigit(c)) sb.Append(c);
        return sb.ToString();
    }

    public static ImportResult Import(string text, string sourceName)
    {
        var result = new ImportResult { Source = sourceName };
        var rows = Csv.Parse(text);
        if (rows.Count == 0)
        {
            result.Warn(WarningCodes.Empty, sourceName);
            return result;
        }

        var header = rows[0];
        var columns = header.Select(h => Map.TryGetValue(Normalize(h), out var f) ? f : (F?)null).ToArray();
        var known = columns.Count(c => c is F.Password or F.Username or F.Url or F.Title);
        var dataRows = rows.Skip(1);

        if (known < 2)
        {
            // Headerless export (Keeper): Folder, Title, Login, Password, Website, Notes, [custom name, value]...
            if (header.Length >= 4)
            {
                columns = new F?[header.Length];
                F[] keeper = [F.Folder, F.Title, F.Username, F.Password, F.Url, F.Notes];
                for (var i = 0; i < Math.Min(keeper.Length, columns.Length); i++) columns[i] = keeper[i];
                dataRows = rows;
                header = new string[columns.Length];
            }
            else
            {
                result.Warn(WarningCodes.Error, sourceName, "Unrecognized CSV header");
                return result;
            }
        }

        var hasUsernameColumn = columns.Contains(F.Username);
        var skipped = 0;
        foreach (var row in dataRows)
        {
            var e = new VaultEntry();
            string? type = null;
            for (var i = 0; i < row.Length && i < columns.Length; i++)
            {
                var v = row[i];
                if (string.IsNullOrEmpty(v)) continue;
                switch (columns[i])
                {
                    case F.Title: e.Title = v.Trim(); break;
                    case F.Url:
                        if (string.IsNullOrEmpty(e.Url)) e.Url = v.Trim();
                        else e.ExtraUrls.Add(v.Trim());
                        break;
                    case F.ExtraUrl:
                        e.ExtraUrls.AddRange(v.Split([',', '\n', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                        break;
                    case F.Username:
                        if (string.IsNullOrEmpty(e.Username)) e.Username = v;
                        break;
                    case F.Username2:
                        e.CustomFields.Add(new CustomField { Name = header[i], Value = v });
                        break;
                    case F.Password: e.Password = v; break;
                    case F.Notes: e.Notes = string.IsNullOrEmpty(e.Notes) ? v : e.Notes + "\n" + v; break;
                    case F.Folder: e.Folder = CleanFolder(v); break;
                    case F.Totp: e.Totp = v.Trim(); break;
                    case F.Email:
                        if (hasUsernameColumn) e.Email = v.Trim();
                        else if (string.IsNullOrEmpty(e.Username)) e.Username = v.Trim();
                        break;
                    case F.Phone: e.Phone = v.Trim(); break;
                    case F.Favorite: e.Favorite = v.Trim() is "1" or "true" or "True" or "TRUE" or "yes" or "да"; break;
                    case F.Tags:
                        e.Tags.AddRange(v.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                        break;
                    case F.Type: type = v.Trim().ToLowerInvariant(); break;
                    case F.Fields: ParseFieldsBlob(v, e); break;
                    case F.SecretKey: e.SecretKey = v; break;
                    case F.Ignore: break;
                    case null:
                        if (i < header.Length && !string.IsNullOrWhiteSpace(header[i]))
                            e.CustomFields.Add(new CustomField { Name = header[i].Trim(), Value = v, Protected = ImportHelpers.LooksSecret(header[i]) });
                        break;
                }
            }

            if (type is "folder") continue; // NordPass exports folders as rows
            if (e.Url.Equals("http://sn", StringComparison.OrdinalIgnoreCase)) e.Url = ""; // LastPass secure note
            if (e.Username.Length == 0 && e.Email.Length > 0) e.Username = e.Email; // Proton Pass: login by e-mail
            if (ImportHelpers.IsEmpty(e) && string.IsNullOrEmpty(e.Title))
            {
                skipped++;
                continue;
            }
            e.Title = ImportHelpers.MakeTitle(e.Title, e.Url, e.Username);
            result.Entries.Add(e);
        }
        if (skipped > 0) result.Warn(WarningCodes.SkippedRows, skipped);
        return result;
    }

    /// <summary>Bitwarden/RoboForm style "name: value" lines.</summary>
    private static void ParseFieldsBlob(string blob, VaultEntry e)
    {
        foreach (var line in blob.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (value.Length > 0)
                e.CustomFields.Add(new CustomField { Name = name, Value = value, Protected = ImportHelpers.LooksSecret(name) });
        }
    }

    internal static string CleanFolder(string folder)
    {
        folder = folder.Replace('\\', '/').Trim().Trim('/');
        if (folder.StartsWith("Root/", StringComparison.OrdinalIgnoreCase)) folder = folder[5..];
        else if (folder.Equals("Root", StringComparison.OrdinalIgnoreCase)) folder = "";
        return folder;
    }
}
