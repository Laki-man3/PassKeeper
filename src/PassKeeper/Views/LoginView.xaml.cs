using System.Windows;
using System.Windows.Controls;
using PassKeeper.Localization;

namespace PassKeeper.Views;

/// <summary>Sign-in after "sign out": user name and master password; a new PIN is set afterwards.</summary>
public partial class LoginView : UserControl
{
    private bool _busy;

    public LoginView()
    {
        InitializeComponent();
        (Loc.I.IsRussian ? LangRu : LangEn).IsChecked = true;
        var profiles = App.Instance.Profiles.List();
        foreach (var p in profiles)
        {
            var chip = new Button { Style = (Style)FindResource("Btn.Chip"), Content = p.UserName };
            chip.Click += (_, _) =>
            {
                UserName.Text = p.UserName;
                Master.FocusInput();
            };
            Users.Children.Add(chip);
        }
        UsersLabel.Visibility = Users.Visibility = profiles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (profiles.Count == 1) UserName.Text = profiles[0].UserName;
        Master.EnterPressed += (_, _) => Login_Click(this, new RoutedEventArgs());
        Loaded += (_, _) =>
        {
            if (UserName.Text.Length > 0) Master.FocusInput();
            else UserName.Focus();
        };
    }

    private void Lang_Checked(object sender, RoutedEventArgs e)
    {
        var lang = sender == LangEn ? "en" : "ru";
        Loc.I.Language = lang;
        App.Instance.Settings.Language = lang;
        App.Instance.Settings.Save();
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var name = UserName.Text.Trim();
        if (name.Length == 0)
        {
            ShowError(Loc.T("Setup.ErrName"));
            UserName.Focus();
            return;
        }
        if (Master.Value.Length == 0)
        {
            Master.FocusInput();
            return;
        }
        SetBusy(true);
        var result = await App.Instance.LoginAsync(name, Master.Value);
        SetBusy(false);
        switch (result)
        {
            case App.LoginResult.UnknownUser:
                ShowError(Loc.T("Login.UnknownUser"));
                UserName.Focus();
                break;
            case App.LoginResult.WrongPassword:
                ShowError(Loc.T("Unlock.WrongMaster"));
                Master.Clear();
                Master.FocusInput();
                break;
        }
    }

    private void Create_Click(object sender, RoutedEventArgs e) => App.Instance.Main.Navigate(new SetupView());

    private void SetBusy(bool busy)
    {
        _busy = busy;
        Busy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        LoginButton.IsEnabled = !busy;
        if (busy) ShowError(null);
    }

    private void ShowError(string? text)
    {
        Error.Text = text ?? "";
        Error.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
    }
}
