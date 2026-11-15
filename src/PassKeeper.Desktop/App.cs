using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using PassKeeper.Core.Storage;
using PassKeeper.Desktop.Platform;

namespace PassKeeper.Desktop;

public sealed class App : Application
{
    public static App Instance => (App)Current!;

    public string DataDirectory { get; private set; } = "";
    public DesktopSettings Settings { get; private set; } = new();
    public ProfileStore Profiles { get; private set; } = null!;

    public override void Initialize()
    {
        Name = "PassKeeper";
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        DataDirectory = AppPaths.DataDirectory();
        AppPaths.Prepare(DataDirectory);
        // The PIN file is sealed with a key the system keeps (Keychain, Secret Service, Credential Manager).
        PinSealing.Current = DesktopPlatform.Current.Keyring is { } keyring
            ? new KeyringPinSealer(keyring)
            : new NoPinSealer(Loc.T("Pin.NoSecretService"));
        Settings = DesktopSettings.Load(DataDirectory);
        Loc.Use(Settings.Language);
        ApplyTheme();
        Profiles = new ProfileStore(DataDirectory);
        // A vault folder copied from an old Windows installation (vault.pkv directly in the data folder) becomes a user.
        if (Profiles.MigrateLegacy() is { } migrated)
        {
            Settings.ActiveProfile = migrated;
            Settings.Save();
        }
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The application ends by Quit (tray menu, or closing the window when it does not stay in the tray).
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var window = new MainWindow();
            if (!(Program.StartMinimized && Settings.CloseToTray)) desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }

    public void ApplyTheme() =>
        RequestedThemeVariant = Settings.Theme switch
        {
            "light" => ThemeVariant.Light,
            "dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };

    public static string Version
    {
        get
        {
            var version = typeof(App).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "";
            var plus = version.IndexOf('+');
            return plus >= 0 ? version[..plus] : version;
        }
    }

    public static string SystemName => OperatingSystem.IsMacOS() ? "macOS" : OperatingSystem.IsLinux() ? "Linux" : "Windows";
}
