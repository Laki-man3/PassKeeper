using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using PassKeeper.Core.Storage;
using PassKeeper.Desktop.Platform;

namespace PassKeeper.Desktop;

public sealed class SettingsWindow : DialogWindow
{
    private static readonly int[] LockMinutes = [0, 1, 5, 15, 30, 60, 480];
    private static readonly int[] ClipboardSeconds = [0, 10, 30, 60];

    public SettingsWindow(MainWindow main, VaultService vault) : base(Loc.T("Settings.Title"), 520)
    {
        var settings = App.Instance.Settings;
        var language = Choice(["", "ru", "en"], [Loc.T("Theme.System"), "Русский", "English"], settings.Language);
        var theme = Choice(["system", "light", "dark"], [Loc.T("Theme.System"), Loc.T("Theme.Light"), Loc.T("Theme.Dark")], settings.Theme);
        var autoLock = Choice(LockMinutes.Select(m => m.ToString()).ToArray(),
            LockMinutes.Select(m => m == 0 ? Loc.T("Settings.Never") : m >= 60 ? Loc.F("Settings.Hours", m / 60) : Loc.F("Settings.Minutes", m)).ToArray(),
            settings.AutoLockMinutes.ToString());
        var clipboard = Choice(ClipboardSeconds.Select(s => s.ToString()).ToArray(),
            ClipboardSeconds.Select(s => s == 0 ? Loc.T("Settings.Never") : Loc.F("Settings.SecondsN", s)).ToArray(),
            settings.ClipboardSeconds.ToString());
        var tray = new CheckBox { Content = Loc.T("Settings.Tray"), IsChecked = settings.CloseToTray, Margin = new Thickness(0, 12, 0, 0) };
        AutomationProperties.SetAutomationId(tray, "CloseToTray");

        // Auto-type: on/off and the hotkey, typed by pressing it in the box.
        var autoType = new CheckBox { Content = Loc.T("Settings.AutoType"), IsChecked = settings.AutoTypeEnabled };
        AutomationProperties.SetAutomationId(autoType, "AutoTypeEnabled");
        var hotkey = Hotkey.Parse(settings.AutoTypeHotkey) ?? Hotkey.Default;
        var hotkeyBox = new TextBox { Text = hotkey.Display, IsReadOnly = true, Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(hotkeyBox, "Hotkey");
        var autoTypeState = Ui.Muted("");
        autoTypeState.Margin = new Thickness(0, 6, 0, 0);
        var allow = Ui.Button(Loc.T("Settings.AllowAccess"), () => { });
        void UpdateState()
        {
            var problem = DesktopPlatform.Current.AutoTypeProblem();
            var text = problem ?? main.AutoType.HotkeyError ?? Loc.F("Settings.AutoTypeOk", hotkey.Display);
            if (problem == null && LinuxPlatform.Limitation is { } limitation && OperatingSystem.IsLinux()) text += " " + limitation;
            autoTypeState.Text = text;
            allow.IsVisible = problem != null && OperatingSystem.IsMacOS();
        }
        allow.Click += (_, _) =>
        {
            DesktopPlatform.Current.RequestAutoTypePermission();
            UpdateState();
        };
        hotkeyBox.KeyDown += (_, e) =>
        {
            e.Handled = true;
            if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.Tab) return;
            var pressed = new Hotkey(e.KeyModifiers, e.Key);
            if (!pressed.IsUsable)
            {
                autoTypeState.Text = Loc.T("Settings.HotkeyBad");
                return;
            }
            hotkey = pressed;
            hotkeyBox.Text = hotkey.Display;
            autoTypeState.Text = Loc.F("Settings.AutoTypeOk", hotkey.Display);
        };
        UpdateState();

        // PIN: set, change or remove.
        var pinState = Ui.Muted("");
        var setPin = Ui.Button("", () => { });
        var removePin = Ui.Button(Loc.T("Settings.PinRemove"), () => { });
        AutomationProperties.SetAutomationId(setPin, "SetPin");
        AutomationProperties.SetAutomationId(removePin, "RemovePin");
        void UpdatePin()
        {
            pinState.Text = Loc.T(vault.HasPin ? "Settings.PinOn" : "Settings.PinOff");
            setPin.Content = Loc.T(vault.HasPin ? "Settings.PinChange" : "Settings.PinSet");
            removePin.IsVisible = vault.HasPin;
        }
        setPin.Click += async (_, _) =>
        {
            await PinWindow.SetAsync(this, vault);
            UpdatePin();
        };
        removePin.Click += (_, _) =>
        {
            vault.RemovePin();
            UpdatePin();
        };
        UpdatePin();

        var changeMaster = Ui.Button(Loc.T("Settings.ChangeMaster"), async () => await new ChangeMasterWindow(vault).ShowDialog(this));

        var save = Ui.Button(Loc.T("Common.Save"), () =>
        {
            var languageChanged = settings.Language != (string)language.Tag!;
            settings.Language = (string)language.Tag!;
            settings.Theme = (string)theme.Tag!;
            settings.AutoLockMinutes = int.Parse((string)autoLock.Tag!);
            settings.ClipboardSeconds = int.Parse((string)clipboard.Tag!);
            settings.CloseToTray = tray.IsChecked == true;
            settings.AutoTypeEnabled = autoType.IsChecked == true;
            settings.AutoTypeHotkey = hotkey.ToString();
            settings.Save();
            App.Instance.ApplyTheme();
            main.AutoType.ApplySettings();
            if (languageChanged)
            {
                Loc.Use(settings.Language);
                main.Rebuild();
            }
            Close();
        }, accent: true);
        save.IsDefault = true;
        AutomationProperties.SetAutomationId(save, "SaveSettings");

        SetBody(Ui.Column(2,
                Ui.Label(Loc.T("Settings.Language")), language,
                Ui.Label(Loc.T("Settings.Theme")), theme,
                Ui.Label(Loc.T("Settings.AutoLock")), autoLock,
                Ui.Label(Loc.T("Settings.Clipboard")), clipboard,
                tray,
                Section(Loc.T("Settings.AutoType")),
                autoType,
                Ui.Label(Loc.T("Settings.Hotkey")), hotkeyBox,
                autoTypeState, allow,
                Section(Loc.T("Settings.Pin")),
                pinState,
                Ui.Row(setPin, removePin),
                Section(Loc.T("Unlock.Master")),
                changeMaster,
                new Border { Height = 18 },
                Ui.Muted(Loc.F("Settings.Version", App.Version, App.SystemName)),
                Ui.Muted(Loc.F("Settings.DataFolder", App.Instance.DataDirectory)),
                Ui.Muted(Loc.T("Settings.Preview"))),
            Ui.Button(Loc.T("Common.Cancel"), () => Close()), save);
    }

    private static TextBlock Section(string text) =>
        new() { Text = text, FontWeight = FontWeight.SemiBold, FontSize = 15, Margin = new Thickness(0, 18, 0, 4) };

    /// <summary>A drop-down whose <c>Tag</c> holds the value of the chosen item.</summary>
    private static ComboBox Choice(string[] values, string[] texts, string current)
    {
        var box = new ComboBox { ItemsSource = texts, HorizontalAlignment = HorizontalAlignment.Stretch };
        var index = Array.IndexOf(values, current);
        box.SelectedIndex = index >= 0 ? index : 0;
        box.Tag = values[box.SelectedIndex];
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex >= 0) box.Tag = values[box.SelectedIndex];
        };
        return box;
    }
}

public sealed class ChangeMasterWindow : DialogWindow
{
    public ChangeMasterWindow(VaultService vault) : base(Loc.T("Change.Title"), 440)
    {
        var current = Ui.Secret(id: "CurrentMaster");
        var fresh = Ui.Secret(id: "FreshMaster");
        var repeat = Ui.Secret(id: "RepeatFresh");
        var error = Ui.ErrorText();
        var save = Ui.Button(Loc.T("Common.Save"), () => { }, accent: true);
        save.IsDefault = true;
        save.Click += async (_, _) =>
        {
            var password = fresh.Text ?? "";
            var problem = password.Length < VaultService.MinMasterPasswordLength ? Loc.F("Create.ErrShort", VaultService.MinMasterPasswordLength)
                : password != (repeat.Text ?? "") ? Loc.T("Create.ErrMismatch") : "";
            Ui.ShowError(error, problem);
            if (problem.Length > 0) return;
            save.IsEnabled = false;
            var changed = await Task.Run(() => vault.ChangeMaster(current.Text ?? "", password));
            save.IsEnabled = true;
            if (!changed)
            {
                Ui.ShowError(error, Loc.T("Unlock.Wrong"));
                current.Text = "";
                return;
            }
            Close();
            if (Owner is Window owner) await Dialogs.Message(owner, Loc.T("Change.Done"));
        };
        SetBody(Ui.Column(0,
                Ui.Label(Loc.T("Change.Current")), current,
                Ui.Label(Loc.T("Change.New")), fresh,
                Ui.Label(Loc.T("Change.Repeat")), repeat,
                error),
            Ui.Button(Loc.T("Common.Cancel"), () => Close()), save);
        Opened += (_, _) => current.Focus();
    }
}
