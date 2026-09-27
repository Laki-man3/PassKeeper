using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PassKeeper.Localization;

namespace PassKeeper.Controls;

/// <summary>Password input with a show/hide toggle. <see cref="Value"/> is kept in sync with both editors.</summary>
public sealed class SecretBox : Grid
{
    private readonly PasswordBox _hidden = new();
    private readonly TextBox _visible = new() { Visibility = Visibility.Collapsed };
    private readonly Button _eye;
    private bool _sync;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(SecretBox),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(SecretBox), new PropertyMetadata(null, (d, e) =>
        {
            var s = (SecretBox)d;
            UI.SetPlaceholder(s._hidden, (string?)e.NewValue);
            UI.SetPlaceholder(s._visible, (string?)e.NewValue);
        }));

    public event EventHandler? ValueChanged;
    public event KeyEventHandler? EnterPressed;

    public SecretBox()
    {
        _eye = new Button
        {
            Style = (Style)Application.Current.FindResource("Btn.Icon"),
            Content = "\uE7B3",
            Width = 32,
            Height = 30,
            FontSize = 14,
            Margin = new Thickness(0, 0, 4, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            IsTabStop = false,
        };
        _eye.SetBinding(ToolTipProperty, new System.Windows.Data.Binding("[Common.ShowHide]") { Source = Loc.I });
        _eye.Click += (_, _) => IsRevealed = !IsRevealed;

        _hidden.Padding = new Thickness(10, 0, 40, 0);
        _visible.Padding = new Thickness(10, 0, 40, 0);
        _visible.FontFamily = (FontFamily)Application.Current.FindResource("Font.Mono");
        _hidden.PasswordChanged += (_, _) =>
        {
            if (_sync) return;
            _sync = true;
            Value = _hidden.Password;
            _sync = false;
        };
        _visible.TextChanged += (_, _) =>
        {
            if (_sync) return;
            _sync = true;
            Value = _visible.Text;
            _sync = false;
        };
        _hidden.KeyDown += OnKeyDown;
        _visible.KeyDown += OnKeyDown;
        Children.Add(_hidden);
        Children.Add(_visible);
        Children.Add(_eye);
        Focusable = false;
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string? Placeholder
    {
        get => (string?)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public bool IsRevealed
    {
        get => _visible.Visibility == Visibility.Visible;
        set
        {
            var focus = _hidden.IsKeyboardFocusWithin || _visible.IsKeyboardFocusWithin;
            _visible.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            _hidden.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            _eye.Content = value ? "\uED1A" : "\uE7B3";
            if (focus) FocusInput();
        }
    }

    public int MaxLength
    {
        get => _hidden.MaxLength;
        set
        {
            _hidden.MaxLength = value;
            _visible.MaxLength = value;
        }
    }

    public void FocusInput()
    {
        if (IsRevealed)
        {
            _visible.Focus();
            _visible.CaretIndex = _visible.Text.Length;
        }
        else _hidden.Focus();
    }

    public void Clear() => Value = "";

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) EnterPressed?.Invoke(this, e);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (SecretBox)d;
        var value = (string?)e.NewValue ?? "";
        var wasSync = box._sync;
        box._sync = true;
        if (box._hidden.Password != value) box._hidden.Password = value;
        if (box._visible.Text != value) box._visible.Text = value;
        box._sync = wasSync;
        box.ValueChanged?.Invoke(box, EventArgs.Empty);
    }
}
