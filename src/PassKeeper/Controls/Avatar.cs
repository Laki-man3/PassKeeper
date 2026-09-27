using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PassKeeper.Core.Matching;

namespace PassKeeper.Controls;

/// <summary>Letter avatar with a stable per-title color (no favicons: the app never goes online).</summary>
public sealed class Avatar : Border
{
    private static readonly Color[] Palette =
    [
        Color.FromRgb(0x7B, 0x61, 0xFF), Color.FromRgb(0x3D, 0x8B, 0xFF), Color.FromRgb(0x1C, 0xAE, 0xC7),
        Color.FromRgb(0x2F, 0xB6, 0x7C), Color.FromRgb(0xF0, 0x9A, 0x0A), Color.FromRgb(0xF7, 0x67, 0x07),
        Color.FromRgb(0xE6, 0x49, 0x80), Color.FromRgb(0xAE, 0x3E, 0xC9), Color.FromRgb(0x12, 0xB8, 0x86),
        Color.FromRgb(0xF0, 0x52, 0x52), Color.FromRgb(0x4C, 0x6E, 0xF5), Color.FromRgb(0x74, 0xB8, 0x16),
    ];

    private readonly TextBlock _text = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        FontWeight = FontWeights.SemiBold,
    };

    public static readonly DependencyProperty SourceTextProperty = DependencyProperty.Register(
        nameof(SourceText), typeof(string), typeof(Avatar), new PropertyMetadata("", (d, _) => ((Avatar)d).Update()));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(Avatar), new PropertyMetadata(34.0, (d, _) => ((Avatar)d).Update()));

    public Avatar()
    {
        Child = _text;
        Update();
        Loaded += (_, _) => Services.ThemeService.Changed += Update;
        Unloaded += (_, _) => Services.ThemeService.Changed -= Update;
    }

    public string SourceText
    {
        get => (string)GetValue(SourceTextProperty);
        set => SetValue(SourceTextProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public static string InitialOf(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "?";
        var host = DomainUtil.GetHost(text.Contains('.') && !text.Contains(' ') ? text : null);
        var source = host ?? text.Trim();
        var e = StringInfo.GetTextElementEnumerator(source);
        while (e.MoveNext())
        {
            var s = (string)e.Current;
            if (s.Length > 0 && char.IsLetterOrDigit(s, 0)) return s.ToUpperInvariant();
        }
        return source[..1].ToUpperInvariant();
    }

    public static Color ColorOf(string? text)
    {
        var key = (text ?? "").Trim().ToLowerInvariant();
        uint hash = 2166136261;
        foreach (var c in key) hash = (hash ^ c) * 16777619;
        return Palette[hash % (uint)Palette.Length];
    }

    private void Update()
    {
        var color = ColorOf(SourceText);
        Width = Size;
        Height = Size;
        CornerRadius = new CornerRadius(Size * 0.28);
        var dark = Services.ThemeService.IsDark;
        Background = new SolidColorBrush(Color.FromArgb(dark ? (byte)0x30 : (byte)0x22, color.R, color.G, color.B));
        var shift = dark ? 30 : -25;
        _text.Foreground = new SolidColorBrush(Color.FromRgb(
            (byte)Math.Clamp(color.R + shift, 0, 255), (byte)Math.Clamp(color.G + shift, 0, 255), (byte)Math.Clamp(color.B + shift, 0, 255)));
        _text.FontSize = Size * 0.42;
        _text.Text = InitialOf(SourceText);
    }
}
