using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PassKeeper.Controls;
using PassKeeper.Core.AutoType;
using PassKeeper.Core.Matching;
using PassKeeper.Core.Models;
using PassKeeper.Core.Security;
using PassKeeper.Localization;

namespace PassKeeper.Views;

/// <summary>Read-only entry card with copy / reveal / open actions.</summary>
public sealed class EntryDetailsView : UserControl
{
    private readonly DispatcherTimer _totpTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
    private VaultEntry _entry = new();
    private Action? _totpTick;

    public EntryDetailsView()
    {
        Content = _scroll;
        _totpTimer.Tick += (_, _) => _totpTick?.Invoke();
        // The 2FA code ticks only while it is on screen (not in the tray, not for entries without TOTP).
        Loaded += (_, _) => UpdateTimer();
        Unloaded += (_, _) => _totpTimer.Stop();
        IsVisibleChanged += (_, _) => UpdateTimer();
    }

    public Guid EntryId => _entry.Id;
    public event Action<VaultEntry>? EditRequested;
    public event Action<VaultEntry>? DeleteRequested;
    public event Action<VaultEntry>? DuplicateRequested;
    public event Action<VaultEntry>? UseForClientRequested;
    public event Action<VaultEntry>? UseForSiteRequested;

    private Style S(string key) => (Style)FindResource(key);

    private void UpdateTimer()
    {
        if (_totpTick != null && IsVisible && IsLoaded) _totpTimer.Start();
        else _totpTimer.Stop();
    }

    public void Show(VaultEntry entry)
    {
        _entry = entry;
        _totpTick = null;
        var offset = _scroll.VerticalOffset;
        var root = new StackPanel { Margin = new Thickness(36, 46, 36, 32), MaxWidth = 760 };
        root.Children.Add(BuildHeader());

        var fields = new StackPanel();
        var login = entry.Username;
        if (login.Length > 0) fields.Children.Add(Row(Loc.T("Field.Login"), login, "\uE77B"));
        if (entry.Password.Length > 0) fields.Children.Add(SecretRow(Loc.T("Field.Password"), entry.Password, "\uE8D7", showStrength: true));
        if (entry.StoredPin().Length > 0) fields.Children.Add(SecretRow(Loc.T("Editor.Pin"), entry.StoredPin(), "\uE928", showStrength: false));
        var urlLabel = Loc.T(entry.EffectiveCategory == EntryCategory.Remote ? "Editor.Server" : "Field.Website");
        foreach (var url in entry.AllUrls()) fields.Children.Add(Row(urlLabel, url, "\uE774", isLink: true));
        if (entry.Email.Length > 0) fields.Children.Add(Row(Loc.T("Field.Email"), entry.Email, "\uE715"));
        if (entry.Phone.Length > 0) fields.Children.Add(Row(Loc.T("Field.Phone"), entry.Phone, "\uE717"));
        if (entry.SecretKey.Length > 0) fields.Children.Add(SecretRow(Loc.T("Field.Key"), entry.SecretKey, "\uE192", showStrength: false));
        if (Totp.Parse(entry.Totp) is { } totp) fields.Children.Add(TotpRow(totp));
        if (fields.Children.Count > 0) root.Children.Add(Card(fields));

        var pinField = entry.FindPinField();
        if (entry.CustomFields.Any(f => f != pinField))
        {
            root.Children.Add(SectionTitle(Loc.T("Details.CustomFields")));
            var custom = new StackPanel();
            foreach (var f in entry.CustomFields.Where(f => f != pinField))
                custom.Children.Add(f.Protected ? SecretRow(f.Name, f.Value, "\uE8D7", false) : Row(f.Name, f.Value, "\uE8EC"));
            root.Children.Add(Card(custom));
        }

        if (entry.Notes.Length > 0)
        {
            root.Children.Add(SectionTitle(Loc.T("Field.Notes")));
            var notes = new TextBox
            {
                Text = entry.Notes,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                MinHeight = 0,
                Background = Brushes.Transparent,
                VerticalContentAlignment = VerticalAlignment.Top,
            };
            root.Children.Add(Card(notes, new Thickness(18, 14, 18, 14)));
        }

        if (entry.WindowPatterns.Count > 0 || entry.AutoTypeSequence.Length > 0)
        {
            root.Children.Add(SectionTitle(Loc.T("Details.AutoType")));
            var at = new StackPanel();
            if (KnownApps.ForPatterns(entry.WindowPatterns) is { } app) at.Children.Add(Row(Loc.T("Editor.Client"), app.Name, "\uE705", copy: false));
            if (entry.WindowPatterns.Count > 0) at.Children.Add(Row(Loc.T("Field.Windows"), string.Join("\n", entry.WindowPatterns), "\uE737", copy: false));
            if (entry.AutoLogin) at.Children.Add(Row(Loc.T("Editor.AutoLogin"), Loc.T("Details.AutoLoginOn"), "\uE768", copy: false));
            if (entry.AutoTypeSequence.Length > 0) at.Children.Add(Row(Loc.T("Field.Sequence"), entry.AutoTypeSequence, "\uE765", copy: false));
            root.Children.Add(Card(at));
        }

        if (entry.PasswordHistory.Count > 0)
        {
            root.Children.Add(SectionTitle(Loc.F("Details.History", entry.PasswordHistory.Count)));
            var history = new StackPanel();
            foreach (var h in entry.PasswordHistory)
                history.Children.Add(SecretRow(h.ChangedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentUICulture), h.Password, "\uE81C", false));
            root.Children.Add(Card(history));
        }

        var meta = new TextBlock { Style = S("Text.Muted"), Margin = new Thickness(4, 16, 0, 0) };
        meta.Text = Loc.F("Details.Meta", entry.CreatedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentUICulture),
            entry.ModifiedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentUICulture));
        if (entry.LastUsedUtc is { } used) meta.Text += " · " + Loc.F("Details.Used", used.ToLocalTime().ToString("g", CultureInfo.CurrentUICulture));
        root.Children.Add(meta);

        _scroll.Content = root;
        _scroll.ScrollToVerticalOffset(offset);
        UpdateTimer();
    }

    private FrameworkElement BuildHeader()
    {
        var e = _entry;
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 22) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new Avatar { SourceText = e.Title.Length > 0 ? e.Title : e.Url, Size = 60 });

        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 12, 0) };
        titles.Children.Add(new TextBlock { Text = e.Title, Style = S("Text.H2"), FontSize = 22, ToolTip = e.Title, TextWrapping = TextWrapping.Wrap });
        var sub = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
        sub.Children.Add(Chip(VaultView.CategoryIcon(e.EffectiveCategory), VaultView.CategoryName(e.EffectiveCategory)));
        if (KnownApps.ForPatterns(e.WindowPatterns) is { } client && client.Name != e.Title) sub.Children.Add(Chip("\uE7F4", client.Name));
        if (e.AutoLogin) sub.Children.Add(Chip("\uE768", Loc.T("Details.AutoLoginChip")));
        if (e.Folder.Length > 0) sub.Children.Add(Chip("\uE8B7", e.Folder.Replace("/", " / ")));
        if (e.EffectiveCategory == EntryCategory.Web && DomainUtil.GetHost(e.Url) is { } host) sub.Children.Add(Chip("\uE774", host));
        if (e.IsDeleted) sub.Children.Add(Chip("\uE74D", Loc.T("Details.InTrash")));
        if (sub.Children.Count > 0) titles.Children.Add(sub);
        Grid.SetColumn(titles, 1);
        grid.Children.Add(titles);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (e.IsDeleted)
        {
            var restore = new Button { Style = S("Btn.Secondary"), Content = Loc.T("Vault.Restore") };
            UI.SetIcon(restore, "\uE7A7");
            restore.Click += (_, _) =>
            {
                App.Instance.Vault.Restore(e.Id);
                App.Instance.Main.ShowToast(Loc.T("Vault.Restored"));
            };
            var purge = new Button { Style = S("Btn.Danger"), Content = Loc.T("Vault.DeleteForever"), Margin = new Thickness(8, 0, 0, 0) };
            UI.SetIcon(purge, "\uE74D");
            purge.Click += (_, _) => DeleteRequested?.Invoke(e);
            actions.Children.Add(restore);
            actions.Children.Add(purge);
        }
        else
        {
            var fav = new Button { Style = S("Btn.Icon"), Content = e.Favorite ? "\uE735" : "\uE734", ToolTip = Loc.T(e.Favorite ? "Vault.Unfavorite" : "Vault.Favorite") };
            if (e.Favorite) fav.SetResourceReference(ForegroundProperty, "Brush.Warning");
            fav.Click += (_, _) => App.Instance.Vault.ToggleFavorite(e.Id);

            var type = new Button { Style = S("Btn.Secondary"), Width = 36, Padding = new Thickness(0), Margin = new Thickness(4, 0, 0, 0), ToolTip = Loc.T("Details.AutoTypeButtonTip") };
            UI.SetIcon(type, "\uE765");
            type.Click += async (_, _) => await App.Instance.AutoType.TypeIntoPreviousWindowAsync(e);

            var edit = new Button { Style = S("Btn.Primary"), Content = Loc.T("Common.Edit"), Margin = new Thickness(8, 0, 0, 0) };
            UI.SetIcon(edit, "\uE70F");
            edit.Click += (_, _) => EditRequested?.Invoke(e);

            var more = new Button { Style = S("Btn.Icon"), Content = "\uE712", ToolTip = Loc.T("Details.More"), Margin = new Thickness(4, 0, 0, 0) };
            more.Click += (_, _) => MoreMenu(e, more).IsOpen = true;

            actions.Children.Add(fav);
            actions.Children.Add(type);
            actions.Children.Add(edit);
            actions.Children.Add(more);
        }
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);
        return grid;
    }

    private ContextMenu MoreMenu(VaultEntry e, Button target)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        menu.Items.Add(MenuItem("\uE8C8", Loc.T("Vault.Duplicate"), () => DuplicateRequested?.Invoke(e)));
        menu.Items.Add(MenuItem("\uE705", Loc.T("Vault.UseForClient"), () => UseForClientRequested?.Invoke(e)));
        if (e.EffectiveCategory != EntryCategory.Web)
            menu.Items.Add(MenuItem("\uE774", Loc.T("Vault.UseForSite"), () => UseForSiteRequested?.Invoke(e)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("\uE74D", Loc.T("Common.Delete"), () => DeleteRequested?.Invoke(e)));
        return menu;
    }

    private static MenuItem MenuItem(string icon, string text, Action click)
    {
        var item = new MenuItem { Header = text, Icon = new TextBlock { Text = icon } };
        item.Click += (_, _) => click();
        return item;
    }

    private Border Chip(string icon, string text)
    {
        var border = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 2, 8, 3), Margin = new Thickness(0, 0, 6, 4) };
        border.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var ic = new TextBlock { Text = icon, Style = S("Icon"), FontSize = 11, Margin = new Thickness(0, 0, 5, 0) };
        ic.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        panel.Children.Add(ic);
        var t = new TextBlock { Text = text, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260 };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        panel.Children.Add(t);
        border.Child = panel;
        return border;
    }

    private TextBlock SectionTitle(string text) =>
        new() { Text = text, Style = S("Text.Label"), Margin = new Thickness(4, 20, 0, 8) };

    private Border Card(UIElement child, Thickness? padding = null)
    {
        var card = new Border { Style = S("Card"), Padding = padding ?? new Thickness(0), Child = child };
        return card;
    }

    private Grid RowShell(string label, string icon, out StackPanel valueHost, out StackPanel actions)
    {
        var grid = new Grid { Background = Brushes.Transparent, MinHeight = 58 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var ic = new TextBlock { Text = icon, Style = S("Icon"), FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
        ic.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        grid.Children.Add(ic);

        valueHost = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 10, 8, 10) };
        var lbl = new TextBlock { Text = label, FontSize = 11.5, Margin = new Thickness(0, 0, 0, 3) };
        lbl.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        valueHost.Children.Add(lbl);
        Grid.SetColumn(valueHost, 1);
        grid.Children.Add(valueHost);

        actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Opacity = 0.55 };
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);
        var a = actions;
        grid.MouseEnter += (_, _) =>
        {
            grid.SetResourceReference(Panel.BackgroundProperty, "Brush.Hover");
            a.Opacity = 1;
        };
        grid.MouseLeave += (_, _) =>
        {
            grid.Background = Brushes.Transparent;
            a.Opacity = 0.55;
        };
        return grid;
    }

    private Button ActionButton(string glyph, string tooltip, Action onClick)
    {
        var b = new Button { Style = S("Btn.Icon"), Content = glyph, ToolTip = tooltip, Width = 32, Height = 32, FontSize = 14 };
        b.Click += (_, _) => onClick();
        return b;
    }

    private FrameworkElement Wrap(Grid row)
    {
        var border = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = row };
        border.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        row.Loaded += (_, _) =>
        {
            if (border.Parent is Panel p && p.Children.IndexOf(border) == p.Children.Count - 1) border.BorderThickness = new Thickness(0);
        };
        return border;
    }

    private FrameworkElement Row(string label, string value, string icon, bool isLink = false, bool copy = true)
    {
        var row = RowShell(label, icon, out var host, out var actions);
        var text = new TextBlock { Text = value, FontSize = 14, TextWrapping = TextWrapping.Wrap };
        if (isLink) text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.AccentText");
        host.Children.Add(text);
        if (isLink && DomainUtil.NormalizeUrlForOpen(value) != null)
            actions.Children.Add(ActionButton("\uE8A7", Loc.T("Details.Open"), () => App.Instance.OpenUrl(value)));
        if (copy) actions.Children.Add(ActionButton("\uE8C8", Loc.T("Common.Copy"), () => App.Instance.CopyPlain(value, Loc.F("Toast.Copied", label))));
        return Wrap(row);
    }

    private FrameworkElement SecretRow(string label, string value, string icon, bool showStrength)
    {
        var row = RowShell(label, icon, out var host, out var actions);
        var text = new TextBlock { Text = new string('•', Math.Min(value.Length, 16)), FontSize = 15, TextWrapping = TextWrapping.Wrap, FontFamily = (FontFamily)FindResource("Font.Mono") };
        host.Children.Add(text);
        if (showStrength)
        {
            var meter = new StrengthMeter { Password = value, Margin = new Thickness(0, 8, 0, 0), MaxWidth = 280, HorizontalAlignment = HorizontalAlignment.Left };
            host.Children.Add(meter);
        }
        var revealed = false;
        Button? eye = null;
        eye = ActionButton("\uE7B3", Loc.T("Common.ShowHide"), () =>
        {
            revealed = !revealed;
            if (revealed) PasswordText.Fill(text, value);
            else
            {
                text.Inlines.Clear();
                text.Text = new string('•', Math.Min(value.Length, 16));
            }
            eye!.Content = revealed ? "\uED1A" : "\uE7B3";
        });
        actions.Children.Add(eye);
        actions.Children.Add(ActionButton("\uE8C8", Loc.T("Common.Copy"), () => App.Instance.CopySecret(value, Loc.F("Toast.CopiedSecret", label), _entry.Id)));
        return Wrap(row);
    }

    private FrameworkElement TotpRow(TotpConfig cfg)
    {
        var row = RowShell(Loc.T("Field.Totp"), "\uE823", out var host, out var actions);
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        var code = new TextBlock { FontSize = 20, FontWeight = FontWeights.SemiBold, FontFamily = (FontFamily)FindResource("Font.Mono") };
        code.SetResourceReference(TextBlock.ForegroundProperty, "Brush.AccentText");
        var ring = new ProgressBar { Width = 60, Height = 4, Maximum = cfg.Period, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var seconds = new TextBlock { FontSize = 12, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        seconds.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        line.Children.Add(code);
        line.Children.Add(ring);
        line.Children.Add(seconds);
        host.Children.Add(line);
        var current = "";
        _totpTick = () =>
        {
            var now = DateTimeOffset.UtcNow;
            current = Totp.Compute(cfg, now);
            code.Text = current.Length == 6 ? current[..3] + " " + current[3..] : current;
            var left = Totp.SecondsRemaining(cfg, now);
            ring.Value = left;
            seconds.Text = left + " " + Loc.T("Details.Sec");
            ring.SetResourceReference(ForegroundProperty, left <= 5 ? "Brush.Danger" : "Brush.Accent");
        };
        _totpTick();
        actions.Children.Add(ActionButton("\uE8C8", Loc.T("Common.Copy"), () => App.Instance.CopySecret(current, Loc.T("Toast.TotpCopied"), _entry.Id)));
        return Wrap(row);
    }
}
