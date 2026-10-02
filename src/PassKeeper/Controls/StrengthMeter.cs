using System.Windows;
using System.Windows.Controls;
using PassKeeper.Core.Security;
using PassKeeper.Localization;

namespace PassKeeper.Controls;

/// <summary>Four-segment password strength indicator with a caption.</summary>
public sealed class StrengthMeter : StackPanel
{
    private readonly Border[] _segments = new Border[4];
    private readonly TextBlock _caption = new() { FontSize = 11.5, Margin = new Thickness(0, 6, 0, 0) };

    public static readonly DependencyProperty PasswordProperty = DependencyProperty.Register(
        nameof(Password), typeof(string), typeof(StrengthMeter), new PropertyMetadata("", (d, _) => ((StrengthMeter)d).Update()));

    public StrengthMeter()
    {
        var bar = new Grid();
        for (var i = 0; i < 4; i++)
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition());
            var seg = new Border { Height = 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(i == 0 ? 0 : 3, 0, 0, 0) };
            Grid.SetColumn(seg, i);
            bar.Children.Add(seg);
            _segments[i] = seg;
        }
        Children.Add(bar);
        Children.Add(_caption);
        Loaded += (_, _) => Loc.I.LanguageChanged += OnLanguageChanged;
        Unloaded += (_, _) => Loc.I.LanguageChanged -= OnLanguageChanged;
        Update();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => Update();

    public string Password
    {
        get => (string)GetValue(PasswordProperty);
        set => SetValue(PasswordProperty, value);
    }

    private void Update()
    {
        var pw = Password ?? "";
        var result = PasswordStrength.Evaluate(pw);
        var brushKey = result.Score switch { 0 => "Brush.Danger", 1 => "Brush.Danger", 2 => "Brush.Warning", _ => "Brush.Success" };
        var filled = pw.Length == 0 ? 0 : Math.Max(1, result.Score);
        for (var i = 0; i < 4; i++)
            _segments[i].SetResourceReference(Border.BackgroundProperty, i < filled ? brushKey : "Brush.Border");
        _caption.SetResourceReference(TextBlock.ForegroundProperty, pw.Length == 0 ? "Brush.TextMuted" : brushKey);
        _caption.Text = pw.Length == 0 ? Loc.T("Strength.Empty") : Loc.F("Strength.Format", Loc.T("Strength." + result.Score), (int)result.Bits);
    }
}
