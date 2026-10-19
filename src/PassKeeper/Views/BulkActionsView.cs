using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using PassKeeper.Controls;
using PassKeeper.Core.Matching;
using PassKeeper.Core.Models;
using PassKeeper.Localization;

namespace PassKeeper.Views;

/// <summary>Details pane for several selected entries: which ones are selected and what can be done with all of them at once.</summary>
public sealed class BulkActionsView : UserControl
{
    private const int Listed = 8;

    public BulkActionsView(IReadOnlyList<VaultEntry> selected, int shown, bool trash)
    {
        var root = new StackPanel { Margin = new Thickness(36, 18, 36, 32), MaxWidth = 760 };

        var header = new Grid { Margin = new Thickness(0, 0, 0, 22) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        var icon = new Border { Width = 60, Height = 60, CornerRadius = new CornerRadius(18) };
        icon.SetResourceReference(Border.BackgroundProperty, "Brush.AccentSoft");
        var glyph = new TextBlock { Text = "", FontSize = 26, Style = S("Icon") };
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "Brush.AccentText");
        icon.Child = glyph;
        header.Children.Add(icon);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        var title = new TextBlock { Text = Loc.F("Bulk.Selected", Loc.Plural(selected.Count, "Plural.Entries")), Style = S("Text.H2"), FontSize = 22, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(title, "BulkTitle");
        titles.Children.Add(title);
        titles.Children.Add(new TextBlock { Text = Loc.F("Bulk.OfShown", shown), Style = S("Text.Muted"), FontSize = 13, Margin = new Thickness(0, 4, 0, 0) });
        Grid.SetColumn(titles, 1);
        header.Children.Add(titles);
        root.Children.Add(header);

        var actions = new WrapPanel();
        if (trash)
        {
            actions.Children.Add(Action("BulkRestore", "Btn.Secondary", "", Loc.T("Vault.Restore"), () => RestoreRequested?.Invoke()));
            actions.Children.Add(Action("BulkDelete", "Btn.Danger", "", Loc.T("Vault.DeleteForever"), () => DeleteRequested?.Invoke()));
        }
        else
        {
            if (selected.Any(e => !e.Favorite))
                actions.Children.Add(Action("BulkFavorite", "Btn.Secondary", "", Loc.T("Vault.Favorite"), () => FavoriteRequested?.Invoke(true)));
            if (selected.Any(e => e.Favorite))
                actions.Children.Add(Action("BulkUnfavorite", "Btn.Secondary", "", Loc.T("Vault.Unfavorite"), () => FavoriteRequested?.Invoke(false)));
            actions.Children.Add(Action("BulkDelete", "Btn.Danger", "", Loc.T("Common.Delete"), () => DeleteRequested?.Invoke()));
        }
        if (selected.Count < shown)
            actions.Children.Add(Action("BulkSelectAll", "Btn.Secondary", "", Loc.F("Bulk.SelectAllN", shown), () => SelectAllRequested?.Invoke()));
        actions.Children.Add(Action("BulkClear", "Btn.Ghost", "", Loc.T("Bulk.Clear"), () => ClearRequested?.Invoke()));
        root.Children.Add(actions);

        var list = new StackPanel();
        foreach (var entry in selected.Take(Listed)) list.Children.Add(Row(entry));
        if (selected.Count > Listed)
        {
            var more = new TextBlock { Text = Loc.F("Bulk.More", selected.Count - Listed), Style = S("Text.Muted"), Margin = new Thickness(16, 10, 16, 12) };
            list.Children.Add(more);
        }
        root.Children.Add(new Border { Style = S("Card"), Padding = new Thickness(0, 6, 0, 6), Margin = new Thickness(0, 14, 0, 0), Child = list });

        root.Children.Add(new TextBlock { Text = Loc.T("Bulk.Hint"), Style = S("Text.Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 14, 0, 0) });

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
    }

    public event Action<bool>? FavoriteRequested;
    public event Action? DeleteRequested;
    public event Action? RestoreRequested;
    public event Action? SelectAllRequested;
    public event Action? ClearRequested;

    private Style S(string key) => (Style)FindResource(key);

    private Button Action(string id, string style, string glyph, string text, Action click)
    {
        var button = new Button { Style = S(style), Content = text, Margin = new Thickness(0, 0, 8, 8) };
        UI.SetIcon(button, glyph);
        AutomationProperties.SetAutomationId(button, id);
        button.Click += (_, _) => click();
        return button;
    }

    private FrameworkElement Row(VaultEntry entry)
    {
        var grid = new Grid { Margin = new Thickness(14, 6, 14, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new Avatar { SourceText = entry.Title.Length > 0 ? entry.Title : entry.Url, Size = 28 });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        text.Children.Add(new TextBlock { Text = entry.Title.Length > 0 ? entry.Title : "—", FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        var login = entry.Username.Length > 0 ? entry.Username : entry.Email.Length > 0 ? entry.Email : DomainUtil.GetHost(entry.Url) ?? "";
        if (login.Length > 0)
            text.Children.Add(new TextBlock { Text = login, Style = S("Text.Muted"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }
}
