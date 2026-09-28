using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PassKeeper.Localization;
using PassKeeper.Services;

namespace PassKeeper.Views;

public partial class SettingsDialog : DialogBase
{
    private bool _loading = true;
    private string _hotkeyBefore = "";

    private static AppSettings Settings => App.Instance.Settings;

    public SettingsDialog()
    {
        InitializeComponent();
        DialogWidth = 640;
        Load();
        Loc.I.LanguageChanged += OnLanguageChanged;
        Unloaded += (_, _) => Loc.I.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        _loading = true;
        FillCombos();
        UpdateAbout();
        _loading = false;
    }

    private void Load()
    {
        (Loc.I.IsRussian ? LangRu : LangEn).IsChecked = true;
        (Settings.Theme switch { "light" => ThemeLight, "system" => ThemeSystem, _ => ThemeDark }).IsChecked = true;

        var machine = AutostartService.IsEnabledForMachine();
        Autostart.IsChecked = machine || AutostartService.IsEnabledForUser();
        Autostart.IsEnabled = !machine;
        if (machine) AutostartNote.Text = Loc.T("Settings.AutostartMachine");

        MinimizeToTray.IsChecked = Settings.MinimizeToTray;
        LockOnWindowsLock.IsChecked = Settings.LockOnWindowsLock;
        Hotkey.Text = Settings.AutoTypeHotkey;
        Suggestions.IsChecked = Settings.SmartSuggestions;
        Submit.IsChecked = Settings.SubmitAfterFill;
        Compatible.IsChecked = Settings.CompatibleTyping;
        DataPath.Text = App.Instance.DataDirectory;
        AutoLock.Minimum = TimeSpan.FromSeconds(AppSettings.MinAutoLockSeconds);
        AutoLock.Value = TimeSpan.FromSeconds(Settings.AutoLockSeconds);
        UninstallButton.Visibility = !AppPaths.IsPortable && (AppPaths.IsUserInstall || AppPaths.IsMachineInstall) &&
                                     File.Exists(Path.Combine(AppPaths.ExeDirectory, Shared.InstallLayout.UninstallerName))
            ? Visibility.Visible : Visibility.Collapsed;
        FillCombos();
        UpdateAbout();
        _loading = false;
    }

    private void FillCombos()
    {
        ClipboardClear.Items.Clear();
        foreach (var s in AppSettings.ClipboardChoices)
            ClipboardClear.Items.Add(new ComboBoxItem { Content = s == 0 ? Loc.T("Settings.Never") : Loc.F("Settings.Seconds", s), Tag = s });
        ClipboardClear.SelectedItem = ClipboardClear.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == Settings.ClipboardClearSeconds)
                                      ?? ClipboardClear.Items[2];

        KeyDelay.Items.Clear();
        foreach (var ms in new[] { 0, 8, 20, 50, 100 })
            KeyDelay.Items.Add(new ComboBoxItem { Content = Loc.F("Settings.Milliseconds", ms), Tag = ms });
        KeyDelay.SelectedItem = KeyDelay.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == Settings.KeystrokeDelayMs) ?? KeyDelay.Items[1];
    }

    private void UpdateAbout()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        Version.Text = $"PassKeeper {version?.ToString(3)}";
        InstallMode.Text = AppPaths.IsPortable ? Loc.T("Settings.ModePortable")
            : AppPaths.IsMachineInstall ? Loc.T("Settings.ModeMachine")
            : AppPaths.IsUserInstall ? Loc.T("Settings.ModeUser")
            : Loc.T("Settings.ModeStandalone");
    }

    private void Lang_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var lang = sender == LangEn ? "en" : "ru";
        Settings.Language = lang;
        Settings.Save();
        Loc.I.Language = lang;
        App.Instance.OnSettingsChanged();
    }

    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Settings.Theme = sender == ThemeLight ? "light" : sender == ThemeSystem ? "system" : "dark";
        Settings.Save();
        ThemeService.Apply(Settings.Theme);
    }

    private void Autostart_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AutostartService.SetForUser(Autostart.IsChecked == true);
        }
        catch (Exception ex)
        {
            Autostart.IsChecked = AutostartService.IsEnabledForUser();
            App.Instance.Main.ShowToast(Loc.F("Common.ErrorFormat", ex.Message), error: true);
        }
    }

    private void Simple_Changed(object sender, EventArgs e)
    {
        if (_loading) return;
        Settings.MinimizeToTray = MinimizeToTray.IsChecked == true;
        Settings.LockOnWindowsLock = LockOnWindowsLock.IsChecked == true;
        Settings.SmartSuggestions = Suggestions.IsChecked == true;
        Settings.SubmitAfterFill = Submit.IsChecked == true;
        Settings.CompatibleTyping = Compatible.IsChecked == true;
        Settings.AutoLockSeconds = (int)AutoLock.EffectiveValue.TotalSeconds;
        if (ClipboardClear.SelectedItem is ComboBoxItem { Tag: int seconds }) Settings.ClipboardClearSeconds = seconds;
        if (KeyDelay.SelectedItem is ComboBoxItem { Tag: int delay }) Settings.KeystrokeDelayMs = delay;
        Settings.Save();
        App.Instance.OnSettingsChanged();
    }

    // ---- hotkey capture ----------------------------------------------------------------------------------------

    private void Hotkey_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _hotkeyBefore = Hotkey.Text;
        App.Instance.Hotkeys?.Unregister();
        Hotkey.Text = Loc.T("Settings.HotkeyPress");
    }

    private void Hotkey_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (Hotkey.Text == Loc.T("Settings.HotkeyPress")) Hotkey.Text = _hotkeyBefore;
        var ok = App.Instance.ApplyHotkey(Hotkey.Text);
        HotkeyError.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Hotkey_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            e.Handled = true;
            return;
        }
        if (key == Key.Tab) return;
        e.Handled = true;
        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.None || mods == ModifierKeys.Shift) return; // need Ctrl/Alt/Win
        Hotkey.Text = HotkeyService.Format(mods, key);
        _hotkeyBefore = Hotkey.Text;
        Settings.AutoTypeHotkey = Hotkey.Text;
        Settings.Save();
        Keyboard.ClearFocus();
        Hotkey_LostFocus(sender, null!);
    }

    // ---- actions -----------------------------------------------------------------------------------------------

    private async void ChangeMaster_Click(object sender, RoutedEventArgs e) =>
        await App.Instance.Main.ShowDialogAsync(new ChangeMasterDialog());

    private async void ChangePin_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new DialogBase { DialogWidth = 460 };
        var view = new PinSetupView(mandatory: false) { Background = null, Margin = new Thickness(0, -10, 0, 24) };
        view.Done += () => dialog.Close(true);
        dialog.Content = view;
        await App.Instance.Main.ShowDialogAsync(dialog);
    }

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (Program.StartUninstaller(quiet: false) != 0)
            App.Instance.Main.ShowToast(Loc.F("Common.ErrorFormat", Shared.InstallLayout.UninstallerName), error: true);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{App.Instance.DataDirectory}\"") { UseShellExecute = true });

    private void Backup_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"PassKeeper-backup-{DateTime.Now:yyyy-MM-dd}.pkv",
            Filter = Loc.T("Settings.BackupFilter") + "|*.pkv",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            App.Instance.Vault.CreateBackup(dlg.FileName);
            App.Instance.Main.ShowToast(Loc.T("Settings.BackupDone"));
        }
        catch (Exception ex)
        {
            App.Instance.Main.ShowToast(Loc.F("Common.ErrorFormat", ex.Message), error: true);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close(null);
}

/// <summary>Change the master password (the vault key stays the same, PIN keeps working).</summary>
public sealed class ChangeMasterDialog : DialogBase
{
    private readonly Controls.SecretBox _current = new();
    private readonly Controls.SecretBox _new = new();
    private readonly Controls.SecretBox _confirm = new();
    private readonly TextBlock _error = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly Button _ok;

    public ChangeMasterDialog()
    {
        DialogWidth = 440;
        var root = new StackPanel { Margin = new Thickness(26, 24, 26, 22) };
        root.Children.Add(new TextBlock { Text = Loc.T("ChangeMaster.Title"), Style = (Style)FindResource("Text.H2"), FontSize = 18 });
        root.Children.Add(new TextBlock { Text = Loc.T("ChangeMaster.Text"), Style = (Style)FindResource("Text.Secondary"), Margin = new Thickness(0, 8, 0, 16) });
        _current.Placeholder = Loc.T("ChangeMaster.Current");
        _new.Placeholder = Loc.T("ChangeMaster.New");
        _confirm.Placeholder = Loc.T("ChangeMaster.Confirm");
        var meter = new Controls.StrengthMeter { Margin = new Thickness(0, 8, 0, 0) };
        _new.ValueChanged += (_, _) => meter.Password = _new.Value;
        _new.Margin = new Thickness(0, 12, 0, 0);
        _confirm.Margin = new Thickness(0, 12, 0, 0);
        root.Children.Add(_current);
        root.Children.Add(_new);
        root.Children.Add(meter);
        root.Children.Add(_confirm);
        _error.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger");
        root.Children.Add(_error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var cancel = new Button { Style = (Style)FindResource("Btn.Ghost"), Content = Loc.T("Common.Cancel") };
        cancel.Click += (_, _) => Close(null);
        _ok = new Button { Style = (Style)FindResource("Btn.Primary"), Content = Loc.T("ChangeMaster.Button"), Margin = new Thickness(8, 0, 0, 0) };
        _ok.Click += async (_, _) => await SubmitAsync();
        buttons.Children.Add(cancel);
        buttons.Children.Add(_ok);
        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) => _current.FocusInput();
    }

    private async Task SubmitAsync()
    {
        var current = _current.Value;
        var next = _new.Value;
        if (next.Length < Core.Storage.VaultService.MinMasterPasswordLength)
        {
            ShowError(Loc.F("Setup.ErrShort", Core.Storage.VaultService.MinMasterPasswordLength));
            return;
        }
        if (next != _confirm.Value)
        {
            ShowError(Loc.T("Setup.ErrMismatch"));
            return;
        }
        _ok.IsEnabled = false;
        var vault = App.Instance.Vault;
        var ok = await Task.Run(() => vault.ChangeMaster(current, next));
        _ok.IsEnabled = true;
        if (!ok)
        {
            ShowError(Loc.T("Unlock.WrongMaster"));
            return;
        }
        App.Instance.Main.ShowToast(Loc.T("ChangeMaster.Done"));
        Close(true);
    }

    private void ShowError(string text)
    {
        _error.Text = text;
        _error.Visibility = Visibility.Visible;
    }
}
