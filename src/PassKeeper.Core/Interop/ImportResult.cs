using PassKeeper.Core.Matching;
using PassKeeper.Core.Models;

namespace PassKeeper.Core.Interop;

/// <summary>Language-neutral warning; the UI turns <see cref="Code"/> into a localized message.</summary>
public sealed class ImportWarning(string code, params object[] args)
{
    public string Code { get; } = code;
    public object[] Args { get; } = args;
    public override string ToString() => Code + (Args.Length > 0 ? ": " + string.Join(", ", Args) : "");
}

public static class WarningCodes
{
    /// <summary>Args: browser, count. Chrome 127+ App-Bound Encryption (v20) cannot be read by other apps.</summary>
    public const string AppBound = "AppBound";
    /// <summary>Args: source, count.</summary>
    public const string DecryptFailed = "DecryptFailed";
    /// <summary>Args: source. Firefox primary password is set and was not supplied / wrong.</summary>
    public const string PrimaryPassword = "PrimaryPassword";
    /// <summary>Args: source. Yandex Browser profile protected with a master password.</summary>
    public const string YandexMasterPassword = "YandexMasterPassword";
    /// <summary>Args: source, message.</summary>
    public const string Error = "Error";
    /// <summary>Args: count.</summary>
    public const string SkippedRows = "SkippedRows";
    /// <summary>Args: source.</summary>
    public const string Empty = "Empty";
}

public sealed class ImportResult
{
    public string Source { get; set; } = "";
    public List<VaultEntry> Entries { get; } = [];
    public List<ImportWarning> Warnings { get; } = [];

    public void Warn(string code, params object[] args) => Warnings.Add(new ImportWarning(code, args));
}

public static class ImportHelpers
{
    public static string MakeTitle(string? title, string? url, string? username)
    {
        if (!string.IsNullOrWhiteSpace(title)) return title.Trim();
        var host = DomainUtil.GetHost(url);
        if (host != null) return host;
        if (!string.IsNullOrWhiteSpace(url)) return url.Trim();
        if (!string.IsNullOrWhiteSpace(username)) return username.Trim();
        return "Imported";
    }

    public static bool IsEmpty(VaultEntry e) =>
        string.IsNullOrEmpty(e.Password) && string.IsNullOrEmpty(e.Username) && string.IsNullOrEmpty(e.Url) &&
        string.IsNullOrEmpty(e.Notes) && string.IsNullOrEmpty(e.SecretKey) && string.IsNullOrEmpty(e.Totp) &&
        e.CustomFields.Count == 0;

    /// <summary>Identity used for duplicate detection.</summary>
    public static string LoginKey(VaultEntry e) =>
        (DomainUtil.GetHost(e.Url) ?? e.Title.Trim().ToLowerInvariant()) + "\n" + e.Username.Trim().ToLowerInvariant();

    public static bool LooksSecret(string name)
    {
        var n = name.ToLowerInvariant();
        return n.Contains("pass") || n.Contains("пароль") || n.Contains("pin") || n.Contains("cvv") || n.Contains("cvc") ||
               n.Contains("secret") || n.Contains("key") || n.Contains("token") || n.Contains("ключ") || n.Contains("card");
    }

    public static DateTime? FromUnixMilliseconds(long? ms) =>
        ms is > 0 and < 253402300799000 ? DateTimeOffset.FromUnixTimeMilliseconds(ms.Value).UtcDateTime : null;

    /// <summary>Chrome/WebKit timestamps: microseconds since 1601-01-01.</summary>
    public static DateTime? FromWebKit(long? us)
    {
        if (us is not > 0) return null;
        try
        {
            return new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(us.Value * 10);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
