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
    private SuggestionPopup? _popup;

    public App(StartupOptions options, SingleInstance? instance)
    {
        _options = options;
        _instance = instance;
    }

    public static App Instance => (App)Current;

    public string DataDirectory { get; private set; } = "";
    public VaultService Vault { get; private set; } = null!;
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
        if (_options.ScreenshotsDirectory != null || _options.SelfTestReport != null)
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "PassKeeper-screens-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
        }
        Settings = AppSettings.Load(DataDirectory);
        Loc.I.Language = !string.IsNullOrEmpty(Settings.Language) ? Settings.Language : AppPaths.InstallerLanguage() ?? Loc.DefaultLanguage();
        ThemeService.Initialize();
        ThemeService.Apply(Settings.Theme);

        Vault = new VaultService(DataDirectory);
        Vault.StateChanged += (_, _) => Dispatcher.BeginInvoke(OnVaultStateChanged);
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
        if (_options.SelfTestReport != null)
        {
            DevSelfTest.Run(_options.SelfTestReport, _options.SelfTestBrowser);
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
        _idle.Start();

        _watcher = new FocusWatcher();
        _watcher.LoginFieldFocused += OnLoginFieldFocused;
        _watcher.FocusLeft += () => Dispatcher.BeginInvoke(() =>
        {
            if (_popup is { IsMouseOver: false }) _popup.HidePopup();
        });
        if (Settings.SmartSuggestions) _watcher.Start();

        _autoLogin = new AutoLoginService(this);
        if (Settings.AutoLogin) _autoLogin.Start();
        Vault.DataChanged += (_, _) => Dispatcher.BeginInvoke(() => _autoLogin?.UpdateTriggers());

        SystemEvents.SessionSwitch += OnSessionSwitch;
        _instance?.Listen(cmd => Dispatcher.BeginInvoke(() => OnRemoteCommand(cmd)));
        Loc.I.LanguageChanged += (_, _) => UpdateTray();

        AutostartService.RefreshPath();
        CleanupAfterUpdate();
        ShowStartPage();
        UpdateTray();
        if (!(_options.Minimized && Vault.Exists)) ShowMainWindow();
        if (!hotkeyOk)
            Tray.ShowBalloon("PassKeeper", Loc.F("Tray.HotkeyFailed", Settings.AutoTypeHotkey));
    }

    // ------------------------------------------------------------------ navigation / state

    public void ShowStartPage()
    {
        if (!Vault.Exists)
        {
            if (Main.CurrentPage is not SetupView) Main.Navigate(new SetupView());
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
            ClipboardService.ClearIfOurs();
            _popup?.HidePopup();
            Main.CloseAllDialogs();
        }
        else
        {
            _idle?.Reset();
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
            _autoLogin?.Dispose();
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
        Dispatcher.BeginInvoke(async () =>
        {
            if (!Vault.Exists || IsExiting) return;
            if (_autoLogin != null && await _autoLogin.TryWindowAsync(field.Window, field))
            {
                _popup?.HidePopup();
                return;
            }
            _popup ??= CreatePopup();
            if (!Vault.IsUnlocked)
            {
                if (field.Kind is FieldKind.Password or FieldKind.Otp or FieldKind.Pin) _popup.ShowLocked(field, target);
                return;
            }
            var all = EntryMatcher.Match(Vault.ActiveEntries, target.ToContext());
            var matches = all.Select(m => m.Entry).Where(e => AutoTypeService.CanFill(e, field.Kind)).ToList();
            if (matches.Count > 0)
            {
                _popup.ShowEntries(field, target, matches);
                return;
            }
            // A program without any entry: offer to create one when it is a known client or asks for a password / PIN.
            if (all.Count == 0 && !target.IsBrowser)
            {
                var known = KnownApps.Match(target.ProcessName, target.Title);
                var form = known == null ? await Task.Run(() => ClientDetector.InspectForm(field.Window)) : SignInForm.None;
                if (known != null || form.HasPassword || form.HasPin)
                {
                    _popup.ShowCreate(field, target, known?.Name ?? target.Describe());
                    return;
                }
            }
            _popup.HidePopup();
        });
    }

    private SuggestionPopup CreatePopup()
    {
        var popup = new SuggestionPopup();
        popup.EntryChosen += async (entry, field) => await AutoType.FillFieldAsync(entry, field);
        popup.UnlockRequested += async field =>
        {
            if (await QuickUnlockWindow.ShowAsync(null)) Native.ForceForeground(field.Window);
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
