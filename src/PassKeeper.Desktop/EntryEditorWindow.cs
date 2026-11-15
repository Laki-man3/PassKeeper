using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PassKeeper.Core.Models;
using PassKeeper.Core.Security;
using PassKeeper.Core.Storage;

namespace PassKeeper.Desktop;

/// <summary>Adds or changes an entry; returns it on Save (the caller writes it to the vault), null on Cancel.</summary>
public sealed class EntryEditorWindow : DialogWindow
{
    private readonly VaultEntry _entry;
    private readonly ComboBox _section = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _title;
    private readonly TextBox _login;
    private readonly TextBox _password;
    private readonly TextBlock _strength = new() { FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBox _url;
    private readonly TextBox _email;
    private readonly TextBox _phone;
    private readonly TextBox _totp;
    private readonly TextBox _key;
    private readonly AutoCompleteBox _folder = new() { HorizontalAlignment = HorizontalAlignment.Stretch, FilterMode = AutoCompleteFilterMode.ContainsOrdinal };
    private readonly TextBox _notes;
    private readonly CheckBox _favorite = new() { Content = Loc.T("Field.Favorite"), Margin = new Thickness(0, 12, 0, 0) };
    private readonly CheckBox _autoLogin = new() { Content = Loc.T("Field.AutoLogin"), Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBox _sequence;
    private readonly TextBox _windows;
    private readonly TextBlock _error = Ui.ErrorText();
    private readonly EntryCategory? _sectionAtStart;
    private bool _sectionChosen;

    public EntryEditorWindow(VaultEntry entry, bool isNew, IReadOnlyList<string> folders) : base(Loc.T(isNew ? "Editor.New" : "Editor.Edit"), 580)
    {
        _entry = entry;
        _sectionAtStart = entry.Category;
        _section.ItemsSource = EntryCategories.All.Select(c => Loc.T("Section." + c)).ToList();
        _section.SelectedIndex = Array.IndexOf(EntryCategories.All, entry.EffectiveCategory);
        // A section the user did not choose is worked out from the entry (a website address makes it a website).
        _section.SelectionChanged += (_, _) => _sectionChosen = true;
        _title = Ui.Text(entry.Title, "EditTitle");
        _login = Ui.Text(entry.Username, "EditLogin");
        _password = Ui.Secret(entry.Password, "EditPassword");
        _password.TextChanged += (_, _) => UpdateStrength();
        _url = Ui.Text(entry.Url, "EditUrl");
        _email = Ui.Text(entry.Email, "EditEmail");
        _phone = Ui.Text(entry.Phone, "EditPhone");
        _totp = Ui.Text(entry.Totp, "EditTotp");
        _key = Ui.Secret(entry.SecretKey, "EditKey");
        _folder.ItemsSource = folders;
        _folder.Text = entry.Folder;
        _folder.PlaceholderText = Loc.T("Editor.FolderHint");
        _notes = new TextBox { Text = entry.Notes, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 90 };
        _favorite.IsChecked = entry.Favorite;
        _autoLogin.IsChecked = entry.AutoLogin;
        AutomationProperties.SetAutomationId(_autoLogin, "EditAutoLogin");
        _sequence = Ui.Text(entry.AutoTypeSequence, "EditSequence");
        _sequence.PlaceholderText = Loc.T("Field.SequenceHint");
        _sequence.FontFamily = new FontFamily("Menlo, Consolas, DejaVu Sans Mono, monospace");
        _windows = new TextBox { Text = string.Join(Environment.NewLine, entry.WindowPatterns), AcceptsReturn = true, Height = 60, PlaceholderText = Loc.T("Field.WindowsHint") };
        AutomationProperties.SetAutomationId(_windows, "EditWindows");

        var show = Ui.Button(Loc.T("Common.Show"), () => { });
        show.Click += (_, _) =>
        {
            _password.RevealPassword = !_password.RevealPassword;
            show.Content = Loc.T(_password.RevealPassword ? "Common.Hide" : "Common.Show");
        };
        var generate = Ui.Button(Loc.T("Editor.Generate"), async () =>
        {
            var generated = await new GeneratorWindow(useButton: true).ShowDialog<string?>(this);
            if (generated == null) return;
            _password.Text = generated;
        });
        AutomationProperties.SetAutomationId(generate, "Generate");
        var passwordRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var passwordButtons = Ui.Row(show, generate);
        passwordButtons.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(passwordButtons, 1);
        passwordRow.Children.Add(_password);
        passwordRow.Children.Add(passwordButtons);
        UpdateStrength();

        var save = Ui.Button(Loc.T("Common.Save"), Save, accent: true);
        save.IsDefault = true;
        AutomationProperties.SetAutomationId(save, "SaveEntry");
        SetBody(Ui.Column(0,
                Ui.Label(Loc.T("Field.Section")), _section,
                Ui.Label(Loc.T("Field.Title")), _title,
                Ui.Label(Loc.T("Field.Login")), _login,
                Ui.Label(Loc.T("Field.Password")), passwordRow, _strength,
                Ui.Label(Loc.T("Field.Url")), _url,
                Ui.Label(Loc.T("Field.Email")), _email,
                Ui.Label(Loc.T("Field.Phone")), _phone,
                Ui.Label(Loc.T("Field.Totp")), _totp,
                Ui.Label(Loc.T("Field.Key")), _key,
                Ui.Label(Loc.T("Field.Folder")), _folder,
                Ui.Label(Loc.T("Field.Notes")), _notes,
                _favorite,
                _autoLogin,
                Ui.Label(Loc.T("Field.Sequence")), _sequence,
                Ui.Label(Loc.T("Field.Windows")), _windows,
                _error),
            Ui.Button(Loc.T("Common.Cancel"), () => Close(null)), save);
        Opened += (_, _) => _title.Focus();
    }

    private void UpdateStrength()
    {
        var password = _password.Text ?? "";
        if (password.Length == 0)
        {
            _strength.Text = "";
            return;
        }
        var result = PasswordStrength.Evaluate(password);
        var score = Math.Clamp(result.Score, 0, 4);
        _strength.Text = Loc.T("Strength." + score);
        _strength.Foreground = score switch
        {
            <= 1 => Brushes.IndianRed,
            2 => Brushes.DarkGoldenrod,
            _ => Brushes.SeaGreen,
        };
    }

    private void Save()
    {
        var title = (_title.Text ?? "").Trim();
        var url = (_url.Text ?? "").Trim();
        if (title.Length == 0 && url.Length > 0) title = url;
        if (title.Length == 0)
        {
            Ui.ShowError(_error, Loc.T("Editor.ErrTitle"));
            _title.Focus();
            return;
        }
        var totp = (_totp.Text ?? "").Trim();
        if (totp.Length > 0 && Totp.Parse(totp) == null)
        {
            Ui.ShowError(_error, Loc.T("Editor.ErrTotp"));
            _totp.Focus();
            return;
        }
        var sequence = (_sequence.Text ?? "").Trim();
        if (sequence.Length > 0 && Core.AutoType.AutoTypeSequence.Validate(sequence) is { } sequenceError)
        {
            Ui.ShowError(_error, Loc.F("Editor.ErrSequence", sequenceError));
            _sequence.Focus();
            return;
        }
        var e = _entry;
        e.AutoLogin = _autoLogin.IsChecked == true;
        e.AutoTypeSequence = sequence;
        e.WindowPatterns = (_windows.Text ?? "").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        e.Category = _sectionChosen && _section.SelectedIndex >= 0 ? EntryCategories.All[_section.SelectedIndex] : _sectionAtStart;
        e.Title = title;
        e.Username = (_login.Text ?? "").Trim();
        e.SetPassword(_password.Text ?? "");
        e.Url = url;
        e.Email = (_email.Text ?? "").Trim();
        e.Phone = (_phone.Text ?? "").Trim();
        e.Totp = totp;
        e.SecretKey = _key.Text ?? "";
        e.Folder = VaultService.NormalizeFolder(_folder.Text);
        e.Notes = _notes.Text ?? "";
        e.Favorite = _favorite.IsChecked == true;
        Close(e);
    }
}

/// <summary>Makes a password; with <c>useButton</c> it returns the one shown, otherwise it only offers to copy it.</summary>
public sealed class GeneratorWindow : DialogWindow
{
    private static readonly GeneratorOptions Options = new();
    private readonly TextBox _result = new() { IsReadOnly = true, FontSize = 16, FontFamily = new FontFamily("Menlo, Consolas, DejaVu Sans Mono, monospace") };

    public GeneratorWindow(bool useButton) : base(Loc.T("Generator.Title"), 480)
    {
        AutomationProperties.SetAutomationId(_result, "GeneratedPassword");
        var lengthText = new TextBlock { Text = Loc.F("Generator.Length", Options.Length), Margin = new Thickness(0, 12, 0, 0) };
        var length = new Slider { Minimum = 8, Maximum = 64, Value = Options.Length, TickFrequency = 1, IsSnapToTickEnabled = true };
        length.ValueChanged += (_, _) =>
        {
            Options.Length = (int)length.Value;
            lengthText.Text = Loc.F("Generator.Length", Options.Length);
            Generate();
        };
        CheckBox Option(string key, bool value, Action<bool> set)
        {
            var box = new CheckBox { Content = Loc.T(key), IsChecked = value };
            box.IsCheckedChanged += (_, _) =>
            {
                set(box.IsChecked == true);
                if (!Options.Upper && !Options.Lower && !Options.Digits && !Options.Symbols)
                {
                    box.IsChecked = true;
                    return;
                }
                Generate();
            };
            return box;
        }
        var body = Ui.Column(4, _result, lengthText, length,
            Option("Generator.Upper", Options.Upper, v => Options.Upper = v),
            Option("Generator.Lower", Options.Lower, v => Options.Lower = v),
            Option("Generator.Digits", Options.Digits, v => Options.Digits = v),
            Option("Generator.Symbols", Options.Symbols, v => Options.Symbols = v),
            Option("Generator.Ambiguous", Options.ExcludeAmbiguous, v => Options.ExcludeAmbiguous = v));
        var again = Ui.Button(Loc.T("Generator.Again"), Generate);
        if (useButton)
        {
            var use = Ui.Button(Loc.T("Generator.Use"), () => Close(_result.Text), accent: true);
            use.IsDefault = true;
            AutomationProperties.SetAutomationId(use, "UsePassword");
            SetBody(body, again, Ui.Button(Loc.T("Common.Cancel"), () => Close(null)), use);
        }
        else
        {
            var copy = Ui.Button(Loc.T("Common.Copy"), async () => await ClipboardService.CopyAsync(this, _result.Text ?? ""), accent: true);
            SetBody(body, again, copy, Ui.Button(Loc.T("Common.Close"), () => Close(null)));
        }
        Generate();
    }

    private void Generate() => _result.Text = PasswordGenerator.Generate(Options);
}
