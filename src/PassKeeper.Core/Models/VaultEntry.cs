using System.Text.Json;

namespace PassKeeper.Core.Models;

public sealed class VaultEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Url { get; set; } = "";
    public List<string> ExtraUrls { get; set; } = [];
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    /// <summary>API key, licence key, token or any other secret key.</summary>
    public string SecretKey { get; set; } = "";
    /// <summary>otpauth:// URI or Base32 TOTP secret.</summary>
    public string Totp { get; set; } = "";
    public string Notes { get; set; } = "";
    /// <summary>Folder path, segments separated by '/'.</summary>
    public string Folder { get; set; } = "";
    public List<string> Tags { get; set; } = [];
    public bool Favorite { get; set; }
    /// <summary>Custom auto-type sequence; empty means the default one.</summary>
    public string AutoTypeSequence { get; set; } = "";
    /// <summary>Window title / process name wildcard patterns for desktop applications.</summary>
    public List<string> WindowPatterns { get; set; } = [];
    /// <summary>Section chosen by the user; null means it is derived from the entry (see <see cref="EntryCategories.Detect"/>).</summary>
    public EntryCategory? Category { get; set; }
    /// <summary>Sign in automatically when a matching application window shows its login form.</summary>
    public bool AutoLogin { get; set; }
    public List<CustomField> CustomFields { get; set; } = [];
    public List<PasswordHistoryItem> PasswordHistory { get; set; } = [];
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedUtc { get; set; }
    /// <summary>Set when the entry is in the trash.</summary>
    public DateTime? DeletedUtc { get; set; }

    public bool IsDeleted => DeletedUtc.HasValue;

    [System.Text.Json.Serialization.JsonIgnore]
    public EntryCategory EffectiveCategory => Category ?? EntryCategories.Detect(this);

    private static readonly string[] PinFieldNames = ["pin", "пин", "pin-код", "пин-код", "pin code", "token pin", "pin токена"];

    /// <summary>
    /// Smart-card / token PIN: a custom field named "PIN" (also "ПИН", "PIN-код", "Token PIN"), otherwise the password.
    /// </summary>
    public string PinCode()
    {
        var pin = StoredPin();
        return pin.Length > 0 ? pin : Password;
    }

    /// <summary>The PIN stored in the custom field only (no fallback to the password).</summary>
    public string StoredPin() => FindPinField()?.Value ?? "";

    /// <summary>Stores the PIN in the "PIN" custom field (protected); an empty value removes the field.</summary>
    public void SetPinCode(string pin)
    {
        var field = FindPinField();
        if (pin.Length == 0)
        {
            if (field != null) CustomFields.Remove(field);
            return;
        }
        if (field == null) CustomFields.Add(new CustomField { Name = "PIN", Value = pin, Protected = true });
        else
        {
            field.Value = pin;
            field.Protected = true;
        }
    }

    /// <summary>The custom field holding the token / smart-card PIN, if any.</summary>
    public CustomField? FindPinField() =>
        CustomFields.FirstOrDefault(f => PinFieldNames.Contains(f.Name.Trim().Replace('\u2011', '-').Replace('\u2010', '-').ToLowerInvariant()));

    /// <summary>Independent copy with a new identity, fresh dates and no password history.</summary>
    public VaultEntry CreateDuplicate(string title)
    {
        var copy = Clone();
        copy.Id = Guid.NewGuid();
        copy.Title = title;
        copy.CreatedUtc = copy.ModifiedUtc = DateTime.UtcNow;
        copy.LastUsedUtc = null;
        copy.DeletedUtc = null;
        copy.PasswordHistory.Clear();
        return copy;
    }

    public IEnumerable<string> AllUrls()
    {
        if (!string.IsNullOrWhiteSpace(Url)) yield return Url;
        foreach (var u in ExtraUrls)
            if (!string.IsNullOrWhiteSpace(u)) yield return u;
    }

    public VaultEntry Clone()
    {
        var json = JsonSerializer.Serialize(this, VaultJson.Options);
        return JsonSerializer.Deserialize<VaultEntry>(json, VaultJson.Options)!;
    }

    /// <summary>Updates the password keeping the previous one in the history.</summary>
    public void SetPassword(string newPassword, int historyLimit = 10)
    {
        if (newPassword == Password) return;
        if (!string.IsNullOrEmpty(Password))
        {
            PasswordHistory.Insert(0, new PasswordHistoryItem { Password = Password, ChangedUtc = DateTime.UtcNow });
            if (PasswordHistory.Count > historyLimit)
                PasswordHistory.RemoveRange(historyLimit, PasswordHistory.Count - historyLimit);
        }
        Password = newPassword;
    }
}

public sealed class CustomField
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public bool Protected { get; set; }
}

public sealed class PasswordHistoryItem
{
    public string Password { get; set; } = "";
    public DateTime ChangedUtc { get; set; }
}

public sealed class VaultData
{
    public int Version { get; set; } = 1;
    public List<VaultEntry> Entries { get; set; } = [];
}
