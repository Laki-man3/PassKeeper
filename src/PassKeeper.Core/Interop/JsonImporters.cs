using System.IO.Compression;
using System.Text.Json;
using PassKeeper.Core.Models;

namespace PassKeeper.Core.Interop;

/// <summary>Bitwarden (unencrypted JSON) and 1Password (.1pux) importers.</summary>
public static class JsonImporters
{
    public static bool LooksLikeBitwarden(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("items", out var items) &&
        items.ValueKind == JsonValueKind.Array;

    public static ImportResult ImportBitwarden(string json, string source)
    {
        var result = new ImportResult { Source = source };
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (root.TryGetProperty("encrypted", out var enc) && enc.ValueKind == JsonValueKind.True)
        {
            result.Warn(WarningCodes.Error, source, "Encrypted Bitwarden exports are not supported — export as unencrypted JSON");
            return result;
        }

        var folders = new Dictionary<string, string>();
        foreach (var array in new[] { "folders", "collections" })
        {
            if (!root.TryGetProperty(array, out var fs) || fs.ValueKind != JsonValueKind.Array) continue;
            foreach (var f in fs.EnumerateArray())
                if (Str(f, "id") is { Length: > 0 } id) folders[id] = Str(f, "name");
        }

        foreach (var item in root.GetProperty("items").EnumerateArray())
        {
            var e = new VaultEntry
            {
                Title = Str(item, "name"),
                Notes = Str(item, "notes"),
                Favorite = item.TryGetProperty("favorite", out var fav) && fav.ValueKind == JsonValueKind.True,
            };
            var folderId = Str(item, "folderId");
            if (folderId.Length > 0 && folders.TryGetValue(folderId, out var folderName)) e.Folder = folderName;
            else if (item.TryGetProperty("collectionIds", out var cids) && cids.ValueKind == JsonValueKind.Array)
                foreach (var c in cids.EnumerateArray())
                    if (c.GetString() is { } cid && folders.TryGetValue(cid, out var cn)) { e.Folder = cn; break; }

            if (item.TryGetProperty("login", out var login) && login.ValueKind == JsonValueKind.Object)
            {
                e.Username = Str(login, "username");
                e.Password = Str(login, "password");
                e.Totp = Str(login, "totp");
                if (login.TryGetProperty("uris", out var uris) && uris.ValueKind == JsonValueKind.Array)
                    foreach (var u in uris.EnumerateArray())
                    {
                        var uri = Str(u, "uri");
                        if (uri.Length == 0) continue;
                        if (e.Url.Length == 0) e.Url = uri;
                        else e.ExtraUrls.Add(uri);
                    }
            }
            if (item.TryGetProperty("card", out var card) && card.ValueKind == JsonValueKind.Object)
            {
                AddField(e, "Cardholder", Str(card, "cardholderName"), false);
                AddField(e, "Brand", Str(card, "brand"), false);
                AddField(e, "Card number", Str(card, "number"), true);
                var exp = $"{Str(card, "expMonth")}/{Str(card, "expYear")}".Trim('/');
                AddField(e, "Expiration", exp, false);
                AddField(e, "CVV", Str(card, "code"), true);
            }
            if (item.TryGetProperty("identity", out var id) && id.ValueKind == JsonValueKind.Object)
            {
                if (e.Username.Length == 0) e.Username = Str(id, "username");
                e.Email = Str(id, "email");
                e.Phone = Str(id, "phone");
                foreach (var p in id.EnumerateObject())
                {
                    if (p.Name is "username" or "email" or "phone" || p.Value.ValueKind != JsonValueKind.String) continue;
                    AddField(e, p.Name, p.Value.GetString() ?? "", p.Name is "ssn" or "passportNumber" or "licenseNumber");
                }
            }
            if (item.TryGetProperty("sshKey", out var ssh) && ssh.ValueKind == JsonValueKind.Object)
            {
                e.SecretKey = Str(ssh, "privateKey");
                AddField(e, "Public key", Str(ssh, "publicKey"), false);
                AddField(e, "Fingerprint", Str(ssh, "keyFingerprint"), false);
            }
            if (item.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Array)
                foreach (var f in fields.EnumerateArray())
                {
                    var type = f.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : 0;
                    AddField(e, Str(f, "name"), Str(f, "value"), type == 1);
                }

            if (ImportHelpers.IsEmpty(e) && e.Title.Length == 0) continue;
            e.Title = ImportHelpers.MakeTitle(e.Title, e.Url, e.Username);
            result.Entries.Add(e);
        }
        return result;
    }

    public static ImportResult Import1Pux(byte[] zipBytes, string source)
    {
        var result = new ImportResult { Source = source };
        using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        var data = zip.GetEntry("export.data") ?? throw new InvalidDataException("export.data not found in the 1PUX archive.");
        using var stream = data.Open();
        using var doc = JsonDocument.Parse(stream);

        foreach (var account in Arr(doc.RootElement, "accounts"))
        foreach (var vault in Arr(account, "vaults"))
        {
            var vaultName = vault.TryGetProperty("attrs", out var va) ? Str(va, "name") : "";
            foreach (var item in Arr(vault, "items"))
            {
                if (Str(item, "state") == "archived") continue;
                var overview = item.TryGetProperty("overview", out var ov) ? ov : default;
                var details = item.TryGetProperty("details", out var dt) ? dt : default;
                var e = new VaultEntry
                {
                    Title = Str(overview, "title"),
                    Url = Str(overview, "url"),
                    Folder = vaultName,
                    Favorite = item.TryGetProperty("favIndex", out var fi) && fi.ValueKind == JsonValueKind.Number && fi.GetInt32() > 0,
                    Notes = Str(details, "notesPlain"),
                };
                foreach (var u in Arr(overview, "urls"))
                {
                    var url = Str(u, "url");
                    if (url.Length > 0 && url != e.Url) e.ExtraUrls.Add(url);
                }
                foreach (var t in Arr(overview, "tags"))
                    if (t.GetString() is { Length: > 0 } tag) e.Tags.Add(tag);

                foreach (var lf in Arr(details, "loginFields"))
                {
                    var designation = Str(lf, "designation");
                    var value = Str(lf, "value");
                    if (designation == "username") e.Username = value;
                    else if (designation == "password") e.Password = value;
                }
                if (e.Password.Length == 0) e.Password = Str(details, "password");

                foreach (var section in Arr(details, "sections"))
                foreach (var field in Arr(section, "fields"))
                {
                    var title = Str(field, "title");
                    if (!field.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Object) continue;
                    foreach (var p in value.EnumerateObject())
                    {
                        string text = p.Value.ValueKind switch
                        {
                            JsonValueKind.String => p.Value.GetString() ?? "",
                            JsonValueKind.Number => p.Value.GetRawText(),
                            JsonValueKind.Object when p.Name == "email" => Str(p.Value, "email_address"),
                            _ => "",
                        };
                        if (text.Length == 0) continue;
                        switch (p.Name)
                        {
                            case "totp" when e.Totp.Length == 0: e.Totp = text; break;
                            case "phone" when e.Phone.Length == 0: e.Phone = text; break;
                            case "email" when e.Email.Length == 0: e.Email = text; break;
                            default: AddField(e, title.Length > 0 ? title : p.Name, text, p.Name == "concealed"); break;
                        }
                    }
                }

                if (ImportHelpers.IsEmpty(e) && e.Title.Length == 0) continue;
                e.Title = ImportHelpers.MakeTitle(e.Title, e.Url, e.Username);
                result.Entries.Add(e);
            }
        }
        return result;
    }

    private static void AddField(VaultEntry e, string name, string value, bool isProtected)
    {
        if (string.IsNullOrEmpty(value)) return;
        e.CustomFields.Add(new CustomField { Name = string.IsNullOrWhiteSpace(name) ? "Field" : name, Value = value, Protected = isProtected || ImportHelpers.LooksSecret(name) });
    }

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";

    private static IEnumerable<JsonElement> Arr(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray()
            : [];
}
