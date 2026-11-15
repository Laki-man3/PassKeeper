using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace PassKeeper.Desktop;

/// <summary>A modal window: a body above, buttons at the bottom right; Esc closes it.</summary>
public class DialogWindow : Window
{
    public DialogWindow(string title, double width = 460)
    {
        Title = title;
        Width = width;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = Ui.Icon;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
    }

    public void SetBody(Control body, params Button[] buttons)
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        foreach (var b in buttons) bar.Children.Add(b);
        var root = new DockPanel { Margin = new Thickness(22) };
        DockPanel.SetDock(bar, Dock.Bottom);
        root.Children.Add(bar);
        root.Children.Add(new ScrollViewer { Content = body, MaxHeight = 600 });
        Content = root;
    }
}

public static class Dialogs
{
    public static async Task Message(Window owner, string text)
    {
        var w = new DialogWindow("PassKeeper", 420);
        var ok = Ui.Button(Loc.T("Common.OK"), () => w.Close(), accent: true);
        ok.IsDefault = true;
        w.SetBody(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, ok);
        await w.ShowDialog(owner);
    }

    public static async Task<bool> Confirm(Window owner, string text, string yes)
    {
        var w = new DialogWindow("PassKeeper", 440);
        var confirm = Ui.Button(yes, () => w.Close(true), accent: true);
        confirm.IsDefault = true;
        w.SetBody(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, Ui.Button(Loc.T("Common.Cancel"), () => w.Close(false)), confirm);
        return await w.ShowDialog<bool>(owner);
    }

    /// <summary>Asks for a password; null when cancelled.</summary>
    public static async Task<string?> AskPassword(Window owner, string title, string prompt)
    {
        var w = new DialogWindow(title, 420);
        var box = Ui.Secret(id: "PromptPassword");
        var ok = Ui.Button(Loc.T("Common.OK"), () => w.Close(box.Text ?? ""), accent: true);
        ok.IsDefault = true;
        w.SetBody(Ui.Column(6, new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap }, box),
            Ui.Button(Loc.T("Common.Cancel"), () => w.Close(null)), ok);
        w.Opened += (_, _) => box.Focus();
        var answer = await w.ShowDialog<string?>(owner);
        return string.IsNullOrEmpty(answer) ? null : answer;
    }
}
