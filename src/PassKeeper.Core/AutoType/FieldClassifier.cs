using System.Text.RegularExpressions;

namespace PassKeeper.Core.AutoType;

/// <summary>What a focused input field expects.</summary>
public enum FieldKind { Login, Password, Email, Phone, Otp, Key, Pin }

/// <summary>Guesses the purpose of an input field from its accessible name, automation id or help text (RU/EN).</summary>
public static class FieldClassifier
{
    private static readonly string[] OtpHints =
    [
        "one-time", "one time", "otp", "2fa", "two-factor", "totp", "authenticator", "verification code", "security code",
        "passcode", "second password", "token code", "tokencode",
        "код подтверждения", "одноразов", "код из", "код 2fa", "код аутентификатора", "второй пароль",
    ];

    private static readonly (FieldKind Kind, string[] Hints)[] Rules =
    [
        (FieldKind.Otp, OtpHints),
        (FieldKind.Key, ["api key", "apikey", "api-key", "token", "licen", "serial", "product key", "ключ", "лиценз", "токен"]),
        (FieldKind.Phone, ["phone", "mobile", "телефон", "мобильн"]),
        (FieldKind.Email, ["e-mail", "email", "почт"]),
        (FieldKind.Login, ["login", "user", "account", "identifier", "signin", "sign in", "logon", "mail", "логин", "пользоват",
            "учетн", "учётн", "аккаунт", "имя входа"]),
    ];

    private static readonly string[] Excluded = ["search", "поиск", "find", "найти", "query", "адресн", "address and search", "url"];

    /// <summary>"PIN", "PIN-код", "ПИН", "Token PIN" as a separate word (not "spinner", "pinned").</summary>
    private static readonly Regex PinWord = new(@"(^|[^\p{L}])(pin|пин)([^\p{L}]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static FieldKind? Classify(bool isPassword, params string?[] texts)
    {
        var text = string.Join(" ", texts.Where(t => !string.IsNullOrWhiteSpace(t))).ToLowerInvariant();
        if (isPassword)
        {
            // Masked fields of VPN clients and token prompts: a smart-card/token PIN or a second factor.
            if (PinWord.IsMatch(text)) return FieldKind.Pin;
            if (OtpHints.Any(text.Contains)) return FieldKind.Otp;
            return FieldKind.Password;
        }
        if (text.Length == 0 || Excluded.Any(text.Contains)) return null;
        if (PinWord.IsMatch(text)) return FieldKind.Pin;
        var matched = Rules.Where(r => r.Hints.Any(text.Contains)).Select(r => r.Kind).ToList();
        if (matched.Count == 0) return null;
        if (matched[0] is FieldKind.Otp or FieldKind.Key) return matched[0];
        // "Email or phone", "Логин или телефон": an identifier field — fill the main login.
        return matched.Count > 1 ? FieldKind.Login : matched[0];
    }
}
