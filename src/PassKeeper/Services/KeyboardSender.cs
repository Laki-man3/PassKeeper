using System.Runtime.InteropServices;
using PassKeeper.Core.AutoType;
using static PassKeeper.Services.Native;

namespace PassKeeper.Services;

public sealed class AutoTypeAbortedException() : Exception("The target window lost focus; auto-type was aborted.");

/// <summary>Synthesizes keystrokes with SendInput (Unicode by default, layout virtual keys in compatibility mode).</summary>
public sealed class KeyboardSender
{
    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    private static readonly Dictionary<string, (ushort Vk, bool Extended)> SpecialKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TAB"] = (0x09, false), ["ENTER"] = (0x0D, false), ["SPACE"] = (0x20, false), ["BACKSPACE"] = (0x08, false),
        ["DELETE"] = (0x2E, true), ["ESC"] = (0x1B, false), ["UP"] = (0x26, true), ["DOWN"] = (0x28, true),
        ["LEFT"] = (0x25, true), ["RIGHT"] = (0x27, true), ["HOME"] = (0x24, true), ["END"] = (0x23, true),
        ["PGUP"] = (0x21, true), ["PGDN"] = (0x22, true), ["INSERT"] = (0x2D, true), ["WIN"] = (0x5B, true),
        ["APPS"] = (0x5D, true),
    };

    public int KeyDelayMs { get; set; } = 8;
    public bool UseVirtualKeys { get; set; }
    /// <summary>When set, typing stops as soon as another window becomes active.</summary>
    public IntPtr TargetWindow { get; set; }

    public void Execute(IEnumerable<AutoTypeAction> actions, CancellationToken ct = default)
    {
        foreach (var action in actions)
        {
            ct.ThrowIfCancellationRequested();
            switch (action)
            {
                case TypeTextAction t: TypeText(t.Text, ct); break;
                case KeyAction k:
                    for (var i = 0; i < k.Repeat; i++) PressKey(k.Key);
                    break;
                case DelayAction d: Thread.Sleep(d.Milliseconds); break;
                case KeyDelayAction kd: KeyDelayMs = kd.Milliseconds; break;
            }
        }
    }

    public void TypeText(string text, CancellationToken ct = default)
    {
        var layout = GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow(), out _));
        foreach (var c in text)
        {
            ct.ThrowIfCancellationRequested();
            CheckTarget();
            switch (c)
            {
                case '\r': continue;
                case '\n': Tap(0x0D, false); break;
                case '\t': Tap(0x09, false); break;
                default:
                    if (!UseVirtualKeys || !TypeWithLayout(c, layout)) SendUnicode(c);
                    break;
            }
            Pause();
        }
    }

    public void PressKey(string name)
    {
        CheckTarget();
        if (name.Equals("CLEARFIELD", StringComparison.OrdinalIgnoreCase))
        {
            ClearField();
            return;
        }
        if (name.Length >= 2 && name[0] is 'F' or 'f' && int.TryParse(name[1..], out var f) && f is >= 1 and <= 24)
        {
            Tap((ushort)(0x70 + f - 1), false);
        }
        else if (SpecialKeys.TryGetValue(name, out var key))
        {
            Tap(key.Vk, key.Extended);
        }
        Pause();
    }

    /// <summary>
    /// Select everything in the focused field and delete it. Classic Win32 edit controls of older dialogs
    /// (VPN clients) ignore Ctrl+A, so the line is also selected with End, Shift+Home.
    /// </summary>
    public void ClearField()
    {
        CheckTarget();
        Send(Key(0x11, false, false), Key(0x41, false, false), Key(0x41, false, true), Key(0x11, false, true));
        Pause();
        Tap(0x08, false);
        Pause();
        Tap(0x23, true);
        Send(Key(0x10, false, false), Key(0x24, true, false), Key(0x24, true, true), Key(0x10, false, true));
        Pause();
        Tap(0x08, false);
        Pause();
    }

    /// <summary>The user may still hold the hotkey modifiers: wait for them to be released (or release them).</summary>
    public static void WaitForModifiersReleased(int timeoutMs = 2000)
    {
        int[] modifiers = [0x10, 0x11, 0x12, 0x5B, 0x5C];
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (modifiers.All(vk => (GetAsyncKeyState(vk) & 0x8000) == 0)) return;
            Thread.Sleep(25);
        }
        foreach (var vk in modifiers)
            if ((GetAsyncKeyState(vk) & 0x8000) != 0)
                Send(Key((ushort)vk, vk is 0x5B or 0x5C, true));
    }

    private void CheckTarget()
    {
        if (TargetWindow != IntPtr.Zero && GetForegroundWindow() != TargetWindow) throw new AutoTypeAbortedException();
    }

    private void Pause()
    {
        if (KeyDelayMs > 0) Thread.Sleep(KeyDelayMs);
    }

    private static bool TypeWithLayout(char c, IntPtr layout)
    {
        var scan = VkKeyScanEx(c, layout);
        if (scan == -1) return false;
        var vk = (ushort)(scan & 0xFF);
        var shiftState = (scan >> 8) & 0xFF;
        if ((shiftState & 0x06) != 0) return false; // needs Ctrl/Alt (AltGr) – use Unicode instead
        var shift = (shiftState & 0x01) != 0;
        var list = new List<INPUT>();
        if (shift) list.Add(Key(0x10, false, false));
        list.Add(Key(vk, false, false));
        list.Add(Key(vk, false, true));
        if (shift) list.Add(Key(0x10, false, true));
        Send([.. list]);
        return true;
    }

    private static void SendUnicode(char c) =>
        Send(
            new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE } } },
            new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP } } });

    private static void Tap(ushort vk, bool extended) => Send(Key(vk, extended, false), Key(vk, extended, true));

    private static INPUT Key(ushort vk, bool extended, bool up)
    {
        uint flags = 0;
        if (extended) flags |= KEYEVENTF_EXTENDEDKEY;
        if (up) flags |= KEYEVENTF_KEYUP;
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = (ushort)MapVirtualKey(vk, 0), dwFlags = flags } },
        };
    }

    private static void Send(params INPUT[] inputs)
    {
        if (SendInput((uint)inputs.Length, inputs, InputSize) != inputs.Length)
            throw new InvalidOperationException("SendInput was blocked (the target may run with higher privileges).");
    }
}
