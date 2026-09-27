using System.Windows;
using PassKeeper.Localization;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace PassKeeper.Services;

/// <summary>Notification-area icon: PassKeeper keeps running in the background for autofill and hotkeys.</summary>
public sealed class TrayService : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ContextMenuStrip _menu = new();
    private readonly WinForms.ToolStripMenuItem _open = new();
    private readonly WinForms.ToolStripMenuItem _lock = new();
    private readonly WinForms.ToolStripMenuItem _generator = new();
    private readonly WinForms.ToolStripMenuItem _hotkey = new() { Enabled = false };
    private readonly WinForms.ToolStripMenuItem _exit = new();

    public event Action? OpenRequested;
    public event Action? LockRequested;
    public event Action? GeneratorRequested;
    public event Action? ExitRequested;

    public TrayService()
    {
        var stream = Application.GetResourceStream(new Uri("pack://application:,,,/PassKeeper;component/Assets/PassKeeper.ico"))!.Stream;
        _icon = new WinForms.NotifyIcon
        {
            Icon = new Drawing.Icon(stream, WinForms.SystemInformation.SmallIconSize),
            Text = "PassKeeper",
            Visible = true,
            ContextMenuStrip = _menu,
        };
        _open.Font = new Drawing.Font(_open.Font, Drawing.FontStyle.Bold);
        _open.Click += (_, _) => OpenRequested?.Invoke();
        _lock.Click += (_, _) => LockRequested?.Invoke();
        _generator.Click += (_, _) => GeneratorRequested?.Invoke();
        _exit.Click += (_, _) => ExitRequested?.Invoke();
        _menu.Items.AddRange([_open, _generator, _lock, new WinForms.ToolStripSeparator(), _hotkey, new WinForms.ToolStripSeparator(), _exit]);
        _menu.Renderer = new WinForms.ToolStripProfessionalRenderer(new MenuColors());
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) OpenRequested?.Invoke();
        };
        UpdateTexts(false, "");
    }

    public void UpdateTexts(bool unlocked, string hotkey)
    {
        _open.Text = Loc.T("Tray.Open");
        _lock.Text = Loc.T("Tray.Lock");
        _lock.Enabled = unlocked;
        _generator.Text = Loc.T("Tray.Generator");
        _hotkey.Text = Loc.F("Tray.Hotkey", hotkey);
        _hotkey.Visible = !string.IsNullOrEmpty(hotkey);
        _exit.Text = Loc.T("Tray.Exit");
        _icon.Text = unlocked ? "PassKeeper" : "PassKeeper — " + Loc.T("Tray.Locked");
    }

    public void ShowBalloon(string title, string text) =>
        _icon.ShowBalloonTip(4000, title, text, WinForms.ToolTipIcon.None);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }

    private sealed class MenuColors : WinForms.ProfessionalColorTable
    {
        public override Drawing.Color MenuItemSelected => Drawing.Color.FromArgb(0xEC, 0xEA, 0xFF);
        public override Drawing.Color MenuItemBorder => Drawing.Color.FromArgb(0xC9, 0xC0, 0xFF);
        public override Drawing.Color MenuBorder => Drawing.Color.FromArgb(0xD8, 0xDB, 0xE4);
        public override Drawing.Color ToolStripDropDownBackground => Drawing.Color.White;
        public override Drawing.Color ImageMarginGradientBegin => Drawing.Color.White;
        public override Drawing.Color ImageMarginGradientMiddle => Drawing.Color.White;
        public override Drawing.Color ImageMarginGradientEnd => Drawing.Color.White;
    }
}
