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
/// A sequence made only of form fields ({USERNAME}, {EMAIL}, {PHONE}, {PASSWORD}, {PIN}, {TOTP}, {KEY}) joined by
/// {TAB} or {ENTER}. Such a sequence is filled through the form's fields (as an entry without a sequence is), and
/// says which fields to fill and after which of them to press Enter. Any other sequence is typed as keystrokes.
/// </summary>
public sealed class FieldPlan
{
    private readonly List<(FieldKind Kind, bool EnterAfter)> _steps;

    private FieldPlan(List<(FieldKind, bool)> steps, bool generated)
    {
        _steps = steps;
        Generated = generated;
    }

    /// <summary>Made by PassKeeper from the sign-in form, not written by the user.</summary>
    public bool Generated { get; }

    public IEnumerable<FieldKind> Kinds => _steps.Select(s => s.Kind);

    public bool HasEnter => _steps.Any(s => s.EnterAfter);

    public static FieldPlan? Parse(string? sequence, bool generated = false)
    {
        if (string.IsNullOrWhiteSpace(sequence)) return null;
        var steps = new List<(FieldKind Kind, bool EnterAfter)>();
        foreach (var token in AutoTypeSequence.Tokenize(sequence.Trim()))
        {
            if (!token.StartsWith('{') || !token.EndsWith('}')) return null;
            switch (token[1..^1].Trim().ToUpperInvariant())
            {
                case "USERNAME" or "USER" or "LOGIN": steps.Add((FieldKind.Login, false)); break;
                case "EMAIL": steps.Add((FieldKind.Email, false)); break;
                case "PHONE": steps.Add((FieldKind.Phone, false)); break;
                case "PASSWORD": steps.Add((FieldKind.Password, false)); break;
                case "PIN": steps.Add((FieldKind.Pin, false)); break;
                case "TOTP" or "OTP": steps.Add((FieldKind.Otp, false)); break;
                case "KEY": steps.Add((FieldKind.Key, false)); break;
                case "TAB": break;
                case "ENTER":
                    if (steps.Count == 0) return null;
                    steps[^1] = (steps[^1].Kind, true);
                    break;
                default: return null;
            }
        }
        return steps.Count == 0 ? null : new FieldPlan(steps, generated);
    }

    /// <summary>The plan for filling the entry: its own field sequence or the one made from its form; null otherwise.</summary>
    public static FieldPlan? For(VaultEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.AutoTypeSequence) ? Parse(entry.AutoTypeSequence) : Parse(AutoTypeSequence.Generated(entry), generated: true);

    /// <summary>The field of this kind is filled ({USERNAME}, {EMAIL} and {PHONE} all stand for the login field).</summary>
    public bool Includes(FieldKind kind) => _steps.Any(s => Same(s.Kind, kind));

    /// <summary>Enter is pressed when this field is the last one filled on the page.</summary>
    public bool EnterAfter(FieldKind kind) => _steps.Any(s => Same(s.Kind, kind) && s.EnterAfter);

    private static bool Same(FieldKind a, FieldKind b) => a == b || IsLogin(a) && IsLogin(b);

    private static bool IsLogin(FieldKind k) => k is FieldKind.Login or FieldKind.Email or FieldKind.Phone;
}

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

    /// <summary>
    /// Splits a sequence into its elements as the editor shows them: placeholders and keys ("{USERNAME}", "{TAB}",
    /// "{DELAY 500}") and runs of plain text. Joining the elements gives the sequence back.
    /// </summary>
    public static List<string> Tokenize(string sequence)
    {
        var tokens = new List<string>();
        var text = new StringBuilder();
        var i = 0;
        while (i < sequence.Length)
        {
            var c = sequence[i];
            // Literal braces stay inside the text they belong to.
            if (c == '{' && i + 2 < sequence.Length && sequence[i + 2] == '}' && sequence[i + 1] is '{' or '}')
            {
                text.Append(sequence, i, 3);
                i += 3;
                continue;
            }
            var end = c == '{' ? sequence.IndexOf('}', i + 1) : -1;
            if (end < 0)
            {
                text.Append(c);
                i++;
                continue;
            }
            if (text.Length > 0) tokens.Add(text.ToString());
            text.Clear();
            tokens.Add(sequence[i..(end + 1)]);
            i = end + 1;
        }
        if (text.Length > 0) tokens.Add(text.ToString());
        return tokens;
    }

    /// <summary>
    /// The sequence for a sign-in form with these fields: the fields in their order, separated by {TAB}; a form that
    /// asks for the login first and the password on the next page gets {ENTER} between them. With
    /// <paramref name="submit"/> (automatic sign-in) it ends with {ENTER}.
    /// </summary>
    public static string ForForm(bool login, bool password, bool pin = false, bool otp = false, bool twoSteps = false, bool submit = false)
    {
        var fields = new List<string>();
        if (login) fields.Add("{USERNAME}");
        if (password) fields.Add("{PASSWORD}");
        if (pin) fields.Add("{PIN}");
        if (otp) fields.Add("{TOTP}");
        if (fields.Count == 0) return "";
        var separator = submit && twoSteps && login && password && fields.Count == 2 ? "{ENTER}" : "{TAB}";
        return string.Join(separator, fields) + (submit ? "{ENTER}" : "");
    }

    /// <summary>
    /// The sequence without the element at <paramref name="index"/> (the cross on it in the editor): exactly that
    /// element goes, the rest stays as it is. A {TAB} left without a field around it does nothing in a sequence of
    /// fields.
    /// </summary>
    public static string Remove(string sequence, int index)
    {
        var tokens = Tokenize(sequence);
        if (index < 0 || index >= tokens.Count) return sequence;
        tokens.RemoveAt(index);
        return string.Concat(tokens);
    }

    /// <summary>The same fields without pressing Enter anywhere (automatic sign-in off).</summary>
    public static string WithoutEnter(string sequence)
    {
        var tokens = Tokenize(sequence);
        while (tokens.Count > 0 && IsKey(tokens[^1], "ENTER")) tokens.RemoveAt(tokens.Count - 1);
        return string.Concat(tokens.Select(t => IsKey(t, "ENTER") ? "{TAB}" : t));
    }

    /// <summary>
    /// The sequence PassKeeper made for the entry from its sign-in form (see <see cref="VaultEntry.FormSequence"/>):
    /// with Enter when the entry signs in automatically. Empty when the form was never seen.
    /// </summary>
    public static string Generated(VaultEntry entry) =>
        string.IsNullOrWhiteSpace(entry.FormSequence) ? "" : entry.AutoLogin ? entry.FormSequence : WithoutEnter(entry.FormSequence);

    private static bool IsKey(string token, string key) =>
        token.StartsWith('{') && token.EndsWith('}') && token[1..^1].Trim().Equals(key, StringComparison.OrdinalIgnoreCase);

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
