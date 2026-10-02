using System.Windows;
using System.Windows.Controls;
using PassKeeper.Localization;

namespace PassKeeper.Views;

/// <summary>Details pane placeholder with a small offline security overview.</summary>
public sealed class EmptyDetailsView : UserControl
{
    public EmptyDetailsView()
    {
        var entries = App.Instance.Vault.ActiveEntries.Where(e => e.Password.Length > 0).ToList();
        var reused = entries.GroupBy(e => e.Password).Where(g => g.Count() > 1).Sum(g => g.Count());

        var root = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 460, Margin = new Thickness(32, 40, 32, 32) };

        var icon = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(20), HorizontalAlignment = HorizontalAlignment.Center };
        icon.SetResourceReference(Border.BackgroundProperty, "Brush.AccentSoft");
        var glyph = new TextBlock { Text = "\uEA18", FontSize = 28, Style = (Style)FindResource("Icon") };
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "Brush.AccentText");
        icon.Child = glyph;
        root.Children.Add(icon);

        root.Children.Add(new TextBlock { Text = Loc.T("Details.EmptyTitle"), Style = (Style)FindResource("Text.H2"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 6) });
        root.Children.Add(new TextBlock { Text = Loc.T("Details.EmptyHint"), Style = (Style)FindResource("Text.Muted"), TextAlignment = TextAlignment.Center, FontSize = 13 });

        var stats = new Grid { Margin = new Thickness(0, 26, 0, 0) };
        for (var i = 0; i < 2; i++) stats.ColumnDefinitions.Add(new ColumnDefinition());
        AddStat(stats, 0, entries.Count.ToString(), Loc.T("Details.StatTotal"));
        AddStat(stats, 1, reused.ToString(), Loc.T("Details.StatReused"));
        root.Children.Add(stats);

        var tip = new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, 14, 0, 0), Padding = new Thickness(14, 12, 14, 12) };
        var tipGrid = new Grid();
        tipGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tipGrid.ColumnDefinitions.Add(new ColumnDefinition());
        var tipIcon = new TextBlock { Text = "\uE765", FontSize = 18, Style = (Style)FindResource("Icon"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 12, 0) };
        tipIcon.SetResourceReference(TextBlock.ForegroundProperty, "Brush.AccentText");
        tipGrid.Children.Add(tipIcon);
        var tipText = new TextBlock { Text = Loc.F("Details.AutoTypeTip", App.Instance.Settings.AutoTypeHotkey), Style = (Style)FindResource("Text.Secondary"), FontSize = 12.5 };
        Grid.SetColumn(tipText, 1);
        tipGrid.Children.Add(tipText);
        tip.Child = tipGrid;
        root.Children.Add(tip);

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private void AddStat(Grid grid, int column, string value, string label)
    {
        var card = new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(column == 0 ? 0 : 5, 0, column == 0 ? 5 : 0, 0), Padding = new Thickness(12, 12, 12, 12) };
        var panel = new StackPanel();
        var number = new TextBlock { Text = value, FontSize = 24, FontWeight = FontWeights.SemiBold };
        number.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        panel.Children.Add(number);
        panel.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("Text.Muted"), Margin = new Thickness(0, 2, 0, 0) });
        card.Child = panel;
        Grid.SetColumn(card, column);
        grid.Children.Add(card);
    }
}
