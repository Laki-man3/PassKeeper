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
    private int _typing;

    public AutoTypeService(App app) => _app = app;

    /// <summary>Keystrokes are being sent right now (a second fill would interleave with them).</summary>
    public bool IsTyping => Volatile.Read(ref _typing) > 0;

    /// <summary>The hotkey is being handled (the user may be choosing an entry): automatic sign-in stands aside.</summary>
    public bool IsBusy => _busy || IsTyping;

    /// <summary>A window was filled (by the hotkey, a suggestion or after saving a new entry).</summary>
    public event Action<IntPtr>? WindowFilled;

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
            if (hwnd == IntPtr.Zero || IsOwnWindow(hwnd) || !_app.HasProfile)
            {
                _app.ShowMainWindow();
                return;
            }
            var target = await Task.Run(() => TargetDetector.Capture(hwnd, detectUrl: true));
            var field = await Task.Run(() => FocusedField(hwnd));

            if (!_app.Vault.IsUnlocked && !await QuickUnlockWindow.ShowAsync(target.Describe())) return;

            var matches = EntryMatcher.Match(_app.Vault.ActiveEntries, target.ToContext());
            if (matches.Count == 0 && !target.IsBrowser)
            {
                // A program nobody has an entry for: if it is a known client or shows a login form, create the entry
                // for exactly this window instead of asking to pick one.
                var form = await Task.Run(() => ClientDetector.InspectForm(hwnd));
                if (field != null || form.Any || KnownApps.Match(target.ProcessName, target.Title) != null)
                {
                    _app.CreateEntryForWindow(hwnd, form);
                    return;
                }
            }
            VaultEntry? entry;
            if (matches.Count == 1 || (matches.Count > 1 && matches[0].Score > matches[1].Score && matches[0].Score >= 95))
                entry = matches[0].Entry;
            else
            {
                var (picked, remember) = await AutoTypePickerWindow.PickAsync(target, matches, _app.Vault.ActiveEntries);
                entry = picked;
                if (entry != null && remember) entry = RememberChoice(entry, target);
            }

            if (entry == null)
            {
                Native.ForceForeground(hwnd);
                return;
            }
            if (Native.IsInputBlocked(hwnd))
            {
                Native.ForceForeground(hwnd);
                CopyInsteadOfTyping(entry, target.Describe(), field?.Kind ?? FieldKind.Password);
                return;
            }
            // A page without the cursor in its form: the form is found and the cursor put into it.
            if (field == null && target.IsBrowser && string.IsNullOrWhiteSpace(entry.AutoTypeSequence))
            {
                Native.ForceForeground(hwnd);
                field = await Task.Run(() => FieldFinder.FindSignInField(hwnd) is { } found && PutCursorInto(found) ? found : null);
            }
            // Without a custom sequence fill by fields: a pre-filled login (VPN clients remember it) is kept and
            // a PIN, code or key field gets just that value.
            if (field != null && string.IsNullOrWhiteSpace(entry.AutoTypeSequence))
            {
                Native.ForceForeground(hwnd);
                await Task.Delay(120);
                MarkFilled(hwnd, target);
                await FillFieldAsync(entry, field);
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
        // A program entry: its window is found among the open ones, whichever window was used last.
        if (entry.WindowPatterns.Count > 0)
        {
            var window = await Task.Run(() => ClientDetector.OpenWindows().FirstOrDefault(w =>
                EntryMatcher.Match([entry], new TargetContext { WindowTitle = w.Title, ProcessName = w.ProcessName }).Any(m => m.Score >= 95)));
            if (window != null)
            {
                main.WindowState = System.Windows.WindowState.Minimized;
                await Task.Delay(250);
                if (!await FillWindowAsync(entry, window.Handle, _app.Settings.SubmitAfterFill))
                    _app.Tray?.ShowBalloon("PassKeeper", Loc.T("AutoType.NoForm"));
                return;
            }
        }
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

    private readonly Dictionary<string, DateTime> _autoFilled = [];
    private readonly HashSet<string> _autoFilling = [];
    private (IntPtr Window, DateTime At, List<System.Windows.Rect> Fields) _lastFill = (IntPtr.Zero, DateTime.MinValue, []);

    /// <summary>
    /// Links an entry to the site or program it was just used for (another address / a window pattern), so that it
    /// matches there next time — a VPN account used on the company portal, a site account used in a program.
    /// </summary>
    public VaultEntry Associate(VaultEntry entry, TargetWindow target)
    {
        var copy = entry.Clone();
        copy.Category ??= entry.EffectiveCategory;
        if (target.IsBrowser && DomainUtil.GetHost(target.Url) is { } host)
        {
            if (copy.AllUrls().Any(u => DomainUtil.GetHost(u) == host)) return entry;
            if (copy.Url.Length == 0) copy.Url = "https://" + host;
            else copy.ExtraUrls.Add("https://" + host);
        }
        else if (!target.IsBrowser && target.ProcessName.Length > 0)
        {
            var pattern = target.ProcessName + ".exe";
            if (copy.WindowPatterns.Contains(pattern, StringComparer.OrdinalIgnoreCase)) return entry;
            copy.WindowPatterns.Add(pattern);
        }
        else return entry;
        _app.Vault.Upsert(copy);
        return _app.Vault.Find(copy.Id) ?? copy;
    }

    /// <summary>
    /// The user chose an entry for a site or program: it is linked to it (the address or window is added when missing)
    /// and, on a site, becomes the account filled in there without asking from now on; other entries give that site up.
    /// The site is its host name, so a sign-in page with a fresh one-time link every time is still the same site.
    /// </summary>
    public VaultEntry RememberChoice(VaultEntry entry, TargetWindow target)
    {
        entry = Associate(entry, target);
        if (!target.IsBrowser || DomainUtil.GetHost(target.Url) is not { } host) return entry;
        foreach (var other in _app.Vault.ActiveEntries.Where(e => e.Id != entry.Id && e.AutoFillHosts.Contains(host, StringComparer.OrdinalIgnoreCase)).ToList())
        {
            var copy = other.Clone();
            copy.AutoFillHosts.RemoveAll(h => h.Equals(host, StringComparison.OrdinalIgnoreCase));
            _app.Vault.Upsert(copy);
        }
        if (entry.AutoFillHosts.Contains(host, StringComparer.OrdinalIgnoreCase)) return entry;
        var chosen = entry.Clone();
        chosen.AutoFillHosts.Add(host);
        _app.Vault.Upsert(chosen);
        return _app.Vault.Find(chosen.Id) ?? chosen;
    }

    /// <summary>
    /// Fills a sign-in form by itself: the cursor is in an empty login or password field (or, with
    /// <paramref name="putCursor"/>, is put into it) and one entry clearly belongs to the site or program. A page or
    /// window is filled once in 3 minutes (a failed sign-in that shows the form again is not filled again), and fields
    /// already filled by the browser, the client or the user are left alone.
    /// </summary>
    public async Task<bool> TryAutoFillAsync(VaultEntry entry, LoginField field, TargetWindow target, bool putCursor = false)
    {
        if (IsTyping || field.Kind is not (FieldKind.Login or FieldKind.Email or FieldKind.Phone or FieldKind.Password)) return false;
        var place = Place(field.Window, target);
        if (place == null) return false;
        foreach (var old in _autoFilled.Where(p => DateTime.UtcNow - p.Value > TimeSpan.FromMinutes(3)).Select(p => p.Key).ToList()) _autoFilled.Remove(old);
        if (_autoFilled.ContainsKey(place))
        {
            AutoFillLog.Write("  not filled again: this page was filled a moment ago");
            return false;
        }
        if (!SiteAllowsAutoFill(target))
        {
            AutoFillLog.Write("  not filled: the site was filled twice in the last 3 minutes");
            return false;
        }
        if (!_autoFilling.Add(place)) return false;
        try
        {
            if (!await Task.Run(() => IsUntouched(field, entry)))
            {
                AutoFillLog.Write("  not filled: the form already holds something typed");
                return false;
            }
            // The page may still be moving the cursor, or the user may start typing: check again after a moment.
            await Task.Delay(250);
            if (IsTyping) return false;
            if (!await Task.Run(() => putCursor ? PutCursorInto(field) : IsFocused(field)))
            {
                AutoFillLog.Write(putCursor ? "  not filled: the cursor could not be put into the field (covered or window not in front)" : "  not filled: the cursor left the field");
                return false;
            }
            if (!await Task.Run(() => IsUntouched(field, entry))) return false;
            _autoFilled[place] = DateTime.UtcNow;
            RecordSiteAutoFill(target);
            AutoFillLog.Write($"  filled in by itself: \"{entry.Title}\"");
            await FillFieldAsync(entry, field, _app.Settings.SubmitAfterFill);
            return true;
        }
        finally
        {
            _autoFilling.Remove(place);
        }
    }

    private readonly Dictionary<string, List<DateTime>> _siteFills = [];

    /// <summary>
    /// A site is filled by itself at most twice in 3 minutes, whatever its address: sign-in pages with a fresh one-time
    /// link (and an error page after a wrong password) are not filled over and over.
    /// </summary>
    private bool SiteAllowsAutoFill(TargetWindow target)
    {
        var site = target.IsBrowser ? DomainUtil.GetHost(target.Url) : target.ProcessName;
        if (string.IsNullOrEmpty(site) || !_siteFills.TryGetValue(site, out var times)) return true;
        times.RemoveAll(t => DateTime.UtcNow - t > TimeSpan.FromMinutes(3));
        return times.Count < 2;
    }

    private void RecordSiteAutoFill(TargetWindow target)
    {
        var site = target.IsBrowser ? DomainUtil.GetHost(target.Url) : target.ProcessName;
        if (string.IsNullOrEmpty(site)) return;
        if (!_siteFills.TryGetValue(site, out var times)) _siteFills[site] = times = [];
        times.Add(DateTime.UtcNow);
    }

    /// <summary>A page (window and address) or a program window (window and title): filled at most once in 3 minutes by itself.</summary>
    private static string? Place(IntPtr window, TargetWindow target) => target.IsBrowser
        ? DomainUtil.GetHost(target.Url) != null ? window + "|" + target.Url : null
        : window + "|" + target.Title;

    /// <summary>
    /// A form was filled at the user's request (suggestion, hotkey, chosen entry): it is not filled again by itself
    /// when the cursor moves on through it.
    /// </summary>
    public void MarkFilled(IntPtr window, TargetWindow? target)
    {
        if (target != null && Place(window, target) is { } place) _autoFilled[place] = DateTime.UtcNow;
    }

    /// <summary>The form of this page or window is being filled by itself right now.</summary>
    public bool IsAutoFilling(IntPtr window) => _autoFilling.Any(p => p.StartsWith(window + "|", StringComparison.Ordinal));

    /// <summary>The field was filled a moment ago (the cursor moving on through a form just filled needs no suggestion).</summary>
    public bool JustFilled(LoginField field) =>
        _lastFill.Window == field.Window && DateTime.UtcNow - _lastFill.At < TimeSpan.FromSeconds(3) && _lastFill.Fields.Contains(field.Bounds);

    /// <summary>
    /// Puts the cursor into a field the user has not clicked: through UI Automation, otherwise (browsers ignore it) by
    /// clicking the field, only when the window is in front and nothing covers the field at that point.
    /// </summary>
    public static bool PutCursorInto(LoginField field)
    {
        try
        {
            if (FieldFinder.IsFocused(field.Element)) return true;
            if (Native.GetForegroundWindow() != field.Window) return false;
            var unreported = FieldFinder.FocusStaysOnWindow(field.Window);
            if (!unreported && FieldFinder.FocusAndVerify(field.Element)) return true;
            if (!FieldFinder.Click(field.Element, field.Window)) return false;
            // A browser that does not report the cursor (Yandex): the click on the uncovered field put it there.
            if (unreported) return true;
            for (var i = 0; i < 8; i++)
            {
                if (FieldFinder.IsFocused(field.Element)) return true;
                Thread.Sleep(40);
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or TimeoutException or InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        return false;
    }

    /// <summary>
    /// Puts the cursor into another field of the form and confirms it: by UI Automation, otherwise (browsers refuse
    /// a programmatic focus) with Tab / Shift+Tab. Nothing is typed if the cursor did not arrive.
    /// </summary>
    private static bool MoveTo(AutomationElement target, KeyboardSender sender, bool back)
    {
        // A browser that does not report the cursor in its pages: the field is clicked (only if nothing covers it).
        var window = sender.TargetWindow;
        if (window != IntPtr.Zero && FieldFinder.FocusStaysOnWindow(window)) return FieldFinder.Click(target, window);
        if (FieldFinder.FocusAndVerify(target)) return true;
        if (back) sender.ShiftTab();
        else sender.PressKey("TAB");
        for (var i = 0; i < 6; i++)
        {
            if (FieldFinder.IsFocused(target)) return true;
            Thread.Sleep(40);
        }
        return false;
    }

    /// <summary>
    /// Types a password into its field, checking before every key that the cursor is still there: if the user clicks
    /// into the login field meanwhile, typing stops instead of putting the password where it would be visible. Browsers
    /// that do not report the cursor (Yandex) cannot be checked.
    /// </summary>
    private static void TypePassword(KeyboardSender sender, AutomationElement password, IntPtr window, string value)
    {
        if (!FieldFinder.FocusStaysOnWindow(window)) sender.Guard = () => FieldFinder.IsFocused(password);
        try
        {
            sender.ClearField();
            sender.TypeText(value);
        }
        finally
        {
            sender.Guard = null;
        }
    }

    /// <summary>The field is empty; for a password field the login before it is empty or already this entry's.</summary>
    /// <summary>
    /// The form has nothing the user typed: the field is empty or already holds this entry's login (the browser's own
    /// autofill), and for a password field the login before it is empty or this entry's.
    /// </summary>
    private static bool IsUntouched(LoginField field, VaultEntry entry)
    {
        var value = FieldFinder.GetValue(field.Element);
        if (value == null) return false;
        if (!field.IsPassword) return value.Length == 0 || value == ValueFor(entry, field.Kind);
        if (value.Length > 0) return false;
        var user = FieldFinder.FindUsernameBefore(field.Element);
        var typed = user == null ? "" : FieldFinder.GetValue(user) ?? "";
        return typed.Length == 0 || typed == ValueFor(entry, FieldKind.Login);
    }

    private static bool IsFocused(LoginField field) => FieldFinder.IsFocused(field.Element);

    public async Task<bool> FillWindowAsync(VaultEntry entry, IntPtr hwnd, bool submit)
    {
        if (!Native.IsWindow(hwnd)) return false;
        Interlocked.Increment(ref _typing);
        try
        {
            Native.ForceForeground(hwnd);
            await Task.Delay(250);
            var field = await Task.Run(() => FocusedField(hwnd) ?? FieldFinder.FirstSignInField(hwnd));
            if (field != null) await Task.Run(() => FieldFinder.Focus(field.Element));
            if (!string.IsNullOrWhiteSpace(entry.AutoTypeSequence))
            {
                var target = await Task.Run(() => TargetDetector.Capture(hwnd, detectUrl: false));
                await TypeAsync(entry, target, entry.AutoTypeSequence);
                return true;
            }
            if (field == null) return false;
            await FillFieldAsync(entry, field, submit);
            return true;
        }
        finally
        {
            Interlocked.Decrement(ref _typing);
        }
    }

    /// <summary>The sign-in field focused in the given window, if any (bounded: UI Automation may hang on a busy app).</summary>
    private static LoginField? FocusedField(IntPtr hwnd)
    {
        var task = Task.Run(() =>
        {
            try
            {
                Native.GetWindowThreadProcessId(hwnd, out var pid);
                var focused = AutomationElement.FocusedElement;
                return focused != null && focused.Current.ProcessId == pid ? LoginField.From(focused, hwnd) : null;
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or TimeoutException or InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                return null;
            }
        });
        return task.Wait(1500) ? task.Result : null;
    }

    /// <summary>
    /// Windows (UIPI) drops simulated input into windows that run as administrator or as SYSTEM. The value is handed
    /// over through the clipboard instead (cleared as configured) and the user pastes it.
    /// </summary>
    private void CopyInsteadOfTyping(VaultEntry entry, string target, FieldKind kind)
    {
        var (value, what) = kind switch
        {
            FieldKind.Pin => (entry.PinCode(), "AutoType.WhatPin"),
            FieldKind.Otp => (ValueFor(entry, kind), "AutoType.WhatCode"),
            FieldKind.Key => (entry.SecretKey, "AutoType.WhatKey"),
            _ => (entry.Password, "AutoType.WhatPassword"),
        };
        if (value.Length == 0 || !ClipboardService.Copy(value, _app.Settings.ClipboardClearSeconds))
        {
            _app.Tray?.ShowBalloon(Loc.T("AutoType.ErrorTitle"), Loc.T("AutoType.Blocked"));
            return;
        }
        var message = Loc.F("AutoType.Elevated", target, Loc.T(what));
        var login = ValueFor(entry, FieldKind.Login);
        if (kind is FieldKind.Password or FieldKind.Login && login.Length > 0) message += " " + Loc.F("AutoType.ElevatedLogin", login);
        _app.Tray?.ShowBalloon("PassKeeper", message);
        try { _app.Vault.MarkUsed(entry.Id); } catch (IOException) { }
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

        if (!Native.ForceForeground(target.Handle) && Native.IsHungAppWindow(target.Handle)) return;
        if (Native.IsInputBlocked(target.Handle))
        {
            CopyInsteadOfTyping(entry, target.Describe(), FieldKind.Password);
            return;
        }
        await Task.Delay(120);
        var sender = CreateSender(target.Handle);
        Interlocked.Increment(ref _typing);
        try
        {
            await Task.Run(() =>
            {
                KeyboardSender.WaitForModifiersReleased();
                sender.Execute(actions);
            });
            _app.Vault.MarkUsed(entry.Id);
            WindowFilled?.Invoke(target.Handle);
        }
        catch (AutoTypeAbortedException)
        {
            _app.Tray?.ShowBalloon(Loc.T("AutoType.ErrorTitle"), Loc.T("AutoType.Aborted"));
        }
        catch (InvalidOperationException)
        {
            _app.Tray?.ShowBalloon(Loc.T("AutoType.ErrorTitle"), Loc.T("AutoType.Blocked"));
        }
        finally
        {
            Interlocked.Decrement(ref _typing);
        }
    }

    /// <summary>Whether the entry has something to put into a field of this kind.</summary>
    public static bool CanFill(VaultEntry e, FieldKind kind) => kind switch
    {
        FieldKind.Password => e.Password.Length > 0,
        FieldKind.Pin => e.PinCode().Length > 0,
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
            FieldKind.Pin => e.PinCode(),
            _ => login,
        };
    }

    /// <summary>Suggestion click: fill the focused field (and its login/password counterpart) in place.</summary>
    public async Task FillFieldAsync(VaultEntry entry, LoginField field, bool? submitAfter = null)
    {
        if (Native.IsHungAppWindow(field.Window)) return;
        if (Native.IsInputBlocked(field.Window))
        {
            CopyInsteadOfTyping(entry, Native.GetWindowTitle(field.Window), field.Kind);
            return;
        }
        var login = ValueFor(entry, FieldKind.Login);
        var sender = CreateSender(field.Window);
        var submit = submitAfter ?? _app.Settings.SubmitAfterFill;
        var filled = new List<System.Windows.Rect> { field.Bounds };
        Interlocked.Increment(ref _typing);
        try
        {
            await Task.Run(() =>
            {
                KeyboardSender.WaitForModifiersReleased();
                if (field.Kind is FieldKind.Otp or FieldKind.Key or FieldKind.Pin)
                {
                    sender.ClearField();
                    sender.TypeText(ValueFor(entry, field.Kind));
                }
                else if (field.IsPassword)
                {
                    // The cursor is moved only when the move is confirmed: a login must never land in a password field.
                    var user = FieldFinder.FindUsernameBefore(field.Element);
                    if (user != null && login.Length > 0 && FieldFinder.GetValue(user) != login && MoveTo(user, sender, back: true))
                    {
                        filled.Add(FieldFinder.Bounds(user));
                        sender.ClearField();
                        sender.TypeText(login);
                        FieldFinder.WaitForValue(user, login);
                        if (!MoveTo(field.Element, sender, back: false) || FieldFinder.IsFocused(user)) return;
                    }
                    TypePassword(sender, field.Element, field.Window, entry.Password);
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
                        if (!MoveTo(password, sender, back: false)) return;
                        // A password is never typed while the cursor is still reported in the login field.
                        if (FieldFinder.IsFocused(field.Element)) return;
                        filled.Add(FieldFinder.Bounds(password));
                        TypePassword(sender, password, field.Window, entry.Password);
                    }
                }
                if (submit) sender.PressKey("ENTER");
            });
            _lastFill = (field.Window, DateTime.UtcNow, filled);
            _app.Vault.MarkUsed(entry.Id);
            WindowFilled?.Invoke(field.Window);
        }
        catch (AutoTypeAbortedException)
        {
            _app.Tray?.ShowBalloon(Loc.T("AutoType.ErrorTitle"), Loc.T("AutoType.Aborted"));
        }
        catch (InvalidOperationException)
        {
            _app.Tray?.ShowBalloon(Loc.T("AutoType.ErrorTitle"), Loc.T("AutoType.Blocked"));
        }
        finally
        {
            Interlocked.Decrement(ref _typing);
        }
    }
}

/// <summary>Locates sibling login/password inputs through UI Automation.</summary>
internal static class FieldFinder
{
    private static readonly Condition EditCondition = new AndCondition(
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
        new PropertyCondition(AutomationElement.IsEnabledProperty, true));

    private static readonly Condition VisibleEditCondition = new AndCondition(
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
        new PropertyCondition(AutomationElement.IsEnabledProperty, true),
        new PropertyCondition(AutomationElement.IsOffscreenProperty, false));

    private static readonly Condition VisiblePasswordCondition = new AndCondition(
        VisibleEditCondition,
        new PropertyCondition(AutomationElement.IsPasswordProperty, true));

    /// <summary>
    /// The sign-in form of a page the cursor is not in: the login field before the first visible password field (or
    /// that password field), otherwise (sign-in in two steps) the first visible login, e-mail or phone field.
    /// </summary>
    public static LoginField? FindSignInField(AutomationElement page, IntPtr window)
    {
        try
        {
            var password = page.FindFirst(TreeScope.Descendants, VisiblePasswordCondition);
            if (password != null)
            {
                var user = FindUsernameBefore(password);
                if (user != null && LoginNear(user, password) is { } login) return Describe(user, window, login);
                return Describe(password, window, FieldKind.Password);
            }
            var checkedFields = 0;
            foreach (AutomationElement edit in page.FindAll(TreeScope.Descendants, VisibleEditCondition))
            {
                if (++checkedFields > 20) break;
                var info = edit.Current;
                var kind = FieldClassifier.Classify(info.IsPassword, info.Name, info.AutomationId, info.HelpText);
                if (kind is FieldKind.Login or FieldKind.Email or FieldKind.Phone) return Describe(edit, window, kind.Value);
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or TimeoutException or InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException) { }
        return null;
    }

    /// <summary>The sign-in form of the page shown in a browser window (hotkey without the cursor in a field).</summary>
    public static LoginField? FindSignInField(IntPtr window)
    {
        try
        {
            Native.GetWindowThreadProcessId(window, out var pid);
            var focused = AutomationElement.FocusedElement;
            var page = focused != null && focused.Current.ProcessId == pid ? Container(focused) : null;
            if (page == null || page.Current.ControlType != ControlType.Document) page = PageOf(window);
            return page == null ? null : FindSignInField(page, window);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or TimeoutException or InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// The page (document) shown in a browser window. Some browsers (Yandex) report focus on the window itself rather
    /// than on the page: the element in the middle of the window leads to it in a few steps up.
    /// </summary>
    public static AutomationElement? PageOf(IntPtr window)
    {
        try
        {
            if (!Native.GetWindowRect(window, out var r) || r.Right - r.Left < 100) return null;
            Native.GetWindowThreadProcessId(window, out var pid);
            var at = AutomationElement.FromPoint(new System.Windows.Point((r.Left + r.Right) / 2.0, r.Top + (r.Bottom - r.Top) * 0.6));
            var walker = TreeWalker.ControlViewWalker;
            for (var depth = 0; at != null && depth < 8; depth++)
            {
                var info = at.Current;
                if (info.ProcessId != pid) return null;
                if (info.ControlType == ControlType.Document) return at;
                at = walker.GetParent(at);
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or TimeoutException or InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException) { }
        return null;
    }

    /// <summary>
    /// The text box before a password field is its login when it is labelled as one, or when it has no label and sits
    /// right above or beside the password (a search box elsewhere on the page is not).
    /// </summary>
    private static FieldKind? LoginNear(AutomationElement user, AutomationElement password)
    {
        var info = user.Current;
        var kind = FieldClassifier.Classify(false, info.Name, info.AutomationId, info.HelpText);
        if (kind is FieldKind.Login or FieldKind.Email or FieldKind.Phone) return kind;
        if (kind != null || !string.IsNullOrWhiteSpace(info.Name)) return null;
        var u = info.BoundingRectangle;
        var p = password.Current.BoundingRectangle;
        var below = p.Top - u.Bottom is >= -2 and < 160 && Math.Abs(p.Left - u.Left) < 60;
        var beside = Math.Abs(p.Top - u.Top) < 8 && p.Left - u.Right is >= -2 and < 80;
        return below || beside ? FieldKind.Login : null;
    }

    private static LoginField? Describe(AutomationElement element, IntPtr window, FieldKind kind)
    {
        var bounds = element.Current.BoundingRectangle;
        return bounds.IsEmpty || bounds.Width < 20 ? null : new LoginField { Element = element, Kind = kind, Bounds = bounds, Window = window };
    }

    /// <summary>
    /// The browser reports focus on its window only, never on the field with the cursor (Yandex Browser): the cursor
    /// can then neither be confirmed nor followed through UI Automation.
    /// </summary>
    public static bool FocusStaysOnWindow(IntPtr window)
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused == null) return false;
            var info = focused.Current;
            return info.ControlType == ControlType.Window && info.NativeWindowHandle == window.ToInt32();
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or TimeoutException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    /// <summary>Clicks the left part of a field (its right edge often holds a "show password" or clear button) if nothing covers it.</summary>
    public static bool Click(AutomationElement field, IntPtr window)
    {
        try
        {
            var r = field.Current.BoundingRectangle;
            if (r.IsEmpty) return false;
            var x = (int)(r.Left + Math.Min(r.Width / 2, 24));
            var y = (int)(r.Top + r.Height / 2);
            // Right after a page appears the browser may not yet tell what is at a point: a few looks.
            var uncovered = false;
            for (var attempt = 0; attempt < 4 && !uncovered; attempt++)
            {
                if (attempt > 0) Thread.Sleep(150);
                uncovered = IsAt(field, x, y);
            }
            if (!uncovered || Native.GetForegroundWindow() != window) return false;
            Native.Click(x, y);
            Thread.Sleep(60);
            return true;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or TimeoutException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    public static System.Windows.Rect Bounds(AutomationElement element)
    {
        try
        {
            return element.Current.BoundingRectangle;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or TimeoutException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return System.Windows.Rect.Empty;
        }
    }

    /// <summary>The field itself is at this screen point (not a banner or dialog covering it).</summary>
    public static bool IsAt(AutomationElement field, int x, int y)
    {
        var at = AutomationElement.FromPoint(new System.Windows.Point(x, y));
        if (at == null) return false;
        if (Automation.Compare(at, field)) return true;
        var a = at.Current.BoundingRectangle;
        var f = field.Current.BoundingRectangle;
        // Browsers expose the text inside an input as a separate element lying within the input.
        return !a.IsEmpty && a.Left >= f.Left - 2 && a.Top >= f.Top - 2 && a.Right <= f.Right + 2 && a.Bottom <= f.Bottom + 2;
    }

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

    /// <summary>
    /// Input fields of the form around the element, in document order. Browsers expose some inputs twice (the field
    /// and its inner text at the same place): only the first element of each place is kept.
    /// </summary>
    private static List<AutomationElement> Edits(AutomationElement element, out int index)
    {
        index = -1;
        var container = Container(element);
        if (container == null) return [];
        var list = new List<AutomationElement>();
        var places = new HashSet<System.Windows.Rect>();
        foreach (AutomationElement e in container.FindAll(TreeScope.Descendants, EditCondition))
        {
            try
            {
                var info = e.Current;
                if (info.IsOffscreen || !places.Add(info.BoundingRectangle)) continue;
                list.Add(e);
            }
            catch (ElementNotAvailableException) { }
            catch (TimeoutException) { } // the program does not answer UI Automation in time
        }
        var bounds = element.Current.BoundingRectangle;
        for (var i = 0; i < list.Count && index < 0; i++)
            if (Automation.Compare(list[i], element) || list[i].Current.BoundingRectangle == bounds) index = i;
        return list;
    }

    /// <summary>
    /// The field to start filling a window's login form from: the first password / PIN field (the login before it
    /// is filled from there), otherwise the first login field.
    /// </summary>
    public static LoginField? FirstSignInField(IntPtr hwnd)
    {
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            LoginField? login = null;
            foreach (AutomationElement edit in root.FindAll(TreeScope.Descendants, EditCondition))
            {
                if (edit.Current.IsOffscreen) continue;
                var field = LoginField.From(edit, hwnd);
                if (field == null) continue;
                if (field.Kind is PassKeeper.Core.AutoType.FieldKind.Password or PassKeeper.Core.AutoType.FieldKind.Pin) return field;
                if (field.Kind != PassKeeper.Core.AutoType.FieldKind.Otp) login ??= field;
            }
            return login;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or TimeoutException or InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            return null;
        }
    }

    public static AutomationElement? FindUsernameBefore(AutomationElement password)
    {
        try
        {
            var edits = Edits(password, out var index);
            for (var i = index - 1; i >= 0 && i >= index - 3; i--)
                if (!edits[i].Current.IsPassword) return edits[i];
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or TimeoutException or InvalidOperationException or System.Runtime.InteropServices.COMException) { }
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
        catch (Exception ex) when (ex is ElementNotAvailableException or TimeoutException or InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        return null;
    }

    public static string? GetValue(AutomationElement element)
    {
        try
        {
            return element.TryGetCurrentPattern(ValuePattern.Pattern, out var p) ? ((ValuePattern)p).Current.Value : null;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or TimeoutException or InvalidOperationException or System.Runtime.InteropServices.COMException)
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
        catch (Exception ex) when (ex is ElementNotAvailableException or TimeoutException or InvalidOperationException or System.Runtime.InteropServices.COMException) { }
    }

    /// <summary>Moves the cursor to the field and confirms it arrived there.</summary>
    public static bool FocusAndVerify(AutomationElement element)
    {
        Focus(element);
        for (var i = 0; i < 6; i++)
        {
            if (IsFocused(element)) return true;
            Thread.Sleep(50);
        }
        return false;
    }

    /// <summary>The cursor is in this field (browsers may expose one input as two elements: compared by place).</summary>
    public static bool IsFocused(AutomationElement element)
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused == null) return false;
            if (Automation.Compare(focused, element)) return true;
            var info = focused.Current;
            return info.ControlType == ControlType.Edit && info.BoundingRectangle == element.Current.BoundingRectangle;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or TimeoutException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }
}
