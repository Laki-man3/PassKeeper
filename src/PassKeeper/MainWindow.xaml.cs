using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using PassKeeper.Services;
using PassKeeper.Views;

namespace PassKeeper;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(2.4) };

    public MainWindow()
    {
        InitializeComponent();
        _toastTimer.Tick += (_, _) => HideToast();
        StateChanged += (_, _) => UpdateMaximizedState();
        SourceInitialized += (_, _) => ThemeService.ApplyWindowFrame(this);
    }

    public bool IsDialogOpen => DialogLayer.Children.Count > 0;

    public void Navigate(UserControl page)
    {
        DialogLayer.Children.Clear();
        PageHost.Content = page;
        page.Opacity = 0;
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
    }

    public UserControl? CurrentPage => PageHost.Content as UserControl;

    public Task<object?> ShowDialogAsync(DialogBase dialog)
    {
        var tcs = new TaskCompletionSource<object?>();
        var previousFocus = Keyboard.FocusedElement;

        var card = new Border
        {
            Child = dialog,
            Background = (Brush)FindResource("Brush.Surface"),
            BorderBrush = (Brush)FindResource("Brush.Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Width = dialog.DialogWidth,
            MaxWidth = Math.Max(320, ActualWidth - 48),
            MaxHeight = Math.Max(300, ActualHeight - 64),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 8, Opacity = 0.35, Color = (Color)FindResource("Color.Shadow") },
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(0.97, 0.97),
        };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");

        var layer = new Grid();
        layer.SetResourceReference(Panel.BackgroundProperty, "Brush.Overlay");
        layer.Children.Add(card);
        DialogLayer.Children.Add(layer);

        layer.Opacity = 0;
        layer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        var scale = new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        card.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        card.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scale);

        dialog.Loaded += (_, _) =>
        {
            if (dialog.InitialFocus != null) Keyboard.Focus(dialog.InitialFocus);
            else dialog.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        };
        dialog.Closed += result =>
        {
            DialogLayer.Children.Remove(layer);
            if (previousFocus != null && DialogLayer.Children.Count == 0) Keyboard.Focus(previousFocus);
            tcs.TrySetResult(result);
        };
        return tcs.Task;
    }

    public void CloseAllDialogs()
    {
        foreach (var layer in DialogLayer.Children.OfType<Grid>().ToList())
            if (layer.Children.OfType<Border>().FirstOrDefault()?.Child is DialogBase d) d.Close(null);
        DialogLayer.Children.Clear();
    }

    public void ShowToast(string text, bool error = false)
    {
        ToastText.Text = text;
        ToastIcon.Text = error ? "\uE783" : "\uE73E";
        ToastIcon.SetResourceReference(TextBlock.ForegroundProperty, error ? "Brush.Danger" : "Brush.Success");
        Toast.Visibility = Visibility.Visible;
        Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromSeconds(error ? 4 : 2.4);
        _toastTimer.Start();
    }

    private void HideToast()
    {
        _toastTimer.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
        fade.Completed += (_, _) => Toast.Visibility = Visibility.Collapsed;
        Toast.BeginAnimation(OpacityProperty, fade);
    }

    private void UpdateMaximizedState()
    {
        Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        MaxButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (App.Instance.IsExiting) return;
        e.Cancel = true;
        App.Instance.OnMainWindowClosing();
    }
}
