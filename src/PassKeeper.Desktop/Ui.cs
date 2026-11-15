using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace PassKeeper.Desktop;

/// <summary>Small builders for the interface (it is made in code, without XAML).</summary>
public static class Ui
{
    private static Bitmap? _logo;

    public static Bitmap Logo
    {
        get
        {
            if (_logo != null) return _logo;
            using var stream = typeof(Ui).Assembly.GetManifestResourceStream("PassKeeper.png")!;
            return _logo = new Bitmap(stream);
        }
    }

    public static WindowIcon Icon
    {
        get
        {
            using var stream = typeof(Ui).Assembly.GetManifestResourceStream("PassKeeper.png")!;
            return new WindowIcon(stream);
        }
    }

    public static TextBlock Heading(string text, double size = 22) =>
        new() { Text = text, FontSize = size, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };

    public static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(0, 10, 0, 4), Opacity = 0.75 };

    public static TextBlock Muted(string text) => new() { Text = text, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };

    public static TextBlock ErrorText() => new() { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(0, 8, 0, 0) };

    public static void ShowError(TextBlock block, string text)
    {
        block.Text = text;
        block.IsVisible = text.Length > 0;
    }

    public static Button Button(string text, Action onClick, bool accent = false)
    {
        var button = new Button { Content = text };
        if (accent) button.Classes.Add("accent");
        button.Click += (_, _) => onClick();
        return button;
    }

    public static TextBox Text(string value = "", string? id = null)
    {
        var box = new TextBox { Text = value };
        if (id != null) AutomationProperties.SetAutomationId(box, id);
        return box;
    }

    public static TextBox Secret(string value = "", string? id = null)
    {
        var box = Text(value, id);
        box.PasswordChar = '•';
        box.RevealPassword = false;
        return box;
    }

    public static StackPanel Row(params Control[] items)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var item in items) row.Children.Add(item);
        return row;
    }

    public static StackPanel Column(double spacing, params Control[] items)
    {
        var column = new StackPanel { Spacing = spacing };
        foreach (var item in items) column.Children.Add(item);
        return column;
    }

    /// <summary>The command key of the platform (Cmd on macOS, Ctrl elsewhere) is held.</summary>
    public static bool Command(KeyEventArgs e) =>
        OperatingSystem.IsMacOS() ? e.KeyModifiers.HasFlag(KeyModifiers.Meta) : e.KeyModifiers.HasFlag(KeyModifiers.Control);

    /// <summary>Runs work off the interface thread (key derivation takes a moment).</summary>
    public static Task<T> Background<T>(Func<T> work) => Task.Run(work);

    public static void Later(Action action) => Dispatcher.UIThread.Post(action);
}
