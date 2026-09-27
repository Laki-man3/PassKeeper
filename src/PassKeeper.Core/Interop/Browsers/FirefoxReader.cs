using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PassKeeper.Core.Models;
using PassKeeper.Core.Storage;

namespace PassKeeper.Core.Interop.Browsers;

public sealed class FirefoxPrimaryPasswordException() : Exception("Firefox primary password is required.");

/// <summary>
/// Decrypts Firefox/Thunderbird (NSS) saved logins: key4.db holds the master key protected by
/// PBES2 (PBKDF2-SHA256 + AES-256-CBC) or the legacy SHA1/3DES PBE; logins.json values are
/// DES-EDE3-CBC or AES-256-CBC encrypted with that key.
/// </summary>
public static class FirefoxReader
{
    private const string OidPbes2 = "1.2.840.113549.1.5.13";
    private const string OidPbkdf2 = "1.2.840.113549.1.5.12";
    private const string OidHmacSha256 = "1.2.840.113549.2.9";
    private const string OidHmacSha1 = "1.2.840.113549.2.7";
    private const string OidAes256Cbc = "2.16.840.1.101.3.4.1.42";
    private const string OidDesEde3Cbc = "1.2.840.113549.3.7";
    private const string OidPbeSha1TripleDes = "1.2.840.113549.1.12.5.1.3";

    public static ImportResult Read(BrowserProfile profile, string? primaryPassword)
    {
        var result = new ImportResult { Source = profile.DisplayName };
        Dictionary<string, byte[]> keys;
        try
        {
            keys = ReadKeys(Path.Combine(profile.ProfilePath, "key4.db"), primaryPassword ?? "");
        }
        catch (FirefoxPrimaryPasswordException)
        {
            result.Warn(WarningCodes.PrimaryPassword, profile.DisplayName);
            return result;
        }
        catch (Exception ex) when (ex is SqliteException or IOException or CryptographicException or AsnContentException)
        {
            result.Warn(WarningCodes.Error, profile.DisplayName, ex.Message);
            return result;
        }

        var failed = 0;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(profile.ProfilePath, "logins.json")));
            if (doc.RootElement.TryGetProperty("logins", out var logins))
                foreach (var login in logins.EnumerateArray())
                {
                    try
                    {
                        var hostname = Str(login, "hostname");
                        var username = DecryptField(Str(login, "encryptedUsername"), keys);
                        var password = DecryptField(Str(login, "encryptedPassword"), keys);
                        long? created = login.TryGetProperty("timeCreated", out var tc) && tc.ValueKind == JsonValueKind.Number ? tc.GetInt64() : null;
                        result.Entries.Add(new VaultEntry
                        {
                            Title = ImportHelpers.MakeTitle(null, hostname, username),
                            Url = hostname,
                            Username = username,
                            Password = password,
                            Folder = profile.Browser,
                            CreatedUtc = ImportHelpers.FromUnixMilliseconds(created) ?? DateTime.UtcNow,
                        });
                    }
                    catch (Exception ex) when (ex is CryptographicException or AsnContentException or FormatException)
                    {
                        failed++;
                    }
                }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            result.Warn(WarningCodes.Error, profile.DisplayName, ex.Message);
        }
        if (failed > 0) result.Warn(WarningCodes.DecryptFailed, profile.DisplayName, failed);
        foreach (var k in keys.Values) CryptographicOperations.ZeroMemory(k);
        return result;
    }

    /// <returns>Map of hex(CKA_ID) → key bytes.</returns>
    internal static Dictionary<string, byte[]> ReadKeys(string key4Path, string primaryPassword)
    {
        var tmp = FileUtil.CreateTempDirectory();
        try
        {
            var copy = Path.Combine(tmp, "key4.db");
            FileUtil.CopyShared(key4Path, copy);
            foreach (var suffix in new[] { "-wal", "-journal" })
                if (File.Exists(key4Path + suffix)) FileUtil.CopyShared(key4Path + suffix, copy + suffix);

            var cs = new SqliteConnectionStringBuilder { DataSource = copy, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString();
            using var conn = new SqliteConnection(cs);
            conn.Open();

            byte[] globalSalt, check;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT item1, item2 FROM metaData WHERE id = 'password'";
                using var r = cmd.ExecuteReader();
                if (!r.Read()) throw new CryptographicException("key4.db has no password entry.");
                globalSalt = (byte[])r.GetValue(0);
                check = (byte[])r.GetValue(1);
            }

            var passwordBytes = Encoding.UTF8.GetBytes(primaryPassword);
            byte[] checkPlain;
            try
            {
                checkPlain = DecryptPbe(check, globalSalt, passwordBytes);
            }
            catch (CryptographicException)
            {
                throw new FirefoxPrimaryPasswordException();
            }
            if (!Encoding.ASCII.GetString(checkPlain).StartsWith("password-check", StringComparison.Ordinal))
                throw new FirefoxPrimaryPasswordException();

            var keys = new Dictionary<string, byte[]>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT a11, a102 FROM nssPrivate";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    if (r.IsDBNull(0)) continue;
                    var a11 = (byte[])r.GetValue(0);
                    var a102 = r.IsDBNull(1) ? [] : (byte[])r.GetValue(1);
                    try
                    {
                        var key = Unpad(DecryptPbe(a11, globalSalt, passwordBytes));
                        keys[Convert.ToHexString(a102)] = key;
                    }
                    catch (CryptographicException) { }
                }
            }
            if (keys.Count == 0) throw new CryptographicException("No usable key found in key4.db.");
            return keys;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            FileUtil.TryDeleteDirectory(tmp);
        }
    }

    /// <summary>Decrypts an NSS PBE-protected item (PBES2/AES or legacy SHA1-3DES).</summary>
    internal static byte[] DecryptPbe(byte[] der, byte[] globalSalt, byte[] password)
    {
        var outer = new AsnReader(der, AsnEncodingRules.BER).ReadSequence();
        var algorithm = outer.ReadSequence();
        var oid = algorithm.ReadObjectIdentifier();

        if (oid == OidPbes2)
        {
            var parameters = algorithm.ReadSequence();
            var kdf = parameters.ReadSequence();
            if (kdf.ReadObjectIdentifier() != OidPbkdf2) throw new CryptographicException("Unsupported NSS KDF.");
            var kdfParams = kdf.ReadSequence();
            var salt = kdfParams.ReadOctetString();
            var iterations = (int)kdfParams.ReadInteger();
            var keyLength = 32;
            var prf = HashAlgorithmName.SHA1;
            while (kdfParams.HasData)
            {
                var tag = kdfParams.PeekTag();
                if (tag.HasSameClassAndValue(Asn1Tag.Integer)) keyLength = (int)kdfParams.ReadInteger();
                else if (tag.HasSameClassAndValue(Asn1Tag.Sequence))
                {
                    var prfSeq = kdfParams.ReadSequence();
                    var prfOid = prfSeq.ReadObjectIdentifier();
                    prf = prfOid == OidHmacSha256 ? HashAlgorithmName.SHA256 : prfOid == OidHmacSha1 ? HashAlgorithmName.SHA1 : throw new CryptographicException("Unsupported PRF.");
                }
                else kdfParams.ReadEncodedValue();
            }
            var cipher = parameters.ReadSequence();
            if (cipher.ReadObjectIdentifier() != OidAes256Cbc) throw new CryptographicException("Unsupported NSS cipher.");
            var ivTail = cipher.ReadOctetString();
            var ciphertext = outer.ReadOctetString();

            var k = SHA1.HashData([.. globalSalt, .. password]);
            var key = Rfc2898DeriveBytes.Pbkdf2(k, salt, Math.Max(1, iterations), prf, keyLength);
            // NSS stores a 14-byte IV; the full IV is the DER header (04 0E) followed by it.
            var iv = ivTail.Length == 16 ? ivTail : [0x04, 0x0E, .. ivTail];
            using var aes = Aes.Create();
            aes.Key = key;
            return aes.DecryptCbc(ciphertext, iv, PaddingMode.None);
        }

        if (oid == OidPbeSha1TripleDes)
        {
            var parameters = algorithm.ReadSequence();
            var entrySalt = parameters.ReadOctetString();
            var ciphertext = outer.ReadOctetString();

            var hp = SHA1.HashData([.. globalSalt, .. password]);
            var pes = new byte[20];
            entrySalt.AsSpan(0, Math.Min(20, entrySalt.Length)).CopyTo(pes);
            var chp = SHA1.HashData([.. hp, .. entrySalt]);
            byte[] pesSalt = [.. pes, .. entrySalt];
            var k1 = HMACSHA1.HashData(chp, pesSalt);
            var tk = HMACSHA1.HashData(chp, pes);
            byte[] tkSalt = [.. tk, .. entrySalt];
            var k2 = HMACSHA1.HashData(chp, tkSalt);
            byte[] k = [.. k1, .. k2];
            using var des = TripleDES.Create();
            des.Key = k[..24];
            return des.DecryptCbc(ciphertext, k[^8..], PaddingMode.None);
        }

        throw new CryptographicException($"Unsupported NSS algorithm {oid}.");
    }

    internal static string DecryptField(string base64, Dictionary<string, byte[]> keys)
    {
        if (string.IsNullOrEmpty(base64)) return "";
        var der = Convert.FromBase64String(base64);
        var seq = new AsnReader(der, AsnEncodingRules.BER).ReadSequence();
        var keyId = seq.ReadOctetString();
        var alg = seq.ReadSequence();
        var oid = alg.ReadObjectIdentifier();
        var iv = alg.ReadOctetString();
        var ciphertext = seq.ReadOctetString();

        if (!keys.TryGetValue(Convert.ToHexString(keyId), out var key))
            key = keys.Values.FirstOrDefault(k => oid == OidAes256Cbc ? k.Length == 32 : k.Length >= 24)
                  ?? throw new CryptographicException("Key not found.");

        byte[] plain;
        if (oid == OidDesEde3Cbc)
        {
            using var des = TripleDES.Create();
            des.Key = key[..24];
            plain = des.DecryptCbc(ciphertext, iv, PaddingMode.None);
        }
        else if (oid == OidAes256Cbc)
        {
            using var aes = Aes.Create();
            aes.Key = key[..32];
            plain = aes.DecryptCbc(ciphertext, iv, PaddingMode.None);
        }
        else throw new CryptographicException($"Unsupported cipher {oid}.");
        return Encoding.UTF8.GetString(Unpad(plain));
    }

    internal static byte[] Unpad(byte[] data)
    {
        if (data.Length == 0) return data;
        var pad = data[^1];
        if (pad is 0 or > 16 || pad > data.Length) return data;
        for (var i = data.Length - pad; i < data.Length; i++)
            if (data[i] != pad) return data;
        return data[..^pad];
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
