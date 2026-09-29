using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PassKeeper.Controls;
using PassKeeper.Core.AutoType;
using PassKeeper.Core.Matching;
using PassKeeper.Core.Models;
using PassKeeper.Localization;
using PassKeeper.Services;

namespace PassKeeper.Views;

/// <summary>One choice in a <see cref="SearchListDialog"/>.</summary>
public sealed record PickerRow(string Title, string Subtitle, object Value, string? Icon = null, string? Badge = null, string SearchText = "");

/// <summary>Dialog with a search box and grouped, clickable rows; the chosen row's value is the result.</summary>
public abstract class SearchListDialog : DialogBase
{
    private readonly TextBox _search = new();
    private readonly StackPanel _rows = new() { Margin = new Thickness(14, 0, 14, 14) };
    private readonly TextBlock _status = new() { Margin = new Thickness(26, 4, 26, 14), TextWrapping = TextWrapping.Wrap };
    private Button? _first;

    protected SearchListDialog(string title, string subtitle)
    {
        DialogWidth = 580;
        var dock = new DockPanel { MaxHeight = 640 };

        var header = new Grid { Margin = new Thickness(26, 20, 18, 0) };
        header.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("Text.H2"), FontSize = 18, VerticalAlignment = VerticalAlignment.Center });
        var close = new Button { Style = (Style)FindResource("Btn.Icon"), Content = "\uE711", HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close(null);
        header.Children.Add(close);
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);

        var sub = new TextBlock { Text = subtitle, Style = (Style)FindResource("Text.Secondary"), Margin = new Thickness(26, 6, 26, 12), TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(sub, Dock.Top);
        dock.Children.Add(sub);

        var searchHost = new Grid { Margin = new Thickness(26, 0, 26, 10) };
        _search.Style = (Style)FindResource("TextBox.Search");
        UI.SetPlaceholder(_search, Loc.T("Picker.Search"));
        _search.TextChanged += (_, _) => Rebuild();
        _search.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && _first != null)
            {
                _first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                e.Handled = true;
            }
            else if (e.Key == Key.Down && _first != null)
            {
                _first.Focus();
                e.Handled = true;
            }
        };
        searchHost.Children.Add(_search);
        var icon = new TextBlock { Text = "\uE721", Style = (Style)FindResource("Icon"), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(12, 0, 0, 0), IsHitTestVisible = false };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        searchHost.Children.Add(icon);
        DockPanel.SetDock(searchHost, Dock.Top);
        dock.Children.Add(searchHost);

        _status.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        DockPanel.SetDock(_status, Dock.Bottom);
        dock.Children.Add(_status);

        dock.Children.Add(new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false });
        Content = dock;
        InitialFocus = _search;
    }

    protected string Query => _search.Text.Trim();

    protected void SetStatus(string text)
    {
        _status.Text = text;
        _status.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Groups of rows for the current search text.</summary>
    protected abstract IEnumerable<(string Section, IReadOnlyList<PickerRow> Rows)> Sections();

    protected void Rebuild()
    {
        _rows.Children.Clear();
        _first = null;
        var query = Query;
        foreach (var (section, rows) in Sections())
        {
            var visible = rows.Where(r => query.Length == 0 || Contains(r.Title, query) || Contains(r.Subtitle, query) || Contains(r.SearchText, query)).ToList();
            if (visible.Count == 0) continue;
            _rows.Children.Add(new TextBlock { Text = section.ToUpperInvariant(), Style = (Style)FindResource("Text.Label"), FontSize = 11, Margin = new Thickness(12, 10, 0, 4) });
            foreach (var row in visible) _rows.Children.Add(RowButton(row));
        }
        if (_rows.Children.Count == 0)
            _rows.Children.Add(new TextBlock { Text = Loc.T("Vault.NothingFound"), Style = (Style)FindResource("Text.Muted"), Margin = new Thickness(12, 16, 12, 16) });

        static bool Contains(string s, string q) => s.Contains(q, StringComparison.CurrentCultureIgnoreCase);
    }

    private Button RowButton(PickerRow row)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        FrameworkElement lead;
        if (row.Icon != null)
        {
            var box = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(10) };
            box.SetResourceReference(Border.BackgroundProperty, "Brush.AccentSoft");
            var glyph = new TextBlock { Text = row.Icon, Style = (Style)FindResource("Icon"), FontSize = 15 };
            glyph.SetResourceReference(TextBlock.ForegroundProperty, "Brush.AccentText");
            box.Child = glyph;
            lead = box;
        }
        else lead = new Avatar { SourceText = row.Title, Size = 34 };
        grid.Children.Add(lead);

        var texts = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = row.Title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        if (row.Subtitle.Length > 0)
        {
            var sub = new TextBlock { Text = row.Subtitle, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
            sub.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            texts.Children.Add(sub);
        }
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);

        if (row.Badge != null)
        {
            var badge = new Border { Style = (Style)FindResource("Badge"), VerticalAlignment = VerticalAlignment.Center };
            var t = new TextBlock { Text = row.Badge, FontSize = 11, FontWeight = FontWeights.SemiBold };
            t.SetResourceReference(TextBlock.ForegroundProperty, "Brush.AccentText");
            badge.Child = t;
            Grid.SetColumn(badge, 2);
            grid.Children.Add(badge);
        }

        var button = new Button { Style = (Style)FindResource("Btn.Row"), Content = grid };
        button.Click += (_, _) => Close(row.Value);
        _first ??= button;
        return button;
    }
}

/// <summary>
/// Chooses the sign-in client or program of an entry. Clients running now and installed ones are found
/// automatically and listed first; any open window can be taken as well.
/// </summary>
public sealed class ClientPickerDialog : SearchListDialog
{
    private List<DetectedWindow> _running = [];
    private List<KnownApp> _installed = [];
    private List<DetectedWindow> _windows = [];

    public ClientPickerDialog() : base(Loc.T("ClientPicker.Title"), Loc.T("ClientPicker.Subtitle"))
    {
        SetStatus(Loc.T("ClientPicker.Searching"));
        Rebuild();
        Loaded += async (_, _) =>
        {
            var (running, installed, windows) = await Task.Run(() =>
            {
                var r = ClientDetector.RunningClients();
                var i = ClientDetector.InstalledClients();
                var w = ClientDetector.OpenWindows().Where(x => x.App == null).ToList();
                return (r, i, w);
            });
            _running = running;
            _installed = installed.Where(a => running.All(r => r.App?.Id != a.Id)).ToList();
            _windows = windows;
            SetStatus(_running.Count + _installed.Count == 0 ? Loc.T("ClientPicker.NoneFound") : "");
            Rebuild();
        };
    }

    protected override IEnumerable<(string Section, IReadOnlyList<PickerRow> Rows)> Sections()
    {
        yield return (Loc.T("ClientPicker.Running"), _running.Select(w => Row(w.App!, Loc.F("ClientPicker.RunningInfo", w.ProcessName + ".exe"), w, Loc.T("ClientPicker.RunningBadge"))).ToList());
        yield return (Loc.T("ClientPicker.Installed"), _installed.Select(a => Row(a, Loc.T("ClientPicker.InstalledInfo"), new DetectedWindow { App = a }, null)).ToList());
        yield return (Loc.T("ClientPicker.Windows"), _windows.Select(w =>
            new PickerRow(w.Title, w.ProcessName + ".exe", w, VaultView.CategoryIcon(EntryCategory.App), SearchText: w.ProcessName)).ToList());
        var shown = _running.Select(r => r.App!.Id).Concat(_installed.Select(a => a.Id)).ToHashSet();
        yield return (Loc.T("ClientPicker.Catalog"), KnownApps.All.Where(a => !shown.Contains(a.Id))
            .Select(a => Row(a, VaultView.CategoryName(a.Category), new DetectedWindow { App = a }, null)).ToList());
    }

    private static PickerRow Row(KnownApp app, string subtitle, DetectedWindow value, string? badge) =>
        new(app.Name, subtitle, value, VaultView.CategoryIcon(app.Category), badge, string.Join(" ", app.Patterns));
}

/// <summary>Chooses another entry to take the login and password from (sites first).</summary>
public sealed class EntryPickerDialog : SearchListDialog
{
    private readonly Guid _exclude;

    public EntryPickerDialog(Guid exclude) : base(Loc.T("EntryPicker.Title"), Loc.T("EntryPicker.Subtitle"))
    {
        _exclude = exclude;
        Rebuild();
    }

    protected override IEnumerable<(string Section, IReadOnlyList<PickerRow> Rows)> Sections()
    {
        var entries = App.Instance.Vault.ActiveEntries
            .Where(e => e.Id != _exclude && (e.Password.Length > 0 || e.Username.Length > 0))
            .OrderBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        foreach (var category in EntryCategories.All)
        {
            yield return (VaultView.CategoryName(category), entries.Where(e => e.EffectiveCategory == category)
                .Select(e => new PickerRow(e.Title, Subtitle(e), e, SearchText: e.Url + " " + e.Email)).ToList());
        }
    }

    private static string Subtitle(VaultEntry e)
    {
        var login = e.Username.Length > 0 ? e.Username : e.Email;
        var host = DomainUtil.GetHost(e.Url);
        return host != null && login.Length > 0 ? login + " · " + host : login.Length > 0 ? login : host ?? "";
    }
}
