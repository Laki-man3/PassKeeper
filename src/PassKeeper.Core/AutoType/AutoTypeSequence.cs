using System.Text;
using PassKeeper.Core.Models;
using PassKeeper.Core.Security;

namespace PassKeeper.Core.AutoType;

public abstract record AutoTypeAction;
public sealed record TypeTextAction(string Text) : AutoTypeAction;
public sealed record KeyAction(string Key, int Repeat = 1) : AutoTypeAction;
public sealed record DelayAction(int Milliseconds) : AutoTypeAction;
/// <summary>Sets the delay between keystrokes for the rest of the sequence.</summary>
public sealed record KeyDelayAction(int Milliseconds) : AutoTypeAction;

public sealed class AutoTypeException(string message) : Exception(message);

/// <summary>
/// Compiles KeePass-style sequences: <c>{USERNAME}{TAB}{PASSWORD}{ENTER}</c>.
/// Placeholders: TITLE, USERNAME/LOGIN, PASSWORD, PIN, URL, EMAIL, PHONE, KEY, TOTP, NOTES, S:&lt;field&gt;.
/// Keys: TAB, ENTER, SPACE, BACKSPACE/BS, DELETE/DEL, ESC, UP, DOWN, LEFT, RIGHT, HOME, END, PGUP, PGDN,
/// INSERT, F1–F24, CLEARFIELD. Commands: {DELAY 500}, {DELAY=30} (keystroke delay), {TAB 3} (repeat).
/// Literal braces: {{} and {}}.
/// </summary>
public static class AutoTypeSequence
{
    public const string Default = "{USERNAME}{TAB}{PASSWORD}{ENTER}";
    public const string DefaultNoSubmit = "{USERNAME}{TAB}{PASSWORD}";

    public static readonly HashSet<string> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        "TAB", "ENTER", "SPACE", "BACKSPACE", "BS", "BKSP", "DELETE", "DEL", "ESC", "UP", "DOWN", "LEFT", "RIGHT",
        "HOME", "END", "PGUP", "PGDN", "INSERT", "INS", "CLEARFIELD", "WIN", "APPS",
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
        "F13", "F14", "F15", "F16", "F17", "F18", "F19", "F20", "F21", "F22", "F23", "F24",
    };

    public static string EffectiveSequence(VaultEntry entry, bool submit = true)
    {
        if (!string.IsNullOrWhiteSpace(entry.AutoTypeSequence)) return entry.AutoTypeSequence;
        var login = !string.IsNullOrEmpty(entry.Username) || !string.IsNullOrEmpty(entry.Email);
        if (!login) return submit ? "{PASSWORD}{ENTER}" : "{PASSWORD}";
        return submit ? Default : DefaultNoSubmit;
    }

    public static List<AutoTypeAction> Compile(string sequence, VaultEntry entry, DateTimeOffset? now = null)
    {
        var actions = new List<AutoTypeAction>();
        var text = new StringBuilder();
        var i = 0;
        while (i < sequence.Length)
        {
            var c = sequence[i];
            if (c != '{')
            {
                if (c == '}') throw new AutoTypeException("Unexpected '}' in the auto-type sequence.");
                text.Append(c);
                i++;
                continue;
            }

            // Literal braces.
            if (i + 2 < sequence.Length && sequence[i + 2] == '}' && sequence[i + 1] is '{' or '}')
            {
                text.Append(sequence[i + 1]);
                i += 3;
                continue;
            }

            var end = sequence.IndexOf('}', i + 1);
            if (end < 0) throw new AutoTypeException("Missing '}' in the auto-type sequence.");
            var token = sequence[(i + 1)..end].Trim();
            i = end + 1;

            var placeholder = ResolvePlaceholder(token, entry, now ?? DateTimeOffset.UtcNow);
            if (placeholder != null)
            {
                text.Append(placeholder);
                continue;
            }

            Flush();
            if (token.StartsWith("DELAY=", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(token[6..].Trim(), out var keyDelay))
            {
                actions.Add(new KeyDelayAction(Math.Clamp(keyDelay, 0, 1000)));
                continue;
            }

            var parts = token.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var name = parts.Length > 0 ? parts[0] : "";
            var arg = parts.Length > 1 ? parts[1] : null;

            if (name.Equals("DELAY", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(arg, out var ms)) throw new AutoTypeException("{DELAY} requires a number.");
                actions.Add(new DelayAction(Math.Clamp(ms, 0, 60_000)));
                continue;
            }

            if (Keys.Contains(name))
            {
                var repeat = arg != null && int.TryParse(arg, out var r) ? Math.Clamp(r, 1, 100) : 1;
                actions.Add(new KeyAction(NormalizeKey(name), repeat));
                continue;
            }

            throw new AutoTypeException($"Unknown placeholder {{{token}}}.");
        }
        Flush();
        return actions;

        void Flush()
        {
            if (text.Length == 0) return;
            actions.Add(new TypeTextAction(text.ToString()));
            text.Clear();
        }
    }

    public static string? ResolvePlaceholder(string token, VaultEntry e, DateTimeOffset now)
    {
        var upper = token.ToUpperInvariant();
        switch (upper)
        {
            case "TITLE": return e.Title;
            case "USERNAME" or "USER" or "LOGIN":
                return string.IsNullOrEmpty(e.Username) ? e.Email : e.Username;
            case "PASSWORD": return e.Password;
            case "PIN": return e.PinCode();
            case "URL": return e.Url;
            case "EMAIL": return e.Email;
            case "PHONE": return e.Phone;
            case "KEY": return e.SecretKey;
            case "NOTES": return e.Notes;
            case "TOTP" or "OTP": return Totp.Compute(e.Totp, now) ?? "";
        }
        if (upper.StartsWith("S:", StringComparison.Ordinal))
        {
            var fieldName = token[2..];
            return e.CustomFields.FirstOrDefault(f => f.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase))?.Value ?? "";
        }
        return null;
    }

    private static string NormalizeKey(string name) => name.ToUpperInvariant() switch
    {
        "BS" or "BKSP" => "BACKSPACE",
        "DEL" => "DELETE",
        "INS" => "INSERT",
        var k => k,
    };

    /// <summary>Validates a sequence without an entry (for the editor).</summary>
    public static string? Validate(string sequence)
    {
        try
        {
            Compile(sequence, new VaultEntry());
            return null;
        }
        catch (AutoTypeException ex)
        {
            return ex.Message;
        }
    }
}
