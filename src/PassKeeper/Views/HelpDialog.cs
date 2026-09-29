using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using PassKeeper.Localization;

namespace PassKeeper.Views;

/// <summary>
/// Built-in user manual (docs/manual.ru.md, docs/manual.en.md embedded into the program: it works offline).
/// Text can be selected and copied; code blocks have a copy button.
/// </summary>
public sealed class HelpDialog : DialogBase
{
    private readonly FlowDocumentScrollViewer _viewer = new() { IsToolBarVisible = false, Focusable = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly ListBox _toc = new();
    private readonly Dictionary<string, Block> _anchors = new(StringComparer.OrdinalIgnoreCase);

    private HelpDialog(string? anchor)
    {
        DialogWidth = 1000;
        var main = App.Instance.Main;
        Height = Math.Max(420, main.ActualHeight - 90);

        var dock = new DockPanel();
        var header = new Grid { Margin = new Thickness(26, 18, 18, 10) };
        header.Children.Add(new TextBlock { Text = Loc.T("Help.Title"), Style = (Style)FindResource("Text.H2"), FontSize = 18, VerticalAlignment = VerticalAlignment.Center });
        var close = new Button { Style = (Style)FindResource("Btn.Icon"), Content = "\uE711", HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close(null);
        header.Children.Add(close);
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        body.ColumnDefinitions.Add(new ColumnDefinition());
        _toc.Style = (Style)FindResource("List.Plain");
        _toc.ItemContainerStyle = (Style)FindResource("Item.Nav");
        _toc.Margin = new Thickness(6, 0, 0, 16);
        _toc.SelectionChanged += (_, _) =>
        {
            if (_toc.SelectedItem is ListBoxItem { Tag: string id }) ScrollTo(id);
        };
        body.Children.Add(_toc);

        var border = new Border { BorderThickness = new Thickness(1, 0, 0, 0) };
        border.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        border.Child = _viewer;
        Grid.SetColumn(border, 1);
        body.Children.Add(border);
        dock.Children.Add(body);
        Content = dock;

        var copy = new MenuItem { Header = Loc.T("Common.Copy"), Command = ApplicationCommands.Copy, CommandTarget = _viewer };
        var selectAll = new MenuItem { Header = Loc.T("Common.SelectAll"), Command = ApplicationCommands.SelectAll, CommandTarget = _viewer };
        _viewer.ContextMenu = new ContextMenu { Items = { copy, selectAll } };

        _viewer.Document = BuildDocument(LoadManual());
        InitialFocus = _viewer;
        Loaded += (_, _) =>
        {
            if (anchor != null) Dispatcher.BeginInvoke(() => ScrollTo(anchor), System.Windows.Threading.DispatcherPriority.Loaded);
        };
    }

    public static Task ShowAsync(string? anchor = null) => App.Instance.Main.ShowDialogAsync(new HelpDialog(anchor));

    internal static HelpDialog Create(string? anchor) => new(anchor);

    private static string LoadManual()
    {
        var name = Loc.I.IsRussian ? "manual.ru.md" : "manual.en.md";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        if (stream == null) return "# " + name;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private void ScrollTo(string id)
    {
        if (_anchors.TryGetValue(id, out var block)) block.BringIntoView();
    }

    // ------------------------------------------------------------------ markdown subset → FlowDocument

    private static readonly Regex HeadingAnchor = new(@"\s*\{#([a-z0-9\-]+)\}\s*$", RegexOptions.IgnoreCase);
    private static readonly Regex Inline = new(@"(\*\*(?<b>.+?)\*\*)|(`(?<c>[^`]+)`)|(\[(?<lt>[^\]]+)\]\((?<lu>[^)]+)\))", RegexOptions.Compiled);

    private FlowDocument BuildDocument(string markdown)
    {
        var doc = new FlowDocument
        {
            FontFamily = (FontFamily)FindResource("Font.Ui"),
            FontSize = 13.5,
            PagePadding = new Thickness(28, 6, 28, 28),
            ColumnWidth = double.PositiveInfinity,
            LineHeight = 21,
            TextAlignment = TextAlignment.Left,
        };
        doc.SetResourceReference(FlowDocument.ForegroundProperty, "Brush.Text");
        doc.SetResourceReference(FlowDocument.BackgroundProperty, "Brush.Surface");

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var paragraph = new List<string>();
        List? list = null;
        var tocFirst = true;

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            var p = new Paragraph { Margin = new Thickness(0, 0, 0, 10) };
            AddInlines(p.Inlines, string.Join(" ", paragraph));
            doc.Blocks.Add(p);
            paragraph.Clear();
        }

        void FlushList()
        {
            if (list == null) return;
            doc.Blocks.Add(list);
            list = null;
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph();
                FlushList();
                var code = new List<string>();
                for (i++; i < lines.Length && !lines[i].Trim().StartsWith("```", StringComparison.Ordinal); i++) code.Add(lines[i]);
                doc.Blocks.Add(CodeBlock(string.Join(Environment.NewLine, code)));
                continue;
            }
            if (trimmed.Length == 0)
            {
                FlushParagraph();
                FlushList();
                continue;
            }
            if (trimmed.StartsWith('#'))
            {
                FlushParagraph();
                FlushList();
                var level = trimmed.TakeWhile(ch => ch == '#').Count();
                var text = trimmed[level..].Trim();
                string? id = null;
                var m = HeadingAnchor.Match(text);
                if (m.Success)
                {
                    id = m.Groups[1].Value;
                    text = text[..m.Index];
                }
                var heading = new Paragraph
                {
                    FontSize = level switch { 1 => 24, 2 => 19, _ => 15.5 },
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, level == 1 ? 8 : level == 2 ? 22 : 14, 0, 8),
                };
                AddInlines(heading.Inlines, text);
                doc.Blocks.Add(heading);
                if (level == 2)
                {
                    id ??= "s" + _toc.Items.Count;
                    var item = new ListBoxItem { Content = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis }, Tag = id };
                    _toc.Items.Add(item);
                    if (tocFirst) tocFirst = false;
                }
                if (id != null) _anchors[id] = heading;
                continue;
            }
            if (trimmed.StartsWith('|'))
            {
                FlushParagraph();
                FlushList();
                var rows = new List<string>();
                for (; i < lines.Length && lines[i].Trim().StartsWith('|'); i++) rows.Add(lines[i].Trim());
                i--;
                doc.Blocks.Add(TableBlock(rows));
                continue;
            }
            if (trimmed.StartsWith("> ", StringComparison.Ordinal))
            {
                FlushParagraph();
                FlushList();
                var note = new Paragraph { Margin = new Thickness(0, 0, 0, 12), Padding = new Thickness(14, 10, 14, 10), BorderThickness = new Thickness(3, 0, 0, 0) };
                note.SetResourceReference(Block.BorderBrushProperty, "Brush.Accent");
                note.SetResourceReference(TextElement.BackgroundProperty, "Brush.AccentSoft");
                AddInlines(note.Inlines, trimmed[2..]);
                doc.Blocks.Add(note);
                continue;
            }
            var bullet = trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal);
            var numbered = Regex.Match(trimmed, @"^\d+\.\s");
            if (bullet || numbered.Success)
            {
                FlushParagraph();
                var marker = bullet ? TextMarkerStyle.Disc : TextMarkerStyle.Decimal;
                if (list == null || list.MarkerStyle != marker)
                {
                    FlushList();
                    list = new List { MarkerStyle = marker, Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(22, 0, 0, 0) };
                }
                var itemParagraph = new Paragraph { Margin = new Thickness(0, 0, 0, 4) };
                AddInlines(itemParagraph.Inlines, bullet ? trimmed[2..] : trimmed[numbered.Length..]);
                list.ListItems.Add(new ListItem(itemParagraph));
                continue;
            }
            if (list != null && line.StartsWith("  ", StringComparison.Ordinal) && list.ListItems.LastListItem?.Blocks.LastBlock is Paragraph last)
            {
                last.Inlines.Add(new Run(" "));
                AddInlines(last.Inlines, trimmed);
                continue;
            }
            FlushList();
            paragraph.Add(trimmed);
        }
        FlushParagraph();
        FlushList();
        return doc;
    }

    private void AddInlines(InlineCollection target, string text)
    {
        var pos = 0;
        foreach (Match m in Inline.Matches(text))
        {
            if (m.Index > pos) target.Add(new Run(text[pos..m.Index]));
            if (m.Groups["b"].Success) target.Add(new Bold(new Run(m.Groups["b"].Value)));
            else if (m.Groups["c"].Success)
            {
                var run = new Run(m.Groups["c"].Value) { FontFamily = (FontFamily)FindResource("Font.Mono"), FontSize = 12.5 };
                run.SetResourceReference(TextElement.BackgroundProperty, "Brush.SurfaceAlt");
                run.SetResourceReference(TextElement.ForegroundProperty, "Brush.AccentText");
                target.Add(run);
            }
            else
            {
                var url = m.Groups["lu"].Value;
                var link = new Hyperlink(new Run(m.Groups["lt"].Value));
                link.SetResourceReference(TextElement.ForegroundProperty, "Brush.AccentText");
                if (url.StartsWith('#')) link.Click += (_, _) => ScrollTo(url[1..]);
                else link.ToolTip = url;
                target.Add(link);
            }
            pos = m.Index + m.Length;
        }
        if (pos < text.Length) target.Add(new Run(text[pos..]));
    }

    private Block CodeBlock(string code)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var box = new TextBox
        {
            Text = code,
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontFamily = (FontFamily)FindResource("Font.Mono"),
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 0,
            Padding = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        grid.Children.Add(box);
        var copy = new Button { Style = (Style)FindResource("Btn.Chip"), Content = Loc.T("Common.Copy"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10, 0, 0, 0) };
        copy.Click += (_, _) => App.Instance.CopyPlain(code, Loc.T("Help.Copied"));
        Grid.SetColumn(copy, 1);
        grid.Children.Add(copy);
        var border = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10, 10, 6), BorderThickness = new Thickness(1), Child = grid };
        border.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
        border.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return new BlockUIContainer(border) { Margin = new Thickness(0, 0, 0, 12) };
    }

    private Block TableBlock(List<string> rows)
    {
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 12), BorderThickness = new Thickness(1) };
        table.SetResourceReference(Table.BorderBrushProperty, "Brush.Border");
        var cells = rows.Where(r => !Regex.IsMatch(r, @"^\|[\s\-:|]+\|$"))
            .Select(r => r.Trim('|').Split('|').Select(c => c.Trim()).ToList())
            .ToList();
        if (cells.Count == 0) return table;
        var columns = cells.Max(c => c.Count);
        for (var c = 0; c < columns; c++) table.Columns.Add(new TableColumn());
        var group = new TableRowGroup();
        for (var r = 0; r < cells.Count; r++)
        {
            var row = new TableRow();
            if (r == 0) row.SetResourceReference(TextElement.BackgroundProperty, "Brush.SurfaceAlt");
            for (var c = 0; c < columns; c++)
            {
                var p = new Paragraph { Margin = new Thickness(0) };
                if (r == 0) p.FontWeight = FontWeights.SemiBold;
                AddInlines(p.Inlines, c < cells[r].Count ? cells[r][c] : "");
                var cell = new TableCell(p) { Padding = new Thickness(10, 6, 10, 6), BorderThickness = new Thickness(0, 0, 0, 1) };
                cell.SetResourceReference(TableCell.BorderBrushProperty, "Brush.Border");
                row.Cells.Add(cell);
            }
            group.Rows.Add(row);
        }
        table.RowGroups.Add(group);
        return table;
    }
}
