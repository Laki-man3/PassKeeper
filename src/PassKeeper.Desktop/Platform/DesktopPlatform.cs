using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using PassKeeper.Core.AutoType;
using PassKeeper.Core.Storage;

namespace PassKeeper.Desktop.Platform;

/// <summary>The window in front: its title, its program, and a handle to bring it back to the front.</summary>
public sealed record ActiveWindow(string Title, string App, int ProcessId, nint Handle);

/// <summary>What the system offers for auto-type, the global hotkey and the secret store.</summary>
public interface IDesktopPlatform
{
    /// <summary>Null when auto-type can work; otherwise why not (shown in the settings).</summary>
    string? AutoTypeProblem();

    /// <summary>Asks the system for the permission auto-type needs (macOS: Accessibility); true when it is given.</summary>
    bool RequestAutoTypePermission();

    ActiveWindow? ActiveWindow();

    /// <summary>Brings the window back to the front; false when the system refused.</summary>
    bool Activate(ActiveWindow window);

    /// <summary>Gets PassKeeper out of the way, so the window used before it comes back to the front.</summary>
    void HideApplication(Window main);

    void WaitForModifiersReleased();

    void TypeText(string text, int delayMs);

    /// <summary>A named key of a typing sequence (TAB, ENTER, HOME…), with Shift held when asked.</summary>
    void PressKey(string key, bool shift, int delayMs);

    /// <summary>Calls <paramref name="pressed"/> on the interface thread; null and an error text when it cannot.</summary>
    IDisposable? RegisterHotkey(Hotkey hotkey, Action pressed, out string? error);

    /// <summary>The system's secret store; null when there is none.</summary>
    IKeyring? Keyring { get; }
}

/// <summary>A small secret per account name, kept by the system (Keychain, Secret Service, Credential Manager).</summary>
public interface IKeyring
{
    /// <returns>Null when nothing is kept under the name.</returns>
    /// <exception cref="PinSealUnavailableException">The store cannot be reached.</exception>
    byte[]? Get(string account);

    void Set(string account, byte[] secret);

    void Delete(string account);
}

public static class DesktopPlatform
{
    public static IDesktopPlatform Current { get; } =
        OperatingSystem.IsMacOS() ? new MacPlatform() : OperatingSystem.IsWindows() ? new WindowsPlatform() : new LinuxPlatform();

    /// <summary>
    /// Types a compiled sequence. Runs off the interface thread; the caller has checked that the right window is in front.
    /// </summary>
    public static void Run(IDesktopPlatform platform, IReadOnlyList<AutoTypeAction> actions)
    {
        var delay = 12;
        platform.WaitForModifiersReleased();
        foreach (var action in actions)
        {
            switch (action)
            {
                case TypeTextAction text:
                    platform.TypeText(text.Text, delay);
                    break;
                case KeyAction key:
                    for (var i = 0; i < Math.Max(1, key.Repeat); i++)
                    {
                        if (key.Key.Equals("CLEARFIELD", StringComparison.OrdinalIgnoreCase))
                        {
                            platform.PressKey("END", false, delay);
                            platform.PressKey("HOME", true, delay);
                            platform.PressKey("BACKSPACE", false, delay);
                        }
                        else platform.PressKey(key.Key.ToUpperInvariant(), false, delay);
                    }
                    break;
                case DelayAction pause:
                    Thread.Sleep(Math.Clamp(pause.Milliseconds, 0, 10_000));
                    break;
                case KeyDelayAction keyDelay:
                    delay = Math.Clamp(keyDelay.Milliseconds, 0, 1000);
                    break;
            }
        }
    }
}

/// <summary>A key with modifiers, kept in the settings as "Ctrl+Alt+A" (Meta: Cmd on macOS, Super/Win elsewhere).</summary>
public sealed record Hotkey(KeyModifiers Modifiers, Key Key)
{
    public static Hotkey Default { get; } = new(KeyModifiers.Control | KeyModifiers.Alt, Key.A);

    public static Hotkey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return null;
        var modifiers = KeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            modifiers |= part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => KeyModifiers.Control,
                "alt" or "option" => KeyModifiers.Alt,
                "shift" => KeyModifiers.Shift,
                "meta" or "cmd" or "command" or "win" or "super" => KeyModifiers.Meta,
                _ => KeyModifiers.None,
            };
        }
        var last = parts[^1];
        if (last.Length == 1 && char.IsAsciiDigit(last[0])) last = "D" + last;
        return Enum.TryParse<Key>(last, ignoreCase: true, out var key) && Supported(key) ? new Hotkey(modifiers, key) : null;
    }

    public static bool Supported(Key key) => key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9 or >= Key.F1 and <= Key.F12;

    /// <summary>Letters and digits need Ctrl, Alt or Meta, or they would take a key away from typing.</summary>
    public bool IsUsable => Supported(Key) && (Key is >= Key.F1 and <= Key.F12 ||
        (Modifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != 0);

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(KeyModifiers.Meta)) parts.Add("Meta");
        parts.Add(KeyName);
        return string.Join("+", parts);
    }

    /// <summary>As the system writes it: ⌃⌥⇧⌘A on macOS, Ctrl+Alt+A elsewhere.</summary>
    public string Display
    {
        get
        {
            if (OperatingSystem.IsMacOS())
            {
                var mac = new StringBuilder();
                if (Modifiers.HasFlag(KeyModifiers.Control)) mac.Append('⌃');
                if (Modifiers.HasFlag(KeyModifiers.Alt)) mac.Append('⌥');
                if (Modifiers.HasFlag(KeyModifiers.Shift)) mac.Append('⇧');
                if (Modifiers.HasFlag(KeyModifiers.Meta)) mac.Append('⌘');
                return mac + KeyName;
            }
            return ToString().Replace("Meta", OperatingSystem.IsWindows() ? "Win" : "Super", StringComparison.Ordinal);
        }
    }

    public string KeyName => Key is >= Key.D0 and <= Key.D9 ? ((int)(Key - Key.D0)).ToString() : Key.ToString();
}

/// <summary>
/// Seals the PIN file with a random key the system keeps (Keychain, Secret Service): a copy of the file cannot be
/// tried on another computer or by another user, as DPAPI does on Windows.
/// </summary>
public sealed class KeyringPinSealer(IKeyring keyring) : IPinSealer
{
    private static readonly byte[] Magic = "PKS1"u8.ToArray();
    private static readonly byte[] Aad = "PassKeeper.PinSeal.v1"u8.ToArray();

    private static string Account(string path) =>
        "pin-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path))))[..32].ToLowerInvariant();

    public byte[] Seal(string path, byte[] data)
    {
        var account = Account(path);
        var key = keyring.Get(account);
        if (key is not { Length: 32 })
        {
            key = RandomNumberGenerator.GetBytes(32);
            keyring.Set(account, key);
        }
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(12);
            var cipher = new byte[data.Length];
            var tag = new byte[16];
            using (var gcm = new AesGcm(key, 16)) gcm.Encrypt(nonce, data, cipher, tag, Aad);
            return [.. Magic, .. nonce, .. tag, .. cipher];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public byte[] Open(string path, byte[] sealedData)
    {
        if (sealedData.Length < 32 || !sealedData.AsSpan(0, 4).SequenceEqual(Magic)) throw new CryptographicException("Not a sealed PIN file.");
        var key = keyring.Get(Account(path)) ?? throw new CryptographicException("The key of this PIN file is not in the system's secret store.");
        try
        {
            var plain = new byte[sealedData.Length - 32];
            using var gcm = new AesGcm(key, 16);
            gcm.Decrypt(sealedData.AsSpan(4, 12), sealedData.AsSpan(32), sealedData.AsSpan(16, 16), plain, Aad);
            return plain;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public void Forget(string path) => keyring.Delete(Account(path));
}

/// <summary>When the system has no secret store: the PIN cannot be offered.</summary>
public sealed class NoPinSealer(string reason) : IPinSealer
{
    public byte[] Seal(string path, byte[] data) => throw new PinSealUnavailableException(reason);
    public byte[] Open(string path, byte[] sealedData) => throw new PinSealUnavailableException(reason);
    public void Forget(string path) { }
}
