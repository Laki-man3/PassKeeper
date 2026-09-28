using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using PassKeeper.Localization;

namespace PassKeeper.Controls;

/// <summary>
/// Duration editor accurate to the second: an hh:mm:ss readout (each part selectable and typeable),
/// quick presets and a <see cref="TimeDial"/> for the selected part.
/// </summary>
public sealed class DurationPicker : Grid
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(TimeSpan), typeof(DurationPicker),
        new FrameworkPropertyMetadata(TimeSpan.FromHours(8), FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((DurationPicker)d).OnValueChanged(), (_, v) => TimeDial.Clamp((TimeSpan)v)));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(TimeSpan), typeof(DurationPicker),
        new FrameworkPropertyMetadata(TimeSpan.Zero, (d, _) => ((DurationPicker)d).OnValueChanged()));

    private static readonly int[] PresetMinutes = [1, 5, 15, 30, 60, 240, 480, 1440];

    private readonly TimeDial _dial = new() { Width = 196, Height = 196 };
    private readonly ToggleButton[] _segments = new ToggleButton[3];
    private readonly TextBlock[] _captions = new TextBlock[3];
    private readonly WrapPanel _presets = new() { Margin = new Thickness(0, 16, 0, 0) };
    private readonly TextBlock _warning = new() { Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private int _typedDigits;
    private bool _sync;

    public event EventHandler? ValueChanged;

    public DurationPicker()
    {
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0) };
        var readout = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < 3; i++)
        {
            if (i > 0)
            {
                var colon = new TextBlock { Text = ":", FontSize = 30, Margin = new Thickness(2, 0, 2, 20), VerticalAlignment = VerticalAlignment.Center };
                colon.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
                readout.Children.Add(colon);
            }
            readout.Children.Add(CreateSegment(i));
        }
        left.Children.Add(readout);
        left.Children.Add(_presets);
        _warning.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Warning");
        _warning.FontSize = 12;
        left.Children.Add(_warning);
        Children.Add(left);

        SetColumn(_dial, 1);
        _dial.SetBinding(TimeDial.ValueProperty, new System.Windows.Data.Binding(nameof(Value)) { Source = this, Mode = System.Windows.Data.BindingMode.TwoWay });
        _dial.DragCompleted += (_, _) =>
        {
            if (_dial.Unit != TimeUnit.Seconds) SelectUnit(_dial.Unit + 1, focusSegment: false);
        };
        Children.Add(_dial);

        BuildPresets();
        Loc.I.LanguageChanged += OnLanguageChanged;
        Unloaded += (_, _) => Loc.I.LanguageChanged -= OnLanguageChanged;
        Loaded += (_, _) =>
        {
            Loc.I.LanguageChanged -= OnLanguageChanged;
            Loc.I.LanguageChanged += OnLanguageChanged;
        };
        SelectUnit(TimeUnit.Hours, focusSegment: false);
        OnValueChanged();
    }

    public TimeSpan Value
    {
        get => (TimeSpan)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public TimeSpan Minimum
    {
        get => (TimeSpan)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>The value that takes effect: never below <see cref="Minimum"/>.</summary>
    public TimeSpan EffectiveValue => Value < Minimum ? Minimum : Value;

    private FrameworkElement CreateSegment(int index)
    {
        var unit = (TimeUnit)index;
        var toggle = new ToggleButton
        {
            Style = (Style)Application.Current.FindResource("DurationSegment"),
            FontFamily = (FontFamily)Application.Current.FindResource("Font.Mono"),
            FontSize = 30,
            Tag = unit,
        };
        toggle.Click += (_, _) => SelectUnit(unit, focusSegment: true);
        toggle.GotKeyboardFocus += (_, _) =>
        {
            _typedDigits = 0;
            if (_dial.Unit != unit) SelectUnit(unit, focusSegment: false);
        };
        toggle.PreviewTextInput += Segment_TextInput;
        toggle.PreviewKeyDown += Segment_KeyDown;
        toggle.MouseWheel += (_, e) =>
        {
            SelectUnit(unit, focusSegment: false);
            StepSelected(e.Delta > 0 ? 1 : -1);
            e.Handled = true;
        };
        _segments[index] = toggle;

        var caption = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0) };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        _captions[index] = caption;

        var stack = new StackPanel();
        stack.Children.Add(toggle);
        stack.Children.Add(caption);
        return stack;
    }

    private void SelectUnit(TimeUnit unit, bool focusSegment)
    {
        _dial.Unit = unit;
        _typedDigits = 0;
        for (var i = 0; i < 3; i++) _segments[i].IsChecked = i == (int)unit;
        if (focusSegment) _segments[(int)unit].Focus();
    }

    private void StepSelected(int delta)
    {
        var unit = _dial.Unit;
        var max = unit == TimeUnit.Hours ? 24 : 59;
        var next = TimeDial.Part(Value, unit) + delta;
        if (next > max) next = 0;
        else if (next < 0) next = max;
        Value = TimeDial.WithPart(Value, unit, next);
    }

    /// <summary>Typing digits: two digits per part, then the next part is selected.</summary>
    private void Segment_TextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = true;
        if (e.Text.Length != 1 || !char.IsAsciiDigit(e.Text[0])) return;
        var unit = _dial.Unit;
        var digit = e.Text[0] - '0';
        var max = unit == TimeUnit.Hours ? 24 : 59;
        var current = TimeDial.Part(Value, unit);
        var next = _typedDigits == 0 ? digit : current * 10 + digit;
        if (next > max) next = digit;
        Value = TimeDial.WithPart(Value, unit, next);
        _typedDigits++;
        if (_typedDigits >= 2 || next * 10 > max)
        {
            if (unit != TimeUnit.Seconds) SelectUnit(unit + 1, focusSegment: true);
            else _typedDigits = 0;
        }
    }

    private void Segment_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up: StepSelected(1); break;
            case Key.Down: StepSelected(-1); break;
            case Key.PageUp: StepSelected(_dial.Unit == TimeUnit.Hours ? 1 : 5); break;
            case Key.PageDown: StepSelected(_dial.Unit == TimeUnit.Hours ? -1 : -5); break;
            case Key.Left when _dial.Unit != TimeUnit.Hours: SelectUnit(_dial.Unit - 1, focusSegment: true); break;
            case Key.Right when _dial.Unit != TimeUnit.Seconds: SelectUnit(_dial.Unit + 1, focusSegment: true); break;
            case Key.Back or Key.Delete: Value = TimeDial.WithPart(Value, _dial.Unit, 0); _typedDigits = 0; break;
            default: return;
        }
        e.Handled = true;
    }

    private void BuildPresets()
    {
        _presets.Children.Clear();
        foreach (var minutes in PresetMinutes)
        {
            var span = TimeSpan.FromMinutes(minutes);
            var chip = new ToggleButton
            {
                Style = (Style)Application.Current.FindResource("DurationPreset"),
                Content = Loc.Duration(span),
                Tag = span,
                Margin = new Thickness(0, 0, 6, 6),
            };
            if (minutes == 480) chip.ToolTip = Loc.T("Settings.Recommended");
            chip.Click += (_, _) =>
            {
                Value = span;
                UpdatePresets();
            };
            _presets.Children.Add(chip);
        }
        UpdatePresets();
    }

    private void UpdatePresets()
    {
        foreach (ToggleButton chip in _presets.Children) chip.IsChecked = (TimeSpan)chip.Tag == Value;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        BuildPresets();
        OnValueChanged();
    }

    private void OnValueChanged()
    {
        if (_segments[0] == null) return;
        var v = Value;
        _segments[0].Content = ((int)v.TotalHours).ToString("00", CultureInfo.InvariantCulture);
        _segments[1].Content = v.Minutes.ToString("00", CultureInfo.InvariantCulture);
        _segments[2].Content = v.Seconds.ToString("00", CultureInfo.InvariantCulture);
        _captions[0].Text = Loc.T("Duration.Hours");
        _captions[1].Text = Loc.T("Duration.Minutes");
        _captions[2].Text = Loc.T("Duration.Seconds");
        UpdatePresets();
        var tooShort = v < Minimum;
        _warning.Visibility = tooShort ? Visibility.Visible : Visibility.Collapsed;
        if (tooShort) _warning.Text = Loc.F("Duration.Minimum", Loc.Duration(Minimum));
        if (_sync) return;
        _sync = true;
        try { ValueChanged?.Invoke(this, EventArgs.Empty); }
        finally { _sync = false; }
    }
}
