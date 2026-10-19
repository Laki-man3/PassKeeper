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
            if (field == null && target.IsBrowser && FillsByFields(entry))
            {
                Native.ForceForeground(hwnd);
                field = await Task.Run(() => FieldFinder.FindSignInField(hwnd) is { } found && PutCursorInto(found) ? found : null);
            }
            // Without a sequence of keystrokes fill by fields: a pre-filled login (VPN clients remember it) is kept
            // and a PIN, code or key field gets just that value.
            if (field != null && FillsByFields(entry))
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

    /// <summary>The entry is filled field by field: it has no sequence, or one made of fields only.</summary>
    public static bool FillsByFields(VaultEntry entry) =>
        string.IsNullOrWhiteSpace(entry.AutoTypeSequence) || FieldPlan.Parse(entry.AutoTypeSequence) != null;

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
            if (copy.Url.Length == 0 && copy.Category != EntryCategory.Remote) copy.Url = "https://" + host;
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
        if (field.Kind == FieldKind.Phone && !LooksLikePhone(ValueFor(entry, FieldKind.Phone)))
        {
            AutoFillLog.Write("  not filled: the field asks for a phone number, the entry has none");
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
            await Task.Delay(120);
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
            await FillAndSignInAsync(entry, field, target);
            return true;
        }
        finally
        {
            _autoFilling.Remove(place);
        }
    }

    private readonly Dictionary<string, List<DateTime>> _siteFills = [];
    private readonly Dictionary<Guid, List<DateTime>> _siteSignIns = [];
    private readonly HashSet<Guid> _siteSignInPaused = [];

    /// <summary>
    /// Fills a form; on a site, an entry with auto sign-in (and the setting for sites on) also submits it. Only the
    /// attempts that typed a password are counted: after <see cref="AutoLoginService.MaxAttempts"/> of them in
    /// <see cref="AutoLoginService.AttemptWindow"/> the form is filled but not submitted, so a wrong saved password
    /// cannot lock the account.
    /// </summary>
    public async Task FillAndSignInAsync(VaultEntry entry, LoginField field, TargetWindow? target)
    {
        var signIn = SignsInOnSite(entry, target);
        var (passwordTyped, entered) = await FillFieldAsync(entry, field, signIn || _app.Settings.SubmitAfterFill);
        if (!signIn) return;
        if (!entered)
        {
            AutoFillLog.Write("  filled, Enter not pressed (the entry's sequence has none here)");
            return;
        }
        AutoFillLog.Write(passwordTyped ? "  signed in: Enter pressed after the password" : "  Enter pressed after the login (the password comes next)");
        if (!passwordTyped) return;
        if (!_siteSignIns.TryGetValue(entry.Id, out var times)) _siteSignIns[entry.Id] = times = [];
        times.Add(DateTime.UtcNow);
    }

    private bool SignsInOnSite(VaultEntry entry, TargetWindow? target)
    {
        if (target is not { IsBrowser: true } || !entry.SignsInToSite || !_app.Settings.AutoLoginWeb) return false;
        var count = 0;
        if (_siteSignIns.TryGetValue(entry.Id, out var times))
        {
            times.RemoveAll(t => DateTime.UtcNow - t >= AutoLoginService.AttemptWindow);
            count = times.Count;
        }
        if (count < AutoLoginService.MaxAttempts)
        {
            _siteSignInPaused.Remove(entry.Id);
            return true;
        }
        AutoFillLog.Write($"  not signed in: {count} attempts in the last 10 minutes, the form is only filled in");
        if (_siteSignInPaused.Add(entry.Id)) _app.Tray?.ShowBalloon("PassKeeper", Loc.F("AutoLogin.Paused", entry.Title, AutoLoginService.MaxAttempts));
        return false;
    }

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
            // A form further down the page is brought into view first (the card and the click need its place).
            FieldFinder.ScrollIntoView(field.Element);
            field.Bounds = FieldFinder.Bounds(field.Element) is { IsEmpty: false } moved ? moved : field.Bounds;
            var unreported = FieldFinder.FocusStaysOnWindow(field.Window);
            if (!unreported && FieldFinder.TryFocus(field.Element, field.Window)) return true;
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
        if (FieldFinder.TryFocus(target, window)) return true;
        if (back) sender.ShiftTab();
        else sender.PressKey("TAB");
        for (var i = 0; i < 6; i++)
        {
            if (FieldFinder.IsFocused(target)) return true;
            Thread.Sleep(40);
        }
        // Something else sits between the fields in the tab order (a "forgot password" link): the field is clicked.
        if (window == IntPtr.Zero || !FieldFinder.Click(target, window)) return false;
        for (var i = 0; i < 5; i++)
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
        if (!field.IsPassword) return value.Length == 0 || value == ValueFor(entry, field.Kind) || field.Kind == FieldKind.Phone && IsCountryCode(value);
        if (value.Length > 0) return false;
        var user = FieldFinder.FindUsernameBefore(field.Element, FieldFinder.FormOf(field));
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
            if (!FillsByFields(entry))
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

    /// <summary>A phone field that shows only its country code ("+7"), which the site put there.</summary>
    private static bool IsCountryCode(string value) =>
        System.Text.RegularExpressions.Regex.IsMatch(value.Trim(), @"^\+?\d{0,4}$");

    private static bool LooksLikePhone(string value) =>
        value.Count(char.IsDigit) >= 7 && value.All(c => char.IsDigit(c) || "+-() ".Contains(c));

    /// <summary>
    /// A token PIN, a one-time code or a key asked for in the same form after the password (a VPN client's dialog):
    /// filled when the entry's sequence lists it. Returns the last field filled here.
    /// </summary>
    private static FieldKind? FillFollowing(VaultEntry entry, FieldPlan? plan, AutomationElement after, FormFields? form, KeyboardSender sender, List<System.Windows.Rect> filled)
    {
        if (plan == null || form == null) return null;
        FieldKind? last = null;
        var from = form.IndexOf(after);
        foreach (var kind in plan.Kinds.Where(k => k is FieldKind.Pin or FieldKind.Otp or FieldKind.Key))
        {
            var value = ValueFor(entry, kind);
            if (from < 0 || value.Length == 0) break;
            var next = form.Fields.Skip(from + 1).Take(3).FirstOrDefault(f => f.Usable && f.Kind == kind);
            if (next == null || !MoveTo(next.Element, sender, back: false)) break;
            filled.Add(next.Bounds);
            sender.ClearField();
            sender.TypeText(value);
            last = kind;
            from = form.Fields.IndexOf(next);
        }
        return last;
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

    /// <summary>
    /// Suggestion click: fill the focused field (and its login/password counterpart) in place. Returns whether a
    /// password was typed (a sign-in attempt rather than the first step of a two-step form) and Enter pressed.
    /// </summary>
    public async Task<FillResult> FillFieldAsync(VaultEntry entry, LoginField field, bool? submitAfter = null)
    {
        if (Native.IsHungAppWindow(field.Window)) return default;
        if (Native.IsInputBlocked(field.Window))
        {
            CopyInsteadOfTyping(entry, Native.GetWindowTitle(field.Window), field.Kind);
            return default;
        }
        var login = ValueFor(entry, FieldKind.Login);
        var sender = CreateSender(field.Window);
        var submit = submitAfter ?? _app.Settings.SubmitAfterFill;
        var filled = new List<System.Windows.Rect> { field.Bounds };
        var passwordTyped = false;
        var entered = false;
        // A sequence of fields says which fields to fill and after which one to press Enter: the user's own one as it
        // is written (a caller may still rule Enter out), the one made from the form when Enter is wanted.
        var plan = FieldPlan.For(entry);
        bool Wants(FieldKind kind) => plan == null || plan.Includes(kind);
        bool EnterAfter(FieldKind? last) => last != null && (plan == null ? submit
            : plan.Generated ? submit && (plan.EnterAfter(last.Value) || !plan.HasEnter)
            : submitAfter != false && plan.EnterAfter(last.Value));
        FieldKind? last = null;
        Interlocked.Increment(ref _typing);
        try
        {
            await Task.Run(() =>
            {
                KeyboardSender.WaitForModifiersReleased();
                // The form is read before the first key: right after typing, a browser is busy updating the page and
                // answers UI Automation several times slower.
                var form = field.Kind is FieldKind.Otp or FieldKind.Key or FieldKind.Pin ? null : FieldFinder.FormOf(field);
                if (field.Kind is FieldKind.Otp or FieldKind.Key or FieldKind.Pin)
                {
                    sender.ClearField();
                    sender.TypeText(ValueFor(entry, field.Kind));
                    last = field.Kind;
                }
                else if (field.IsPassword)
                {
                    // The cursor is moved only when the move is confirmed: a login must never land in a password field.
                    var user = Wants(FieldKind.Login) ? FieldFinder.FindUsernameBefore(field.Element, form) : null;
                    if (user != null && login.Length > 0 && FieldFinder.GetValue(user) != login && MoveTo(user, sender, back: true))
                    {
                        filled.Add(FieldFinder.Bounds(user));
                        sender.ClearField();
                        sender.TypeText(login);
                        FieldFinder.WaitForValue(user, login);
                        last = FieldKind.Login;
                        if (!MoveTo(field.Element, sender, back: false) || FieldFinder.IsFocused(user)) return;
                    }
                    if (Wants(FieldKind.Password))
                    {
                        TypePassword(sender, field.Element, field.Window, entry.Password);
                        passwordTyped = entry.Password.Length > 0;
                        last = FieldKind.Password;
                        last = FillFollowing(entry, plan, field.Element, form, sender, filled) ?? last;
                    }
                }
                else
                {
                    if (Wants(field.Kind))
                    {
                        var typed = ValueFor(entry, field.Kind);
                        sender.ClearField();
                        sender.TypeText(typed);
                        FieldFinder.WaitForValue(field.Element, typed);
                        last = field.Kind;
                    }
                    var password = Wants(FieldKind.Password) ? FieldFinder.FindPasswordAfter(field.Element, form) : null;
                    if (password != null && entry.Password.Length > 0)
                    {
                        if (!MoveTo(password, sender, back: false)) return;
                        // A password is never typed while the cursor is still reported in the login field.
                        if (FieldFinder.IsFocused(field.Element)) return;
                        filled.Add(FieldFinder.Bounds(password));
                        TypePassword(sender, password, field.Window, entry.Password);
                        passwordTyped = true;
                        last = FieldKind.Password;
                        last = FillFollowing(entry, plan, password, form, sender, filled) ?? last;
                    }
                }
                if (!EnterAfter(last)) return;
                sender.PressKey("ENTER");
                entered = true;
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
        return new FillResult(passwordTyped, entered);
    }
}

/// <summary>What a fill did: a password was typed (a sign-in attempt), Enter was pressed.</summary>
public readonly record struct FillResult(bool PasswordTyped, bool Entered);

/// <summary>An input field of a page or window as read in one pass (see <see cref="FieldFinder.ReadForm"/>).</summary>
internal sealed record FormField(AutomationElement Element, bool IsPassword, string Name, string AutomationId, string HelpText, System.Windows.Rect Bounds, bool Offscreen)
{
    /// <summary>The field has a place on the page (hidden inputs have none); it may still be scrolled out of view.</summary>
    public bool Usable => !Bounds.IsEmpty && Bounds.Width >= 20 && Bounds.Height >= 8;

    public FieldKind? Kind => FieldClassifier.Classify(IsPassword, Name, AutomationId, HelpText);
}

/// <summary>The input fields of a form in document order.</summary>
internal sealed class FormFields(List<FormField> fields)
{
    public List<FormField> Fields { get; } = fields;

    /// <summary>Position of the element (browsers may expose one input as two elements: compared by place as well).</summary>
    public int IndexOf(AutomationElement element)
    {
        for (var i = 0; i < Fields.Count; i++)
            if (Automation.Compare(Fields[i].Element, element)) return i;
        System.Windows.Rect bounds;
        try { bounds = element.Current.BoundingRectangle; }
        catch (Exception ex) when (ex is ElementNotAvailableException or TimeoutException or InvalidOperationException or System.Runtime.InteropServices.COMException) { return -1; }
        return Fields.FindIndex(f => f.Bounds == bounds);
    }
}

/// <summary>Locates sibling login/password inputs through UI Automation.</summary>
internal static class FieldFinder
{
    private static readonly Condition EditCondition = new AndCondition(
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
        new PropertyCondition(AutomationElement.IsEnabledProperty, true));

    /// <summary>
    /// The sign-in form of a page the cursor is not in: the login field before the first password field (or that
    /// password field), otherwise (sign-in in two steps) the first login, e-mail or phone field. The page's fields are
    /// read in one pass and kept with the result for filling; fields in view come first, a form further down the page
    /// is scrolled to when it is filled.
    /// </summary>
    public static LoginField? FindSignInField(AutomationElement page, IntPtr window)
    {
        try
        {
            var form = ReadForm(page);
            var fields = form.Fields.Where(f => f.Usable).OrderBy(f => f.Offscreen).ToList();
            var password = fields.FirstOrDefault(f => f.IsPassword);
            if (password != null)
            {
                var index = form.Fields.IndexOf(password);
                var user = Before(form, index);
                if (user != null && LoginNear(user, password) is { } login) return Describe(user, window, login, form);
                return Describe(password, window, FieldKind.Password, form);
            }
            foreach (var field in fields.Take(20))
                if (field.Kind is FieldKind.Login or FieldKind.Email or FieldKind.Phone) return Describe(field, window, field.Kind.Value, form);
            // A field without a label of its own, explained by the text next to it ("Enter your phone number"): a few
            // of the first ones in view are looked at more closely.
            foreach (var field in fields.Where(f => !f.IsPassword && !f.Offscreen && string.IsNullOrWhiteSpace(f.Name)).Take(3))
            {
                var kind = FieldClassifier.Classify(false, LabelAround(field.Element));
                if (kind is FieldKind.Login or FieldKind.Email or FieldKind.Phone) return Describe(field, window, kind.Value, form);
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or TimeoutException or InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException) { }
        return null;
    }

    private static readonly CacheRequest FormProperties = CreateFormRequest();

    private static CacheRequest CreateFormRequest()
    {
        var request = new CacheRequest { TreeScope = TreeScope.Element, AutomationElementMode = AutomationElementMode.Full };
        request.Add(AutomationElement.IsPasswordProperty);
        request.Add(AutomationElement.NameProperty);
        request.Add(AutomationElement.AutomationIdProperty);
        request.Add(AutomationElement.HelpTextProperty);
        request.Add(AutomationElement.BoundingRectangleProperty);
        request.Add(AutomationElement.IsOffscreenProperty);
        return request;
    }

    /// <summary>
    /// The input fields under a page or window in document order, with what is needed to tell them apart, in one
    /// UI Automation pass (each property otherwise costs a call into the other process). Browsers expose some inputs
    /// twice (the field and its inner text at the same place): only the first element of each place is kept.
    /// </summary>
    internal static FormFields ReadForm(AutomationElement container)
    {
        var list = new List<FormField>();
        var places = new HashSet<System.Windows.Rect>();
        AutomationElementCollection found;
        using (FormProperties.Activate()) found = container.FindAll(TreeScope.Descendants, EditCondition);
        foreach (AutomationElement e in found)
        {
            try
            {
                var info = e.Cached;
                if (!places.Add(info.BoundingRectangle)) continue;
                list.Add(new FormField(e, info.IsPassword, info.Name ?? "", info.AutomationId ?? "", info.HelpText ?? "", info.BoundingRectangle, info.IsOffscreen));
            }
            catch (ElementNotAvailableException) { }
            catch (InvalidOperationException) { } // a provider that cannot cache
        }
        return new FormFields(list);
    }

    /// <summary>The fields of the field's form, read once per field (a form filled now does not change its fields).</summary>
    internal static FormFields FormOf(LoginField field)
    {
        if (field.Form == null)
        {
            var container = Container(field.Element);
            field.Form = container == null ? new FormFields([]) : ReadForm(container);
        }
        return field.Form;
    }

    /// <summary>The text box among the three fields before <paramref name="index"/> (the login of a password field).</summary>
    private static FormField? Before(FormFields form, int index)
    {
        for (var i = index - 1; i >= 0 && i >= index - 3; i--)
            if (form.Fields[i].Usable && !form.Fields[i].IsPassword) return form.Fields[i];
        return null;
    }

    private static readonly CacheRequest LabelProperties = CreateLabelRequest();

    private static CacheRequest CreateLabelRequest()
    {
        var request = new CacheRequest { TreeScope = TreeScope.Element, AutomationElementMode = AutomationElementMode.Full };
        request.Add(AutomationElement.NameProperty);
        request.Add(AutomationElement.ControlTypeProperty);
        return request;
    }

    /// <summary>
    /// The text shown just before a field that has no label of its own (sites that draw the label as a separate text,
    /// such as "Enter your phone number" above the field): up to two texts preceding it in the same group.
    /// </summary>
    public static string LabelAround(AutomationElement field)
    {
        try
        {
            var parent = TreeWalker.ControlViewWalker.GetParent(field);
            if (parent == null) return "";
            AutomationElementCollection children;
            using (LabelProperties.Activate()) children = parent.FindAll(TreeScope.Children, Condition.TrueCondition);
            var texts = new List<string>();
            foreach (AutomationElement child in children)
            {
                if (Automation.Compare(child, field)) break;
                if (child.Cached.ControlType == ControlType.Text && !string.IsNullOrWhiteSpace(child.Cached.Name)) texts.Add(child.Cached.Name);
            }
            return string.Join(" ", texts.TakeLast(2));
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or TimeoutException or InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            return "";
        }
    }

    /// <summary>
    /// Which fields the field's sign-in form has, and whether it asks for the login first and the password on the next
    /// page (a login field without a password field).
    /// </summary>
    public static (SignInForm Form, bool TwoSteps) DescribeForm(LoginField field)
    {
        try
        {
            var fields = FormOf(field).Fields.Where(f => f.Usable).ToList();
            var password = fields.Any(f => f.IsPassword && f.Kind == FieldKind.Password) || field.IsPassword;
            var otp = fields.Any(f => f.Kind == FieldKind.Otp);
            var login = field.Kind is FieldKind.Login or FieldKind.Email or FieldKind.Phone ||
                fields.Any(f => !f.IsPassword && f.Kind is FieldKind.Login or FieldKind.Email or FieldKind.Phone);
            return (new SignInForm(login, password, false, otp), login && !password);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or TimeoutException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return (new SignInForm(true, true, false, false), false);
        }
    }

    /// <summary>Scrolls a field that lies outside the visible part of the page into view.</summary>
    public static void ScrollIntoView(AutomationElement element)
    {
        try
        {
            if (!element.Current.IsOffscreen) return;
            if (element.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var p)) ((ScrollItemPattern)p).ScrollIntoView();
            Thread.Sleep(120);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or TimeoutException or InvalidOperationException or System.Runtime.InteropServices.COMException) { }
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
    private static FieldKind? LoginNear(FormField user, FormField password)
    {
        var kind = FieldClassifier.Classify(false, user.Name, user.AutomationId, user.HelpText);
        if (kind is FieldKind.Login or FieldKind.Email or FieldKind.Phone) return kind;
        if (kind != null || !string.IsNullOrWhiteSpace(user.Name)) return null;
        var u = user.Bounds;
        var p = password.Bounds;
        var below = p.Top - u.Bottom is >= -2 and < 160 && Math.Abs(p.Left - u.Left) < 60;
        var beside = Math.Abs(p.Top - u.Top) < 8 && p.Left - u.Right is >= -2 and < 80;
        return below || beside ? FieldKind.Login : null;
    }

    private static LoginField? Describe(FormField field, IntPtr window, FieldKind kind, FormFields form) =>
        field.Usable ? new LoginField { Element = field.Element, Kind = kind, Bounds = field.Bounds, Window = window, Form = form } : null;

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
            ScrollIntoView(field);
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

    /// <summary>The form of the element: given (read for its field a moment ago) or read now.</summary>
    private static FormFields FormAround(AutomationElement element, FormFields? form)
    {
        if (form != null) return form;
        var container = Container(element);
        return container == null ? new FormFields([]) : ReadForm(container);
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

    public static AutomationElement? FindUsernameBefore(AutomationElement password, FormFields? form = null)
    {
        try
        {
            var fields = FormAround(password, form);
            var index = fields.IndexOf(password);
            if (index < 0 && form != null) return FindUsernameBefore(password); // the form changed: read it again
            return index < 0 ? null : Before(fields, index)?.Element;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or TimeoutException or InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        return null;
    }

    /// <summary>
    /// The password field among the three fields after a login. A form read before may not have it yet (some sites
    /// show the password field once the login is typed): it is read again then.
    /// </summary>
    public static AutomationElement? FindPasswordAfter(AutomationElement login, FormFields? form = null)
    {
        try
        {
            var fields = FormAround(login, form);
            var index = fields.IndexOf(login);
            for (var i = index + 1; index >= 0 && i < fields.Fields.Count && i <= index + 3; i++)
                if (fields.Fields[i].Usable && fields.Fields[i].IsPassword) return fields.Fields[i].Element;
            if (form != null) return FindPasswordAfter(login);
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

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<uint, bool> FocusIgnored = new();

    /// <summary>
    /// Moves the cursor to the field through UI Automation, unless the browser is known to ignore that (Chrome does
    /// for its pages): asking it again would cost a wait of a third of a second every time, before Tab or a click.
    /// </summary>
    public static bool TryFocus(AutomationElement element, IntPtr window)
    {
        Native.GetWindowThreadProcessId(window, out var pid);
        if (pid != 0 && FocusIgnored.ContainsKey(pid)) return false;
        if (FocusAndVerify(element)) return true;
        if (pid != 0 && IsBrowser(pid)) FocusIgnored[pid] = true;
        return false;
    }

    private static bool IsBrowser(uint pid)
    {
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            return TargetDetector.IsBrowserProcess(p.ProcessName);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Moves the cursor to the field and confirms it arrived there.</summary>
    public static bool FocusAndVerify(AutomationElement element)
    {
        try
        {
            element.SetFocus();
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or TimeoutException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return false;
        }
        // Checked often: a program that takes the focus does so at once, one that ignores it costs only this wait.
        for (var i = 0; i < 6; i++)
        {
            Thread.Sleep(30);
            if (IsFocused(element)) return true;
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
