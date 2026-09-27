using System.Security.Cryptography;
using System.Text;

namespace PassKeeper.Core.Security;

public sealed class TotpConfig
{
    public byte[] Secret { get; init; } = [];
    public int Digits { get; init; } = 6;
    public int Period { get; init; } = 30;
    public string Algorithm { get; init; } = "SHA1";
    public string Issuer { get; init; } = "";
    public string Account { get; init; } = "";
}

/// <summary>RFC 6238 time-based one-time passwords.</summary>
public static class Totp
{
    /// <summary>Accepts an otpauth://totp/… URI or a bare Base32 secret.</summary>
    public static TotpConfig? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        try
        {
            if (value.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(value);
                if (!uri.Host.Equals("totp", StringComparison.OrdinalIgnoreCase)) return null;
                var query = ParseQuery(uri.Query);
                if (!query.TryGetValue("secret", out var secret)) return null;
                var label = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
                var issuer = query.GetValueOrDefault("issuer") ?? "";
                var account = label.Contains(':') ? label[(label.IndexOf(':') + 1)..].Trim() : label;
                if (issuer.Length == 0 && label.Contains(':')) issuer = label[..label.IndexOf(':')];
                return new TotpConfig
                {
                    Secret = Base32.Decode(secret),
                    Digits = int.TryParse(query.GetValueOrDefault("digits"), out var d) && d is >= 6 and <= 10 ? d : 6,
                    Period = int.TryParse(query.GetValueOrDefault("period"), out var p) && p is > 0 and <= 300 ? p : 30,
                    Algorithm = (query.GetValueOrDefault("algorithm") ?? "SHA1").ToUpperInvariant(),
                    Issuer = issuer,
                    Account = account,
                };
            }
            var bytes = Base32.Decode(value);
            return bytes.Length == 0 ? null : new TotpConfig { Secret = bytes };
        }
        catch (FormatException) // includes UriFormatException
        {
            return null;
        }
    }

    public static string Compute(TotpConfig config, DateTimeOffset now)
    {
        var counter = (long)Math.Floor(now.ToUnixTimeSeconds() / (double)config.Period);
        return Hotp(config.Secret, counter, config.Digits, config.Algorithm);
    }

    public static string? Compute(string? value, DateTimeOffset now)
    {
        var cfg = Parse(value);
        return cfg == null ? null : Compute(cfg, now);
    }

    public static int SecondsRemaining(TotpConfig config, DateTimeOffset now) =>
        config.Period - (int)(now.ToUnixTimeSeconds() % config.Period);

    public static string Hotp(byte[] secret, long counter, int digits, string algorithm = "SHA1")
    {
        Span<byte> msg = stackalloc byte[8];
        for (var i = 7; i >= 0; i--)
        {
            msg[i] = (byte)(counter & 0xFF);
            counter >>= 8;
        }
        byte[] hash = algorithm switch
        {
            "SHA256" => HMACSHA256.HashData(secret, msg),
            "SHA512" => HMACSHA512.HashData(secret, msg),
            _ => HMACSHA1.HashData(secret, msg),
        };
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        var mod = (long)Math.Pow(10, digits);
        return (binary % mod).ToString(new string('0', digits));
    }

    public static string BuildUri(string base32Secret, string issuer, string account) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}?secret={base32Secret}&issuer={Uri.EscapeDataString(issuer)}";

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            result[Uri.UnescapeDataString(part[..eq])] = Uri.UnescapeDataString(part[(eq + 1)..].Replace('+', ' '));
        }
        return result;
    }
}

public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static byte[] Decode(string input)
    {
        var clean = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            if (ch is ' ' or '-' or '=' or '\t') continue;
            clean.Append(char.ToUpperInvariant(ch));
        }
        var output = new List<byte>(clean.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var ch in clean.ToString())
        {
            var v = Alphabet.IndexOf(ch);
            if (v < 0) throw new FormatException("Invalid Base32 character.");
            buffer = (buffer << 5) | v;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }
        return output.ToArray();
    }

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }
}
