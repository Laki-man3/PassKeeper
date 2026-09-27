using System.Windows;
using System.Windows.Controls;
using PassKeeper.Localization;

namespace PassKeeper.Views;

/// <summary>Generic confirmation / information dialog.</summary>
public sealed class ConfirmDialog : DialogBase
{
    private readonly CheckBox? _check;

    public ConfirmDialog(string title, string message, string confirmText, bool danger = false, string? cancelText = null,
        string? checkboxText = null, bool showCancel = true)
    {
        DialogWidth = 440;
        var root = new StackPanel { Margin = new Thickness(26, 24, 26, 22) };
        root.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("Text.H2"), FontSize = 18, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new TextBlock { Text = message, Style = (Style)FindResource("Text.Secondary"), Margin = new Thickness(0, 10, 0, 0) });
        if (checkboxText != null)
        {
            _check = new CheckBox { Content = checkboxText, Margin = new Thickness(0, 16, 0, 0) };
            root.Children.Add(_check);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
        if (showCancel)
        {
            var cancel = new Button { Style = (Style)FindResource("Btn.Ghost"), Content = cancelText ?? Loc.T("Common.Cancel"), IsCancel = true };
            cancel.Click += (_, _) => Close(false);
            buttons.Children.Add(cancel);
        }
        var ok = new Button { Style = (Style)FindResource(danger ? "Btn.Danger" : "Btn.Primary"), Content = confirmText, Margin = new Thickness(8, 0, 0, 0), MinWidth = 110, IsDefault = true };
        ok.Click += (_, _) => Close(true);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);
        Content = root;
        InitialFocus = ok;
    }

    public bool IsChecked => _check?.IsChecked == true;

    public static async Task<bool> AskAsync(string title, string message, string confirmText, bool danger = false)
    {
        var result = await App.Instance.Main.ShowDialogAsync(new ConfirmDialog(title, message, confirmText, danger));
        return result is true;
    }

    public static Task InfoAsync(string title, string message) =>
        App.Instance.Main.ShowDialogAsync(new ConfirmDialog(title, message, Loc.T("Common.Ok"), showCancel: false));
}

/// <summary>Asks for a password (optionally a KeePass key file). Result: (string Password, byte[]? KeyFile) or null.</summary>
public sealed class PasswordPromptDialog : DialogBase
{
    private readonly Controls.SecretBox _password = new();
    private readonly Controls.SecretBox? _confirm;
    private readonly TextBlock _error;
    private readonly TextBlock? _keyFileText;
    private byte[]? _keyFile;

    public PasswordPromptDialog(string title, string message, bool allowKeyFile = false, bool confirm = false, bool strength = false)
    {
        DialogWidth = 440;
        var root = new StackPanel { Margin = new Thickness(26, 24, 26, 22) };
        root.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("Text.H2"), FontSize = 18 });
        root.Children.Add(new TextBlock { Text = message, Style = (Style)FindResource("Text.Secondary"), Margin = new Thickness(0, 8, 0, 16) });
        _password.Placeholder = Loc.T("Common.Password");
        _password.EnterPressed += (_, _) => Submit();
        root.Children.Add(_password);
        if (strength)
        {
            var meter = new Controls.StrengthMeter { Margin = new Thickness(0, 8, 0, 0) };
            root.Children.Add(meter);
            _password.ValueChanged += (_, _) => meter.Password = _password.Value;
        }
        if (confirm)
        {
            _confirm = new Controls.SecretBox { Placeholder = Loc.T("Common.PasswordRepeat"), Margin = new Thickness(0, 10, 0, 0) };
            _confirm.EnterPressed += (_, _) => Submit();
            root.Children.Add(_confirm);
        }
        if (allowKeyFile)
        {
            var row = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _keyFileText = new TextBlock { Text = Loc.T("Import.NoKeyFile"), Style = (Style)FindResource("Text.Muted"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var browse = new Button { Style = (Style)FindResource("Btn.Secondary"), Content = Loc.T("Import.KeyFile"), Height = 32 };
            Controls.UI.SetIcon(browse, "\uE8A5");
            browse.Click += (_, _) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Title = Loc.T("Import.KeyFile"), Filter = Loc.T("Import.AllFiles") + "|*.*" };
                if (dlg.ShowDialog() != true) return;
                _keyFile = File.ReadAllBytes(dlg.FileName);
                _keyFileText.Text = Path.GetFileName(dlg.FileName);
            };
            Grid.SetColumn(browse, 1);
            row.Children.Add(_keyFileText);
            row.Children.Add(browse);
            root.Children.Add(row);
        }
        _error = new TextBlock { Style = (Style)FindResource("Text.Body"), Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
        _error.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger");
        root.Children.Add(_error);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var cancel = new Button { Style = (Style)FindResource("Btn.Ghost"), Content = Loc.T("Common.Cancel") };
        cancel.Click += (_, _) => Close(null);
        var ok = new Button { Style = (Style)FindResource("Btn.Primary"), Content = Loc.T("Common.Continue"), Margin = new Thickness(8, 0, 0, 0), MinWidth = 110 };
        ok.Click += (_, _) => Submit();
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) => _password.FocusInput();
    }

    public int MinLength { get; set; }

    private void Submit()
    {
        var pw = _password.Value;
        if (pw.Length == 0 && _keyFile == null) return;
        if (MinLength > 0 && pw.Length < MinLength)
        {
            ShowError(Loc.F("Setup.ErrShort", MinLength));
            return;
        }
        if (_confirm != null && _confirm.Value != pw)
        {
            ShowError(Loc.T("Setup.ErrMismatch"));
            return;
        }
        Close((pw, _keyFile));
    }

    private void ShowError(string text)
    {
        _error.Text = text;
        _error.Visibility = Visibility.Visible;
    }
}
