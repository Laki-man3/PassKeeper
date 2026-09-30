using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using PassKeeper.Controls;
using PassKeeper.Core.Matching;
using PassKeeper.Core.Models;
using PassKeeper.Localization;
using PassKeeper.Services;
using PassKeeper.ViewModels;

namespace PassKeeper.Windows;

/// <summary>Entry chooser shown by the auto-type hotkey when there is no single obvious match.</summary>
public sealed class AutoTypePickerWindow : Window
{
    private readonly TaskCompletionSource<VaultEntry?> _result = new();
    private readonly CheckBox? _remember;
    private readonly List<EntryItem> _matches;
    private readonly List<EntryItem> _all;
    private readonly TextBox _search = new();
    private readonly ListBox _list = new();
    private readonly TextBlock _hint = new() { FontSize = 11.5, Margin = new Thickness(18, 10, 18, 14) };
    private bool _closing;

    private AutoTypePickerWindow(TargetWindow target, List<EntryMatch> matches, IEnumerable<VaultEntry> all)
    {
        // Nothing matched: whatever is chosen can be remembered for this site / program.
        var rememberFor = matches.Count == 0 ? RememberLabel(target) : null;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        Width = 480;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Title = "PassKeeper";
        FontFamily = (FontFamily)FindResource("Font.Ui");
        SetResourceReference(TextElement.ForegroundProperty, "Brush.Text");

        _matches = matches.Select(m => new EntryItem(m.Entry)).ToList();
        _all = all.Where(e => !e.IsDeleted).OrderBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase).Select(e => new EntryItem(e)).ToList();

        var card = FloatingCard.Create(this);
        var dock = new DockPanel();
        var header = FloatingCard.Header(this, Loc.F("Picker.Target", target.Describe()), () => Finish(null));
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);

        var searchHost = new Grid { Margin = new Thickness(16, 14, 16, 8) };
        _search.Style = (Style)FindResource("TextBox.Search");
        UI.SetPlaceholder(_search, Loc.T("Picker.Search"));
        _search.TextChanged += (_, _) => Refresh();
        _search.PreviewKeyDown += Search_PreviewKeyDown;
        searchHost.Children.Add(_search);
        var icon = new TextBlock { Text = "\uE721", Style = (Style)FindResource("Icon"), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(12, 0, 0, 0), IsHitTestVisible = false };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        searchHost.Children.Add(icon);
        DockPanel.SetDock(searchHost, Dock.Top);
        dock.Children.Add(searchHost);

        _hint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        _hint.Text = Loc.T("Picker.Hint");
        DockPanel.SetDock(_hint, Dock.Bottom);
        dock.Children.Add(_hint);
        if (rememberFor != null)
        {
            _remember = new CheckBox { Content = Loc.F("Picker.Remember", rememberFor), IsChecked = true, Margin = new Thickness(18, 8, 18, 0), Focusable = false };
            DockPanel.SetDock(_remember, Dock.Bottom);
            dock.Children.Add(_remember);
        }

        _list.Style = (Style)FindResource("List.Plain");
        _list.ItemContainerStyle = (Style)FindResource("Item.Card");
        _list.ItemTemplate = BuildTemplate();
        _list.MouseDoubleClick += (_, _) => Choose();
        _list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Choose();
        };
        dock.Children.Add(_list);

        card.Child = dock;
        Content = card;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Finish(null);
        };
        Deactivated += (_, _) => Finish(null);
        Loaded += (_, _) =>
        {
            if (_closing) return; // preview rendering
            Native.ForceForeground(new WindowInteropHelper(this).Handle);
            _search.Focus();
        };
        Refresh();
    }

    private DataTemplate BuildTemplate()
    {
        var factory = new FrameworkElementFactory(typeof(DockPanel));
        factory.SetValue(HeightProperty, 40.0);

        var avatar = new FrameworkElementFactory(typeof(Avatar));
        avatar.SetBinding(Avatar.SourceTextProperty, new System.Windows.Data.Binding(nameof(EntryItem.AvatarSource)));
        avatar.SetValue(Avatar.SizeProperty, 34.0);
        avatar.SetValue(DockPanel.DockProperty, Dock.Left);
        factory.AppendChild(avatar);

        var stack = new FrameworkElementFactory(typeof(StackPanel));
        stack.SetValue(MarginProperty, new Thickness(12, 0, 0, 0));
        stack.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        var title = new FrameworkElementFactory(typeof(TextBlock));
        title.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(EntryItem.Title)));
        title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        title.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        var sub = new FrameworkElementFactory(typeof(TextBlock));
        sub.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(EntryItem.Subtitle)));
        sub.SetValue(TextBlock.FontSizeProperty, 12.0);
        sub.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        sub.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        stack.AppendChild(title);
        stack.AppendChild(sub);
        factory.AppendChild(stack);
        return new DataTemplate { VisualTree = factory };
    }

    private void Refresh()
    {
        var q = _search.Text.Trim();
        IEnumerable<EntryItem> source = q.Length == 0 && _matches.Count > 0
            ? _matches
            : _all.Where(i => EntryMatcher.MatchesSearch(i.Entry, q));
        var list = source.ToList();
        _list.ItemsSource = list;
        if (list.Count > 0) _list.SelectedIndex = 0;
    }

    private void Search_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                _list.SelectedIndex = Math.Min(_list.Items.Count - 1, _list.SelectedIndex + 1);
                _list.ScrollIntoView(_list.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up:
                _list.SelectedIndex = Math.Max(0, _list.SelectedIndex - 1);
                _list.ScrollIntoView(_list.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter:
                Choose();
                e.Handled = true;
                break;
        }
    }

    private void Choose()
    {
        if (_list.SelectedItem is EntryItem item) Finish(item.Entry);
    }

    private void Finish(VaultEntry? entry)
    {
        if (_closing) return;
        _closing = true;
        _result.TrySetResult(entry);
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _result.TrySetResult(null);
        base.OnClosed(e);
    }

    /// <summary>The site or program the chosen entry can be linked to, or null.</summary>
    private static string? RememberLabel(TargetWindow target)
    {
        if (target.IsBrowser) return DomainUtil.GetHost(target.Url);
        return target.ProcessName.Length > 0 ? target.ProcessName + ".exe" : null;
    }

    /// <summary>Whether the user asked to link the chosen entry to the site / program.</summary>
    public bool Remember => _remember?.IsChecked == true;

    internal static AutoTypePickerWindow CreateForPreview(TargetWindow target, List<EntryMatch> matches, IEnumerable<VaultEntry> all) =>
        new(target, matches, all) { _closing = true };

    public static async Task<(VaultEntry? Entry, bool Remember)> PickAsync(TargetWindow target, List<EntryMatch> matches, IEnumerable<VaultEntry> all)
    {
        var w = new AutoTypePickerWindow(target, matches, all);
        w.Show();
        w.Activate();
        var entry = await w._result.Task;
        return (entry, w.Remember);
    }
}
