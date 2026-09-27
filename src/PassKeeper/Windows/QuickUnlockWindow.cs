using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using PassKeeper.Localization;
using PassKeeper.Services;
using PassKeeper.Views;

namespace PassKeeper.Windows;

/// <summary>Compact PIN / master password prompt used when autofill is requested while the vault is locked.</summary>
public sealed class QuickUnlockWindow : Window
{
    private readonly TaskCompletionSource<bool> _result = new();

    private QuickUnlockWindow(string? target)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Title = "PassKeeper";
        Icon = App.Instance.Main.Icon;
        FontFamily = (FontFamily)FindResource("Font.Ui");
        SetResourceReference(TextElement.ForegroundProperty, "Brush.Text");

        var card = FloatingCard.Create(this);
        var root = new StackPanel { Width = 380, Margin = new Thickness(0, 0, 0, 22) };
        root.Children.Add(FloatingCard.Header(this, target == null ? Loc.T("Quick.Title") : Loc.F("Quick.TitleFor", target), () => Finish(false)));
        var unlock = new UnlockView(compact: true) { Margin = new Thickness(0, 10, 0, 0) };
        unlock.Unlocked += () => Finish(true);
        root.Children.Add(unlock);
        card.Child = root;
        Content = card;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Finish(false);
        };
        Loaded += (_, _) =>
        {
            Native.ForceForeground(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            unlock.FocusInput();
        };
    }

    private void Finish(bool ok)
    {
        _result.TrySetResult(ok);
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _result.TrySetResult(false);
        base.OnClosed(e);
    }

    public static Task<bool> ShowAsync(string? target)
    {
        var w = new QuickUnlockWindow(target);
        w.Show();
        w.Activate();
        return w._result.Task;
    }
}

internal static class FloatingCard
{
    public static Border Create(Window owner)
    {
        var card = new Border
        {
            Margin = new Thickness(24),
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 6, Opacity = 0.35, Color = (Color)owner.FindResource("Color.Shadow") },
        };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return card;
    }

    public static FrameworkElement Header(Window owner, string subtitle, Action onClose)
    {
        var grid = new Grid { Margin = new Thickness(16, 12, 10, 0), Background = Brushes.Transparent };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new Image { Source = (ImageSource)owner.FindResource("LogoImage"), Width = 22, Height = 22 });
        var titles = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = "PassKeeper", FontWeight = FontWeights.SemiBold, FontSize = 13 });
        var sub = new TextBlock { Text = subtitle, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        titles.Children.Add(sub);
        Grid.SetColumn(titles, 1);
        grid.Children.Add(titles);
        var close = new Button { Style = (Style)owner.FindResource("Btn.Icon"), Content = "\uE711", Width = 30, Height = 30, FontSize = 11 };
        close.Click += (_, _) => onClose();
        Grid.SetColumn(close, 2);
        grid.Children.Add(close);
        grid.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) owner.DragMove();
        };
        return grid;
    }
}
