using System.Windows;
using System.Windows.Controls;
using PassKeeper.Core.Storage;
using PassKeeper.Localization;
using PassKeeper.Services;

namespace PassKeeper.Views;

public partial class SetupView : UserControl
{
    private bool _busy;

    public SetupView()
    {
        InitializeComponent();
        UserName.Text = Environment.UserName;
        (Loc.I.IsRussian ? LangRu : LangEn).IsChecked = true;
        // An all-users autostart (set by the administrator) already covers this user.
        if (AutostartService.IsEnabledForMachine()) Autostart.Visibility = Visibility.Collapsed;
        else Autostart.IsChecked = AutostartService.DefaultForNewProfile();
        Master.EnterPressed += (_, _) => Confirm.FocusInput();
        Confirm.EnterPressed += (_, _) => Create_Click(this, new RoutedEventArgs());
        Loaded += (_, _) => Master.FocusInput();
        SignInLink.Visibility = App.Instance.Profiles.List().Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SignIn_Click(object sender, RoutedEventArgs e) => App.Instance.Main.Navigate(new LoginView());

    private void Lang_Checked(object sender, RoutedEventArgs e)
    {
        var lang = sender == LangEn ? "en" : "ru";
        Loc.I.Language = lang;
        App.Instance.Settings.Language = lang;
        App.Instance.Settings.Save();
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var name = UserName.Text.Trim();
        var master = Master.Value;
        string? error = null;
        if (name.Length == 0) error = Loc.T("Setup.ErrName");
        else if (master.Length < VaultService.MinMasterPasswordLength) error = Loc.F("Setup.ErrShort", VaultService.MinMasterPasswordLength);
        else if (master != Confirm.Value) error = Loc.T("Setup.ErrMismatch");
        else if (App.Instance.Profiles.FindByName(name) != null) error = Loc.T("Setup.ErrExists");
        ShowError(error);
        if (error != null) return;

        SetBusy(true);
        try
        {
            App.Instance.PrepareNewProfile();
            var vault = App.Instance.Vault;
            await Task.Run(() => vault.Create(name, master));
            if (Autostart.Visibility == Visibility.Visible)
            {
                try { AutostartService.SetForUser(Autostart.IsChecked == true); }
                catch (Exception) { /* policy may forbid it; the setting can be changed later */ }
            }
            // Vault.StateChanged makes the app continue with the mandatory PIN setup.
        }
        catch (Exception ex)
        {
            ShowError(Loc.F("Common.ErrorFormat", ex.Message));
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        Busy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CreateButton.IsEnabled = !busy;
    }

    private void ShowError(string? text)
    {
        Error.Text = text ?? "";
        Error.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
    }
}
