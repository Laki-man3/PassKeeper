using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using PassKeeper.Core.Storage;

namespace PassKeeper.Desktop.Platform;

/// <summary>
/// Linux with X11 (Xorg, or XWayland windows under Wayland): typing with XTest, the window in front through EWMH,
/// the hotkey with XGrabKey, the secret store through libsecret (GNOME Keyring, KWallet).
/// </summary>
public sealed class LinuxPlatform : IDesktopPlatform
{
    private const string X11 = "libX11.so.6";
    private const string Xtst = "libXtst.so.6";

    public IKeyring? Keyring { get; } = new SecretService();

    private static bool Wayland =>
        string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase);

    public string? AutoTypeProblem()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))) return Loc.T("AutoType.NoX11");
        try
        {
            var display = XOpenDisplay(null);
            if (display == 0) return Loc.T("AutoType.NoX11");
            var ok = XTestQueryExtension(display, out _, out _, out _, out _);
            XCloseDisplay(display);
            if (!ok) return Loc.T("AutoType.NoXTest");
        }
        catch (DllNotFoundException)
        {
            return Loc.T("AutoType.NoXTest");
        }
        return null;
    }

    public bool RequestAutoTypePermission() => AutoTypeProblem() == null;

    /// <summary>Under Wayland only windows of X11 programs can be typed into: said in the settings, not an error.</summary>
    public static string? Limitation => Wayland ? Loc.T("AutoType.Wayland") : null;

    public ActiveWindow? ActiveWindow()
    {
        var display = XOpenDisplay(null);
        if (display == 0) return null;
        try
        {
            var root = XDefaultRootWindow(display);
            var window = (nint)ReadCardinal(display, root, "_NET_ACTIVE_WINDOW", 33 /* XA_WINDOW */);
            if (window == 0) return null;
            var title = ReadText(display, window, "_NET_WM_NAME") ?? ReadText(display, window, "WM_NAME") ?? "";
            var pid = (int)ReadCardinal(display, window, "_NET_WM_PID", 6 /* XA_CARDINAL */);
            var app = "";
            if (pid > 0)
            {
                try { app = File.ReadAllText($"/proc/{pid}/comm").Trim(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            return new ActiveWindow(title, app, pid, window);
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    /// <summary>Asks the window manager to activate the window (a _NET_ACTIVE_WINDOW request, as task bars send it).</summary>
    public bool Activate(ActiveWindow window)
    {
        var display = XOpenDisplay(null);
        if (display == 0) return false;
        try
        {
            var root = XDefaultRootWindow(display);
            var e = new byte[192];
            BitConverter.TryWriteBytes(e.AsSpan(0), 33);                        // type: ClientMessage
            BitConverter.TryWriteBytes(e.AsSpan(16), 1);                        // send_event
            BitConverter.TryWriteBytes(e.AsSpan(24), (long)display);            // display
            BitConverter.TryWriteBytes(e.AsSpan(32), (long)window.Handle);      // window
            BitConverter.TryWriteBytes(e.AsSpan(40), (long)XInternAtom(display, "_NET_ACTIVE_WINDOW", false));
            BitConverter.TryWriteBytes(e.AsSpan(48), 32);                       // format
            BitConverter.TryWriteBytes(e.AsSpan(56), 2L);                       // source: a pager / task bar
            XSendEvent(display, root, false, 0x80000 | 0x100000, e);            // SubstructureNotify | SubstructureRedirect
            XFlush(display);
            return true;
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    public void HideApplication(Window main)
    {
        if (main.IsVisible) main.WindowState = Avalonia.Controls.WindowState.Minimized;
    }

    public void WaitForModifiersReleased()
    {
        var display = XOpenDisplay(null);
        if (display == 0) return;
        try
        {
            var codes = new[] { 0xffe1, 0xffe2, 0xffe3, 0xffe4, 0xffe9, 0xffea, 0xffeb, 0xffec, 0xfe03 }
                .Select(s => XKeysymToKeycode(display, (nuint)s)).Where(c => c != 0).ToArray();
            var keys = new byte[32];
            var deadline = Environment.TickCount64 + 2000;
            while (Environment.TickCount64 < deadline)
            {
                XQueryKeymap(display, keys);
                if (!codes.Any(c => (keys[c / 8] & (1 << (c % 8))) != 0)) return;
                Thread.Sleep(20);
            }
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    /// <summary>
    /// Each character goes through a spare key code mapped to its keysym for the moment (as xdotool does), so any
    /// character is typed whatever the keyboard layout.
    /// </summary>
    public void TypeText(string text, int delayMs)
    {
        var display = XOpenDisplay(null);
        if (display == 0) return;
        try
        {
            var spare = SpareKeycode(display);
            if (spare == 0) return;
            foreach (var rune in text.EnumerateRunes())
            {
                var keysym = rune.Value is >= 0x20 and <= 0x7E or >= 0xA0 and <= 0xFF ? (nuint)rune.Value : (nuint)(0x01000000 + rune.Value);
                var map = new[] { keysym, keysym, keysym, keysym };
                XChangeKeyboardMapping(display, spare, 4, map, 1);
                XSync(display, false);
                XTestFakeKeyEvent(display, (uint)spare, true, 0);
                XTestFakeKeyEvent(display, (uint)spare, false, 0);
                XSync(display, false);
                Thread.Sleep(Math.Max(delayMs, 8));
            }
            XChangeKeyboardMapping(display, spare, 1, [0], 1);
            XSync(display, false);
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    public void PressKey(string key, bool shift, int delayMs)
    {
        nuint keysym = key switch
        {
            "TAB" => 0xff09, "ENTER" => 0xff0d, "SPACE" => 0x20, "BACKSPACE" or "BS" or "BKSP" => 0xff08, "DELETE" or "DEL" => 0xffff,
            "ESC" => 0xff1b, "UP" => 0xff52, "DOWN" => 0xff54, "LEFT" => 0xff51, "RIGHT" => 0xff53, "HOME" => 0xff50,
            "END" => 0xff57, "PGUP" => 0xff55, "PGDN" => 0xff56, "INSERT" or "INS" => 0xff63, "APPS" => 0xff67, "WIN" => 0xffeb,
            _ when key.Length >= 2 && key[0] == 'F' && int.TryParse(key[1..], out var f) && f is >= 1 and <= 24 => (nuint)(0xffbe + f - 1),
            _ => 0,
        };
        if (keysym == 0) return;
        var display = XOpenDisplay(null);
        if (display == 0) return;
        try
        {
            var code = XKeysymToKeycode(display, keysym);
            if (code == 0) return;
            var shiftCode = XKeysymToKeycode(display, 0xffe1);
            if (shift) XTestFakeKeyEvent(display, shiftCode, true, 0);
            XTestFakeKeyEvent(display, code, true, 0);
            XTestFakeKeyEvent(display, code, false, 0);
            if (shift) XTestFakeKeyEvent(display, shiftCode, false, 0);
            XSync(display, false);
        }
        finally
        {
            XCloseDisplay(display);
        }
        if (delayMs > 0) Thread.Sleep(delayMs);
    }

    public IDisposable? RegisterHotkey(Hotkey hotkey, Action pressed, out string? error)
    {
        error = AutoTypeProblem();
        if (error != null) return null;
        nuint keysym = hotkey.Key switch
        {
            >= Key.A and <= Key.Z => (nuint)('a' + (hotkey.Key - Key.A)),
            >= Key.D0 and <= Key.D9 => (nuint)('0' + (hotkey.Key - Key.D0)),
            >= Key.F1 and <= Key.F12 => (nuint)(0xffbe + (hotkey.Key - Key.F1)),
            _ => 0,
        };
        uint mods = 0;
        if (hotkey.Modifiers.HasFlag(KeyModifiers.Shift)) mods |= 0x01;
        if (hotkey.Modifiers.HasFlag(KeyModifiers.Control)) mods |= 0x04;
        if (hotkey.Modifiers.HasFlag(KeyModifiers.Alt)) mods |= 0x08;
        if (hotkey.Modifiers.HasFlag(KeyModifiers.Meta)) mods |= 0x40;
        var grab = new HotkeyGrab(keysym, mods, pressed);
        error = grab.Error;
        if (error == null) return grab;
        grab.Dispose();
        return null;
    }

    /// <summary>
    /// XGrabKey on the root window from a connection of its own, read on a thread of its own. The key is grabbed with
    /// and without Caps Lock and Num Lock, which X counts as modifiers.
    /// </summary>
    private sealed class HotkeyGrab : IDisposable
    {
        private static XErrorHandler? _handler;
        private static nint _previousHandler;
        private static volatile int _lastError;
        private volatile bool _stop;

        public string? Error { get; private set; }

        public HotkeyGrab(nuint keysym, uint mods, Action pressed)
        {
            using var ready = new ManualResetEventSlim();
            new Thread(() =>
            {
                var display = XOpenDisplay(null);
                if (display == 0)
                {
                    Error = Loc.T("AutoType.NoX11");
                    ready.Set();
                    return;
                }
                InstallErrorHandler();
                var root = XDefaultRootWindow(display);
                var code = XKeysymToKeycode(display, keysym);
                uint[] variants = [0, 0x02, 0x10, 0x12]; // none, Lock, Mod2 (Num Lock), both
                _lastError = 0;
                foreach (var v in variants) XGrabKey(display, code, mods | v, root, true, 1, 1);
                XSync(display, false);
                if (code == 0 || _lastError != 0) Error = Loc.T("AutoType.HotkeyTaken");
                ready.Set();
                var e = new byte[192];
                while (!_stop && Error == null)
                {
                    while (XPending(display) > 0)
                    {
                        XNextEvent(display, e);
                        if (BitConverter.ToInt32(e, 0) == 2) Dispatcher.UIThread.Post(pressed); // KeyPress
                    }
                    Thread.Sleep(40);
                }
                foreach (var v in variants) XUngrabKey(display, code, mods | v, root);
                XCloseDisplay(display);
            }) { IsBackground = true, Name = "PassKeeper hotkey" }.Start();
            ready.Wait(3000);
        }

        /// <summary>A refused grab (another program has the key) must not end the program, as Xlib's default handler would.</summary>
        private static void InstallErrorHandler()
        {
            if (_handler != null) return;
            _handler = (display, error) =>
            {
                _lastError = Marshal.ReadByte(error, 32); // XErrorEvent.error_code
                if (_previousHandler != 0)
                    return Marshal.GetDelegateForFunctionPointer<XErrorHandler>(_previousHandler)(display, error);
                return 0;
            };
            _previousHandler = XSetErrorHandler(_handler);
        }

        public void Dispose() => _stop = true;
    }

    // ---------------------------------------------------------------- Xlib helpers

    private static uint SpareKeycode(nint display)
    {
        XDisplayKeycodes(display, out var min, out var max);
        var count = max - min + 1;
        var map = XGetKeyboardMapping(display, (byte)min, count, out var perCode);
        if (map == 0) return 0;
        try
        {
            for (var code = max; code >= min; code--)
            {
                var empty = true;
                for (var i = 0; i < perCode && empty; i++)
                    if (Marshal.ReadIntPtr(map, ((code - min) * perCode + i) * IntPtr.Size) != 0) empty = false;
                if (empty) return (uint)code;
            }
            return 0;
        }
        finally
        {
            XFree(map);
        }
    }

    private static ulong ReadCardinal(nint display, nint window, string property, nuint type)
    {
        if (XGetWindowProperty(display, window, XInternAtom(display, property, false), 0, 1, false, type,
                out _, out var format, out var items, out _, out var data) != 0 || data == 0) return 0;
        try
        {
            // Format 32 data comes as C longs: 8 bytes each on 64-bit systems.
            return items > 0 && format == 32 ? (ulong)Marshal.ReadInt64(data) : 0;
        }
        finally
        {
            XFree(data);
        }
    }

    private static string? ReadText(nint display, nint window, string property)
    {
        if (XGetWindowProperty(display, window, XInternAtom(display, property, false), 0, 1024, false, 0 /* AnyPropertyType */,
                out _, out var format, out var items, out _, out var data) != 0 || data == 0) return null;
        try
        {
            if (format != 8 || items == 0) return null;
            var bytes = new byte[(int)items];
            Marshal.Copy(data, bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            XFree(data);
        }
    }

    private delegate int XErrorHandler(nint display, nint error);

    [DllImport(X11)] private static extern nint XOpenDisplay(string? name);
    [DllImport(X11)] private static extern int XCloseDisplay(nint display);
    [DllImport(X11)] private static extern nint XDefaultRootWindow(nint display);
    [DllImport(X11)] private static extern nuint XInternAtom(nint display, string name, [MarshalAs(UnmanagedType.Bool)] bool onlyIfExists);
    [DllImport(X11)] private static extern int XGetWindowProperty(nint display, nint window, nuint property, nint offset, nint length,
        [MarshalAs(UnmanagedType.Bool)] bool delete, nuint type, out nuint actualType, out int actualFormat, out nuint items, out nuint bytesAfter, out nint data);
    [DllImport(X11)] private static extern int XFree(nint data);
    [DllImport(X11)] private static extern int XSendEvent(nint display, nint window, [MarshalAs(UnmanagedType.Bool)] bool propagate, nint mask, byte[] e);
    [DllImport(X11)] private static extern int XFlush(nint display);
    [DllImport(X11)] private static extern int XSync(nint display, [MarshalAs(UnmanagedType.Bool)] bool discard);
    [DllImport(X11)] private static extern byte XKeysymToKeycode(nint display, nuint keysym);
    [DllImport(X11)] private static extern int XQueryKeymap(nint display, byte[] keys);
    [DllImport(X11)] private static extern int XDisplayKeycodes(nint display, out int min, out int max);
    [DllImport(X11)] private static extern nint XGetKeyboardMapping(nint display, byte first, int count, out int keysymsPerKeycode);
    [DllImport(X11)] private static extern int XChangeKeyboardMapping(nint display, uint first, int keysymsPerKeycode, nuint[] keysyms, int count);
    [DllImport(X11)] private static extern int XGrabKey(nint display, int keycode, uint modifiers, nint window, [MarshalAs(UnmanagedType.Bool)] bool ownerEvents, int pointerMode, int keyboardMode);
    [DllImport(X11)] private static extern int XUngrabKey(nint display, int keycode, uint modifiers, nint window);
    [DllImport(X11)] private static extern int XPending(nint display);
    [DllImport(X11)] private static extern int XNextEvent(nint display, byte[] e);
    [DllImport(X11)] private static extern nint XSetErrorHandler(XErrorHandler handler);
    [DllImport(Xtst)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool XTestQueryExtension(nint display, out int eventBase, out int errorBase, out int major, out int minor);
    [DllImport(Xtst)] private static extern int XTestFakeKeyEvent(nint display, uint keycode, [MarshalAs(UnmanagedType.Bool)] bool press, nuint delay);

    /// <summary>libsecret's simple password API: one secret per account name, attribute "account".</summary>
    private sealed class SecretService : IKeyring
    {
        private const string LibSecret = "libsecret-1.so.0";
        private const string GLib = "libglib-2.0.so.0";
        private static nint _schema;

        private static nint Schema()
        {
            if (_schema != 0) return _schema;
            // SecretSchema: name, flags, 32 attributes {name, type}, reserved: 592 bytes on 64-bit systems.
            var schema = Marshal.AllocHGlobal(592);
            for (var i = 0; i < 592; i++) Marshal.WriteByte(schema, i, 0);
            Marshal.WriteIntPtr(schema, 0, Marshal.StringToCoTaskMemUTF8("io.github.laki-man3.PassKeeper"));
            Marshal.WriteIntPtr(schema, 16, Marshal.StringToCoTaskMemUTF8("account"));
            return _schema = schema;
        }

        private static T Call<T>(string account, Func<nint, nint, (T Result, nint Error)> call)
        {
            nint table = 0, key = 0, value = 0;
            try
            {
                var glib = NativeLibrary.Load(GLib);
                table = g_hash_table_new(NativeLibrary.GetExport(glib, "g_str_hash"), NativeLibrary.GetExport(glib, "g_str_equal"));
                key = Marshal.StringToCoTaskMemUTF8("account");
                value = Marshal.StringToCoTaskMemUTF8(account);
                g_hash_table_insert(table, key, value);
                var (result, error) = call(Schema(), table);
                if (error != 0)
                {
                    var message = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(error, 8)) ?? "error";
                    g_error_free(error);
                    throw new PinSealUnavailableException("Secret Service: " + message);
                }
                return result;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                throw new PinSealUnavailableException(Loc.T("Pin.NoSecretService"), ex);
            }
            finally
            {
                if (table != 0) g_hash_table_unref(table);
                if (key != 0) Marshal.FreeCoTaskMem(key);
                if (value != 0) Marshal.FreeCoTaskMem(value);
            }
        }

        public byte[]? Get(string account) => Call(account, (schema, attributes) =>
        {
            var found = secret_password_lookupv_sync(schema, attributes, 0, out var error);
            if (found == 0) return ((byte[]?)null, error);
            var text = Marshal.PtrToStringUTF8(found) ?? "";
            secret_password_free(found);
            return (Convert.FromBase64String(text), error);
        });

        public void Set(string account, byte[] secret) => Call(account, (schema, attributes) =>
        {
            var ok = secret_password_storev_sync(schema, attributes, null, "PassKeeper PIN", Convert.ToBase64String(secret), 0, out var error);
            if (!ok && error == 0) throw new PinSealUnavailableException("Secret Service: not stored");
            return (ok, error);
        });

        public void Delete(string account) => Call(account, (schema, attributes) => (secret_password_clearv_sync(schema, attributes, 0, out var error), error));

        [DllImport(LibSecret)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool secret_password_storev_sync(nint schema, nint attributes, string? collection, string label, string password, nint cancellable, out nint error);
        [DllImport(LibSecret)] private static extern nint secret_password_lookupv_sync(nint schema, nint attributes, nint cancellable, out nint error);
        [DllImport(LibSecret)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool secret_password_clearv_sync(nint schema, nint attributes, nint cancellable, out nint error);
        [DllImport(LibSecret)] private static extern void secret_password_free(nint password);
        [DllImport(GLib)] private static extern nint g_hash_table_new(nint hash, nint equal);
        [DllImport(GLib)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool g_hash_table_insert(nint table, nint key, nint value);
        [DllImport(GLib)] private static extern void g_hash_table_unref(nint table);
        [DllImport(GLib)] private static extern void g_error_free(nint error);
    }
}
