using System.Windows;
using System.Windows.Controls;
using PassKeeper.Controls;
using PassKeeper.Core.AutoType;
using PassKeeper.Core.Matching;
using PassKeeper.Core.Models;
using PassKeeper.Core.Security;
using PassKeeper.Localization;

namespace PassKeeper.Views;

public partial class EntryEditorView : UserControl
{
    private readonly VaultEntry _entry;
    private readonly bool _isNew;
    private readonly string _originalSnapshot;
    private readonly List<(TextBox Name, SecretBox Value, CheckBox Protected, FrameworkElement Row)> _fields = [];
    private bool _loading = true;

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
        EmailBox.Text = entry.Email;
        PhoneBox.Text = entry.Phone;
        KeyBox.Value = entry.SecretKey;
        TotpBox.Text = entry.Totp;
        foreach (var f in folders) FolderBox.Items.Add(f);
        FolderBox.Text = entry.Folder;
        NotesBox.Text = entry.Notes;
        FavoriteBox.IsChecked = entry.Favorite;
        WindowsBox.Text = string.Join(Environment.NewLine, entry.WindowPatterns);
        ExtraUrlsBox.Text = string.Join(Environment.NewLine, entry.ExtraUrls);
        SequenceBox.Text = entry.AutoTypeSequence;
        foreach (var f in entry.CustomFields) AddFieldRow(f.Name, f.Value, f.Protected);
        UpdateNoFields();

        if (isNew && entry.Password.Length == 0)
            PasswordBox.Value = PasswordGenerator.Generate(GeneratorDialog.LastOptions);

        _originalSnapshot = Snapshot();
        _loading = false;
        Loaded += (_, _) =>
        {
            TitleBox.Focus();
            TitleBox.SelectAll();
        };
    }

    public event Action<VaultEntry>? Saved;
    public event Action? Cancelled;

    public bool IsDirty => Snapshot() != _originalSnapshot;

    private string Snapshot() => string.Join("\u0001",
        TitleBox.Text, LoginBox.Text, PasswordBox.Value, UrlBox.Text, EmailBox.Text, PhoneBox.Text, KeyBox.Value, TotpBox.Text,
        FolderBox.Text, NotesBox.Text, FavoriteBox.IsChecked, WindowsBox.Text, ExtraUrlsBox.Text, SequenceBox.Text,
        string.Join("\u0002", _fields.Select(f => f.Name.Text + "\u0003" + f.Value.Value + "\u0003" + f.Protected.IsChecked)));

    private void Any_Changed(object sender, EventArgs e)
    {
        if (_loading) return;
        Error.Visibility = Visibility.Collapsed;
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

    /// <summary>Known sign-in clients (VPN, VDI, token prompts): adds their window patterns and, if needed, a sequence.</summary>
    private void AddApp_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = AddAppButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var app in KnownApps.All)
        {
            var item = new MenuItem { Header = app.Name };
            item.Click += (_, _) => AddApp(app);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void AddApp(KnownApp app)
    {
        var lines = Lines(WindowsBox.Text);
        foreach (var pattern in app.Patterns)
            if (!lines.Contains(pattern, StringComparer.OrdinalIgnoreCase)) lines.Add(pattern);
        WindowsBox.Text = string.Join(Environment.NewLine, lines);
        if (app.Sequence != null && SequenceBox.Text.Trim().Length == 0) SequenceBox.Text = app.Sequence;
        if (TitleBox.Text.Trim().Length == 0) TitleBox.Text = app.Name;
        App.Instance.Main.ShowToast(Loc.F("Editor.AppAdded", app.Name));
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
        var title = TitleBox.Text.Trim();
        var url = UrlBox.Text.Trim();
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

        var e2 = _entry;
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
        e2.WindowPatterns = Lines(WindowsBox.Text);
        e2.ExtraUrls = Lines(ExtraUrlsBox.Text);
        e2.AutoTypeSequence = sequence;
        e2.CustomFields = _fields
            .Where(f => f.Name.Text.Trim().Length > 0 || f.Value.Value.Length > 0)
            .Select(f => new CustomField { Name = f.Name.Text.Trim().Length > 0 ? f.Name.Text.Trim() : Loc.T("Editor.FieldDefault"), Value = f.Value.Value, Protected = f.Protected.IsChecked == true })
            .ToList();
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
