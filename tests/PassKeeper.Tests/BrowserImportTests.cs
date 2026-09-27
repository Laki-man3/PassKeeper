using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PassKeeper.Core.Crypto;
using PassKeeper.Core.Interop;
using PassKeeper.Core.Interop.Browsers;

namespace PassKeeper.Tests;

/// <summary>Builds synthetic browser profiles (encrypted exactly like the browsers do) and imports them.</summary>
public class BrowserImportTests
{
    private static void WriteLocalState(string userData, byte[] key)
    {
        var sealedKey = ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser);
        var blob = Encoding.ASCII.GetBytes("DPAPI").Concat(sealedKey).ToArray();
        File.WriteAllText(Path.Combine(userData, "Local State"),
            JsonSerializer.Serialize(new { os_crypt = new { encrypted_key = Convert.ToBase64String(blob) } }));
    }

    private static void Exec(SqliteConnection c, string sql, params (string, object)[] args)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void Chromium_DecryptsV10_ReportsV20_SkipsBlacklisted()
    {
        using var dir = new TempDir();
        var profile = Directory.CreateDirectory(Path.Combine(dir.Path, "Default")).FullName;
        var key = RandomNumberGenerator.GetBytes(32);
        WriteLocalState(dir.Path, key);

        var db = Path.Combine(profile, "Login Data");
        using (var c = new SqliteConnection($"Data Source={db};Pooling=False"))
        {
            c.Open();
            Exec(c, "CREATE TABLE logins (origin_url TEXT, username_value TEXT, password_value BLOB, signon_realm TEXT, date_created INTEGER, blacklisted_by_user INTEGER)");
            void Add(string url, string user, byte[] pw, int black = 0) => Exec(c,
                "INSERT INTO logins VALUES ($u, $n, $p, $r, 13300000000000000, $b)", ("$u", url), ("$n", user), ("$p", pw), ("$r", url), ("$b", black));
            Add("https://site.example/", "alice", [.. "v10"u8, .. Aead.Encrypt(key, "Секрет-1"u8, [])]);
            Add("https://other.example/", "bob", [.. "v20"u8, .. RandomNumberGenerator.GetBytes(40)]);
            Add("https://never.example/", "", [], 1);
        }

        var browser = new BrowserProfile { Browser = "Test Chrome", ProfileName = "Default", Kind = BrowserKind.Chromium, ProfilePath = profile, UserDataPath = dir.Path };
        var result = BrowserDetector.Read(browser);
        var e = Assert.Single(result.Entries);
        Assert.Equal("alice", e.Username);
        Assert.Equal("Секрет-1", e.Password);
        Assert.Equal("site.example", e.Title);
        Assert.Contains(result.Warnings, w => w.Code == WarningCodes.AppBound && (int)w.Args[1] == 1);
    }

    [Fact]
    public void Yandex_DecryptsWithDataKeyAndRowAad()
    {
        using var dir = new TempDir();
        var profile = Directory.CreateDirectory(Path.Combine(dir.Path, "Default")).FullName;
        var osKey = RandomNumberGenerator.GetBytes(32);
        var dataKey = RandomNumberGenerator.GetBytes(32);
        WriteLocalState(dir.Path, osKey);

        byte[] keyProto = [0x08, 0x01, 0x12, 0x20, .. dataKey, .. RandomNumberGenerator.GetBytes(32)];
        byte[] encryptorData = [0x0A, 0x60, .. "v10"u8, .. Aead.Encrypt(osKey, keyProto, [])];

        var db = Path.Combine(profile, "Ya Passman Data");
        using (var c = new SqliteConnection($"Data Source={db};Pooling=False"))
        {
            c.Open();
            Exec(c, "CREATE TABLE meta (key TEXT, value BLOB)");
            Exec(c, "INSERT INTO meta VALUES ('local_encryptor_data', $v)", ("$v", encryptorData));
            Exec(c, "CREATE TABLE logins (origin_url TEXT, username_element TEXT, username_value TEXT, password_element TEXT, password_value BLOB, signon_realm TEXT, date_created INTEGER, blacklisted_by_user INTEGER)");
            var aad = SHA1.HashData(Encoding.UTF8.GetBytes("https://ya.example/\0login\0yuser\0passwd\0https://ya.example/"));
            Exec(c, "INSERT INTO logins VALUES ('https://ya.example/', 'login', 'yuser', 'passwd', $p, 'https://ya.example/', 0, 0)",
                ("$p", Aead.Encrypt(dataKey, "ya-pass"u8, aad)));
        }

        var browser = new BrowserProfile { Browser = "Yandex", ProfileName = "Default", Kind = BrowserKind.Yandex, ProfilePath = profile, UserDataPath = dir.Path };
        var result = BrowserDetector.Read(browser);
        var e = Assert.Single(result.Entries);
        Assert.Equal("yuser", e.Username);
        Assert.Equal("ya-pass", e.Password);
    }

    private const string OidPbes2 = "1.2.840.113549.1.5.13";
    private const string OidPbkdf2 = "1.2.840.113549.1.5.12";
    private const string OidHmacSha256 = "1.2.840.113549.2.9";
    private const string OidAes = "2.16.840.1.101.3.4.1.42";
    private const string OidDes3 = "1.2.840.113549.3.7";

    private static byte[] NssPbes2(byte[] plain, byte[] globalSalt, string primary)
    {
        var salt = RandomNumberGenerator.GetBytes(32);
        var ivTail = RandomNumberGenerator.GetBytes(14);
        var k = SHA1.HashData([.. globalSalt, .. Encoding.UTF8.GetBytes(primary)]);
        var key = Rfc2898DeriveBytes.Pbkdf2(k, salt, 10000, HashAlgorithmName.SHA256, 32);
        using var aes = Aes.Create();
        aes.Key = key;
        byte[] iv = [0x04, 0x0E, .. ivTail];
        var ct = aes.EncryptCbc(plain, iv, PaddingMode.PKCS7);

        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence())
        {
            using (w.PushSequence())
            {
                w.WriteObjectIdentifier(OidPbes2);
                using (w.PushSequence())
                {
                    using (w.PushSequence())
                    {
                        w.WriteObjectIdentifier(OidPbkdf2);
                        using (w.PushSequence())
                        {
                            w.WriteOctetString(salt);
                            w.WriteInteger(10000);
                            w.WriteInteger(32);
                            using (w.PushSequence()) w.WriteObjectIdentifier(OidHmacSha256);
                        }
                    }
                    using (w.PushSequence())
                    {
                        w.WriteObjectIdentifier(OidAes);
                        w.WriteOctetString(ivTail);
                    }
                }
            }
            w.WriteOctetString(ct);
        }
        return w.Encode();
    }

    private static string LoginField(string value, byte[] keyId, byte[] key, bool aesField)
    {
        byte[] iv, ct;
        if (aesField)
        {
            iv = RandomNumberGenerator.GetBytes(16);
            using var aes = Aes.Create();
            aes.Key = key;
            ct = aes.EncryptCbc(Encoding.UTF8.GetBytes(value), iv, PaddingMode.PKCS7);
        }
        else
        {
            iv = RandomNumberGenerator.GetBytes(8);
            using var des = TripleDES.Create();
            des.Key = key[..24];
            ct = des.EncryptCbc(Encoding.UTF8.GetBytes(value), iv, PaddingMode.PKCS7);
        }
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence())
        {
            w.WriteOctetString(keyId);
            using (w.PushSequence())
            {
                w.WriteObjectIdentifier(aesField ? OidAes : OidDes3);
                w.WriteOctetString(iv);
            }
            w.WriteOctetString(ct);
        }
        return Convert.ToBase64String(w.Encode());
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("", false)]
    [InlineData("primary-pass", true)]
    public void Firefox_DecryptsKey4AndLogins(string primary, bool aesField)
    {
        using var dir = new TempDir();
        var globalSalt = RandomNumberGenerator.GetBytes(20);
        var keyId = Convert.FromHexString("F8000000000000000000000000000001");
        var key = RandomNumberGenerator.GetBytes(aesField ? 32 : 24);

        using (var c = new SqliteConnection($"Data Source={dir.File("key4.db")};Pooling=False"))
        {
            c.Open();
            Exec(c, "CREATE TABLE metaData (id TEXT PRIMARY KEY, item1, item2)");
            Exec(c, "INSERT INTO metaData VALUES ('password', $s, $c)", ("$s", globalSalt), ("$c", NssPbes2("password-check"u8.ToArray(), globalSalt, primary)));
            Exec(c, "CREATE TABLE nssPrivate (id INTEGER PRIMARY KEY, a11, a102)");
            Exec(c, "INSERT INTO nssPrivate (a11, a102) VALUES ($k, $i)", ("$k", NssPbes2(key, globalSalt, primary)), ("$i", keyId));
        }
        var logins = new
        {
            logins = new[]
            {
                new
                {
                    hostname = "https://fx.example", encryptedUsername = LoginField("fxuser", keyId, key, aesField),
                    encryptedPassword = LoginField("fx-пароль", keyId, key, aesField), timeCreated = 1700000000000L,
                },
            },
        };
        File.WriteAllText(dir.File("logins.json"), JsonSerializer.Serialize(logins));

        var browser = new BrowserProfile { Browser = "Firefox", ProfileName = "test", Kind = BrowserKind.Firefox, ProfilePath = dir.Path };
        if (primary.Length > 0)
        {
            var locked = BrowserDetector.Read(browser);
            Assert.Empty(locked.Entries);
            Assert.Contains(locked.Warnings, w => w.Code == WarningCodes.PrimaryPassword);
        }
        var result = BrowserDetector.Read(browser, primary);
        var e = Assert.Single(result.Entries);
        Assert.Equal("fxuser", e.Username);
        Assert.Equal("fx-пароль", e.Password);
        Assert.Equal("https://fx.example", e.Url);
    }

    [Fact]
    public void Detector_DoesNotThrow()
    {
        var profiles = BrowserDetector.Detect();
        Assert.NotNull(profiles);
    }
}
