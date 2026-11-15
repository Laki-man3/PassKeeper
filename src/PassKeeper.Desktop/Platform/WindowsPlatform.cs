using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using PassKeeper.Core.Storage;

namespace PassKeeper.Desktop.Platform;

/// <summary>
/// Windows (the app is made for macOS and Linux; on Windows it runs for development and testing): SendInput,
/// RegisterHotKey, Credential Manager.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPlatform : IDesktopPlatform
{
    public string? AutoTypeProblem() => null;

    public bool RequestAutoTypePermission() => true;

    public IKeyring? Keyring { get; } = new CredentialManagerKeyring();

    public ActiveWindow? ActiveWindow()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == 0) return null;
        GetWindowThreadProcessId(hwnd, out var pid);
        var title = new StringBuilder(512);
        GetWindowText(hwnd, title, title.Capacity);
        string app;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            app = p.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            app = "";
        }
        return new ActiveWindow(title.ToString(), app, (int)pid, hwnd);
    }

    public bool Activate(ActiveWindow window)
    {
        if (GetForegroundWindow() == window.Handle) return true;
        if (SetForegroundWindow(window.Handle)) return true;
        var front = GetForegroundWindow();
        var frontThread = GetWindowThreadProcessId(front, out _);
        var current = GetCurrentThreadId();
        var attached = frontThread != 0 && frontThread != current && AttachThreadInput(current, frontThread, true);
        try
        {
            return SetForegroundWindow(window.Handle);
        }
        finally
        {
            if (attached) AttachThreadInput(current, frontThread, false);
        }
    }

    public void HideApplication(Window main)
    {
        if (main.IsVisible) main.WindowState = Avalonia.Controls.WindowState.Minimized;
    }

    public void WaitForModifiersReleased()
    {
        var deadline = Environment.TickCount64 + 2000;
        while (Environment.TickCount64 < deadline && new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(vk => (GetAsyncKeyState(vk) & 0x8000) != 0))
            Thread.Sleep(20);
    }

    public void TypeText(string text, int delayMs)
    {
        foreach (var c in text)
        {
            Send(new Input { Type = 1, Key = new KeyInput { Scan = c, Flags = 0x0004 } }, new Input { Type = 1, Key = new KeyInput { Scan = c, Flags = 0x0004 | 0x0002 } });
            if (delayMs > 0) Thread.Sleep(delayMs);
        }
    }

    public void PressKey(string key, bool shift, int delayMs)
    {
        ushort vk = key switch
        {
            "TAB" => 0x09, "ENTER" => 0x0D, "SPACE" => 0x20, "BACKSPACE" or "BS" or "BKSP" => 0x08, "DELETE" or "DEL" => 0x2E,
            "ESC" => 0x1B, "UP" => 0x26, "DOWN" => 0x28, "LEFT" => 0x25, "RIGHT" => 0x27, "HOME" => 0x24, "END" => 0x23,
            "PGUP" => 0x21, "PGDN" => 0x22, "INSERT" or "INS" => 0x2D, "WIN" => 0x5B, "APPS" => 0x5D,
            _ when key.Length >= 2 && key[0] == 'F' && int.TryParse(key[1..], out var f) && f is >= 1 and <= 24 => (ushort)(0x70 + f - 1),
            _ => 0,
        };
        if (vk == 0) return;
        var inputs = new List<Input>();
        if (shift) inputs.Add(new Input { Type = 1, Key = new KeyInput { Vk = 0x10 } });
        var extended = vk is >= 0x21 and <= 0x2E ? 0x0001u : 0u;
        inputs.Add(new Input { Type = 1, Key = new KeyInput { Vk = vk, Flags = extended } });
        inputs.Add(new Input { Type = 1, Key = new KeyInput { Vk = vk, Flags = extended | 0x0002 } });
        if (shift) inputs.Add(new Input { Type = 1, Key = new KeyInput { Vk = 0x10, Flags = 0x0002 } });
        Send([.. inputs]);
        if (delayMs > 0) Thread.Sleep(delayMs);
    }

    public IDisposable? RegisterHotkey(Hotkey hotkey, Action pressed, out string? error)
    {
        uint mods = 0x4000; // MOD_NOREPEAT
        if (hotkey.Modifiers.HasFlag(KeyModifiers.Alt)) mods |= 0x1;
        if (hotkey.Modifiers.HasFlag(KeyModifiers.Control)) mods |= 0x2;
        if (hotkey.Modifiers.HasFlag(KeyModifiers.Shift)) mods |= 0x4;
        if (hotkey.Modifiers.HasFlag(KeyModifiers.Meta)) mods |= 0x8;
        uint vk = hotkey.Key switch
        {
            >= Key.A and <= Key.Z => (uint)(0x41 + (hotkey.Key - Key.A)),
            >= Key.D0 and <= Key.D9 => (uint)(0x30 + (hotkey.Key - Key.D0)),
            >= Key.F1 and <= Key.F12 => (uint)(0x70 + (hotkey.Key - Key.F1)),
            _ => 0,
        };
        var registration = new HotkeyThread(mods, vk, pressed);
        error = registration.Error;
        if (error == null) return registration;
        registration.Dispose();
        return null;
    }

    /// <summary>RegisterHotKey on a thread of its own with its message loop: WM_HOTKEY arrives there.</summary>
    private sealed class HotkeyThread : IDisposable
    {
        private readonly Thread _thread;
        private uint _threadId;

        public string? Error { get; private set; }

        public HotkeyThread(uint mods, uint vk, Action pressed)
        {
            using var ready = new ManualResetEventSlim();
            _thread = new Thread(() =>
            {
                _threadId = GetCurrentThreadId();
                if (!RegisterHotKey(0, 1, mods, vk)) Error = Loc.T("AutoType.HotkeyTaken");
                ready.Set();
                if (Error != null) return;
                while (GetMessage(out var msg, 0, 0, 0) > 0)
                    if (msg.Message == 0x0312) Dispatcher.UIThread.Post(pressed);
                UnregisterHotKey(0, 1);
            }) { IsBackground = true, Name = "PassKeeper hotkey" };
            _thread.Start();
            ready.Wait(3000);
        }

        public void Dispose()
        {
            if (_threadId != 0) PostThreadMessage(_threadId, 0x0012, 0, 0); // WM_QUIT
        }
    }

    private sealed class CredentialManagerKeyring : IKeyring
    {
        private static string Target(string account) => "PassKeeper Desktop/" + account;

        public byte[]? Get(string account)
        {
            if (!CredRead(Target(account), 1, 0, out var ptr))
            {
                if (Marshal.GetLastWin32Error() == 1168) return null; // ERROR_NOT_FOUND
                throw new PinSealUnavailableException("Credential Manager: error " + Marshal.GetLastWin32Error());
            }
            try
            {
                var cred = Marshal.PtrToStructure<Credential>(ptr);
                var secret = new byte[cred.BlobSize];
                Marshal.Copy(cred.Blob, secret, 0, secret.Length);
                return secret;
            }
            finally
            {
                CredFree(ptr);
            }
        }

        public void Set(string account, byte[] secret)
        {
            var blob = Marshal.AllocHGlobal(secret.Length);
            try
            {
                Marshal.Copy(secret, 0, blob, secret.Length);
                var cred = new Credential { Type = 1, TargetName = Target(account), BlobSize = (uint)secret.Length, Blob = blob, Persist = 2, UserName = Environment.UserName };
                if (!CredWrite(ref cred, 0)) throw new PinSealUnavailableException("Credential Manager: error " + Marshal.GetLastWin32Error());
            }
            finally
            {
                Marshal.Copy(new byte[secret.Length], 0, blob, secret.Length);
                Marshal.FreeHGlobal(blob);
            }
        }

        public void Delete(string account) => CredDelete(Target(account), 1, 0);
    }

    private static void Send(params Input[] inputs) => SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyInput
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public KeyInput Key;
        [FieldOffset(8)] private readonly MouseInputPadding _padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputPadding
    {
        public int Dx, Dy;
        public uint Data, Flags, Time;
        public nint Extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint BlobSize;
        public nint Blob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AttachThreadInput(uint attach, uint to, [MarshalAs(UnmanagedType.Bool)] bool doAttach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RegisterHotKey(nint hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")] private static extern int GetMessage(out Msg msg, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostThreadMessage(uint thread, uint msg, nuint wParam, nint lParam);
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredRead(string target, uint type, uint flags, out nint credential);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(nint buffer);
}
