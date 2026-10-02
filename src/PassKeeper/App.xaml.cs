using PassKeeper.Core.AutoType;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using PassKeeper.Core.Matching;
using PassKeeper.Core.Storage;
using PassKeeper.Localization;
using PassKeeper.Services;
using PassKeeper.Views;
using PassKeeper.Windows;

namespace PassKeeper;

public partial class App : Application
{
    private readonly StartupOptions _options;
    private readonly SingleInstance? _instance;
    private IdleMonitor? _idle;
    private FocusWatcher? _watcher;
    private AutoLoginService? _autoLogin;
    private ForegroundWatcher? _foreground;
    private BrowserPageWatcher? _pages;
    private readonly Dictionary<string, DateTime> _pageScans = [];
    private SuggestionPopup? _popup;

    public App(StartupOptions options, SingleInstance? instance)
    {
        _options = options;
        _instance = instance;
    }

    public static App Instance => (App)Current;

    public string DataDirectory { get; private set; } = "";
    public VaultService Vault { get; private set; } = null!;
    /// <summary>Local users of this data folder (each with its own vault, PIN and backups).</summary>
    public ProfileStore Profiles { get; private set; } = null!;
    /// <summary>A user is signed in (possibly locked behind the PIN); false after "sign out".</summary>
    public bool HasProfile => !string.IsNullOrEmpty(Settings.ActiveProfile) && Vault.Exists;
    private string _profileId = "";
    public AppSettings Settings { get; private set; } = new();
    public MainWindow Main { get; private set; } = null!;
    public TrayService? Tray { get; private set; }
    public HotkeyService? Hotkeys { get; private set; }
    public AutoTypeService AutoType { get; private set; } = null!;
    public bool IsExiting { get; private set; }
    /// <summary>Set by the screenshot renderer, which drives navigation itself.</summary>
    internal bool SuppressAutoNavigation { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        DataDirectory = AppPaths.ResolveDataDirectory(_options.DataDirectory);
        if (_options.ScreenshotsDirectory != null)
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "PassKeeper-screens-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
        }
        Settings = AppSettings.Load(DataDirectory);
        Loc.I.Language = !string.IsNullOrEmpty(Settings.Language) ? Settings.Language : AppPaths.InstallerLanguage() ?? Loc.DefaultLanguage();
        ThemeService.Initialize();
        ThemeService.Apply(Settings.Theme);

        Profiles = new ProfileStore(DataDirectory);
        if (Profiles.MigrateLegacy() is { } migrated)
        {
            Settings.ActiveProfile = migrated;
            Settings.Save();
        }
        UseProfile(Profiles.Find(Settings.ActiveProfile)?.Id ?? Profiles.NewId());
        AutoType = new AutoTypeService(this);
        Main = new MainWindow
        {
            Width = Math.Max(Settings.WindowWidth, 920),
            Height = Math.Max(Settings.WindowHeight, 580),
        };

        if (_options.ScreenshotsDirectory != null)
        {
            DevScreens.Run(_options.ScreenshotsDirectory, _options.ScreenshotsLanguage);
            return;
        }

        Tray = new TrayService();
        Tray.OpenRequested += ShowMainWindow;
        Tray.LockRequested += Lock;
        Tray.ExitRequested += Quit;
        Tray.GeneratorRequested += async () =>
        {
            ShowMainWindow();
            if (Vault.IsUnlocked && !Main.IsDialogOpen) await Main.ShowDialogAsync(new GeneratorDialog(pickMode: false));
        };

        Hotkeys = new HotkeyService();
        Hotkeys.Pressed += () => AutoType.OnHotkey();
        var hotkeyOk = ApplyHotkey(Settings.AutoTypeHotkey);

        _idle = new IdleMonitor { Period = TimeSpan.FromSeconds(Settings.AutoLockSeconds) };
        _idle.Timeout += OnIdleTimeout;

        _watcher = new FocusWatcher();
        _watcher.LoginFieldFocused += OnLoginFieldFocused;
        _watcher.PageShown += OnPageShown;
        _watcher.FocusLeft += () => Dispatcher.BeginInvoke(() =>
        {
            if (_popup is { IsMouseOver: false }) _popup.HidePopup();
        });
        if (Settings.SmartSuggestions) _watcher.Start();

        // Switching to another window takes the suggestion card away, even with the mouse over it.
        _foreground = new ForegroundWatcher();
        _foreground.Changed += hwnd =>
        {
            if (_popup is { IsVisible: true } && !_popup.IsFor(hwnd)) _popup.HidePopup();
        };
        _pages = new BrowserPageWatcher(_foreground);
        _pages.PageChanged += () =>
        {
            if (Settings.SmartSuggestions && Settings.AutoFillWeb) _watcher?.ProbePage();
        };
        _autoLogin = new AutoLoginService(this, _foreground);
        if (Settings.AutoLogin) _autoLogin.Start();

        SystemEvents.SessionSwitch += OnSessionSwitch;
        _instance?.Listen(cmd => Dispatcher.BeginInvoke(() => OnRemoteCommand(cmd)));
        Loc.I.LanguageChanged += (_, _) => UpdateTray();

        AutostartService.RefreshPath();
        CleanupAfterUpdate();
        ShowStartPage();
        UpdateTray();
        Main.IsVisibleChanged += (_, _) =>
        {
            if (!Main.IsVisible) MemoryTrimmer.TrimSoon();
        };
        Main.StateChanged += (_, _) =>
        {
            if (Main.WindowState == WindowState.Minimized) MemoryTrimmer.TrimSoon();
        };
        if (!(_options.Minimized && HasProfile)) ShowMainWindow();
        else MemoryTrimmer.TrimSoon();
        if (!hotkeyOk)
            Tray.ShowBalloon("PassKeeper", Loc.F("Tray.HotkeyFailed", Settings.AutoTypeHotkey));
    }

    // ------------------------------------------------------------------ navigation / state

    // ------------------------------------------------------------------ users

    /// <summary>Switches to the vault of a user (an existing one or one being registered).</summary>
    private void UseProfile(string id)
    {
        if (Vault != null)
        {
            Vault.StateChanged -= OnVaultStateEvent;
            Vault.DataChanged -= OnVaultDataEvent;
        }
        _profileId = id;
        Vault = new VaultService(Profiles.DirectoryOf(id));
        Vault.StateChanged += OnVaultStateEvent;
        Vault.DataChanged += OnVaultDataEvent;
    }

    private void OnVaultStateEvent(object? sender, EventArgs e) => Dispatcher.BeginInvoke(OnVaultStateChanged);

    private void OnVaultDataEvent(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => _autoLogin?.UpdateTriggers());

    /// <summary>Registration screen: the vault it creates is a new user.</summary>
    public void PrepareNewProfile()
    {
        if (Vault.Exists) UseProfile(Profiles.NewId());
    }

    public enum LoginResult { Ok, UnknownUser, WrongPassword }

    /// <summary>Sign in with user name and master password (after "sign out" or for another user).</summary>
    public async Task<LoginResult> LoginAsync(string userName, string masterPassword)
    {
        var profile = Profiles.FindByName(userName);
        if (profile == null) return LoginResult.UnknownUser;
        UseProfile(profile.Id);
        var vault = Vault;
        // Success raises StateChanged: the user becomes the signed-in one and is asked for a new PIN.
        return await Task.Run(() => vault.UnlockWithMaster(masterPassword)) ? LoginResult.Ok : LoginResult.WrongPassword;
    }

    /// <summary>
    /// Sign out: the PIN of this user is deleted and the vault locked; signing in again takes the user name and the
    /// master password, then a new PIN.
    /// </summary>
    public void Logout()
    {
        var vault = Vault;
        Settings.ActiveProfile = "";
        Settings.AutoLoginTriggers = [];
        Settings.Save();
        UseProfile(_profileId); // detach the events of the signed-out vault
        vault.RemovePin();
        if (vault.IsUnlocked) vault.Lock();
        EndSession();
        ShowStartPage();
        UpdateTray();
    }

    /// <summary>Deletes the current user with the vault, PIN and backups.</summary>
    public void DeleteCurrentProfile()
    {
        var id = _profileId;
        var vault = Vault;
        Settings.ActiveProfile = "";
        Settings.AutoLoginTriggers = [];
        Settings.Save();
        UseProfile(Profiles.NewId());
        if (vault.IsUnlocked) vault.Lock();
        EndSession();
        Profiles.Delete(id);
        ShowStartPage();
        UpdateTray();
    }

    /// <summary>What locking does besides the vault itself: clipboard, suggestion, dialogs, inactivity timer, memory.</summary>
    private void EndSession()
    {
        ClipboardService.ClearIfOurs();
        _popup?.HidePopup();
        Main.CloseAllDialogs();
        _idle?.Stop();
        MemoryTrimmer.TrimSoon();
    }

    public void ShowStartPage()
    {
        if (!HasProfile)
        {
            if (Main.CurrentPage is LoginView or SetupView) return;
            if (Profiles.List().Count > 0) Main.Navigate(new LoginView());
            else Main.Navigate(new SetupView());
        }
        else if (!Vault.IsUnlocked)
        {
            if (Main.CurrentPage is not UnlockView) Main.Navigate(new UnlockView());
        }
        else if (!Vault.HasPin)
        {
            if (Main.CurrentPage is not PinSetupView) Main.Navigate(new PinSetupView(mandatory: true));
        }
        else if (Main.CurrentPage is not VaultView)
        {
            Main.Navigate(new VaultView());
        }
    }

    private void OnVaultStateChanged()
    {
        if (SuppressAutoNavigation) return;
        if (!Vault.IsUnlocked)
        {
            EndSession();
        }
        else
        {
            if (Settings.ActiveProfile != _profileId)
            {
                Settings.ActiveProfile = _profileId; // registered or signed in
                Settings.Save();
            }
            _idle?.Start();
            _autoLogin?.UpdateTriggers();
        }
        ShowStartPage();
        UpdateTray();
    }

    public void Lock()
    {
        if (Vault.IsUnlocked) Vault.Lock();
    }

    private void OnIdleTimeout()
    {
        if (Vault.IsUnlocked) Lock();
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock && Settings.LockOnWindowsLock)
            Dispatcher.BeginInvoke(Lock);
    }

    private void OnRemoteCommand(string command)
    {
        switch (command)
        {
            case "SHOW": ShowMainWindow(); break;
            case "LOCK": Lock(); break;
            case "EXIT": Quit(); break;
        }
    }

    public void ShowMainWindow()
    {
        if (IsExiting) return;
        if (!Main.IsVisible) Main.Show();
        if (Main.WindowState == WindowState.Minimized) Main.WindowState = WindowState.Normal;
        Main.Activate();
        Native.ForceForeground(new WindowInteropHelper(Main).Handle);
    }

    public void OnMainWindowClosing()
    {
        SaveWindowSize();
        if (Settings.MinimizeToTray)
        {
            Main.Hide();
            if (!Settings.TrayHintShown)
            {
                Settings.TrayHintShown = true;
                Settings.Save();
                Tray?.ShowBalloon("PassKeeper", Loc.F("Tray.StillRunning", Settings.AutoTypeHotkey));
            }
        }
        else Quit();
    }

    private void SaveWindowSize()
    {
        if (Main.WindowState != WindowState.Normal) return;
        Settings.WindowWidth = Main.Width;
        Settings.WindowHeight = Main.Height;
        Settings.Save();
    }

    public void Quit()
    {
        if (IsExiting) return;
        IsExiting = true;
        try
        {
            SaveWindowSize();
            _watcher?.Dispose();
            _foreground?.Dispose();
            Hotkeys?.Dispose();
            _idle?.Dispose();
            Tray?.Dispose();
            ClipboardService.ClearIfOurs();
            Vault.Lock();
            SystemEvents.SessionSwitch -= OnSessionSwitch;
        }
        finally
        {
            _instance?.Dispose();
            Main.Close();
            Shutdown();
        }
    }

    // ------------------------------------------------------------------ settings

    public bool ApplyHotkey(string gesture)
    {
        if (Hotkeys == null) return false;
        var ok = Hotkeys.Register(gesture);
        UpdateTray();
        return ok;
    }

    public void OnSettingsChanged()
    {
        if (_idle != null) _idle.Period = TimeSpan.FromSeconds(Settings.AutoLockSeconds);
        if (Settings.SmartSuggestions) _watcher?.Start();
        else
        {
            _watcher?.Stop();
            _popup?.HidePopup();
        }
        if (Settings.AutoLogin) _autoLogin?.Start();
        else _autoLogin?.Stop();
        UpdateTray();
    }

    private void UpdateTray() => Tray?.UpdateTexts(Vault?.IsUnlocked == true, Settings.AutoTypeHotkey);

    // ------------------------------------------------------------------ autofill suggestions

    private void OnLoginFieldFocused(LoginField field)
    {
        if (!Settings.SmartSuggestions || IsExiting) return;
        var target = TargetDetector.Capture(field.Window, detectUrl: true, focused: field.Element);
        var hasValue = !string.IsNullOrEmpty(FieldFinder.GetValue(field.Element));
        Dispatcher.BeginInvoke(async () =>
        {
            if (!HasProfile || IsExiting) return;
            if (_autoLogin != null && await _autoLogin.TryWindowAsync(field.Window, field))
            {
                _popup?.HidePopup();
                return;
            }
            _popup ??= CreatePopup();
            // PassKeeper itself is filling this form: the cursor moving on needs no card.
            if (AutoType.IsTyping || AutoType.IsAutoFilling(field.Window))
            {
                _popup.HidePopup();
                return;
            }
            if (!Vault.IsUnlocked)
            {
                if (field.Kind is FieldKind.Password or FieldKind.Otp or FieldKind.Pin) _popup.ShowLocked(field, target);
                return;
            }
            var all = EntryMatcher.Match(Vault.ActiveEntries, target.ToContext());
            var matches = all.Where(m => AutoTypeService.CanFill(m.Entry, field.Kind)).ToList();
            if (Settings.AutoFillWeb && EntryMatcher.Obvious(matches, target.IsBrowser) is { } entry &&
                await AutoType.TryAutoFillAsync(entry, field, target))
            {
                _popup.HidePopup();
                return;
            }
            // The form was filled a moment ago and the cursor went on to its next field, or the window is already gone
            // (a client closes its sign-in window after signing in).
            if (AutoType.JustFilled(field) && (hasValue || field.IsPassword) || !Native.IsWindow(field.Window) || target.ProcessName.Length == 0)
            {
                _popup.HidePopup();
                return;
            }
            if (matches.Count > 0)
            {
                // Several accounts: the user chooses (the one used last is on top).
                _popup.ShowEntries(field, target, matches);
                return;
            }
            if (all.Count == 0 && target.IsBrowser && field.IsPassword)
            {
                _popup.ShowChoose(field, target);
                return;
            }
            // A program without any entry: offer to create one when it is a known client or asks for a password / PIN.
            if (all.Count == 0 && !target.IsBrowser)
            {
                var known = KnownApps.Match(target.ProcessName, target.Title);
                if (known != null || field.Kind is FieldKind.Password or FieldKind.Pin)
                {
                    _popup.ShowCreate(field, target, known?.Name ?? target.Describe());
                    return;
                }
            }
            _popup.HidePopup();
        });
    }

    /// <summary>
    /// A browser page is in front and the cursor is in none of its fields. If the site has entries, its sign-in form is
    /// looked for: with one obvious entry the cursor is put into the login field and the form filled, with several the
    /// list is shown next to the login field. Pages of sites without entries are not examined.
    /// </summary>
    private void OnPageShown(BrowserPage page)
    {
        if (!Settings.SmartSuggestions || !Settings.AutoFillWeb || IsExiting) return;
        var target = TargetDetector.Capture(page.Window, detectUrl: true, focused: page.Document);
        if (DomainUtil.GetHost(target.Url) == null) return;
        Dispatcher.BeginInvoke(async () =>
        {
            if (!HasProfile || !Vault.IsUnlocked || IsExiting || AutoType.IsBusy) return;
            var matches = EntryMatcher.Match(Vault.ActiveEntries, target.ToContext())
                .Where(m => AutoTypeService.CanFill(m.Entry, FieldKind.Login))
                .ToList();
            if (matches.Count == 0) return;
            // A page already examined a moment ago (its title changes, the user comes back to it) is left as it is.
            var key = page.Window + "|" + target.Url;
            foreach (var old in _pageScans.Where(p => DateTime.UtcNow - p.Value > TimeSpan.FromSeconds(15)).Select(p => p.Key).ToList()) _pageScans.Remove(old);
            if (!_pageScans.TryAdd(key, DateTime.UtcNow)) return;
            LoginField? field = null;
            // The form may still be appearing: a few looks over two seconds.
            for (var attempt = 0; attempt < 3 && field == null; attempt++)
            {
                if (attempt > 0) await Task.Delay(700);
                if (_watcher?.IsCurrent(page.Sequence) != true) return;
                field = await Task.Run(() => FieldFinder.FindSignInField(page.Document, page.Window));
            }
            if (field == null || _watcher?.IsCurrent(page.Sequence) != true) return;
            _popup ??= CreatePopup();
            if (EntryMatcher.Obvious(matches, site: true) is { } entry &&
                await AutoType.TryAutoFillAsync(entry, field, target, putCursor: true))
            {
                _popup.HidePopup();
                return;
            }
            if (_watcher?.IsCurrent(page.Sequence) == true) _popup.ShowEntries(field, target, matches);
        });
    }

    private SuggestionPopup CreatePopup()
    {
        var popup = new SuggestionPopup();
        popup.EntryChosen += async (entry, field, target, exact) =>
        {
            // The card may belong to a page the user has left, or to a form found on the page with the cursor
            // elsewhere: nothing is typed unless the cursor is (or could be put) in that very field.
            if (!await Task.Run(() => AutoTypeService.PutCursorInto(field))) return;
            // An entry chosen for another address of its domain (single sign-on page) is filled there by itself next time.
            if (target is { IsBrowser: true } && !exact) entry = AutoType.Associate(entry, target);
            AutoType.MarkFilled(field.Window, target);
            await AutoType.FillFieldAsync(entry, field);
        };
        popup.UnlockRequested += async field =>
        {
            if (await QuickUnlockWindow.ShowAsync(null)) Native.ForceForeground(field.Window);
        };
        popup.ChooseRequested += async (field, target) =>
        {
            var (entry, remember) = await AutoTypePickerWindow.PickAsync(target, [], Vault.ActiveEntries);
            Native.ForceForeground(field.Window);
            if (entry == null) return;
            if (remember) entry = AutoType.Associate(entry, target);
            await Task.Delay(150);
            AutoType.MarkFilled(field.Window, target);
            await AutoType.FillFieldAsync(entry, field);
        };
        popup.CreateRequested += async field =>
        {
            var form = await Task.Run(() => ClientDetector.InspectForm(field.Window));
            CreateEntryForWindow(field.Window, form);
        };
        return popup;
    }

    /// <summary>
    /// Opens a new entry for the given sign-in window (client, patterns and found fields filled in); once it is saved,
    /// PassKeeper returns to the window and signs in.
    /// </summary>
    public void CreateEntryForWindow(IntPtr hwnd, SignInForm form)
    {
        var window = ClientDetector.Describe(hwnd);
        ShowMainWindow();
        Main.CloseAllDialogs();
        ShowStartPage(); // right after a quick unlock the vault page may not be shown yet
        if (Main.CurrentPage is not VaultView view) return;
        view.StartEntryForWindow(window, form, async saved =>
        {
            Main.WindowState = WindowState.Minimized;
            if (!await AutoType.FillWindowAsync(saved, hwnd, submit: true))
                Tray?.ShowBalloon("PassKeeper", Loc.T("AutoType.WindowGone"));
        });
    }

    // ------------------------------------------------------------------ helpers used by views

    public void CopySecret(string value, string message, Guid? entryId)
    {
        if (string.IsNullOrEmpty(value)) return;
        if (!ClipboardService.Copy(value, Settings.ClipboardClearSeconds))
        {
            Main.ShowToast(Loc.T("Toast.ClipboardBusy"), error: true);
            return;
        }
        Main.ShowToast(Settings.ClipboardClearSeconds > 0 ? message + " · " + Loc.F("Toast.WillClear", Settings.ClipboardClearSeconds) : message);
        if (entryId is { } id && Vault.IsUnlocked)
        {
            try { Vault.MarkUsed(id); } catch (IOException) { }
        }
    }

    public void CopyPlain(string value, string message)
    {
        if (string.IsNullOrEmpty(value)) return;
        if (ClipboardService.Copy(value, 0, secret: false)) Main.ShowToast(message);
        else Main.ShowToast(Loc.T("Toast.ClipboardBusy"), error: true);
    }

    public void OpenUrl(string url)
    {
        var normalized = DomainUtil.NormalizeUrlForOpen(url);
        if (normalized == null) return;
        try
        {
            Process.Start(new ProcessStartInfo(normalized) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Main.ShowToast(Loc.F("Common.ErrorFormat", ex.Message), error: true);
        }
    }

    /// <summary>Removes *.old files left by an in-place upgrade of a running installation.</summary>
    private static void CleanupAfterUpdate()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(AppPaths.ExeDirectory, "*.old", SearchOption.AllDirectories))
            {
                try { File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        catch (Exception) { }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        try
        {
            var log = Path.Combine(string.IsNullOrEmpty(DataDirectory) ? Path.GetTempPath() : DataDirectory, "error.log");
            File.AppendAllText(log, $"[{DateTime.Now:O}] {e.Exception}\n\n");
        }
        catch (Exception) { }
        if (Main is { IsLoaded: true }) Main.ShowToast(Loc.F("Common.ErrorFormat", e.Exception.Message), error: true);
    }
}
