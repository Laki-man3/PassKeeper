using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PassKeeper.Controls;
using PassKeeper.Core.AutoType;
using PassKeeper.Core.Matching;
using PassKeeper.Core.Models;
using PassKeeper.Localization;
using PassKeeper.Services;

namespace PassKeeper.Windows;

/// <summary>
/// Small non-activating card shown next to a focused login/password field in any application.
/// Clicking an entry fills the field without taking focus away from the target window.
/// </summary>
public sealed class SuggestionPopup : Window
{
    private readonly StackPanel _items = new();
    private readonly TextBlock _subtitle = new() { FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _footer = new() { FontSize = 11, Margin = new Thickness(14, 6, 14, 10), TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer _autoHide = new() { Interval = TimeSpan.FromSeconds(20) };
    private LoginField? _field;
    private TargetWindow? _target;
    private HashSet<Guid> _exact = [];

    public SuggestionPopup()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = "PassKeeper";
        FontFamily = (FontFamily)FindResource("Font.Ui");
        SetResourceReference(TextElement.ForegroundProperty, "Brush.Text");

        var card = FloatingCard.Create(this);
        card.Margin = new Thickness(14);
        var root = new StackPanel { Width = 300 };

        var header = new Grid { Margin = new Thickness(14, 10, 6, 6) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new Image { Source = (ImageSource)FindResource("LogoImage"), Width = 18, Height = 18 });
        var titles = new StackPanel { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = "PassKeeper", FontSize = 12, FontWeight = FontWeights.SemiBold });
        _subtitle.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        titles.Children.Add(_subtitle);
        Grid.SetColumn(titles, 1);
        header.Children.Add(titles);
        var close = new Button { Style = (Style)FindResource("Btn.Icon"), Content = "\uE711", Width = 26, Height = 26, FontSize = 10, Focusable = false };
        close.Click += (_, _) => HidePopup();
        Grid.SetColumn(close, 2);
        header.Children.Add(close);
        root.Children.Add(header);

        _items.Margin = new Thickness(6, 0, 6, 0);
        root.Children.Add(_items);
        _footer.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        root.Children.Add(_footer);
        card.Child = root;
        Content = card;

        _autoHide.Tick += (_, _) => HidePopup();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Native.MakeNoActivate(hwnd);
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
            ThemeService.ApplyWindowFrame(this);
        };
    }

    /// <summary>An entry was clicked: the entry, its field, the site or window, and whether it was saved for exactly this address.</summary>
    public event Action<VaultEntry, LoginField, TargetWindow?, bool>? EntryChosen;
    public event Action<LoginField>? UnlockRequested;
    public event Action<LoginField>? CreateRequested;
    public event Action<LoginField, TargetWindow>? ChooseRequested;

    /// <summary>The card belongs to a field of this window.</summary>
    public bool IsFor(IntPtr window) => _field != null && _field.Window == window;

    public void ShowEntries(LoginField field, TargetWindow target, IReadOnlyList<EntryMatch> matches)
    {
        _field = field;
        _target = target;
        _exact = matches.Where(m => m.Score >= 100).Select(m => m.Entry.Id).ToHashSet();
        Fill(matches.Select(m => m.Entry).ToList(), target.Describe(), field.Kind);
        Present(field.Bounds);
    }

    internal void Fill(IReadOnlyList<VaultEntry> entries, string target, FieldKind kind = FieldKind.Login)
    {
        _items.Children.Clear();
        foreach (var e in entries.Take(4)) _items.Children.Add(EntryButton(e, kind));
        _subtitle.Text = Loc.F("Suggest.For", target);
        _footer.Text = Loc.F("Suggest.Footer", App.Instance.Settings.AutoTypeHotkey);
    }

    public void ShowLocked(LoginField field, TargetWindow target)
    {
        _field = field;
        _items.Children.Clear();
        var button = ItemShell(out var panel);
        var icon = new TextBlock { Text = "\uE72E", Style = (Style)FindResource("Icon"), FontSize = 16, Width = 30 };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Brush.AccentText");
        panel.Children.Add(icon);
        var text = new StackPanel { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = Loc.T("Suggest.Unlock"), FontWeight = FontWeights.SemiBold, FontSize = 12.5 });
        var hint = new TextBlock { Text = Loc.T("Suggest.UnlockHint"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        text.Children.Add(hint);
        panel.Children.Add(text);
        button.Click += (_, _) =>
        {
            var f = _field;
            HidePopup();
            if (f != null) UnlockRequested?.Invoke(f);
        };
        _items.Children.Add(button);
        _subtitle.Text = Loc.F("Suggest.For", target.Describe());
        _footer.Text = "";
        Present(field.Bounds);
    }

    /// <summary>A program without an entry: offer to create one for this client (the fields are recognised).</summary>
    public void ShowCreate(LoginField field, TargetWindow target, string clientName)
    {
        _field = field;
        _items.Children.Clear();
        var button = ItemShell(out var panel);
        var icon = new TextBlock { Text = "\uE710", Style = (Style)FindResource("Icon"), FontSize = 16, Width = 30 };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Brush.AccentText");
        panel.Children.Add(icon);
        var text = new StackPanel { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 230 };
        text.Children.Add(new TextBlock { Text = Loc.F("Suggest.Create", clientName), FontWeight = FontWeights.SemiBold, FontSize = 12.5, TextWrapping = TextWrapping.Wrap });
        var hint = new TextBlock { Text = Loc.T("Suggest.CreateHint"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        text.Children.Add(hint);
        panel.Children.Add(text);
        button.Click += (_, _) =>
        {
            var f = _field;
            HidePopup();
            if (f != null) CreateRequested?.Invoke(f);
        };
        _items.Children.Add(button);
        _subtitle.Text = Loc.F("Suggest.For", target.Describe());
        _footer.Text = "";
        Present(field.Bounds);
    }

    /// <summary>A site without entries: any entry can be used (and remembered for the site).</summary>
    public void ShowChoose(LoginField field, TargetWindow target)
    {
        _field = field;
        _items.Children.Clear();
        var button = ItemShell(out var panel);
        var icon = new TextBlock { Text = "\uE721", Style = (Style)FindResource("Icon"), FontSize = 16, Width = 30 };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Brush.AccentText");
        panel.Children.Add(icon);
        var text = new StackPanel { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 230 };
        text.Children.Add(new TextBlock { Text = Loc.T("Suggest.Choose"), FontWeight = FontWeights.SemiBold, FontSize = 12.5 });
        var hint = new TextBlock { Text = Loc.T("Suggest.ChooseHint"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        text.Children.Add(hint);
        panel.Children.Add(text);
        button.Click += (_, _) =>
        {
            var f = _field;
            HidePopup();
            if (f != null) ChooseRequested?.Invoke(f, target);
        };
        _items.Children.Add(button);
        _subtitle.Text = Loc.F("Suggest.For", target.Describe());
        _footer.Text = "";
        Present(field.Bounds);
    }

    public void HidePopup()
    {
        _autoHide.Stop();
        _field = null;
        _target = null;
        if (IsVisible) Hide();
    }

    private Button ItemShell(out StackPanel content)
    {
        content = new StackPanel { Orientation = Orientation.Horizontal };
        var button = new Button
        {
            Style = (Style)FindResource("Btn.Ghost"),
            Height = double.NaN,
            Padding = new Thickness(8, 7, 8, 7),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Focusable = false,
            Content = content,
            Margin = new Thickness(0, 0, 0, 2),
        };
        button.SetResourceReference(ForegroundProperty, "Brush.Text");
        return button;
    }

    private Button EntryButton(VaultEntry entry, FieldKind kind)
    {
        var button = ItemShell(out var panel);
        panel.Children.Add(new Avatar { SourceText = entry.Title.Length > 0 ? entry.Title : entry.Url, Size = 30 });
        var text = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 220 };
        text.Children.Add(new TextBlock { Text = entry.Title, FontWeight = FontWeights.SemiBold, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis });
        var login = AutoTypeService.ValueFor(entry, FieldKind.Login);
        var what = kind switch
        {
            FieldKind.Otp => Loc.T("Suggest.FillOtp"),
            FieldKind.Pin => Loc.T("Suggest.FillPin"),
            FieldKind.Key => Loc.T("Suggest.FillKey"),
            FieldKind.Phone when entry.Phone.Length > 0 => entry.Phone,
            FieldKind.Email when entry.Email.Length > 0 => entry.Email,
            _ => login.Length > 0 ? login : Loc.T("Suggest.PasswordOnly"),
        };
        var sub = new TextBlock { Text = what, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        text.Children.Add(sub);
        panel.Children.Add(text);
        button.Click += (_, _) =>
        {
            var f = _field;
            var target = _target;
            var exact = _exact.Contains(entry.Id);
            HidePopup();
            if (f != null) EntryChosen?.Invoke(entry, f, target, exact);
        };
        return button;
    }

    /// <summary>Places the card under the field (physical screen pixels from UI Automation).</summary>
    private void Present(Rect fieldBounds)
    {
        _autoHide.Stop();
        _autoHide.Start();
        if (!IsVisible)
        {
            Opacity = 0;
            Left = -10000;
            Top = -10000;
            Show();
        }
        UpdateLayout();
        var source = PresentationSource.FromVisual(this);
        var toDip = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = toDip.Transform(new Point(fieldBounds.Left, fieldBounds.Bottom));
        var area = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)fieldBounds.Left, (int)fieldBounds.Bottom)).WorkingArea;
        var screen = new Rect(toDip.Transform(new Point(area.Left, area.Top)), toDip.Transform(new Point(area.Right, area.Bottom)));
        var x = topLeft.X - 14;
        var y = topLeft.Y - 8;
        var height = ActualHeight;
        if (y + height > screen.Bottom)
        {
            var above = toDip.Transform(new Point(fieldBounds.Left, fieldBounds.Top));
            y = above.Y - height + 8;
        }
        Left = Math.Max(screen.Left, Math.Min(x, screen.Right - ActualWidth));
        Top = Math.Max(screen.Top, y);
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(Native.MA_NOACTIVATE);
        }
        return IntPtr.Zero;
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        _autoHide.Stop();
        base.OnPreviewMouseDown(e);
    }
}
