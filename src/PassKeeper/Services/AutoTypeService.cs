using System.Windows.Automation;
using System.Windows.Interop;
using PassKeeper.Core.AutoType;
using PassKeeper.Core.Matching;
using PassKeeper.Core.Models;
using PassKeeper.Localization;
using PassKeeper.Windows;

namespace PassKeeper.Services;

/// <summary>Global auto-type (hotkey) and in-place autofill of login fields in any application.</summary>
public sealed class AutoTypeService
{
    private readonly App _app;
    private bool _busy;

    public AutoTypeService(App app) => _app = app;

    private KeyboardSender CreateSender(IntPtr target) => new()
    {
        KeyDelayMs = _app.Settings.KeystrokeDelayMs,
        UseVirtualKeys = _app.Settings.CompatibleTyping,
        TargetWindow = target,
    };

    private bool IsOwnWindow(IntPtr hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        return pid == Environment.ProcessId;
    }

    /// <summary>Hotkey: find the entry for the active window/site and type it.</summary>
    public async void OnHotkey()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var hwnd = Native.GetForegroundWindow();
            if (hwnd == IntPtr.Zero || IsOwnWindow(hwnd) || !_app.Vault.Exists)
            {
                _app.ShowMainWindow();
                return;
            }
            var target = await Task.Run(() => TargetDetector.Capture(hwnd, detectUrl: true));

            if (!_app.Vault.IsUnlocked && !await QuickUnlockWindow.ShowAsync(target.Describe())) return;

            var matches = EntryMatcher.Match(_app.Vault.ActiveEntries, target.ToContext());
            VaultEntry? entry;
            if (matches.Count == 1 || (matches.Count > 1 && matches[0].Score > matches[1].Score && matches[0].Score >= 95))
                entry = matches[0].Entry;
            else
                entry = await AutoTypePickerWindow.PickAsync(target, matches, _app.Vault.ActiveEntries);

            if (entry == null)
            {
                Native.ForceForeground(hwnd);
                return;
            }
            await TypeAsync(entry, target, AutoTypeSequence.EffectiveSequence(entry, _app.Settings.SubmitAfterFill));
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>"Auto-type" button in the details pane: hide PassKeeper and type into the window below it.</summary>
    public async Task TypeIntoPreviousWindowAsync(VaultEntry entry)
    {
        var main = _app.Main;
        main.WindowState = System.Windows.WindowState.Minimized;
        await Task.Delay(450);
        var hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || IsOwnWindow(hwnd))
        {
            _app.Tray?.ShowBalloon("PassKeeper", Loc.T("AutoType.NoTarget"));
            return;
        }
        var target = await Task.Run(() => TargetDetector.Capture(hwnd, detectUrl: false));
        await TypeAsync(entry, target, AutoTypeSequence.EffectiveSequence(entry, _app.Settings.SubmitAfterFill));
    }

    public async Task TypeAsync(VaultEntry entry, TargetWindow target, string sequence)
    {
        List<AutoTypeAction> actions;
        try
        {
            actions = AutoTypeSequence.Compile(sequence, entry);
        }
        catch (AutoTypeException ex)
        {
            _app.Tray?.ShowBalloon(Loc.T("AutoType.ErrorTitle"), ex.Message);
            return;
        }

        Native.ForceForeground(target.Handle);
        await Task.Delay(120);
        var sender = CreateSender(target.Handle);
        try
        {
            await Task.Run(() =>
            {
                KeyboardSender.WaitForModifiersReleased();
                sender.Execute(actions);
            });
            _app.Vault.MarkUsed(entry.Id);
        }
        catch (AutoTypeAbortedException)
        {
            _app.Tray?.ShowBalloon(Loc.T("AutoType.ErrorTitle"), Loc.T("AutoType.Aborted"));
        }
        catch (InvalidOperationException)
        {
            _app.Tray?.ShowBalloon(Loc.T("AutoType.ErrorTitle"), Loc.T("AutoType.Blocked"));
        }
    }

    /// <summary>Whether the entry has something to put into a field of this kind.</summary>
    public static bool CanFill(VaultEntry e, FieldKind kind) => kind switch
    {
        FieldKind.Password => e.Password.Length > 0,
        FieldKind.Otp => Core.Security.Totp.Parse(e.Totp) != null,
        FieldKind.Key => e.SecretKey.Length > 0,
        _ => e.Username.Length > 0 || e.Email.Length > 0 || e.Phone.Length > 0 || e.Password.Length > 0,
    };

    /// <summary>The value typed into a (non-password) field of the given kind.</summary>
    public static string ValueFor(VaultEntry e, FieldKind kind)
    {
        var login = e.Username.Length > 0 ? e.Username : e.Email.Length > 0 ? e.Email : e.Phone;
        return kind switch
        {
            FieldKind.Email => e.Email.Length > 0 ? e.Email : login,
            FieldKind.Phone => e.Phone.Length > 0 ? e.Phone : login,
            FieldKind.Otp => Core.Security.Totp.Compute(e.Totp, DateTimeOffset.UtcNow) ?? "",
            FieldKind.Key => e.SecretKey,
            FieldKind.Password => e.Password,
            _ => login,
        };
    }

    /// <summary>Suggestion click: fill the focused field (and its login/password counterpart) in place.</summary>
    public async Task FillFieldAsync(VaultEntry entry, LoginField field)
    {
        var login = ValueFor(entry, FieldKind.Login);
        var sender = CreateSender(field.Window);
        var submit = _app.Settings.SubmitAfterFill;
        try
        {
            await Task.Run(() =>
            {
                KeyboardSender.WaitForModifiersReleased(500);
                if (field.Kind is FieldKind.Otp or FieldKind.Key)
                {
                    sender.ClearField();
                    sender.TypeText(ValueFor(entry, field.Kind));
                }
                else if (field.IsPassword)
                {
                    var user = FieldFinder.FindUsernameBefore(field.Element);
                    if (user != null && login.Length > 0 && FieldFinder.GetValue(user) != login)
                    {
                        FieldFinder.Focus(user);
                        sender.ClearField();
                        sender.TypeText(login);
                        FieldFinder.WaitForValue(user, login);
                        FieldFinder.Focus(field.Element);
                    }
                    sender.ClearField();
                    sender.TypeText(entry.Password);
                }
                else
                {
                    var typed = ValueFor(entry, field.Kind);
                    sender.ClearField();
                    sender.TypeText(typed);
                    FieldFinder.WaitForValue(field.Element, typed);
                    var password = FieldFinder.FindPasswordAfter(field.Element);
                    if (password != null && entry.Password.Length > 0)
                    {
                        FieldFinder.Focus(password);
                        sender.ClearField();
                        sender.TypeText(entry.Password);
                    }
                }
                if (submit) sender.PressKey("ENTER");
            });
            _app.Vault.MarkUsed(entry.Id);
        }
        catch (AutoTypeAbortedException)
        {
            _app.Tray?.ShowBalloon(Loc.T("AutoType.ErrorTitle"), Loc.T("AutoType.Aborted"));
        }
        catch (InvalidOperationException)
        {
            _app.Tray?.ShowBalloon(Loc.T("AutoType.ErrorTitle"), Loc.T("AutoType.Blocked"));
        }
    }
}

/// <summary>Locates sibling login/password inputs through UI Automation.</summary>
internal static class FieldFinder
{
    private static readonly Condition EditCondition = new AndCondition(
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
        new PropertyCondition(AutomationElement.IsEnabledProperty, true));

    private static AutomationElement? Container(AutomationElement element)
    {
        var walker = TreeWalker.ControlViewWalker;
        AutomationElement? current = element, lastWindow = null;
        for (var depth = 0; current != null && depth < 60; depth++)
        {
            var type = current.Current.ControlType;
            if (type == ControlType.Document) return current;
            if (type == ControlType.Window) lastWindow = current;
            current = walker.GetParent(current);
        }
        return lastWindow;
    }

    private static List<AutomationElement> Edits(AutomationElement element, out int index)
    {
        index = -1;
        var container = Container(element);
        if (container == null) return [];
        var list = container.FindAll(TreeScope.Descendants, EditCondition).Cast<AutomationElement>()
            .Where(e =>
            {
                try { return !e.Current.IsOffscreen; }
                catch (ElementNotAvailableException) { return false; }
            })
            .ToList();
        for (var i = 0; i < list.Count; i++)
            if (Automation.Compare(list[i], element)) { index = i; break; }
        return list;
    }

    public static AutomationElement? FindUsernameBefore(AutomationElement password)
    {
        try
        {
            var edits = Edits(password, out var index);
            for (var i = index - 1; i >= 0 && i >= index - 3; i--)
                if (!edits[i].Current.IsPassword) return edits[i];
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        return null;
    }

    public static AutomationElement? FindPasswordAfter(AutomationElement login)
    {
        try
        {
            var edits = Edits(login, out var index);
            if (index < 0) return null;
            for (var i = index + 1; i < edits.Count && i <= index + 3; i++)
                if (edits[i].Current.IsPassword) return edits[i];
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        return null;
    }

    public static string? GetValue(AutomationElement element)
    {
        try
        {
            return element.TryGetCurrentPattern(ValuePattern.Pattern, out var p) ? ((ValuePattern)p).Current.Value : null;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    /// <summary>
    /// SendInput only queues keystrokes: before moving focus to another field wait until the target has consumed
    /// them (the field shows the typed value), otherwise the tail of the text would land in the next field.
    /// </summary>
    public static void WaitForValue(AutomationElement element, string expected, int timeoutMs = 1500)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            var value = GetValue(element);
            if (value == null) break; // value not exposed: fall back to a fixed pause
            if (value == expected) return;
            Thread.Sleep(30);
        }
        Thread.Sleep(Math.Min(400, 60 + expected.Length * 6));
    }

    public static void Focus(AutomationElement element)
    {
        try
        {
            element.SetFocus();
            Thread.Sleep(70);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException) { }
    }
}
