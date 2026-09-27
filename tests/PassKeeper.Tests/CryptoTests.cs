using System.Security.Cryptography;
using System.Text;
using PassKeeper.Core.Crypto;
using PassKeeper.Core.Interop.KeePass;
using PassKeeper.Core.Security;

namespace PassKeeper.Tests;

public class CryptoTests
{
    [Fact]
    public void Aead_RoundTrip_And_DetectsTampering()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var aad = "header"u8.ToArray();
        var blob = Aead.Encrypt(key, "secret data"u8, aad);
        Assert.Equal("secret data", Encoding.UTF8.GetString(Aead.Decrypt(key, blob, aad)));

        blob[20] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => Aead.Decrypt(key, blob, aad));
        blob[20] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => Aead.Decrypt(key, blob, "other"u8));
        Assert.ThrowsAny<CryptographicException>(() => Aead.Decrypt(RandomNumberGenerator.GetBytes(32), blob, aad));
    }

    [Fact]
    public void Argon2id_IsDeterministic_AndSaltSensitive()
    {
        var p = new KdfParameters { MemoryKiB = 8192, Iterations = 1, Parallelism = 1, Salt = new byte[16] };
        var a = Kdf.Derive("password", p);
        var b = Kdf.Derive("password", p);
        Assert.Equal(a, b);
        p.Salt = Enumerable.Repeat((byte)1, 16).ToArray();
        Assert.NotEqual(a, Kdf.Derive("password", p));
    }

    [Fact]
    public void Argon2_MatchesRfc9106Vector()
    {
        // RFC 9106 §5.3 Argon2id test vector.
        var password = Enumerable.Repeat((byte)0x01, 32).ToArray();
        var salt = Enumerable.Repeat((byte)0x02, 16).ToArray();
        var secret = Enumerable.Repeat((byte)0x03, 8).ToArray();
        var ad = Enumerable.Repeat((byte)0x04, 12).ToArray();
        var tag = Kdf.Argon2(password, "argon2id", salt, 32, 3, 4, 32, secret, ad);
        Assert.Equal("0d640df58d78766c08c037a34a8b53c9d01ef0452d75b65eb52520e96b01e659", Convert.ToHexStringLower(tag));
    }

    [Fact]
    public void ChaCha20_MatchesPlatformImplementation()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plain = RandomNumberGenerator.GetBytes(1000);
        if (!ChaCha20Poly1305.IsSupported) return;
        var expected = new byte[plain.Length];
        using (var aead = new ChaCha20Poly1305(key)) aead.Encrypt(nonce, plain, expected, new byte[16]);
        var actual = (byte[])plain.Clone();
        new ChaCha20(key, nonce, counter: 1).Xor(actual); // AEAD encryption starts at block counter 1
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ChaCha20_Rfc8439Vector()
    {
        var key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var nonce = Convert.FromHexString("000000000000004a00000000");
        var data = Encoding.ASCII.GetBytes("Ladies and Gentlemen of the class of '99: If I could offer you only one tip for the future, sunscreen would be it.");
        new ChaCha20(key, nonce, 1).Xor(data);
        Assert.Equal("6e2e359a2568f98041ba0728dd0d6981", Convert.ToHexStringLower(data[..16]));
    }

    [Fact]
    public void Salsa20_EstreamVector()
    {
        // eSTREAM Salsa20/20, 256-bit key, set 1 vector 0.
        var key = new byte[32];
        key[0] = 0x80;
        var data = new byte[64];
        new Salsa20(key, new byte[8]).Xor(data);
        Assert.Equal(
            "E3BE8FDD8BECA2E3EA8EF9475B29A6E7003951E1097A5C38D23B7A5FAD9F6844B22C97559E2723C7CBBD3FE4FC8D9A0744652A83E72A9C461876AF4D7EF1A117",
            Convert.ToHexString(data));
    }

    [Theory]
    [InlineData(59L, "94287082", "SHA1")]
    [InlineData(1111111109L, "07081804", "SHA1")]
    [InlineData(1234567890L, "89005924", "SHA1")]
    [InlineData(20000000000L, "65353130", "SHA1")]
    [InlineData(59L, "46119246", "SHA256")]
    [InlineData(59L, "90693936", "SHA512")]
    public void Totp_Rfc6238Vectors(long time, string expected, string algorithm)
    {
        var secret = algorithm switch
        {
            "SHA256" => "12345678901234567890123456789012",
            "SHA512" => "1234567890123456789012345678901234567890123456789012345678901234",
            _ => "12345678901234567890",
        };
        var cfg = new TotpConfig { Secret = Encoding.ASCII.GetBytes(secret), Digits = 8, Algorithm = algorithm };
        Assert.Equal(expected, Totp.Compute(cfg, DateTimeOffset.FromUnixTimeSeconds(time)));
    }

    [Fact]
    public void Totp_ParsesUriAndBase32()
    {
        var raw = Encoding.ASCII.GetBytes("12345678901234567890");
        var b32 = Base32.Encode(raw);
        Assert.Equal("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", b32);
        Assert.Equal(raw, Base32.Decode(b32.ToLowerInvariant()));

        var cfg = Totp.Parse($"otpauth://totp/ACME:john@example.com?secret={b32}&issuer=ACME&digits=8&period=60&algorithm=SHA256")!;
        Assert.Equal(8, cfg.Digits);
        Assert.Equal(60, cfg.Period);
        Assert.Equal("SHA256", cfg.Algorithm);
        Assert.Equal("ACME", cfg.Issuer);
        Assert.Equal("john@example.com", cfg.Account);
        Assert.Null(Totp.Parse("not base32 !!"));
    }

    [Fact]
    public void Generator_RespectsOptions()
    {
        for (var i = 0; i < 50; i++)
        {
            var pw = PasswordGenerator.Generate(new GeneratorOptions { Length = 16, Symbols = false, ExcludeAmbiguous = true });
            Assert.Equal(16, pw.Length);
            Assert.Contains(pw, char.IsAsciiLetterUpper);
            Assert.Contains(pw, char.IsAsciiLetterLower);
            Assert.Contains(pw, char.IsAsciiDigit);
            Assert.DoesNotContain(pw, c => "Il1O0o".Contains(c));
            Assert.All(pw, c => Assert.True(char.IsAsciiLetterOrDigit(c)));
        }
    }

    [Fact]
    public void Strength_RanksPasswords()
    {
        Assert.Equal(0, PasswordStrength.Evaluate("123456").Score);
        Assert.Equal(0, PasswordStrength.Evaluate("password").Score);
        Assert.True(PasswordStrength.Evaluate("Xk7#pQ2!vR9$mW4&").Score >= 4);
        Assert.True(PasswordStrength.Evaluate("correct horse battery staple").Score >= 3);
    }
}
