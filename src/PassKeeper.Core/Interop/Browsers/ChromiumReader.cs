using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PassKeeper.Core.Models;
using PassKeeper.Core.Storage;

namespace PassKeeper.Core.Interop.Browsers;

/// <summary>
/// Reads saved passwords of Chromium-based browsers for the current Windows user:
/// "Local State" → os_crypt.encrypted_key (DPAPI) → AES-256-GCM key for "v10"/"v11" values.
/// "v20" values (Chrome 127+ App-Bound Encryption) cannot be decrypted by third-party apps and are reported.
/// Yandex Browser uses an additional per-database key and AAD bound to the login row.
/// </summary>
public static class ChromiumReader
{
    public static ImportResult Read(BrowserProfile profile)
    {
        var result = new ImportResult { Source = profile.DisplayName };
        byte[]? key;
        try
        {
            key = ReadMasterKey(profile.UserDataPath);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or JsonException or FormatException)
        {
            result.Warn(WarningCodes.Error, profile.DisplayName, ex.Message);
            return result;
        }

        var files = profile.Kind == BrowserKind.Yandex
            ? new[] { "Ya Passman Data" }
            : new[] { "Login Data", "Login Data For Account" };

        int appBound = 0, failed = 0;
        var seen = new HashSet<string>();
        foreach (var file in files)
        {
            var path = Path.Combine(profile.ProfilePath, file);
            if (!File.Exists(path)) continue;
            var tmp = FileUtil.CreateTempDirectory();
            try
            {
                var copy = Path.Combine(tmp, "db");
                FileUtil.CopyShared(path, copy);
                foreach (var suffix in new[] { "-journal", "-wal" })
                    if (File.Exists(path + suffix)) FileUtil.CopyShared(path + suffix, copy + suffix);

                var cs = new SqliteConnectionStringBuilder { DataSource = copy, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString();
                using var conn = new SqliteConnection(cs);
                conn.Open();
                if (profile.Kind == BrowserKind.Yandex)
                    ReadYandex(conn, key, profile, result, seen, ref failed);
                else
                    ReadChromium(conn, key, result, seen, ref appBound, ref failed);
            }
            catch (SqliteException ex)
            {
                result.Warn(WarningCodes.Error, profile.DisplayName, ex.Message);
            }
            catch (IOException ex)
            {
                result.Warn(WarningCodes.Error, profile.DisplayName, ex.Message);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                FileUtil.TryDeleteDirectory(tmp);
            }
        }

        if (appBound > 0) result.Warn(WarningCodes.AppBound, profile.DisplayName, appBound);
        if (failed > 0) result.Warn(WarningCodes.DecryptFailed, profile.DisplayName, failed);
        if (key != null) CryptographicOperations.ZeroMemory(key);
        foreach (var e in result.Entries) e.Folder = profile.Browser;
        return result;
    }

    internal static byte[]? ReadMasterKey(string userDataPath)
    {
        var localState = Path.Combine(userDataPath, "Local State");
        if (!File.Exists(localState)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(localState));
        if (!doc.RootElement.TryGetProperty("os_crypt", out var osCrypt) ||
            !osCrypt.TryGetProperty("encrypted_key", out var encKey) || encKey.GetString() is not { } b64)
            return null;
        var blob = Convert.FromBase64String(b64);
        if (blob.Length < 5 || Encoding.ASCII.GetString(blob, 0, 5) != "DPAPI")
            throw new CryptographicException("Unexpected browser key format.");
        return ProtectedData.Unprotect(blob[5..], null, DataProtectionScope.CurrentUser);
    }

    private static void ReadChromium(SqliteConnection conn, byte[]? key, ImportResult result, HashSet<string> seen,
        ref int appBound, ref int failed)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT origin_url, username_value, password_value, signon_realm, date_created FROM logins WHERE blacklisted_by_user = 0";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var origin = reader.IsDBNull(0) ? "" : reader.GetString(0);
            var username = reader.IsDBNull(1) ? "" : reader.GetString(1);
            var blob = reader.IsDBNull(2) ? [] : (byte[])reader.GetValue(2);
            var realm = reader.IsDBNull(3) ? "" : reader.GetString(3);
            long? created = reader.IsDBNull(4) ? null : reader.GetInt64(4);
            if (blob.Length == 0 && username.Length == 0) continue;

            string password;
            var status = DecryptValue(blob, key, out password);
            if (status == DecryptStatus.AppBound) { appBound++; continue; }
            if (status == DecryptStatus.Failed) { failed++; continue; }

            var url = origin.Length > 0 ? origin : realm;
            if (!seen.Add(url + "\n" + username + "\n" + password)) continue;
            result.Entries.Add(new VaultEntry
            {
                Title = ImportHelpers.MakeTitle(null, url, username),
                Url = url,
                Username = username,
                Password = password,
                CreatedUtc = ImportHelpers.FromWebKit(created) ?? DateTime.UtcNow,
            });
        }
    }

    internal enum DecryptStatus { Ok, AppBound, Failed }

    internal static DecryptStatus DecryptValue(byte[] blob, byte[]? key, out string value)
    {
        value = "";
        if (blob.Length == 0) return DecryptStatus.Ok;
        try
        {
            if (blob.Length > 3 && blob[0] == 'v' && blob[1] == '2' && blob[2] == '0') return DecryptStatus.AppBound;
            if (blob.Length > 3 + 12 + 16 && blob[0] == 'v' && blob[1] == '1' && (blob[2] == '0' || blob[2] == '1'))
            {
                if (key == null) return DecryptStatus.Failed;
                var plain = Crypto.Aead.Decrypt(key, blob.AsSpan(3), ReadOnlySpan<byte>.Empty);
                value = Encoding.UTF8.GetString(plain);
                return DecryptStatus.Ok;
            }
            // Pre-Chrome 80: the value is a raw DPAPI blob.
            value = Encoding.UTF8.GetString(ProtectedData.Unprotect(blob, null, DataProtectionScope.CurrentUser));
            return DecryptStatus.Ok;
        }
        catch (CryptographicException)
        {
            return DecryptStatus.Failed;
        }
    }

    // ---- Yandex Browser ----------------------------------------------------------------------------------------

    private static void ReadYandex(SqliteConnection conn, byte[]? osKey, BrowserProfile profile, ImportResult result,
        HashSet<string> seen, ref int failed)
    {
        if (osKey == null)
        {
            result.Warn(WarningCodes.Error, profile.DisplayName, "Local State key not found");
            return;
        }
        if (HasYandexMasterPassword(conn))
        {
            result.Warn(WarningCodes.YandexMasterPassword, profile.DisplayName);
            return;
        }

        byte[]? dataKey = null;
        using (var meta = conn.CreateCommand())
        {
            meta.CommandText = "SELECT value FROM meta WHERE key = 'local_encryptor_data'";
            if (meta.ExecuteScalar() is byte[] encryptorData) dataKey = DecryptYandexDataKey(encryptorData, osKey);
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT origin_url, username_element, username_value, password_element, password_value, signon_realm, date_created FROM logins WHERE blacklisted_by_user = 0";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string S(int i) => reader.IsDBNull(i) ? "" : reader.GetValue(i) as string ?? "";
            var origin = S(0);
            var usernameElement = S(1);
            var username = S(2);
            var passwordElement = S(3);
            var blob = reader.IsDBNull(4) ? [] : (byte[])reader.GetValue(4);
            var realm = S(5);
            long? created = reader.IsDBNull(6) ? null : reader.GetInt64(6);
            if (blob.Length == 0) continue;

            string? password = null;
            if (dataKey != null && blob.Length > 12 + 16)
            {
                var aad = SHA1.HashData(Encoding.UTF8.GetBytes(
                    origin + "\0" + usernameElement + "\0" + username + "\0" + passwordElement + "\0" + realm));
                password = TryGcm(dataKey, blob, aad) ?? TryGcm(dataKey, blob, []);
            }
            if (password == null && DecryptValue(blob, osKey, out var legacy) == DecryptStatus.Ok) password = legacy;
            if (password == null)
            {
                failed++;
                continue;
            }

            var url = origin.Length > 0 ? origin : realm;
            if (!seen.Add(url + "\n" + username + "\n" + password)) continue;
            result.Entries.Add(new VaultEntry
            {
                Title = ImportHelpers.MakeTitle(null, url, username),
                Url = url,
                Username = username,
                Password = password,
                CreatedUtc = ImportHelpers.FromWebKit(created) ?? DateTime.UtcNow,
            });
        }
        if (dataKey != null) CryptographicOperations.ZeroMemory(dataKey);
    }

    private static bool HasYandexMasterPassword(SqliteConnection conn)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT sealed_key FROM active_keys";
            return cmd.ExecuteScalar() is byte[] { Length: > 0 } or string { Length: > 0 };
        }
        catch (SqliteException)
        {
            return false; // table does not exist
        }
    }

    private static byte[]? DecryptYandexDataKey(byte[] encryptorData, byte[] osKey)
    {
        var index = encryptorData.AsSpan().IndexOf("v10"u8);
        if (index < 0) return null;
        var start = index + 3;
        var length = Math.Min(96, encryptorData.Length - start);
        if (length <= 12 + 16) return null;
        try
        {
            var plain = Crypto.Aead.Decrypt(osKey, encryptorData.AsSpan(start, length), ReadOnlySpan<byte>.Empty);
            // Protobuf: field 1 (varint) = 1, field 2 (bytes, 32) = key.
            if (plain.Length >= 36 && plain[0] == 0x08 && plain[1] == 0x01 && plain[2] == 0x12 && plain[3] == 0x20)
                return plain.AsSpan(4, 32).ToArray();
            return null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static string? TryGcm(byte[] key, byte[] blob, byte[] aad)
    {
        try
        {
            return Encoding.UTF8.GetString(Crypto.Aead.Decrypt(key, blob, aad));
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
