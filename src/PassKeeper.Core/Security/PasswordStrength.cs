namespace PassKeeper.Core.Security;

/// <param name="Score">0 = very weak … 4 = very strong.</param>
/// <param name="Bits">Estimated entropy in bits.</param>
public readonly record struct StrengthResult(int Score, double Bits);

/// <summary>Offline strength estimation: character-pool entropy with penalties for common patterns.</summary>
public static class PasswordStrength
{
    private static readonly string[] Common =
    [
        "password", "passw0rd", "qwerty", "qwertyuiop", "asdfgh", "zxcvbn", "123456", "12345678", "123456789",
        "1234567890", "111111", "000000", "123123", "654321", "abc123", "iloveyou", "admin", "welcome", "letmein",
        "monkey", "dragon", "master", "football", "baseball", "sunshine", "princess", "trustno1", "superman",
        "starwars", "whatever", "login", "access", "shadow", "michael", "secret", "root", "test", "guest",
        "йцукен", "йцукенгшщзхъ", "фывапр", "ячсмит", "пароль", "привет", "любовь", "qazwsx", "1q2w3e", "1q2w3e4r",
        "zaq12wsx", "q1w2e3r4", "password1", "qwerty123", "aa123456", "p@ssw0rd", "changeme", "default",
    ];

    public static StrengthResult Evaluate(string? password)
    {
        if (string.IsNullOrEmpty(password)) return new StrengthResult(0, 0);

        int pool = 0;
        if (password.Any(char.IsAsciiLetterLower)) pool += 26;
        if (password.Any(char.IsAsciiLetterUpper)) pool += 26;
        if (password.Any(char.IsAsciiDigit)) pool += 10;
        if (password.Any(c => c is >= '!' and <= '/' or >= ':' and <= '@' or >= '[' and <= '`' or >= '{' and <= '~' or ' '))
            pool += 33;
        if (password.Any(c => c > 127)) pool += 66; // Cyrillic and other scripts

        // Effective length: collapse repeats and simple sequences.
        double effective = 0;
        for (var i = 0; i < password.Length; i++)
        {
            var c = password[i];
            if (i > 0 && c == password[i - 1]) { effective += 0.2; continue; }
            if (i > 0 && Math.Abs(c - password[i - 1]) == 1) { effective += 0.4; continue; }
            effective += 1;
        }

        var bits = effective * Math.Log2(Math.Max(pool, 1));

        var lower = password.ToLowerInvariant();
        foreach (var common in Common)
        {
            if (lower == common) { bits = Math.Min(bits, 8); break; }
            if (common.Length >= 5 && lower.Contains(common)) bits -= common.Length * 2.5;
        }
        if (password.All(char.IsAsciiDigit)) bits = Math.Min(bits, password.Length * 3.32);
        bits = Math.Max(bits, 0);

        var score = bits switch
        {
            < 28 => 0,
            < 40 => 1,
            < 60 => 2,
            < 80 => 3,
            _ => 4,
        };
        return new StrengthResult(score, Math.Round(bits, 1));
    }
}
