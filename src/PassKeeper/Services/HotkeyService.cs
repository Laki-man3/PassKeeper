using System.Windows.Input;
using System.Windows.Interop;

namespace PassKeeper.Services;

/// <summary>System-wide hotkey (RegisterHotKey) on a message-only window.</summary>
public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 0x5041;
    private readonly HwndSource _source;
    private bool _registered;

    public event Action? Pressed;

    public HotkeyService()
    {
        var p = new HwndSourceParameters("PassKeeperHotkeys") { ParentWindow = new IntPtr(-3), WindowStyle = 0 }; // HWND_MESSAGE
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    public string? Current { get; private set; }

    /// <returns>false when the combination is taken by another application.</returns>
    public bool Register(string? gesture)
    {
        Unregister();
        Current = gesture;
        if (!TryParse(gesture, out var modifiers, out var key)) return false;
        uint mods = Native.MOD_NOREPEAT;
        if (modifiers.HasFlag(ModifierKeys.Alt)) mods |= Native.MOD_ALT;
        if (modifiers.HasFlag(ModifierKeys.Control)) mods |= Native.MOD_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Shift)) mods |= Native.MOD_SHIFT;
        if (modifiers.HasFlag(ModifierKeys.Windows)) mods |= Native.MOD_WIN;
        _registered = Native.RegisterHotKey(_source.Handle, HotkeyId, mods, (uint)KeyInterop.VirtualKeyFromKey(key));
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered) return;
        Native.UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
    }

    public static bool TryParse(string? gesture, out ModifierKeys modifiers, out Key key)
    {
        modifiers = ModifierKeys.None;
        key = Key.None;
        if (string.IsNullOrWhiteSpace(gesture)) return false;
        foreach (var raw in gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= ModifierKeys.Control; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                case "shift": modifiers |= ModifierKeys.Shift; break;
                case "win" or "windows": modifiers |= ModifierKeys.Windows; break;
                default:
                    if (!Enum.TryParse(raw, true, out key))
                    {
                        if (raw.Length == 1 && char.IsDigit(raw[0])) key = Key.D0 + (raw[0] - '0');
                        else return false;
                    }
                    break;
            }
        }
        return key != Key.None && modifiers != ModifierKeys.None;
    }

    public static string Format(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key is >= Key.D0 and <= Key.D9 ? ((char)('0' + (key - Key.D0))).ToString() : key.ToString());
        return string.Join("+", parts);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
