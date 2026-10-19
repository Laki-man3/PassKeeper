using System.Windows;
using System.Windows.Controls;
using PassKeeper.Controls;
using PassKeeper.Core.AutoType;
using PassKeeper.Core.Matching;
using PassKeeper.Core.Models;
using PassKeeper.Core.Security;
using PassKeeper.Localization;
using PassKeeper.Services;

namespace PassKeeper.Views;

/// <summary>
/// Entry editor. The form follows the section: a site asks for its address, a VPN / remote access client or a
/// program for the client window, token PIN and automatic sign-in.
/// </summary>
public partial class EntryEditorView : UserControl
{
    /// <summary>Placeholders offered as clickable chips under the sequence box.</summary>
    private static readonly string[] Placeholders =
        ["{USERNAME}", "{PASSWORD}", "{PIN}", "{TAB}", "{ENTER}", "{DELAY 500}", "{TOTP}", "{EMAIL}", "{PHONE}", "{KEY}", "{CLEARFIELD}", "{S:}"];

    /// <summary>Ready-made sequences: localisation key of the label → sequence.</summary>
    private static readonly (string Key, string Sequence)[] Examples =
    [
        ("Editor.ExLoginPassword", "{USERNAME}{TAB}{PASSWORD}{ENTER}"),
        ("Editor.ExPassword", "{PASSWORD}{ENTER}"),
        ("Editor.ExPin", "{PIN}{ENTER}"),
        ("Editor.ExTerminal", "{USERNAME}{ENTER}{DELAY 800}{PASSWORD}{ENTER}"),
        ("Editor.ExTwoFactor", "{PASSWORD}{ENTER}{DELAY 1500}{TOTP}{ENTER}"),
    ];

    private readonly VaultEntry _entry;
    private readonly bool _isNew;
    private readonly string _originalSnapshot;
    private readonly List<(TextBox Name, SecretBox Value, CheckBox Protected, FrameworkElement Row)> _fields = [];
    private EntryCategory _category;
    /// <summary>The sign-in form the entry is for, as a sequence with automatic sign-in (see <see cref="VaultEntry.FormSequence"/>).</summary>
    private string _formSequence;
    private bool _loading = true;
    private bool _autoLoginTouched;
    /// <summary>Password suggested for a new site entry; an existing account of a client keeps its own password.</summary>
    private string? _generatedPassword;

    public EntryEditorView(VaultEntry entry, bool isNew, IEnumerable<string> folders)
    {
        InitializeComponent();
        _entry = entry;
        _isNew = isNew;
        Heading.Text = Loc.T(isNew ? "Editor.NewTitle" : "Editor.EditTitle");

        TitleBox.Text = entry.Title;
        LoginBox.Text = entry.Username;
        PasswordBox.Value = entry.Password;
        UrlBox.Text = entry.Url;
        ServerBox.Text = entry.Url;
        PinBox.Value = entry.StoredPin();
        EmailBox.Text = entry.Email;
        PhoneBox.Text = entry.Phone;
        KeyBox.Value = entry.SecretKey;
        TotpBox.Text = entry.Totp;
        foreach (var f in folders) FolderBox.Items.Add(f);
        FolderBox.Text = entry.Folder;
        NotesBox.Text = entry.Notes;
        FavoriteBox.IsChecked = entry.Favorite;
        AutoLoginBox.IsChecked = entry.AutoLogin;
        WindowsBox.Text = string.Join(Environment.NewLine, entry.WindowPatterns);
        ExtraUrlsBox.Text = string.Join(Environment.NewLine, entry.ExtraUrls);
        SequenceBox.Text = entry.AutoTypeSequence;
        _formSequence = entry.FormSequence;
        var pinField = entry.FindPinField();
        foreach (var f in entry.CustomFields.Where(f => f != pinField)) AddFieldRow(f.Name, f.Value, f.Protected);
        UpdateNoFields();
        BuildChips();

        _category = entry.Category ?? (isNew && entry.WindowPatterns.Count == 0 && entry.Url.Length == 0 ? EntryCategory.Web : entry.EffectiveCategory);
        if (isNew && entry.Password.Length == 0 && _category is EntryCategory.Web or EntryCategory.Other)
            PasswordBox.Value = _generatedPassword = PasswordGenerator.Generate(GeneratorDialog.LastOptions);
        SelectCategory(_category);
        AutoLoginBox.Click += (_, _) => _autoLoginTouched = true;
        // The sequence made automatically follows the switch, however it is turned (click, keyboard, screen reader).
        AutoLoginBox.Checked += (_, _) => RenderSequence();
        AutoLoginBox.Unchecked += (_, _) => RenderSequence();
        if (entry.AutoTypeSequence.Length > 0) SetAdvanced(true);
        ShowExtraUrls(entry.ExtraUrls.Count > 0);
        // Windows typed in by hand (not a known client) stay in sight.
        ShowWindowsList(entry.WindowPatterns.Count > 0 && KnownApps.ForPatterns(entry.WindowPatterns) == null);
        UpdateClientCard();

        RenderSequence();
        if (entry.AutoTypeSequence.Length > 0 && FieldPlan.Parse(entry.AutoTypeSequence) == null && entry.AutoTypeSequence.Contains("{S:", StringComparison.OrdinalIgnoreCase))
            ShowSequenceText(true);
        _originalSnapshot = Snapshot();
        _loading = false;
        Loaded += (_, _) =>
        {
            if (DetectBanner.Visibility == Visibility.Visible)
            {
                LoginBox.Focus();
                return;
            }
            TitleBox.Focus();
            TitleBox.SelectAll();
        };
    }

    public event Action<VaultEntry>? Saved;
    public event Action? Cancelled;

    public Guid EntryId => _entry.Id;
    public bool IsDirty => Snapshot() != _originalSnapshot;

    /// <summary>After saving, sign in to the window this entry was created for.</summary>
    public bool FillAfterSave => DetectBanner.Visibility == Visibility.Visible && FillAfterSaveBox.IsChecked == true;

    private string Snapshot() => string.Join("\u0001",
        _category, TitleBox.Text, LoginBox.Text, PasswordBox.Value, UrlBox.Text, ServerBox.Text, PinBox.Value, EmailBox.Text, PhoneBox.Text,
        KeyBox.Value, TotpBox.Text, FolderBox.Text, NotesBox.Text, FavoriteBox.IsChecked, AutoLoginBox.IsChecked, WindowsBox.Text,
        ExtraUrlsBox.Text, SequenceBox.Text,
        string.Join("\u0002", _fields.Select(f => f.Name.Text + "\u0003" + f.Value.Value + "\u0003" + f.Protected.IsChecked)));

    // ------------------------------------------------------------------ detection banner

    /// <summary>Shown when the auto-type hotkey found a sign-in window without an entry.</summary>
    public void ShowDetection(DetectedWindow window, SignInForm form)
    {
        DetectBanner.Visibility = Visibility.Visible;
        DetectTitle.Text = Loc.F("Editor.DetectTitle", window.DisplayName);
        var fields = new List<string>();
        if (form.HasLogin) fields.Add(Loc.T("Editor.FieldLogin"));
        if (form.HasPassword) fields.Add(Loc.T("Editor.FieldPassword"));
        if (form.HasPin) fields.Add(Loc.T("Editor.FieldPin"));
        if (form.HasOtp) fields.Add(Loc.T("Editor.FieldOtp"));
        DetectText.Text = fields.Count > 0
            ? Loc.F("Editor.DetectFields", string.Join(", ", fields))
            : Loc.T("Editor.DetectNoFields");
        if (form.HasPin && !form.HasPassword) PinBox.Placeholder = Loc.T("Editor.PinRequired");
        if (window.App?.Sequence == null && form.Any)
            _formSequence = AutoTypeSequence.ForForm(form.HasLogin, form.HasPassword, form.HasPin, form.HasOtp, submit: true);
        RenderSequence();
    }

    /// <summary>An account that already exists on the site: no suggested password, the cursor in the login box.</summary>
    public void ForExistingAccount()
    {
        if (_generatedPassword != null && PasswordBox.Value == _generatedPassword) PasswordBox.Value = "";
        _generatedPassword = null;
        Loaded += (_, _) => Dispatcher.BeginInvoke(() => LoginBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>
    /// A new entry for a site without one, opened from its sign-in page: the fields found there make its sequence,
    /// and the form is filled once the entry is saved.
    /// </summary>
    public void ShowSiteForm(string site, SignInForm form, bool twoSteps)
    {
        DetectBanner.Visibility = Visibility.Visible;
        DetectTitle.Text = Loc.F("Editor.SiteFormTitle", site);
        var fields = new List<string>();
        if (form.HasLogin) fields.Add(Loc.T("Editor.FieldLogin"));
        if (form.HasPassword) fields.Add(Loc.T("Editor.FieldPassword"));
        if (form.HasOtp) fields.Add(Loc.T("Editor.FieldOtp"));
        DetectText.Text = Loc.F("Editor.SiteFormFields", string.Join(", ", fields));
        FillAfterSaveBox.Content = Loc.T("Editor.FillSiteAfterSave");
        _formSequence = AutoTypeSequence.ForForm(form.HasLogin, form.HasPassword || twoSteps, otp: form.HasOtp, twoSteps: twoSteps, submit: true);
        RenderSequence();
    }

    // ------------------------------------------------------------------ section

    private void SelectCategory(EntryCategory category)
    {
        var radio = category switch
        {
            EntryCategory.Remote => CatRemote,
            EntryCategory.App => CatApp,
            EntryCategory.Other => CatOther,
            _ => CatWeb,
        };
        radio.IsChecked = true;
        ApplyCategory();
    }

    private void Category_Checked(object sender, RoutedEventArgs e)
    {
        _category = sender == CatRemote ? EntryCategory.Remote : sender == CatApp ? EntryCategory.App : sender == CatOther ? EntryCategory.Other : EntryCategory.Web;
        if (_loading) return;
        if (_isNew && !_autoLoginTouched) AutoLoginBox.IsChecked = _category == EntryCategory.Remote;
        ApplyCategory();
        Any_Changed(sender, e);
        RenderSequence();
    }

    private void ApplyCategory()
    {
        var client = _category is EntryCategory.Remote or EntryCategory.App;
        if (client && _generatedPassword != null && PasswordBox.Value == _generatedPassword) PasswordBox.Value = "";
        Show(ClientPanel, client);
        Show(PinServerRow, client);
        Show(ServerPanel, _category == EntryCategory.Remote);
        // Clients sign in when their window appears, sites when PassKeeper fills their form by itself.
        Show(AutoLoginBox, client || _category == EntryCategory.Web);
        AutoLoginHint.Text = Loc.T(client ? "Editor.AutoLoginHint" : "Editor.AutoLoginSiteHint");
        Show(WebPanel, _category == EntryCategory.Web);
        Show(ContactRow, _category is EntryCategory.Web or EntryCategory.Other);
        Show(KeyPanel, _category != EntryCategory.Remote);
        UpdateAdvancedSummary();

        static void Show(UIElement element, bool visible) => element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    // ------------------------------------------------------------------ client

    private async void ChooseClient_Click(object sender, RoutedEventArgs e)
    {
        if (await App.Instance.Main.ShowDialogAsync(new ClientPickerDialog()) is DetectedWindow picked) ApplyClient(picked);
    }

    /// <summary>Sets the client: its window patterns replace those of a previously chosen client.</summary>
    public void ApplyClient(DetectedWindow client)
    {
        var lines = Lines(WindowsBox.Text);
        if (KnownApps.ForPatterns(lines) is { } previous && previous.Id != client.App?.Id)
            lines.RemoveAll(l => previous.Patterns.Contains(l, StringComparer.OrdinalIgnoreCase));
        foreach (var pattern in client.Patterns)
            if (!lines.Contains(pattern, StringComparer.OrdinalIgnoreCase)) lines.Add(pattern);
        WindowsBox.Text = string.Join(Environment.NewLine, lines);

        var title = TitleBox.Text.Trim();
        if (title.Length == 0 || KnownApps.All.Any(a => a.Name == title)) TitleBox.Text = client.DisplayName;
        if (client.App?.Sequence != null && SequenceBox.Text.Trim().Length == 0) SequenceBox.Text = client.App.Sequence;
        if (_category != client.Category && (_category is EntryCategory.Web or EntryCategory.Other || client.App != null))
            SelectCategory(client.Category);
        if (_isNew && !_autoLoginTouched) AutoLoginBox.IsChecked = client.Category == EntryCategory.Remote;
        if (client.App?.UsesPin == true) PinBox.Placeholder = Loc.T("Editor.PinRequired");
        UpdateClientCard();
    }

    /// <summary>The window list is set by "Choose…"; it is opened for editing by hand only when asked (or when it is not a known client's).</summary>
    private void WindowsToggle_Click(object sender, RoutedEventArgs e)
    {
        ShowWindowsList(true);
        Dispatcher.BeginInvoke(() => WindowsBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void ShowWindowsList(bool show)
    {
        WindowsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        WindowsToggle.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ExtraUrlsToggle_Click(object sender, RoutedEventArgs e)
    {
        ShowExtraUrls(true);
        Dispatcher.BeginInvoke(() => ExtraUrlsBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void ShowExtraUrls(bool show)
    {
        ExtraUrlsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ExtraUrlsToggle.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Windows_Changed(object sender, TextChangedEventArgs e)
    {
        UpdateClientCard();
        Any_Changed(sender, e);
    }

    private void UpdateClientCard()
    {
        if (ClientName == null) return;
        var patterns = Lines(WindowsBox.Text);
        var app = KnownApps.ForPatterns(patterns);
        if (app != null)
        {
            ClientName.Text = app.Name;
            ClientInfo.Text = Loc.F("Editor.ClientKnown", string.Join(", ", patterns));
        }
        else if (patterns.Count > 0)
        {
            ClientName.Text = Loc.T("Editor.ClientCustom");
            ClientInfo.Text = string.Join(", ", patterns);
        }
        else
        {
            ClientName.Text = Loc.T("Editor.ClientNone");
            ClientInfo.Text = Loc.T("Editor.ClientNoneHint");
        }
        ClientIcon.Text = VaultView.CategoryIcon(app?.Category ?? _category);
        UpdateAdvancedSummary();
    }

    // ------------------------------------------------------------------ login / password from another entry

    private async void TakeFromEntry_Click(object sender, RoutedEventArgs e)
    {
        if (await App.Instance.Main.ShowDialogAsync(new EntryPickerDialog(_entry.Id)) is not VaultEntry source) return;
        CopyCredentials(source);
        App.Instance.Main.ShowToast(Loc.F("Editor.TakenFrom", source.Title));
    }

    /// <summary>Login, password, e-mail and 2FA secret of another entry (the password becomes visible to check it).</summary>
    public void CopyCredentials(VaultEntry source)
    {
        LoginBox.Text = source.Username.Length > 0 ? source.Username : source.Email;
        PasswordBox.Value = source.Password;
        if (EmailBox.Text.Length == 0) EmailBox.Text = source.Email;
        if (TotpBox.Text.Length == 0) TotpBox.Text = source.Totp;
        if (PinBox.Value.Length == 0 && source.StoredPin().Length > 0) PinBox.Value = source.StoredPin();
    }

    /// <summary>Puts the cursor into the site address (a copy made for a website).</summary>
    public void FocusAddress() => Dispatcher.BeginInvoke(() => UrlBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);

    // ------------------------------------------------------------------ auto-type details

    private void AdvancedToggle_Click(object sender, RoutedEventArgs e) => SetAdvanced(AdvancedPanel.Visibility != Visibility.Visible);

    /// <summary>Unfolds the auto-type block (screenshots, help links).</summary>
    internal void ShowAdvanced() => SetAdvanced(true);

    private void SetAdvanced(bool open)
    {
        AdvancedPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        AdvancedChevron.Text = open ? "\uE70E" : "\uE70D";
    }

    private void UpdateAdvancedSummary()
    {
        if (AdvancedSummary == null) return;
        var sequence = SequenceBox.Text.Trim();
        var generated = GeneratedSequence();
        AdvancedSummary.Text = sequence.Length > 0 ? Loc.F("Editor.SummarySequence", sequence)
            : generated.Length > 0 ? Loc.F("Editor.SummaryAuto", generated)
            : Loc.T("Editor.SummaryFields");
    }

    /// <summary>
    /// The sequence PassKeeper makes when none is written: from the entry's sign-in form, or with automatic sign-in on
    /// from its fields as they are (a login and a password); Enter only with automatic sign-in.
    /// </summary>
    private string GeneratedSequence()
    {
        if (AutoLoginBox == null) return "";
        var autoLogin = AutoLoginBox.Visibility == Visibility.Visible && AutoLoginBox.IsChecked == true;
        var form = _formSequence;
        if (string.IsNullOrWhiteSpace(form) && autoLogin)
            form = AutoTypeSequence.ForForm(login: LoginBox.Text.Trim().Length > 0 || EmailBox.Text.Trim().Length > 0 || _category == EntryCategory.Web, password: true, submit: true);
        if (string.IsNullOrWhiteSpace(form)) return "";
        return autoLogin ? form : AutoTypeSequence.WithoutEnter(form);
    }

    /// <summary>
    /// Shows the sequence as elements with a cross each: the written one, otherwise the one PassKeeper makes (removing
    /// an element of it turns the rest into a written sequence).
    /// </summary>
    private void RenderSequence()
    {
        if (SequenceChips == null) return;
        SequenceChips.Children.Clear();
        var written = SequenceBox.Text.Trim();
        var generated = written.Length == 0;
        var tokens = AutoTypeSequence.Tokenize(generated ? GeneratedSequence() : written);
        for (var i = 0; i < tokens.Count; i++) SequenceChips.Children.Add(SequenceChip(tokens, i, generated));
        SequenceNote.Text = tokens.Count == 0 ? Loc.T("Editor.SequencePlaceholder")
            : generated ? Loc.T("Editor.SequenceAuto")
            : FieldPlan.Parse(written) != null ? Loc.T("Editor.SequenceByFields")
            : Loc.T("Editor.SequenceKeys");
        UpdateAdvancedSummary();
    }

    private FrameworkElement SequenceChip(List<string> tokens, int index, bool generated)
    {
        var token = tokens[index];
        var chip = new Border { CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), Padding = new Thickness(9, 0, 2, 0), Margin = new Thickness(0, 0, 6, 6), Height = 28 };
        chip.SetResourceReference(Border.BackgroundProperty, generated ? "Brush.Surface" : "Brush.SurfaceAlt");
        chip.SetResourceReference(Border.BorderBrushProperty, generated ? "Brush.BorderStrong" : "Brush.Border");
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var text = new TextBlock { Text = token, FontFamily = (System.Windows.Media.FontFamily)FindResource("Font.Mono"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, generated ? "Brush.TextSecondary" : "Brush.Text");
        panel.Children.Add(text);
        var remove = new Button { Style = (Style)FindResource("Btn.Icon"), Content = "\uE711", FontSize = 9, Width = 22, Height = 22, Margin = new Thickness(4, 0, 0, 0), ToolTip = Loc.F("Editor.SequenceRemove", token) };
        System.Windows.Automation.AutomationProperties.SetName(remove, Loc.F("Editor.SequenceRemove", token));
        remove.Click += (_, _) => SequenceBox.Text = AutoTypeSequence.Remove(string.Concat(tokens), index);
        panel.Children.Add(remove);
        chip.Child = panel;
        return chip;
    }

    private void SequenceTextToggle_Click(object sender, RoutedEventArgs e) => ShowSequenceText(SequenceBox.Visibility != Visibility.Visible);

    private void ShowSequenceText(bool show)
    {
        SequenceBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        SequenceTextToggle.Content = Loc.T(show ? "Editor.SequenceAsChips" : "Editor.SequenceAsText");
        if (show) Dispatcher.BeginInvoke(() => SequenceBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void BuildChips()
    {
        foreach (var token in Placeholders)
        {
            var chip = new Button
            {
                Style = (Style)FindResource("Btn.Chip"),
                Content = token == "{S:}" ? "{S:" + Loc.T("Editor.FieldName") + "}" : token,
                FontFamily = (System.Windows.Media.FontFamily)FindResource("Font.Mono"),
                ToolTip = Loc.T("Editor.Ph." + token.Trim('{', '}').Split(' ', ':')[0]),
            };
            var text = (string)chip.Content;
            chip.Click += (_, _) => Add(text);
            chip.ContextMenu = CopyMenu(text);
            PlaceholderChips.Children.Add(chip);
        }
        foreach (var (key, sequence) in Examples)
        {
            var chip = new Button { Style = (Style)FindResource("Btn.Chip"), Content = Loc.T(key), ToolTip = sequence };
            chip.Click += (_, _) =>
            {
                SequenceBox.Text = sequence;
                SequenceBox.Focus();
                SequenceBox.CaretIndex = sequence.Length;
            };
            chip.ContextMenu = CopyMenu(sequence);
            ExampleChips.Children.Add(chip);
        }
    }

    private static ContextMenu CopyMenu(string text)
    {
        var copy = new MenuItem { Header = Loc.T("Common.Copy") };
        copy.Click += (_, _) => App.Instance.CopyPlain(text, Loc.F("Toast.Copied", text));
        var menu = new ContextMenu();
        menu.Items.Add(copy);
        return menu;
    }

    /// <summary>
    /// Adds a placeholder: at the caret when the sequence is edited as text, otherwise at the end of the sequence (the
    /// one PassKeeper made becomes a written one). A custom field's name is typed in the text box.
    /// </summary>
    private void Add(string token)
    {
        if (SequenceBox.Visibility != Visibility.Visible && !token.StartsWith("{S:", StringComparison.Ordinal))
        {
            var current = SequenceBox.Text.Trim();
            SequenceBox.Text = (current.Length > 0 ? current : GeneratedSequence()) + token;
            return;
        }
        ShowSequenceText(true);
        Insert(token);
    }

    /// <summary>Inserts a placeholder at the caret of the sequence box.</summary>
    private void Insert(string token)
    {
        var index = Math.Clamp(SequenceBox.CaretIndex, 0, SequenceBox.Text.Length);
        if (SequenceBox.SelectionLength > 0)
        {
            index = SequenceBox.SelectionStart;
            SequenceBox.Text = SequenceBox.Text.Remove(index, SequenceBox.SelectionLength);
        }
        SequenceBox.Text = SequenceBox.Text.Insert(index, token);
        SequenceBox.Focus();
        var caret = index + token.Length;
        if (token.StartsWith("{S:", StringComparison.Ordinal))
        {
            SequenceBox.Select(index + 3, token.Length - 4);
            return;
        }
        SequenceBox.CaretIndex = caret;
    }

    private async void OpenHelp_Click(object sender, RoutedEventArgs e) =>
        await HelpDialog.ShowAsync("autotype");

    // ------------------------------------------------------------------ misc

    private void Any_Changed(object sender, EventArgs e)
    {
        if (_loading) return;
        Error.Visibility = Visibility.Collapsed;
        if (sender == SequenceBox) RenderSequence();
    }

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        var result = await App.Instance.Main.ShowDialogAsync(new GeneratorDialog(pickMode: true));
        if (result is string password)
        {
            PasswordBox.Value = password;
            PasswordBox.IsRevealed = true;
        }
    }

    private void AddField_Click(object sender, RoutedEventArgs e)
    {
        var row = AddFieldRow("", "", false);
        row.Name.Focus();
        UpdateNoFields();
    }

    private (TextBox Name, SecretBox Value, CheckBox Protected, FrameworkElement Row) AddFieldRow(string name, string value, bool isProtected)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameBox = new TextBox { Text = name };
        UI.SetPlaceholder(nameBox, Loc.T("Editor.FieldName"));
        var valueBox = new SecretBox { Value = value, Placeholder = Loc.T("Editor.FieldValue") };
        valueBox.IsRevealed = !isProtected;
        var protect = new CheckBox { IsChecked = isProtected, Margin = new Thickness(12, 0, 4, 0), ToolTip = Loc.T("Editor.FieldProtected"), Content = "\uE72E", FontFamily = (System.Windows.Media.FontFamily)FindResource("Font.Icons") };
        protect.Click += (_, _) => valueBox.IsRevealed = protect.IsChecked != true;
        var remove = new Button { Style = (Style)FindResource("Btn.Icon"), Content = "\uE711", ToolTip = Loc.T("Common.Delete"), Margin = new Thickness(4, 0, 0, 0) };

        Grid.SetColumn(valueBox, 2);
        Grid.SetColumn(protect, 3);
        Grid.SetColumn(remove, 4);
        grid.Children.Add(nameBox);
        grid.Children.Add(valueBox);
        grid.Children.Add(protect);
        grid.Children.Add(remove);
        FieldsPanel.Children.Add(grid);

        var tuple = (nameBox, valueBox, protect, (FrameworkElement)grid);
        _fields.Add(tuple);
        remove.Click += (_, _) =>
        {
            FieldsPanel.Children.Remove(grid);
            _fields.Remove(tuple);
            UpdateNoFields();
        };
        return tuple;
    }

    private void UpdateNoFields() => NoFields.Visibility = _fields.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void Cancel_Click(object sender, RoutedEventArgs e) => Cancelled?.Invoke();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var url = (_category == EntryCategory.Remote ? ServerBox.Text : UrlBox.Text).Trim();
        var title = TitleBox.Text.Trim();
        if (title.Length == 0) title = DomainUtil.GetHost(url) ?? LoginBox.Text.Trim();
        if (title.Length == 0)
        {
            ShowError(Loc.T("Editor.ErrTitle"));
            TitleBox.Focus();
            return;
        }
        var sequence = SequenceBox.Text.Trim();
        if (sequence.Length > 0 && AutoTypeSequence.Validate(sequence) is { } seqError)
        {
            SetAdvanced(true);
            ShowError(Loc.F("Editor.ErrSequence", seqError));
            SequenceBox.Focus();
            return;
        }
        var totp = TotpBox.Text.Trim();
        if (totp.Length > 0 && Totp.Parse(totp) == null)
        {
            ShowError(Loc.T("Editor.ErrTotp"));
            TotpBox.Focus();
            return;
        }
        var client = _category is EntryCategory.Remote or EntryCategory.App;
        if (client && AutoLoginBox.IsChecked == true && Lines(WindowsBox.Text).Count == 0)
        {
            ShowError(Loc.T("Editor.ErrAutoLoginClient"));
            return;
        }

        var e2 = _entry;
        var previousUrls = e2.AllUrls().ToList();
        e2.Category = _category;
        e2.Title = title;
        e2.Username = LoginBox.Text.Trim();
        e2.SetPassword(PasswordBox.Value);
        e2.Url = url;
        e2.Email = EmailBox.Text.Trim();
        e2.Phone = PhoneBox.Text.Trim();
        e2.SecretKey = KeyBox.Value;
        e2.Totp = totp;
        e2.Folder = string.Join("/", FolderBox.Text.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        e2.Notes = NotesBox.Text;
        e2.Favorite = FavoriteBox.IsChecked == true;
        e2.AutoLogin = (client || _category == EntryCategory.Web) && AutoLoginBox.IsChecked == true;
        e2.WindowPatterns = Lines(WindowsBox.Text);
        // A client's or program's other addresses are not edited here: they are the sites its account was chosen on
        // (a domain account on the company's sign-in page) and stay, with that choice, when the entry is saved.
        if (_category == EntryCategory.Web) e2.ExtraUrls = Lines(ExtraUrlsBox.Text);
        e2.ForgetChoicesOfRemovedAddresses(previousUrls);
        e2.AutoTypeSequence = sequence;
        e2.FormSequence = _formSequence;
        e2.CustomFields = _fields
            .Where(f => f.Name.Text.Trim().Length > 0 || f.Value.Value.Length > 0)
            .Select(f => new CustomField { Name = f.Name.Text.Trim().Length > 0 ? f.Name.Text.Trim() : Loc.T("Editor.FieldDefault"), Value = f.Value.Value, Protected = f.Protected.IsChecked == true })
            .ToList();
        e2.SetPinCode(PinBox.Value);
        if (_isNew) e2.CreatedUtc = DateTime.UtcNow;
        Saved?.Invoke(e2);
    }

    private static List<string> Lines(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private void ShowError(string text)
    {
        Error.Text = text;
        Error.Visibility = Visibility.Visible;
    }
}
