using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PassKeeper.Controls;

/// <summary>Attached properties used by the control templates in Themes/Controls.xaml.</summary>
public static class UI
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(string), typeof(UI), new FrameworkPropertyMetadata(null));
    public static string? GetIcon(DependencyObject o) => (string?)o.GetValue(IconProperty);
    public static void SetIcon(DependencyObject o, string? v) => o.SetValue(IconProperty, v);

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached(
        "Placeholder", typeof(string), typeof(UI), new FrameworkPropertyMetadata(null, OnPlaceholderChanged));
    public static string? GetPlaceholder(DependencyObject o) => (string?)o.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject o, string? v) => o.SetValue(PlaceholderProperty, v);

    public static readonly DependencyProperty HasTextProperty = DependencyProperty.RegisterAttached(
        "HasText", typeof(bool), typeof(UI), new FrameworkPropertyMetadata(false));
    public static bool GetHasText(DependencyObject o) => (bool)o.GetValue(HasTextProperty);
    public static void SetHasText(DependencyObject o, bool v) => o.SetValue(HasTextProperty, v);

    public static readonly DependencyProperty HoverBackgroundProperty = DependencyProperty.RegisterAttached(
        "HoverBackground", typeof(Brush), typeof(UI), new FrameworkPropertyMetadata(null));
    public static Brush? GetHoverBackground(DependencyObject o) => (Brush?)o.GetValue(HoverBackgroundProperty);
    public static void SetHoverBackground(DependencyObject o, Brush? v) => o.SetValue(HoverBackgroundProperty, v);

    public static readonly DependencyProperty PressedBackgroundProperty = DependencyProperty.RegisterAttached(
        "PressedBackground", typeof(Brush), typeof(UI), new FrameworkPropertyMetadata(null));
    public static Brush? GetPressedBackground(DependencyObject o) => (Brush?)o.GetValue(PressedBackgroundProperty);
    public static void SetPressedBackground(DependencyObject o, Brush? v) => o.SetValue(PressedBackgroundProperty, v);

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(CornerRadius), typeof(UI), new FrameworkPropertyMetadata(new CornerRadius(8)));
    public static CornerRadius GetCornerRadius(DependencyObject o) => (CornerRadius)o.GetValue(CornerRadiusProperty);
    public static void SetCornerRadius(DependencyObject o, CornerRadius v) => o.SetValue(CornerRadiusProperty, v);

    /// <summary>Secondary line of text for card-like radio buttons.</summary>
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.RegisterAttached(
        "Description", typeof(string), typeof(UI), new FrameworkPropertyMetadata(null));
    public static string? GetDescription(DependencyObject o) => (string?)o.GetValue(DescriptionProperty);
    public static void SetDescription(DependencyObject o, string? v) => o.SetValue(DescriptionProperty, v);

    /// <summary>
    /// Rotates the element while it is visible. A storyboard started by a trigger would keep ticking at the display
    /// frame rate after the element is hidden or removed — a constant load in the background.
    /// </summary>
    public static readonly DependencyProperty SpinProperty = DependencyProperty.RegisterAttached(
        "Spin", typeof(bool), typeof(UI), new FrameworkPropertyMetadata(false, OnSpinChanged));
    public static bool GetSpin(DependencyObject o) => (bool)o.GetValue(SpinProperty);
    public static void SetSpin(DependencyObject o, bool v) => o.SetValue(SpinProperty, v);

    private static void OnSpinChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || e.NewValue is not true) return;
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        var rotate = new RotateTransform();
        element.RenderTransform = rotate;
        void Update()
        {
            if (element.IsVisible && element.IsLoaded)
                rotate.BeginAnimation(RotateTransform.AngleProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
            else rotate.BeginAnimation(RotateTransform.AngleProperty, null);
        }
        element.IsVisibleChanged += (_, _) => Update();
        element.Loaded += (_, _) => Update();
        element.Unloaded += (_, _) => rotate.BeginAnimation(RotateTransform.AngleProperty, null);
    }

    /// <summary>
    /// For a grid of two fields side by side (columns: field, gap, field): below this width the second field moves
    /// under the first and both take the whole width, so nothing is squeezed when a pane is made narrow.
    /// </summary>
    public static readonly DependencyProperty StackBelowProperty = DependencyProperty.RegisterAttached(
        "StackBelow", typeof(double), typeof(UI), new FrameworkPropertyMetadata(0.0, OnStackBelowChanged));
    public static double GetStackBelow(DependencyObject o) => (double)o.GetValue(StackBelowProperty);
    public static void SetStackBelow(DependencyObject o, double v) => o.SetValue(StackBelowProperty, v);

    private static readonly DependencyProperty StackedProperty = DependencyProperty.RegisterAttached(
        "Stacked", typeof(bool?), typeof(UI), new FrameworkPropertyMetadata(null));

    private static void OnStackBelowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Set from XAML before the columns are added: they are checked when the grid gets its size.
        if (d is not Grid grid) return;
        grid.SizeChanged -= StackGrid;
        grid.SizeChanged += StackGrid;
    }

    private static void StackGrid(object sender, SizeChangedEventArgs e)
    {
        var grid = (Grid)sender;
        if (grid.ColumnDefinitions.Count != 3) return;
        var stacked = grid.ActualWidth < GetStackBelow(grid);
        if (grid.GetValue(StackedProperty) is bool current && current == stacked) return;
        grid.SetValue(StackedProperty, stacked);
        if (grid.RowDefinitions.Count == 0)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        grid.ColumnDefinitions[1].Width = new GridLength(stacked ? 0 : 14);
        grid.ColumnDefinitions[2].Width = stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        foreach (UIElement child in grid.Children)
        {
            if (child is not FrameworkElement element) continue;
            var second = Grid.GetColumn(element) == 2 || Grid.GetRow(element) == 1;
            if (second)
            {
                Grid.SetColumn(element, stacked ? 0 : 2);
                Grid.SetRow(element, stacked ? 1 : 0);
                Grid.SetColumnSpan(element, stacked ? 3 : 1);
                element.Margin = new Thickness(element.Margin.Left, stacked ? 14 : 0, element.Margin.Right, element.Margin.Bottom);
            }
            else Grid.SetColumnSpan(element, stacked ? 3 : 1);
        }
    }

    /// <summary>
    /// For a row of "icon or picture, text, buttons" (columns Auto, *, Auto): below this width the buttons go under
    /// the text instead of squeezing it.
    /// </summary>
    public static readonly DependencyProperty WrapLastBelowProperty = DependencyProperty.RegisterAttached(
        "WrapLastBelow", typeof(double), typeof(UI), new FrameworkPropertyMetadata(0.0, OnWrapLastBelowChanged));
    public static double GetWrapLastBelow(DependencyObject o) => (double)o.GetValue(WrapLastBelowProperty);
    public static void SetWrapLastBelow(DependencyObject o, double v) => o.SetValue(WrapLastBelowProperty, v);

    /// <summary>The wrapped buttons start under the first column (the picture) rather than under the text.</summary>
    public static readonly DependencyProperty WrapFromStartProperty = DependencyProperty.RegisterAttached(
        "WrapFromStart", typeof(bool), typeof(UI), new FrameworkPropertyMetadata(false));
    public static bool GetWrapFromStart(DependencyObject o) => (bool)o.GetValue(WrapFromStartProperty);
    public static void SetWrapFromStart(DependencyObject o, bool v) => o.SetValue(WrapFromStartProperty, v);

    private static readonly DependencyProperty WrappedProperty = DependencyProperty.RegisterAttached(
        "Wrapped", typeof(bool?), typeof(UI), new FrameworkPropertyMetadata(null));

    private static readonly DependencyProperty OriginalLayoutProperty = DependencyProperty.RegisterAttached(
        "OriginalLayout", typeof(Tuple<Thickness, HorizontalAlignment>), typeof(UI), new FrameworkPropertyMetadata(null));

    private static void OnWrapLastBelowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Grid grid) return;
        grid.SizeChanged -= WrapLast;
        grid.SizeChanged += WrapLast;
    }

    private static void WrapLast(object sender, SizeChangedEventArgs e)
    {
        var grid = (Grid)sender;
        var last = grid.ColumnDefinitions.Count - 1;
        if (last < 2) return;
        var wrapped = grid.ActualWidth < GetWrapLastBelow(grid);
        if (grid.GetValue(WrappedProperty) is bool current && current == wrapped) return;
        grid.SetValue(WrappedProperty, wrapped);
        if (grid.RowDefinitions.Count == 0)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        foreach (UIElement child in grid.Children)
        {
            if (child is not FrameworkElement element) continue;
            if (Grid.GetColumn(element) != last && Grid.GetRow(element) != 1) continue;
            if (element.GetValue(OriginalLayoutProperty) is not Tuple<Thickness, HorizontalAlignment> original)
            {
                original = Tuple.Create(element.Margin, element.HorizontalAlignment);
                element.SetValue(OriginalLayoutProperty, original);
            }
            var start = GetWrapFromStart(grid) ? 0 : 1;
            Grid.SetRow(element, wrapped ? 1 : 0);
            Grid.SetColumn(element, wrapped ? start : last);
            Grid.SetColumnSpan(element, wrapped ? last + 1 - start : 1);
            element.HorizontalAlignment = wrapped ? HorizontalAlignment.Left : original.Item2;
            element.Margin = wrapped ? new Thickness(0, 10, 0, 0) : original.Item1;
        }
    }

    private static void OnPlaceholderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PasswordBox pb)
        {
            pb.PasswordChanged -= PasswordChanged;
            pb.PasswordChanged += PasswordChanged;
            SetHasText(pb, pb.SecurePassword.Length > 0);
        }
    }

    private static void PasswordChanged(object sender, RoutedEventArgs e)
    {
        var pb = (PasswordBox)sender;
        SetHasText(pb, pb.SecurePassword.Length > 0);
    }
}
