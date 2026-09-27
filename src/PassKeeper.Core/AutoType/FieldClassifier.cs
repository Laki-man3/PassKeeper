namespace PassKeeper.Core.AutoType;

/// <summary>What a focused input field expects.</summary>
public enum FieldKind { Login, Password, Email, Phone, Otp, Key }

/// <summary>Guesses the purpose of an input field from its accessible name, automation id or help text (RU/EN).</summary>
public static class FieldClassifier
{
    private static readonly (FieldKind Kind, string[] Hints)[] Rules =
    [
        (FieldKind.Otp, ["one-time", "one time", "otp", "2fa", "two-factor", "totp", "authenticator", "verification code", "security code",
            "код подтверждения", "одноразов", "код из", "код 2fa", "код аутентификатора"]),
        (FieldKind.Key, ["api key", "apikey", "api-key", "token", "licen", "serial", "product key", "ключ", "лиценз", "токен"]),
        (FieldKind.Phone, ["phone", "mobile", "телефон", "мобильн"]),
        (FieldKind.Email, ["e-mail", "email", "почт"]),
        (FieldKind.Login, ["login", "user", "account", "identifier", "signin", "sign in", "logon", "mail", "логин", "пользоват",
            "учетн", "учётн", "аккаунт", "имя входа"]),
    ];

    private static readonly string[] Excluded = ["search", "поиск", "find", "найти", "query", "адресн", "address and search", "url"];

    public static FieldKind? Classify(bool isPassword, params string?[] texts)
    {
        if (isPassword) return FieldKind.Password;
        var text = string.Join(" ", texts.Where(t => !string.IsNullOrWhiteSpace(t))).ToLowerInvariant();
        if (text.Length == 0 || Excluded.Any(text.Contains)) return null;
        var matched = Rules.Where(r => r.Hints.Any(text.Contains)).Select(r => r.Kind).ToList();
        if (matched.Count == 0) return null;
        if (matched[0] is FieldKind.Otp or FieldKind.Key) return matched[0];
        // "Email or phone", "Логин или телефон": an identifier field — fill the main login.
        return matched.Count > 1 ? FieldKind.Login : matched[0];
    }
}
