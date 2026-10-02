using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        BuildContacts();
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
        AutoLogin.IsChecked = Settings.AutoLogin;
        AutoFillWeb.IsChecked = Settings.AutoFillWeb;
        Submit.IsChecked = Settings.SubmitAfterFill;
        Compatible.IsChecked = Settings.CompatibleTyping;
        DataPath.Text = App.Instance.Vault.DataDirectory;
        ProfileName.Text = App.Instance.Vault.UserName;
        AutoLock.Minimum = TimeSpan.FromSeconds(AppSettings.MinAutoLockSeconds);
        AutoLock.Value = TimeSpan.FromSeconds(Settings.AutoLockSeconds);
        UninstallButton.Visibility = !AppPaths.IsPortable && (AppPaths.IsUserInstall || AppPaths.IsMachineInstall) &&
                                     File.Exists(Path.Combine(AppPaths.ExeDirectory, Shared.InstallLayout.UninstallerName))
            ? Visibility.Visible : Visibility.Collapsed;
        FillCombos();
        UpdateAbout();
        BuildContacts();
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

    /// <summary>The author's contacts: the row opens Telegram, the mail program or GitHub; the button copies the address.</summary>
    private void BuildContacts()
    {
        Contacts.Children.Clear();
        Contacts.Children.Add(Contact(TelegramIcon, "Telegram", "@PowerOfNSK", "https://t.me/PowerOfNSK"));
        Contacts.Children.Add(Contact(null, Loc.T("About.Mail"), "semend2005@mail.ru", "mailto:semend2005@mail.ru"));
        Contacts.Children.Add(Contact(GitHubIcon, "GitHub", "Laki-man3", "https://github.com/Laki-man3"));
    }

    // Brand marks (simple-icons, CC0), drawn in the accent colour like the other icons.
    private const string TelegramIcon = "M11.944 0A12 12 0 0 0 0 12a12 12 0 0 0 12 12 12 12 0 0 0 12-12A12 12 0 0 0 12 0a12 12 0 0 0-.056 0zm4.962 7.224c.1-.002.321.023.465.14a.506.506 0 0 1 .171.325c.016.093.036.306.02.472-.18 1.898-.962 6.502-1.36 8.627-.168.9-.499 1.201-.82 1.23-.696.065-1.225-.46-1.9-.902-1.056-.693-1.653-1.124-2.678-1.8-1.185-.78-.417-1.21.258-1.91.177-.184 3.247-2.977 3.307-3.23.007-.032.014-.15-.056-.212s-.174-.041-.249-.024c-.106.024-1.793 1.14-5.061 3.345-.48.33-.913.49-1.302.48-.428-.008-1.252-.241-1.865-.44-.752-.245-1.349-.374-1.297-.789.027-.216.325-.437.893-.663 3.498-1.524 5.83-2.529 6.998-3.014 3.332-1.386 4.025-1.627 4.476-1.635z";
    private const string GitHubIcon = "M12 .297c-6.63 0-12 5.373-12 12 0 5.303 3.438 9.8 8.205 11.385.6.113.82-.258.82-.577 0-.285-.01-1.04-.015-2.04-3.338.724-4.042-1.61-4.042-1.61C4.422 18.07 3.633 17.7 3.633 17.7c-1.087-.744.084-.729.084-.729 1.205.084 1.838 1.236 1.838 1.236 1.07 1.835 2.809 1.305 3.495.998.108-.776.417-1.305.76-1.605-2.665-.3-5.466-1.332-5.466-5.93 0-1.31.465-2.38 1.235-3.22-.135-.303-.54-1.523.105-3.176 0 0 1.005-.322 3.3 1.23.96-.267 1.98-.399 3-.405 1.02.006 2.04.138 3 .405 2.28-1.552 3.285-1.23 3.285-1.23.645 1.653.24 2.873.12 3.176.765.84 1.23 1.91 1.23 3.22 0 4.61-2.805 5.625-5.475 5.92.42.36.81 1.096.81 2.22 0 1.606-.015 2.896-.015 3.286 0 .315.21.69.825.57C20.565 22.092 24 17.592 24 12.297c0-6.627-5.373-12-12-12";

    private FrameworkElement Contact(string? brandPath, string title, string value, string link)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition());
        var badge = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(10) };
        badge.SetResourceReference(Border.BackgroundProperty, "Brush.AccentSoft");
        if (brandPath != null)
        {
            var mark = new System.Windows.Shapes.Path { Data = Geometry.Parse(brandPath), Stretch = Stretch.Uniform, Width = 17, Height = 17 };
            mark.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Brush.AccentText");
            badge.Child = mark;
        }
        else
        {
            var glyph = new TextBlock { Text = "\uE715", Style = (Style)FindResource("Icon"), FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            glyph.SetResourceReference(TextBlock.ForegroundProperty, "Brush.AccentText");
            badge.Child = glyph;
        }
        content.Children.Add(badge);
        var texts = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("Text.Muted"), FontSize = 11.5 });
        texts.Children.Add(new TextBlock { Text = value, FontWeight = FontWeights.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(texts, 1);
        content.Children.Add(texts);

        var open = new Button { Style = (Style)FindResource("Btn.Row"), Content = content, ToolTip = Loc.F("About.Open", title), Padding = new Thickness(8, 6, 8, 6) };
        open.Click += (_, _) => OpenContact(link);
        row.Children.Add(open);

        var copy = new Button { Style = (Style)FindResource("Btn.Icon"), Content = "\uE8C8", ToolTip = Loc.T("Common.Copy"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
        copy.Click += (_, _) => App.Instance.CopyPlain(value, Loc.F("Toast.Copied", title));
        Grid.SetColumn(copy, 1);
        row.Children.Add(copy);
        return row;
    }

    private static void OpenContact(string link)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(link) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            App.Instance.Main.ShowToast(Loc.F("Common.ErrorFormat", ex.Message), error: true);
        }
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
        Settings.AutoLogin = AutoLogin.IsChecked == true;
        Settings.AutoFillWeb = AutoFillWeb.IsChecked == true;
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

    private async void SignOut_Click(object sender, RoutedEventArgs e) => await ProfileActions.SignOutAsync();

    private async void DeleteProfile_Click(object sender, RoutedEventArgs e) => await ProfileActions.DeleteAsync();

    private void OpenFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{App.Instance.Vault.DataDirectory}\"") { UseShellExecute = true });

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
        _new.Margin = new Thickness(0, 12, 0, 0);
        _confirm.Margin = new Thickness(0, 12, 0, 0);
        root.Children.Add(_current);
        root.Children.Add(_new);
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
