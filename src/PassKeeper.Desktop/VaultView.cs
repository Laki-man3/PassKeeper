using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PassKeeper.Core.Models;
using PassKeeper.Core.Security;
using PassKeeper.Core.Storage;

namespace PassKeeper.Desktop;

/// <summary>Sections and folders on the left, the entries, and the chosen entry with its fields to copy.</summary>
public sealed class VaultView : UserControl
{
    private readonly MainWindow _window;
    private readonly VaultService _vault;
    private readonly ListBox _nav = new() { Margin = new Thickness(8, 0) };
    private readonly ListBox _list = new();
    private readonly ContentControl _details = new();
    private readonly TextBox _search = new() { Margin = new Thickness(0, 0, 8, 0), MinWidth = 220 };
    private readonly TextBlock _status = new() { Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) };
    private readonly DispatcherTimer _totpTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private string _navKey = "all";
    private bool _filling;

    public VaultView(MainWindow window, VaultService vault)
    {
        _window = window;
        _vault = vault;
        AutomationProperties.SetAutomationId(_search, "Search");
        AutomationProperties.SetAutomationId(_list, "EntryList");
        AutomationProperties.SetAutomationId(_nav, "Nav");
        _search.PlaceholderText = Loc.T("Top.Search");
        _search.TextChanged += (_, _) => FillList();
        _nav.SelectionChanged += (_, _) =>
        {
            if (_filling || _nav.SelectedItem is not ListBoxItem { Tag: string key }) return;
            _navKey = key;
            FillList();
        };
        _list.ItemTemplate = new FuncDataTemplate<VaultEntry>((e, _) => e == null ? new TextBlock() : Ui.Column(2,
            new TextBlock { Text = e.Title, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = Subtitle(e), Opacity = 0.65, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis }));
        _list.SelectionChanged += (_, _) => ShowDetails(_list.SelectedItem as VaultEntry);
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            _status.Text = "";
        };

        var top = new DockPanel { Margin = new Thickness(12, 10) };
        var buttons = Ui.Row(
            Named(Ui.Button("+ " + Loc.T("Top.New"), () => _ = NewEntryAsync(), accent: true), "NewEntry"),
            Named(Ui.Button(Loc.T("Top.Import"), () => _ = ImportExport.ImportAsync(_window, _vault, Refresh)), "Import"),
            Named(Ui.Button(Loc.T("Top.Export"), () => _ = ImportExport.ExportAsync(_window, _vault)), "Export"),
            Named(Ui.Button(Loc.T("Top.Generator"), () => _ = new GeneratorWindow(useButton: false).ShowDialog<string?>(_window)), "Generator"),
            Named(Ui.Button(Loc.T("Top.Settings"), () => _ = new SettingsWindow(_window, _vault).ShowDialog(_window)), "Settings"),
            Named(Ui.Button(Loc.T("Top.Lock"), _window.ShowSignIn), "Lock"));
        DockPanel.SetDock(buttons, Dock.Right);
        top.Children.Add(buttons);
        top.Children.Add(Ui.Row(_search, _status));

        _nav.Background = Brushes.Transparent;
        _list.Background = Brushes.Transparent;
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("250,330,*") };
        var navPane = new ScrollViewer { Content = _nav };
        var listPane = new Border { Child = _list, BorderThickness = new Thickness(1, 0), BorderBrush = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)) };
        Grid.SetColumn(navPane, 0);
        Grid.SetColumn(listPane, 1);
        Grid.SetColumn(_details, 2);
        body.Children.Add(navPane);
        body.Children.Add(listPane);
        body.Children.Add(_details);

        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(body);
        Content = root;

        _totpTimer.Tick += (_, _) => UpdateTotp?.Invoke();
        DetachedFromVisualTree += (_, _) =>
        {
            _totpTimer.Stop();
            _statusTimer.Stop();
        };
        KeyDown += (_, e) =>
        {
            if (!Ui.Command(e)) return;
            if (e.Key == Key.F) _search.Focus();
            else if (e.Key == Key.N) _ = NewEntryAsync();
            else if (e.Key == Key.L) _window.ShowSignIn();
            else return;
            e.Handled = true;
        };
        Refresh();
        _search.AttachedToVisualTree += (_, _) => Ui.Later(() => _search.Focus());
    }

    private static T Named<T>(T control, string id) where T : Control
    {
        AutomationProperties.SetAutomationId(control, id);
        return control;
    }

    private static string Subtitle(VaultEntry e) =>
        e.Username.Length > 0 ? e.Username : e.Email.Length > 0 ? e.Email : e.Url;

    private void Refresh(Guid? select = null)
    {
        FillNav();
        FillList(select ?? (_list.SelectedItem as VaultEntry)?.Id);
    }

    private void FillNav()
    {
        _filling = true;
        var active = _vault.ActiveEntries.ToList();
        _nav.Items.Clear();
        void Add(string key, string label, int count)
        {
            var item = new ListBoxItem { Tag = key, Content = new DockPanel { Children = { new TextBlock { Text = count.ToString(CultureInfo.CurrentCulture), Opacity = 0.55, [DockPanel.DockProperty] = Dock.Right }, new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis } } } };
            AutomationProperties.SetName(item, label);
            _nav.Items.Add(item);
        }
        void Header(string text) => _nav.Items.Add(new ListBoxItem { Content = new TextBlock { Text = text.ToUpper(CultureInfo.CurrentCulture), FontSize = 11, Opacity = 0.55 }, IsEnabled = false, Margin = new Thickness(0, 10, 0, 0) });

        Add("all", Loc.T("Nav.All"), active.Count);
        Add("fav", Loc.T("Nav.Favorites"), active.Count(e => e.Favorite));
        Header(Loc.T("Nav.Sections"));
        foreach (var section in EntryCategories.All)
            Add("sec:" + section, Loc.T("Section." + section), active.Count(e => e.EffectiveCategory == section));
        var folders = _vault.Folders();
        if (folders.Count > 0)
        {
            Header(Loc.T("Nav.Folders"));
            foreach (var folder in folders)
                Add("folder:" + folder, folder, active.Count(e => e.Folder == folder || e.Folder.StartsWith(folder + "/", StringComparison.Ordinal)));
        }
        Add("trash", Loc.T("Nav.Trash"), _vault.Data.Entries.Count(e => e.IsDeleted));
        _nav.SelectedItem = _nav.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag as string == _navKey) ?? _nav.Items[0];
        if (_nav.SelectedItem is ListBoxItem { Tag: string key }) _navKey = key;
        _filling = false;
    }

    private IEnumerable<VaultEntry> Filtered()
    {
        var entries = _navKey == "trash" ? _vault.Data.Entries.Where(e => e.IsDeleted) : _vault.ActiveEntries;
        if (_navKey == "fav") entries = entries.Where(e => e.Favorite);
        else if (_navKey.StartsWith("sec:", StringComparison.Ordinal)) entries = entries.Where(e => e.EffectiveCategory.ToString() == _navKey[4..]);
        else if (_navKey.StartsWith("folder:", StringComparison.Ordinal))
        {
            var folder = _navKey[7..];
            entries = entries.Where(e => e.Folder == folder || e.Folder.StartsWith(folder + "/", StringComparison.Ordinal));
        }
        var text = _search.Text?.Trim() ?? "";
        if (text.Length > 0)
            entries = entries.Where(e => Has(e.Title) || Has(e.Username) || Has(e.Url) || Has(e.Email) || Has(e.Folder) || Has(e.Notes) || e.ExtraUrls.Any(Has));
        return entries.OrderBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase);

        bool Has(string value) => value.Contains(text, StringComparison.CurrentCultureIgnoreCase);
    }

    private void FillList(Guid? select = null)
    {
        var entries = Filtered().ToList();
        var keep = select ?? (_list.SelectedItem as VaultEntry)?.Id;
        _list.ItemsSource = entries;
        _list.SelectedItem = entries.FirstOrDefault(e => e.Id == keep) ?? entries.FirstOrDefault();
        if (entries.Count == 0) ShowDetails(null);
    }

    private Action? UpdateTotp;

    private void ShowDetails(VaultEntry? entry)
    {
        _totpTimer.Stop();
        UpdateTotp = null;
        if (entry == null)
        {
            _details.Content = new TextBlock { Text = Loc.T(_list.ItemCount == 0 ? "List.Empty" : "Details.None"), Opacity = 0.6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            return;
        }
        var panel = new StackPanel { Spacing = 2, Margin = new Thickness(28, 20) };
        panel.Children.Add(Ui.Heading(entry.Title, 24));
        var where = Loc.T("Section." + entry.EffectiveCategory) + (entry.Folder.Length > 0 ? " · " + entry.Folder : "");
        panel.Children.Add(Ui.Muted(where));
        panel.Children.Add(new Border { Height = 10 });

        AddField(panel, Loc.T("Field.Login"), entry.Username, secret: false, "CopyLogin");
        AddField(panel, Loc.T("Field.Password"), entry.Password, secret: true, "CopyPassword");
        AddField(panel, Loc.T("Field.Url"), entry.Url, secret: false, "CopyUrl", open: true);
        foreach (var url in entry.ExtraUrls) AddField(panel, "", url, secret: false, null, open: true);
        AddField(panel, Loc.T("Field.Email"), entry.Email, secret: false, null);
        AddField(panel, Loc.T("Field.Phone"), entry.Phone, secret: false, null);
        AddTotp(panel, entry.Totp);
        AddField(panel, Loc.T("Field.Key"), entry.SecretKey, secret: true, null);
        foreach (var field in entry.CustomFields) AddField(panel, field.Name, field.Value, field.Protected, null);
        if (entry.Notes.Length > 0)
        {
            panel.Children.Add(Ui.Label(Loc.T("Field.Notes")));
            panel.Children.Add(new SelectableTextBlock { Text = entry.Notes, TextWrapping = TextWrapping.Wrap });
        }
        panel.Children.Add(new Border { Height = 14 });
        panel.Children.Add(Ui.Muted(Loc.F("Details.Modified", entry.ModifiedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))));

        var actions = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        void Action(string text, Func<Task> run, string id, bool accent = false)
        {
            var b = Named(Ui.Button(text, () => _ = run(), accent), id);
            b.Margin = new Thickness(0, 0, 8, 8);
            actions.Children.Add(b);
        }
        if (entry.IsDeleted)
        {
            Action(Loc.T("Details.Restore"), () =>
            {
                _vault.Restore(entry.Id);
                Refresh();
                return Task.CompletedTask;
            }, "Restore", accent: true);
            Action(Loc.T("Details.DeleteForever"), async () =>
            {
                if (!await Dialogs.Confirm(_window, Loc.F("Confirm.DeleteForever", entry.Title), Loc.T("Details.DeleteForever"))) return;
                _vault.DeletePermanently(entry.Id);
                Refresh();
            }, "DeleteForever");
        }
        else
        {
            Action(Loc.T("Details.AutoType"), () => _window.AutoType.TypeAsync(entry, null), "AutoType", accent: true);
            Action(Loc.T("Details.Edit"), () => EditAsync(entry), "Edit");
            Action(Loc.T(entry.Favorite ? "Details.Unfavorite" : "Details.Favorite"), () =>
            {
                _vault.ToggleFavorite(entry.Id);
                Refresh(entry.Id);
                return Task.CompletedTask;
            }, "Favorite");
            Action(Loc.T("Details.Delete"), async () =>
            {
                if (!await Dialogs.Confirm(_window, Loc.F("Confirm.Delete", entry.Title), Loc.T("Details.Delete"))) return;
                _vault.MoveToTrash(entry.Id);
                Refresh();
            }, "Delete");
        }
        panel.Children.Add(actions);
        _details.Content = new ScrollViewer { Content = panel };
    }

    private void AddField(StackPanel panel, string label, string value, bool secret, string? copyId, bool open = false)
    {
        if (string.IsNullOrEmpty(value)) return;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("160,*,Auto"), Margin = new Thickness(0, 4) };
        var shown = new SelectableTextBlock { Text = secret ? new string('•', Math.Min(value.Length, 16)) : value, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        if (secret) shown.FontFamily = new FontFamily("Menlo, Consolas, DejaVu Sans Mono, monospace");
        var buttons = Ui.Row();
        if (secret)
        {
            var reveal = Ui.Button(Loc.T("Common.Show"), () => { });
            reveal.Click += (_, _) =>
            {
                var hidden = shown.Text != value;
                shown.Text = hidden ? value : new string('•', Math.Min(value.Length, 16));
                reveal.Content = Loc.T(hidden ? "Common.Hide" : "Common.Show");
            };
            buttons.Children.Add(reveal);
        }
        if (open && Uri.TryCreate(value.Contains("://", StringComparison.Ordinal) ? value : "https://" + value, UriKind.Absolute, out var uri))
            buttons.Children.Add(Ui.Button(Loc.T("Details.Open"), () => _ = TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(uri)));
        var copy = Ui.Button(Loc.T("Common.Copy"), () => _ = CopyAsync(value));
        if (copyId != null) AutomationProperties.SetAutomationId(copy, copyId);
        buttons.Children.Add(copy);
        var name = new TextBlock { Text = label, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(shown, 1);
        Grid.SetColumn(buttons, 2);
        row.Children.Add(name);
        row.Children.Add(shown);
        row.Children.Add(buttons);
        panel.Children.Add(row);
    }

    private void AddTotp(StackPanel panel, string totp)
    {
        if (Totp.Parse(totp) is not { } config) return;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("160,*,Auto"), Margin = new Thickness(0, 4) };
        var code = new TextBlock { FontSize = 18, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var left = new TextBlock { Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) };
        var copy = Named(Ui.Button(Loc.T("Common.Copy"), () => _ = CopyAsync(Totp.Compute(config, DateTimeOffset.UtcNow))), "CopyTotp");
        UpdateTotp = () =>
        {
            var now = DateTimeOffset.UtcNow;
            var value = Totp.Compute(config, now);
            code.Text = value.Length == 6 ? value[..3] + " " + value[3..] : value;
            left.Text = Loc.F("Details.Seconds", Totp.SecondsRemaining(config, now));
        };
        UpdateTotp();
        _totpTimer.Start();
        var middle = Ui.Row(code, left);
        Grid.SetColumn(middle, 1);
        Grid.SetColumn(copy, 2);
        row.Children.Add(new TextBlock { Text = Loc.T("Field.Totp"), Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(middle);
        row.Children.Add(copy);
        panel.Children.Add(row);
    }

    private async Task CopyAsync(string value)
    {
        var seconds = await ClipboardService.CopyAsync(this, value);
        ShowStatus(seconds > 0 ? Loc.F("Copied", seconds) : Loc.T("CopiedNoClear"));
    }

    public void ShowStatus(string text)
    {
        _status.Text = text;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private async Task NewEntryAsync()
    {
        var entry = new VaultEntry();
        if (_navKey.StartsWith("folder:", StringComparison.Ordinal)) entry.Folder = _navKey[7..];
        if (_navKey.StartsWith("sec:", StringComparison.Ordinal) && Enum.TryParse<EntryCategory>(_navKey[4..], out var section)) entry.Category = section;
        if (_navKey == "fav") entry.Favorite = true;
        var saved = await new EntryEditorWindow(entry, isNew: true, _vault.Folders()).ShowDialog<VaultEntry?>(_window);
        if (saved == null) return;
        _vault.Upsert(saved);
        if (_navKey == "trash") _navKey = "all";
        Refresh(saved.Id);
    }

    private async Task EditAsync(VaultEntry entry)
    {
        var saved = await new EntryEditorWindow(entry.Clone(), isNew: false, _vault.Folders()).ShowDialog<VaultEntry?>(_window);
        if (saved == null) return;
        _vault.Upsert(saved);
        Refresh(saved.Id);
    }
}
