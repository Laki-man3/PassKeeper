using System.Windows;
using System.Windows.Controls;
using PassKeeper.Controls;
using PassKeeper.Localization;

namespace PassKeeper.Views;

/// <summary>Sign out and user deletion, shared by the sidebar menu, the PIN screen and Settings.</summary>
public static class ProfileActions
{
    public static async Task SignOutAsync()
    {
        if (!await ConfirmDialog.AskAsync(Loc.T("Profile.SignOutTitle"), Loc.F("Profile.SignOutText", App.Instance.Vault.UserName), Loc.T("Profile.SignOut")))
            return;
        App.Instance.Logout();
    }

    public static async Task DeleteAsync()
    {
        var name = App.Instance.Vault.UserName;
        if (await App.Instance.Main.ShowDialogAsync(new DeleteProfileDialog(name)) is not true) return;
        App.Instance.DeleteCurrentProfile();
        App.Instance.Main.ShowToast(Loc.F("Profile.Deleted", name));
    }
}

/// <summary>Confirms deleting the user with the master password: the vault, PIN and backups are erased.</summary>
public sealed class DeleteProfileDialog : DialogBase
{
    private readonly SecretBox _master = new();
    private readonly TextBlock _error = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly Button _delete;

    public DeleteProfileDialog(string userName)
    {
        DialogWidth = 460;
        var root = new StackPanel { Margin = new Thickness(26, 24, 26, 22) };
        root.Children.Add(new TextBlock { Text = Loc.F("Profile.DeleteTitle", userName), Style = (Style)FindResource("Text.H2"), FontSize = 18, TextWrapping = TextWrapping.Wrap });
        var warning = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 14, 0, 14) };
        warning.SetResourceReference(Border.BackgroundProperty, "Brush.DangerSoft");
        warning.Child = new TextBlock { Text = Loc.T("Profile.DeleteText"), Style = (Style)FindResource("Text.Secondary"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
        root.Children.Add(warning);
        _master.Placeholder = Loc.T("Unlock.MasterPlaceholder");
        root.Children.Add(_master);
        _error.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger");
        root.Children.Add(_error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var cancel = new Button { Style = (Style)FindResource("Btn.Ghost"), Content = Loc.T("Common.Cancel") };
        cancel.Click += (_, _) => Close(null);
        _delete = new Button { Style = (Style)FindResource("Btn.Danger"), Content = Loc.T("Profile.DeleteButton"), Margin = new Thickness(8, 0, 0, 0) };
        _delete.Click += async (_, _) => await DeleteAsync();
        buttons.Children.Add(cancel);
        buttons.Children.Add(_delete);
        root.Children.Add(buttons);
        Content = root;
        _master.EnterPressed += async (_, _) => await DeleteAsync();
        Loaded += (_, _) => _master.FocusInput();
    }

    private async Task DeleteAsync()
    {
        var password = _master.Value;
        if (password.Length == 0) return;
        _delete.IsEnabled = false;
        var vault = App.Instance.Vault;
        var ok = await Task.Run(() => vault.VerifyMaster(password));
        _delete.IsEnabled = true;
        if (!ok)
        {
            _error.Text = Loc.T("Unlock.WrongMaster");
            _error.Visibility = Visibility.Visible;
            _master.Clear();
            _master.FocusInput();
            return;
        }
        Close(true);
    }
}
