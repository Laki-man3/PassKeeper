using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PassKeeper.Core.Storage;

namespace PassKeeper.Desktop;

/// <summary>
/// Sign-in screen or the vault; locks itself after the configured time without input. Lives on in the tray (menu bar
/// on macOS) when closed, if so set; the auto-type hotkey works from there.
/// </summary>
public sealed class MainWindow : Window
{
    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private VaultService? _vault;
    private DateTime _lastInput = DateTime.UtcNow;
    private TrayIcon? _tray;
    private bool _quitting;

    public MainWindow()
    {
        var settings = App.Instance.Settings;
        Title = "PassKeeper";
        Icon = Ui.Icon;
        Width = Math.Max(settings.WindowWidth, 760);
        Height = Math.Max(settings.WindowHeight, 520);
        MinWidth = 760;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        AddHandler(KeyDownEvent, (_, _) => _lastInput = DateTime.UtcNow, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, (_, _) => _lastInput = DateTime.UtcNow, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, (_, _) => _lastInput = DateTime.UtcNow, RoutingStrategies.Tunnel, handledEventsToo: true);
        _idleTimer.Tick += (_, _) => LockIfIdle();
        _idleTimer.Start();

        AutoType = new AutoTypeController(this);
        AutoType.ApplySettings();
        CreateTray();
        // macOS: a click on the Dock icon brings the window back.
        if (App.Instance.TryGetFeature<IActivatableLifetime>() is { } activatable)
            activatable.Activated += (_, e) =>
            {
                if (e.Kind == ActivationKind.Reopen) ShowFromTray();
            };

        Closing += (_, e) =>
        {
            settings.WindowWidth = Width;
            settings.WindowHeight = Height;
            settings.Save();
            if (!_quitting && settings.CloseToTray)
            {
                e.Cancel = true;
                foreach (var owned in OwnedWindows.ToList()) owned.Close();
                Hide();
                return;
            }
            AutoType.Dispose();
            _vault?.Lock();
            if (_tray != null) _tray.IsVisible = false;
            if (!_quitting) Dispatcher.UIThread.Post(Quit);
        };
        ShowSignIn();
    }

    public VaultService? Vault => _vault;

    public AutoTypeController AutoType { get; }

    public void ShowSignIn()
    {
        foreach (var owned in OwnedWindows.ToList()) owned.Close();
        _vault?.Lock();
        _vault = null;
        Content = new UnlockView(this);
        UpdateTray();
    }

    public void ShowVault(VaultService vault, bool offerPin = false)
    {
        _vault = vault;
        _lastInput = DateTime.UtcNow;
        Content = new VaultView(this, vault);
        UpdateTray();
        if (offerPin) Dispatcher.UIThread.Post(() => _ = PinWindow.OfferAsync(this, vault));
        else Dispatcher.UIThread.Post(() => _ = AutoType.ResumeAfterSignInAsync());
    }

    /// <summary>The interface again in the language just chosen.</summary>
    public void Rebuild()
    {
        if (_vault is { IsUnlocked: true } vault) Content = new VaultView(this, vault);
        else Content = new UnlockView(this);
        CreateTray();
    }

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    public void Quit()
    {
        _quitting = true;
        if (App.Instance.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
    }

    private void CreateTray()
    {
        var open = new NativeMenuItem(Loc.T("Tray.Open"));
        open.Click += (_, _) => ShowFromTray();
        var lockItem = new NativeMenuItem(Loc.T("Tray.Lock"));
        lockItem.Click += (_, _) => ShowSignIn();
        var quit = new NativeMenuItem(Loc.T("Tray.Quit"));
        quit.Click += (_, _) => Quit();
        var menu = new NativeMenu();
        menu.Items.Add(open);
        menu.Items.Add(lockItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quit);
        if (_tray == null)
        {
            _tray = new TrayIcon { Icon = Ui.Icon, ToolTipText = "PassKeeper" };
            _tray.Clicked += (_, _) => ShowFromTray();
            TrayIcon.SetIcons(App.Instance, [_tray]);
        }
        _tray.Menu = menu;
        UpdateTray();
    }

    private void UpdateTray()
    {
        if (_tray?.Menu?.Items.OfType<NativeMenuItem>().Skip(1).FirstOrDefault() is { } lockItem)
            lockItem.IsEnabled = _vault is { IsUnlocked: true };
    }

    private void LockIfIdle()
    {
        var minutes = App.Instance.Settings.AutoLockMinutes;
        if (_vault is not { IsUnlocked: true } || minutes <= 0) return;
        if (DateTime.UtcNow - _lastInput > TimeSpan.FromMinutes(minutes)) ShowSignIn();
    }
}
