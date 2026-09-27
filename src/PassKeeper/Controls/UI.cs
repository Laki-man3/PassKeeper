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
