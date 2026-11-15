using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using PassKeeper.Core.AutoType;
using PassKeeper.Core.Matching;
using PassKeeper.Core.Models;
using PassKeeper.Desktop.Platform;

namespace PassKeeper.Desktop;

/// <summary>
/// Auto-type: the hotkey in any window finds its entry (or offers a list) and types the login, Tab, the password
/// and, for entries that sign in automatically, Enter; the entry's own typing order when it has one. Nothing is typed
/// unless the window the hotkey was pressed in is back in front.
/// </summary>
public sealed class AutoTypeController : IDisposable
{
    private readonly MainWindow _main;
    private IDisposable? _hotkey;
    private ActiveWindow? _pending;
    private bool _busy;

    public AutoTypeController(MainWindow main) => _main = main;

    /// <summary>Why the hotkey does not work (taken by another program…); null when it does or is off.</summary>
    public string? HotkeyError { get; private set; }

    public void ApplySettings()
    {
        _hotkey?.Dispose();
        _hotkey = null;
        HotkeyError = null;
        var settings = App.Instance.Settings;
        if (!settings.AutoTypeEnabled || Hotkey.Parse(settings.AutoTypeHotkey) is not { IsUsable: true } hotkey) return;
        _hotkey = DesktopPlatform.Current.RegisterHotkey(hotkey, () => _ = OnHotkeyAsync(), out var error);
        HotkeyError = error;
    }

    public void Dispose() => _hotkey?.Dispose();

    private async Task OnHotkeyAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var platform = DesktopPlatform.Current;
            if (platform.AutoTypeProblem() is { } problem)
            {
                _main.ShowFromTray();
                await Dialogs.Message(_main, problem);
                platform.RequestAutoTypePermission();
                return;
            }
            var target = platform.ActiveWindow();
            if (target == null || target.ProcessId == Environment.ProcessId)
            {
                _main.ShowFromTray();
                return;
            }
            if (_main.Vault is not { IsUnlocked: true })
            {
                // Signed out: the PassKeeper window asks for the PIN or master password, typing goes on after that.
                _pending = target;
                _main.ShowFromTray();
                return;
            }
            await TypeForWindowAsync(target);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>After signing in: the auto-type the hotkey asked for while PassKeeper was locked.</summary>
    public async Task ResumeAfterSignInAsync()
    {
        if (_pending is not { } target) return;
        _pending = null;
        await TypeForWindowAsync(target);
    }

    private async Task TypeForWindowAsync(ActiveWindow target)
    {
        var vault = _main.Vault!;
        var matches = EntryMatcher.Match(vault.ActiveEntries, new TargetContext { WindowTitle = target.Title, ProcessName = target.App });
        var entry = matches.Count == 1 || (matches.Count > 1 && matches[0].Score >= 95 && matches[0].Score > matches[1].Score)
            ? matches[0].Entry
            : await AutoTypePicker.PickAsync(target, matches, vault.ActiveEntries.ToList());
        if (entry == null)
        {
            DesktopPlatform.Current.Activate(target);
            return;
        }
        await TypeAsync(entry, target);
    }

    /// <summary>
    /// Types the entry into <paramref name="target"/>, or (from the entry's "Auto-type" button) into the window that
    /// comes to the front when PassKeeper steps aside.
    /// </summary>
    public async Task TypeAsync(VaultEntry entry, ActiveWindow? target)
    {
        var platform = DesktopPlatform.Current;
        if (platform.AutoTypeProblem() is { } problem)
        {
            await Dialogs.Message(_main, problem);
            platform.RequestAutoTypePermission();
            return;
        }
        List<AutoTypeAction> actions;
        try
        {
            actions = AutoTypeSequence.Compile(AutoTypeSequence.EffectiveSequence(entry, submit: entry.AutoLogin), entry);
        }
        catch (AutoTypeException ex)
        {
            await Dialogs.Message(_main, ex.Message);
            return;
        }

        platform.HideApplication(_main);
        await Task.Delay(400);
        if (target == null)
        {
            target = platform.ActiveWindow();
            if (target == null || target.ProcessId == Environment.ProcessId)
            {
                await Problem(Loc.T("AutoType.NoTarget"));
                return;
            }
        }
        else platform.Activate(target);
        await Task.Delay(250);
        var front = platform.ActiveWindow();
        if (front == null || front.ProcessId != target.ProcessId || front.Handle != target.Handle)
        {
            await Problem(Loc.T("AutoType.WindowChanged"));
            return;
        }
        await Task.Run(() => DesktopPlatform.Run(platform, actions));
        _main.Vault?.MarkUsed(entry.Id);
    }

    private async Task Problem(string text)
    {
        _main.ShowFromTray();
        await Dialogs.Message(_main, text);
    }
}

/// <summary>"Which entry?" when the window matches several or none: a list with a search, matches first.</summary>
public sealed class AutoTypePicker : Window
{
    private readonly TaskCompletionSource<VaultEntry?> _result = new();
    private readonly ListBox _list = new() { Height = 300 };
    private readonly TextBox _search = new();
    private readonly List<VaultEntry> _ordered;

    private AutoTypePicker(ActiveWindow target, IReadOnlyList<EntryMatch> matches, IReadOnlyList<VaultEntry> all)
    {
        Title = "PassKeeper";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = Ui.Icon;
        var matched = matches.Select(m => m.Entry).ToList();
        _ordered = [.. matched, .. all.Where(e => !matched.Contains(e)).OrderBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase)];
        AutomationProperties.SetAutomationId(_list, "PickerList");
        AutomationProperties.SetAutomationId(_search, "PickerSearch");
        _search.PlaceholderText = Loc.T("Top.Search");
        _search.TextChanged += (_, _) => Fill();
        _list.ItemTemplate = new FuncDataTemplate<VaultEntry>((e, _) => e == null ? new TextBlock() : Ui.Column(1,
            new TextBlock { Text = e.Title, FontWeight = FontWeight.SemiBold },
            new TextBlock { Text = e.Username, Opacity = 0.65, FontSize = 12 }));
        _list.DoubleTapped += (_, _) => Choose();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Finish(null);
            else if (e.Key == Key.Enter) Choose();
            else if (e.Key == Key.Down && _search.IsFocused && _list.ItemCount > 0)
            {
                _list.SelectedIndex = Math.Min(_list.SelectedIndex + 1, _list.ItemCount - 1);
                e.Handled = true;
            }
        };
        var type = Ui.Button(Loc.T("AutoType.Type"), Choose, accent: true);
        AutomationProperties.SetAutomationId(type, "PickerType");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(Ui.Button(Loc.T("Common.Cancel"), () => Finish(null)));
        buttons.Children.Add(type);
        var where = target.Title.Length > 0 ? target.Title : target.App;
        Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 8,
            Children = { Ui.Heading(Loc.T("AutoType.PickTitle"), 18), Ui.Muted(where), _search, _list, buttons },
        };
        Fill();
        Opened += (_, _) =>
        {
            Activate();
            _search.Focus();
        };
        Closed += (_, _) => _result.TrySetResult(null);
    }

    public static Task<VaultEntry?> PickAsync(ActiveWindow target, IReadOnlyList<EntryMatch> matches, IReadOnlyList<VaultEntry> all)
    {
        var picker = new AutoTypePicker(target, matches, all);
        picker.Show();
        return picker._result.Task;
    }

    private void Fill()
    {
        var text = _search.Text?.Trim() ?? "";
        var shown = text.Length == 0 ? _ordered : _ordered.Where(e =>
            e.Title.Contains(text, StringComparison.CurrentCultureIgnoreCase) || e.Username.Contains(text, StringComparison.CurrentCultureIgnoreCase) ||
            e.Url.Contains(text, StringComparison.CurrentCultureIgnoreCase)).ToList();
        _list.ItemsSource = shown;
        _list.SelectedIndex = shown.Count > 0 ? 0 : -1;
    }

    private void Choose()
    {
        if (_list.SelectedItem is VaultEntry entry) Finish(entry);
    }

    private void Finish(VaultEntry? entry)
    {
        _result.TrySetResult(entry);
        Close();
    }
}
