using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PassKeeper.Core.Storage;

namespace PassKeeper.Desktop;

/// <summary>Sign in with the PIN (when set) or the master password, or create the first (or another) user.</summary>
public sealed class UnlockView : UserControl
{
    private readonly MainWindow _window;
    private bool _master;

    public UnlockView(MainWindow window)
    {
        _window = window;
        if (App.Instance.Profiles.List().Count == 0) ShowCreate(canGoBack: false);
        else ShowSignIn();
    }

    private static Border Card(Control content) => new()
    {
        Child = content,
        Width = 400,
        Padding = new Thickness(28),
        CornerRadius = new CornerRadius(12),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Control Header(string title) => Ui.Column(10,
        new Image { Source = Ui.Logo, Width = 56, Height = 56, HorizontalAlignment = HorizontalAlignment.Left },
        Ui.Heading(title));

    private static Button Link(string text, Action click, string id)
    {
        var link = new Button { Content = text, Margin = new Thickness(0, 6, 0, 0), Background = Brushes.Transparent, Padding = new Thickness(0, 4) };
        AutomationProperties.SetAutomationId(link, id);
        link.Click += (_, _) => click();
        return link;
    }

    private void ShowSignIn()
    {
        var settings = App.Instance.Settings;
        var profiles = App.Instance.Profiles.List();
        var index = profiles.ToList().FindIndex(p => p.Id == settings.ActiveProfile);
        if (index < 0) index = 0;
        var profile = profiles[index];
        var vault = new VaultService(profile.Directory);
        var usePin = vault.HasPin && !_master;

        var users = new ComboBox { ItemsSource = profiles.Select(p => p.UserName).ToList(), SelectedIndex = index, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(users, "User");
        users.SelectionChanged += (_, _) =>
        {
            if (users.SelectedIndex < 0 || users.SelectedIndex == index) return;
            settings.ActiveProfile = profiles[users.SelectedIndex].Id;
            _master = false;
            ShowSignIn();
        };
        var secret = Ui.Secret(id: usePin ? "Pin" : "Master");
        var error = Ui.ErrorText();
        var signIn = Ui.Button(Loc.T("Unlock.Button"), () => { }, accent: true);
        signIn.IsDefault = true;
        signIn.HorizontalAlignment = HorizontalAlignment.Stretch;
        signIn.HorizontalContentAlignment = HorizontalAlignment.Center;
        AutomationProperties.SetAutomationId(signIn, "SignIn");
        signIn.Click += async (_, _) =>
        {
            var value = secret.Text ?? "";
            if (value.Length == 0) return;
            signIn.IsEnabled = false;
            Ui.ShowError(error, "");
            signIn.Content = Loc.T("Unlock.Opening");
            try
            {
                if (usePin) await SignInWithPinAsync(vault, value, error);
                else await SignInWithMasterAsync(vault, value, error);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                Ui.ShowError(error, ex.Message);
            }
            if (vault.IsUnlocked)
            {
                settings.ActiveProfile = profile.Id;
                settings.Save();
                _window.ShowVault(vault);
                return;
            }
            signIn.IsEnabled = true;
            signIn.Content = Loc.T("Unlock.Button");
            secret.Text = "";
            secret.Focus();
        };

        var form = Ui.Column(4, Header(Loc.T("Unlock.Title")));
        if (profiles.Count > 1)
        {
            form.Children.Add(Ui.Label(Loc.T("Unlock.User")));
            form.Children.Add(users);
        }
        else form.Children.Add(Ui.Muted(profile.UserName));
        form.Children.Add(Ui.Label(Loc.T(usePin ? "Unlock.Pin" : "Unlock.Master")));
        form.Children.Add(secret);
        form.Children.Add(error);
        form.Children.Add(new Border { Height = 12 });
        form.Children.Add(signIn);
        if (vault.HasPin)
            form.Children.Add(Link(Loc.T(usePin ? "Unlock.UseMaster" : "Unlock.UsePin"), () =>
            {
                _master = usePin;
                ShowSignIn();
            }, "SwitchMode"));
        form.Children.Add(Link(Loc.T("Unlock.NewUser"), () => ShowCreate(canGoBack: true), "NewUser"));
        Content = Card(form);
        secret.AttachedToVisualTree += (_, _) => Ui.Later(() => secret.Focus());
    }

    private async Task SignInWithMasterAsync(VaultService vault, string password, TextBlock error)
    {
        if (!await Ui.Background(() => vault.UnlockWithMaster(password))) Ui.ShowError(error, Loc.T("Unlock.Wrong"));
    }

    private async Task SignInWithPinAsync(VaultService vault, string pin, TextBlock error)
    {
        PinUnlockResult result;
        try
        {
            result = await Ui.Background(() => vault.UnlockWithPin(pin));
        }
        catch (PinSealUnavailableException ex)
        {
            Ui.ShowError(error, Loc.F("Unlock.PinUnavailable", ex.Message));
            return;
        }
        switch (result)
        {
            case PinUnlockResult.WrongPin:
                Ui.ShowError(error, Loc.F("Unlock.WrongPin", vault.PinAttemptsLeft));
                break;
            case PinUnlockResult.LockedOut:
            case PinUnlockResult.NotConfigured:
                _master = true;
                ShowSignIn();
                if (result == PinUnlockResult.LockedOut) await Dialogs.Message(_window, Loc.T("Unlock.PinLocked"));
                break;
        }
    }

    private void ShowCreate(bool canGoBack)
    {
        var name = Ui.Text(id: "Name");
        var master = Ui.Secret(id: "NewMaster");
        var repeat = Ui.Secret(id: "RepeatMaster");
        var error = Ui.ErrorText();
        var create = Ui.Button(Loc.T("Create.Button"), () => { }, accent: true);
        create.IsDefault = true;
        AutomationProperties.SetAutomationId(create, "Create");
        create.Click += async (_, _) =>
        {
            var userName = (name.Text ?? "").Trim();
            var password = master.Text ?? "";
            var profiles = App.Instance.Profiles;
            var problem = userName.Length == 0 ? Loc.T("Create.ErrName")
                : profiles.FindByName(userName) != null ? Loc.T("Create.ErrExists")
                : password.Length < VaultService.MinMasterPasswordLength ? Loc.F("Create.ErrShort", VaultService.MinMasterPasswordLength)
                : password != (repeat.Text ?? "") ? Loc.T("Create.ErrMismatch")
                : "";
            Ui.ShowError(error, problem);
            if (problem.Length > 0) return;
            var id = profiles.NewId();
            var vault = new VaultService(profiles.DirectoryOf(id));
            create.IsEnabled = false;
            await Task.Run(() => vault.Create(userName, password));
            App.Instance.Settings.ActiveProfile = id;
            App.Instance.Settings.Save();
            _window.ShowVault(vault, offerPin: true);
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        if (canGoBack) buttons.Children.Add(Ui.Button(Loc.T("Common.Back"), ShowSignIn));
        buttons.Children.Add(create);
        Content = Card(Ui.Column(4,
            Header(Loc.T("Create.Title")),
            Ui.Muted(Loc.T("Create.Intro")),
            Ui.Label(Loc.T("Create.Name")), name,
            Ui.Label(Loc.T("Unlock.Master")), master,
            Ui.Label(Loc.T("Create.Repeat")), repeat,
            error,
            buttons));
        name.AttachedToVisualTree += (_, _) => Ui.Later(() => name.Focus());
    }
}
