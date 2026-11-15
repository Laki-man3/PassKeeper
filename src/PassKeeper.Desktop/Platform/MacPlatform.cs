using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using PassKeeper.Core.Storage;

namespace PassKeeper.Desktop.Platform;

/// <summary>
/// macOS: typing with CGEvent and the window in front through the Accessibility API (both need PassKeeper to be
/// allowed in System Settings → Privacy &amp; Security → Accessibility), the hotkey through Carbon, the Keychain.
/// </summary>
public sealed class MacPlatform : IDesktopPlatform
{
    private const string AppServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string ObjC = "/usr/lib/libobjc.dylib";
    private const uint Utf8 = 0x08000100;

    public IKeyring? Keyring { get; } = new MacKeychain();

    public string? AutoTypeProblem() => AXIsProcessTrusted() ? null : Loc.T("AutoType.MacPermission");

    public bool RequestAutoTypePermission()
    {
        if (AXIsProcessTrusted()) return true;
        try
        {
            var appServices = NativeLibrary.Load(AppServices);
            var cf = NativeLibrary.Load(CoreFoundation);
            var promptKey = Marshal.ReadIntPtr(NativeLibrary.GetExport(appServices, "kAXTrustedCheckOptionPrompt"));
            var yes = Marshal.ReadIntPtr(NativeLibrary.GetExport(cf, "kCFBooleanTrue"));
            var options = CFDictionaryCreate(0, [promptKey], [yes], 1,
                NativeLibrary.GetExport(cf, "kCFTypeDictionaryKeyCallBacks"), NativeLibrary.GetExport(cf, "kCFTypeDictionaryValueCallBacks"));
            var trusted = AXIsProcessTrustedWithOptions(options);
            CFRelease(options);
            return trusted;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    public ActiveWindow? ActiveWindow()
    {
        var system = AXUIElementCreateSystemWide();
        if (system == 0) return null;
        try
        {
            var app = CopyAttribute(system, "AXFocusedApplication");
            if (app == 0) return null;
            try
            {
                AXUIElementGetPid(app, out var pid);
                var title = "";
                var window = CopyAttribute(app, "AXFocusedWindow");
                if (window != 0)
                {
                    var value = CopyAttribute(window, "AXTitle");
                    title = CFString(value);
                    if (value != 0) CFRelease(value);
                    CFRelease(window);
                }
                string name;
                try
                {
                    using var process = Process.GetProcessById(pid);
                    name = process.ProcessName;
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    name = "";
                }
                return new ActiveWindow(title, name, pid, pid);
            }
            finally
            {
                CFRelease(app);
            }
        }
        finally
        {
            CFRelease(system);
        }
    }

    public bool Activate(ActiveWindow window)
    {
        var app = AXUIElementCreateApplication(window.ProcessId);
        if (app == 0) return false;
        var attribute = CFSTR("AXFrontmost");
        try
        {
            var yes = Marshal.ReadIntPtr(NativeLibrary.GetExport(NativeLibrary.Load(CoreFoundation), "kCFBooleanTrue"));
            return AXUIElementSetAttributeValue(app, attribute, yes) == 0;
        }
        finally
        {
            CFRelease(attribute);
            CFRelease(app);
        }
    }

    /// <summary>[NSApp hide:nil]: macOS brings the application used before back to the front.</summary>
    public void HideApplication(Window main)
    {
        var nsApp = objc_msgSend(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
        if (nsApp != 0) objc_msgSend(nsApp, sel_registerName("hide:"), 0);
    }

    public void WaitForModifiersReleased()
    {
        const ulong held = 0x20000 | 0x40000 | 0x80000 | 0x100000; // Shift, Control, Option, Command
        var deadline = Environment.TickCount64 + 2000;
        while (Environment.TickCount64 < deadline && (CGEventSourceFlagsState(0) & held) != 0) Thread.Sleep(20);
    }

    public void TypeText(string text, int delayMs)
    {
        var source = CGEventSourceCreate(1);
        try
        {
            for (var i = 0; i < text.Length; i++)
            {
                // A character outside the basic plane goes as its two UTF-16 halves in one event.
                var chars = char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? new[] { text[i], text[++i] } : new[] { text[i] };
                foreach (var down in new[] { true, false })
                {
                    var e = CGEventCreateKeyboardEvent(source, 0, down);
                    CGEventKeyboardSetUnicodeString(e, (nuint)chars.Length, chars);
                    CGEventSetFlags(e, 0);
                    CGEventPost(0, e);
                    CFRelease(e);
                }
                if (delayMs > 0) Thread.Sleep(delayMs);
            }
        }
        finally
        {
            if (source != 0) CFRelease(source);
        }
    }

    public void PressKey(string key, bool shift, int delayMs)
    {
        ushort code = key switch
        {
            "TAB" => 0x30, "ENTER" => 0x24, "SPACE" => 0x31, "BACKSPACE" or "BS" or "BKSP" => 0x33, "DELETE" or "DEL" => 0x75,
            "ESC" => 0x35, "UP" => 0x7E, "DOWN" => 0x7D, "LEFT" => 0x7B, "RIGHT" => 0x7C, "HOME" => 0x73, "END" => 0x77,
            "PGUP" => 0x74, "PGDN" => 0x79, "INSERT" or "INS" => 0x72,
            "F1" => 0x7A, "F2" => 0x78, "F3" => 0x63, "F4" => 0x76, "F5" => 0x60, "F6" => 0x61, "F7" => 0x62, "F8" => 0x64,
            "F9" => 0x65, "F10" => 0x6D, "F11" => 0x67, "F12" => 0x6F,
            _ => 0xFFFF,
        };
        if (code == 0xFFFF) return;
        var source = CGEventSourceCreate(1);
        foreach (var down in new[] { true, false })
        {
            var e = CGEventCreateKeyboardEvent(source, code, down);
            CGEventSetFlags(e, shift ? 0x20000UL : 0);
            CGEventPost(0, e);
            CFRelease(e);
        }
        if (source != 0) CFRelease(source);
        if (delayMs > 0) Thread.Sleep(delayMs);
    }

    // ---------------------------------------------------------------- hotkey (Carbon)

    private static readonly Dictionary<Key, uint> KeyCodes = new()
    {
        [Key.A] = 0x00, [Key.S] = 0x01, [Key.D] = 0x02, [Key.F] = 0x03, [Key.H] = 0x04, [Key.G] = 0x05, [Key.Z] = 0x06,
        [Key.X] = 0x07, [Key.C] = 0x08, [Key.V] = 0x09, [Key.B] = 0x0B, [Key.Q] = 0x0C, [Key.W] = 0x0D, [Key.E] = 0x0E,
        [Key.R] = 0x0F, [Key.Y] = 0x10, [Key.T] = 0x11, [Key.D1] = 0x12, [Key.D2] = 0x13, [Key.D3] = 0x14, [Key.D4] = 0x15,
        [Key.D6] = 0x16, [Key.D5] = 0x17, [Key.D9] = 0x19, [Key.D7] = 0x1A, [Key.D8] = 0x1C, [Key.D0] = 0x1D, [Key.O] = 0x1F,
        [Key.U] = 0x20, [Key.I] = 0x22, [Key.P] = 0x23, [Key.L] = 0x25, [Key.J] = 0x26, [Key.K] = 0x28, [Key.N] = 0x2D,
        [Key.M] = 0x2E, [Key.F1] = 0x7A, [Key.F2] = 0x78, [Key.F3] = 0x63, [Key.F4] = 0x76, [Key.F5] = 0x60, [Key.F6] = 0x61,
        [Key.F7] = 0x62, [Key.F8] = 0x64, [Key.F9] = 0x65, [Key.F10] = 0x6D, [Key.F11] = 0x67, [Key.F12] = 0x6F,
    };

    private static EventHandlerProc? _handler; // kept alive while registered
    private static nint _handlerRef;
    private static Action? _pressed;

    public IDisposable? RegisterHotkey(Hotkey hotkey, Action pressed, out string? error)
    {
        error = null;
        if (!KeyCodes.TryGetValue(hotkey.Key, out var code))
        {
            error = Loc.T("AutoType.HotkeyTaken");
            return null;
        }
        uint mods = 0;
        if (hotkey.Modifiers.HasFlag(KeyModifiers.Meta)) mods |= 0x0100;
        if (hotkey.Modifiers.HasFlag(KeyModifiers.Shift)) mods |= 0x0200;
        if (hotkey.Modifiers.HasFlag(KeyModifiers.Alt)) mods |= 0x0800;
        if (hotkey.Modifiers.HasFlag(KeyModifiers.Control)) mods |= 0x1000;
        try
        {
            _pressed = pressed;
            if (_handlerRef == 0)
            {
                _handler = (_, _, _) =>
                {
                    Dispatcher.UIThread.Post(() => _pressed?.Invoke());
                    return 0;
                };
                var spec = new[] { new EventTypeSpec { Class = 0x6B657962 /* 'keyb' */, Kind = 5 /* kEventHotKeyPressed */ } };
                InstallEventHandler(GetApplicationEventTarget(), _handler, 1, spec, 0, out _handlerRef);
            }
            var id = new EventHotKeyId { Signature = 0x504B4550 /* 'PKEP' */, Id = 1 };
            var status = RegisterEventHotKey(code, mods, id, GetApplicationEventTarget(), 0, out var hotKeyRef);
            if (status != 0)
            {
                error = Loc.T("AutoType.HotkeyTaken");
                return null;
            }
            return new Registration(hotKeyRef);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            error = ex.Message;
            return null;
        }
    }

    private sealed class Registration(nint hotKeyRef) : IDisposable
    {
        public void Dispose() => UnregisterEventHotKey(hotKeyRef);
    }

    // ---------------------------------------------------------------- Core Foundation helpers

    private static nint CFSTR(string text) => CFStringCreateWithCString(0, text, Utf8);

    private static nint CopyAttribute(nint element, string attribute)
    {
        var name = CFSTR(attribute);
        try
        {
            return AXUIElementCopyAttributeValue(element, name, out var value) == 0 ? value : 0;
        }
        finally
        {
            CFRelease(name);
        }
    }

    private static string CFString(nint value)
    {
        if (value == 0 || CFGetTypeID(value) != CFStringGetTypeID()) return "";
        var length = CFStringGetLength(value);
        var buffer = new char[length];
        CFStringGetCharacters(value, new CFRange { Location = 0, Length = length }, buffer);
        return new string(buffer);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CFRange
    {
        public nint Location;
        public nint Length;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTypeSpec
    {
        public uint Class;
        public uint Kind;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventHotKeyId
    {
        public uint Signature;
        public uint Id;
    }

    private delegate int EventHandlerProc(nint callRef, nint eventRef, nint userData);

    [DllImport(AppServices)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool AXIsProcessTrusted();
    [DllImport(AppServices)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool AXIsProcessTrustedWithOptions(nint options);
    [DllImport(AppServices)] private static extern nint AXUIElementCreateSystemWide();
    [DllImport(AppServices)] private static extern nint AXUIElementCreateApplication(int pid);
    [DllImport(AppServices)] private static extern int AXUIElementCopyAttributeValue(nint element, nint attribute, out nint value);
    [DllImport(AppServices)] private static extern int AXUIElementSetAttributeValue(nint element, nint attribute, nint value);
    [DllImport(AppServices)] private static extern int AXUIElementGetPid(nint element, out int pid);
    [DllImport(CoreFoundation)] private static extern void CFRelease(nint value);
    [DllImport(CoreFoundation)] private static extern nint CFStringCreateWithCString(nint allocator, string text, uint encoding);
    [DllImport(CoreFoundation)] private static extern nint CFStringGetLength(nint text);
    // UniChar buffers are UTF-16: char arrays must not be converted to a one-byte encoding on the way.
    [DllImport(CoreFoundation, CharSet = CharSet.Unicode)] private static extern void CFStringGetCharacters(nint text, CFRange range, [Out] char[] buffer);
    [DllImport(CoreFoundation)] private static extern nuint CFGetTypeID(nint value);
    [DllImport(CoreFoundation)] private static extern nuint CFStringGetTypeID();
    [DllImport(CoreFoundation)] private static extern nint CFDictionaryCreate(nint allocator, nint[] keys, nint[] values, nint count, nint keyCallbacks, nint valueCallbacks);
    [DllImport(CoreGraphics)] private static extern nint CGEventSourceCreate(int state);
    [DllImport(CoreGraphics)] private static extern ulong CGEventSourceFlagsState(int state);
    [DllImport(CoreGraphics)] private static extern nint CGEventCreateKeyboardEvent(nint source, ushort key, [MarshalAs(UnmanagedType.I1)] bool down);
    [DllImport(CoreGraphics, CharSet = CharSet.Unicode)] private static extern void CGEventKeyboardSetUnicodeString(nint e, nuint length, char[] text);
    [DllImport(CoreGraphics)] private static extern void CGEventSetFlags(nint e, ulong flags);
    [DllImport(CoreGraphics)] private static extern void CGEventPost(int tap, nint e);
    [DllImport(Carbon)] private static extern nint GetApplicationEventTarget();
    [DllImport(Carbon)] private static extern int InstallEventHandler(nint target, EventHandlerProc handler, nuint count, EventTypeSpec[] types, nint userData, out nint handlerRef);
    [DllImport(Carbon)] private static extern int RegisterEventHotKey(uint keyCode, uint modifiers, EventHotKeyId id, nint target, uint options, out nint hotKeyRef);
    [DllImport(Carbon)] private static extern int UnregisterEventHotKey(nint hotKeyRef);
    [DllImport(ObjC)] private static extern nint objc_getClass(string name);
    [DllImport(ObjC)] private static extern nint sel_registerName(string name);
    [DllImport(ObjC)] private static extern nint objc_msgSend(nint target, nint selector);
    [DllImport(ObjC)] private static extern nint objc_msgSend(nint target, nint selector, nint argument);

    /// <summary>The login Keychain: one generic password per account name, service "PassKeeper".</summary>
    private sealed class MacKeychain : IKeyring
    {
        private const int NotFound = -25300;
        private static readonly byte[] Service = Encoding.UTF8.GetBytes("PassKeeper");

        public byte[]? Get(string account)
        {
            var name = Encoding.UTF8.GetBytes(account);
            var status = SecKeychainFindGenericPassword(0, (uint)Service.Length, Service, (uint)name.Length, name, out var length, out var data, out var item);
            if (status == NotFound) return null;
            if (status != 0) throw new PinSealUnavailableException("Keychain: error " + status);
            try
            {
                var secret = new byte[length];
                Marshal.Copy(data, secret, 0, (int)length);
                return Convert.FromBase64String(Encoding.ASCII.GetString(secret));
            }
            finally
            {
                SecKeychainItemFreeContent(0, data);
                if (item != 0) CFRelease(item);
            }
        }

        public void Set(string account, byte[] secret)
        {
            var name = Encoding.UTF8.GetBytes(account);
            var value = Encoding.ASCII.GetBytes(Convert.ToBase64String(secret));
            var status = SecKeychainFindGenericPassword(0, (uint)Service.Length, Service, (uint)name.Length, name, out _, out var data, out var item);
            if (status == 0)
            {
                SecKeychainItemFreeContent(0, data);
                status = SecKeychainItemModifyAttributesAndData(item, 0, (uint)value.Length, value);
                CFRelease(item);
            }
            else if (status == NotFound) status = SecKeychainAddGenericPassword(0, (uint)Service.Length, Service, (uint)name.Length, name, (uint)value.Length, value, 0);
            if (status != 0) throw new PinSealUnavailableException("Keychain: error " + status);
        }

        public void Delete(string account)
        {
            var name = Encoding.UTF8.GetBytes(account);
            if (SecKeychainFindGenericPassword(0, (uint)Service.Length, Service, (uint)name.Length, name, out _, out var data, out var item) != 0) return;
            SecKeychainItemFreeContent(0, data);
            SecKeychainItemDelete(item);
            CFRelease(item);
        }

        [DllImport(Security)] private static extern int SecKeychainFindGenericPassword(nint keychains, uint serviceLength, byte[] service, uint accountLength, byte[] account, out uint passwordLength, out nint password, out nint item);
        [DllImport(Security)] private static extern int SecKeychainAddGenericPassword(nint keychain, uint serviceLength, byte[] service, uint accountLength, byte[] account, uint passwordLength, byte[] password, nint item);
        [DllImport(Security)] private static extern int SecKeychainItemModifyAttributesAndData(nint item, nint attributes, uint length, byte[] data);
        [DllImport(Security)] private static extern int SecKeychainItemDelete(nint item);
        [DllImport(Security)] private static extern int SecKeychainItemFreeContent(nint attributes, nint data);
    }
}
