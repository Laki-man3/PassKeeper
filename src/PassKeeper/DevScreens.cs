using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PassKeeper.Core.Models;
using PassKeeper.Localization;
using PassKeeper.Services;
using PassKeeper.Views;

namespace PassKeeper;

/// <summary>
/// Developer aid: "PassKeeper.exe --screenshots &lt;dir&gt;" renders the main screens with sample data (temporary
/// vault, never the user's) into PNG files. Used to review the UI without driving it manually.
/// </summary>
internal static class DevScreens
{
    public static async void Run(string outputDir)
    {
        var app = App.Instance;
        app.SuppressAutoNavigation = true;
        Directory.CreateDirectory(outputDir);
        var main = app.Main;
        main.Left = -30000;
        main.Top = -30000;
        main.ShowActivated = false;
        main.Width = 1240;
        main.Height = 780;
        main.Show();

        try
        {
            foreach (var lang in new[] { "ru", "en" })
            {
                Loc.I.Language = lang;
                foreach (var theme in new[] { "dark", "light" })
                {
                    if (lang == "en" && theme == "light") continue;
                    ThemeService.Apply(theme);
                    await Capture(outputDir, $"{lang}-{theme}");
                }
            }
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(outputDir, "error.txt"), ex.ToString());
        }
        finally
        {
            app.Vault.Lock();
            try { Directory.Delete(app.DataDirectory, true); } catch (IOException) { }
            app.Shutdown();
        }
    }

    private static async Task Capture(string dir, string suffix)
    {
        var app = App.Instance;
        var main = app.Main;

        if (!app.Vault.Exists)
        {
            main.Navigate(new SetupView());
            await Snap(main, dir, $"01-setup-{suffix}");
            await Task.Run(() => app.Vault.Create("Анна", "Correct-Horse-Battery-9"));
            main.Navigate(new PinSetupView(mandatory: true));
            await Snap(main, dir, $"02-pin-{suffix}");
            await Task.Run(() => app.Vault.SetPin("482913"));
            Seed(app);
        }
        else
        {
            if (app.Vault.IsUnlocked) app.Vault.Lock();
            main.Navigate(new SetupView());
            await Snap(main, dir, $"01-setup-{suffix}");
            await Task.Run(() => app.Vault.UnlockWithMaster("Correct-Horse-Battery-9"));
        }

        app.Vault.Lock();
        main.Navigate(new UnlockView());
        await Snap(main, dir, $"03-unlock-{suffix}");
        await Task.Run(() => app.Vault.UnlockWithPin("482913"));

        var vault = new VaultView();
        main.Navigate(vault);
        await Snap(main, dir, $"04-vault-empty-{suffix}");

        var list = (System.Windows.Controls.ListBox)vault.FindName("EntryList");
        list.SelectedIndex = 1;
        await Snap(main, dir, $"05-vault-details-{suffix}");

        var details = (System.Windows.Controls.ContentControl)vault.FindName("DetailHost");
        var entry = ((ViewModels.EntryItem)list.SelectedItem).Entry;
        details.Content = new EntryEditorView(entry.Clone(), false, app.Vault.Folders());
        await Snap(main, dir, $"06-editor-{suffix}");

        await Dialog(main, new SettingsDialog(), dir, $"07-settings-{suffix}");
        await Dialog(main, new GeneratorDialog(pickMode: false), dir, $"08-generator-{suffix}");
        await Dialog(main, new ImportDialog(), dir, $"09-import-{suffix}");
        await Dialog(main, new ExportDialog(), dir, $"10-export-{suffix}");

        // Floating autofill windows.
        var target = new TargetWindow { Title = "Вход — Яндекс ID", ProcessName = "chrome", IsBrowser = true, Url = "https://passport.yandex.ru/auth" };
        var matches = Core.Matching.EntryMatcher.Match(app.Vault.ActiveEntries, target.ToContext());
        var popup = new Windows.SuggestionPopup();
        popup.Fill(matches.Select(m => m.Entry).ToList(), target.Describe());
        var card = (FrameworkElement)popup.Content;
        popup.Content = null;
        var host = new Window { Content = card, Width = 380, Height = 250, Left = -30000, Top = -30000, ShowActivated = false, WindowStyle = WindowStyle.None };
        host.Show();
        await Snap(host, dir, $"11-suggestion-{suffix}");
        host.Close();

        var picker = Windows.AutoTypePickerWindow.CreateForPreview(
            new TargetWindow { Title = "Удалённый рабочий стол", ProcessName = "mstsc" }, [], app.Vault.ActiveEntries);
        picker.Left = -30000;
        picker.Top = -30000;
        picker.ShowActivated = false;
        picker.Show();
        await Snap(picker, dir, $"12-picker-{suffix}");
        picker.Close();
    }

    private static async Task Dialog(MainWindow main, DialogBase dialog, string dir, string name)
    {
        _ = main.ShowDialogAsync(dialog);
        await Snap(main, dir, name);
        dialog.Close();
    }

    private static void Seed(App app)
    {
        var now = DateTime.UtcNow;
        var entries = new List<VaultEntry>
        {
            new() { Title = "Госуслуги", Username = "+7 912 345-67-89", Password = "Gu$-2026-secure!", Url = "https://esia.gosuslugi.ru", Folder = "Личное", Favorite = true, Phone = "+7 912 345-67-89" },
            new() { Title = "Почта Яндекс", Username = "anna.petrova@yandex.ru", Password = "k7#Qm2!vRz9@Lp4x", Url = "https://passport.yandex.ru", Folder = "Личное", Email = "anna.petrova@yandex.ru",
                Totp = "JBSWY3DPEHPK3PXP", Notes = "Резервные коды лежат в сейфе.", CustomFields = [new CustomField { Name = "Секретный вопрос", Value = "Барсик", Protected = true }] },
            new() { Title = "GitLab (корпоративный)", Username = "a.petrova", Password = "Tr0ub4dor&3", Url = "https://gitlab.corp.local", Folder = "Работа", SecretKey = "glpat-xxxxxxxxxxxxxxxxxxxx" },
            new() { Title = "1С:Предприятие", Username = "Петрова А.", Password = "1c-Buh-2026", Folder = "Работа", WindowPatterns = ["*1С:Предприятие*"] },
            new() { Title = "Сбербанк Онлайн", Username = "anna_p", Password = "qwerty123", Url = "https://online.sberbank.ru", Folder = "Финансы", Favorite = true },
            new() { Title = "VPN офис", Username = "apetrova", Password = "Vpn!Office#77", Url = "vpn.corp.local", Folder = "Работа" },
            new() { Title = "Wi-Fi дома", Password = "HomeNet-5G-2026", Notes = "SSID: Petrov_5G" },
            new() { Title = "GitHub", Username = "annapetrova", Password = "Tr0ub4dor&3", Url = "https://github.com", Totp = "JBSWY3DPEHPK3PXP" },
        };
        foreach (var e in entries)
        {
            e.CreatedUtc = now.AddDays(-Random.Shared.Next(1, 300));
            e.ModifiedUtc = now.AddDays(-Random.Shared.Next(0, 30));
        }
        app.Vault.AddRange(entries);
    }

    private static async Task Snap(Window window, string dir, string name)
    {
        await Task.Delay(700);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var root = (FrameworkElement)window.Content;
        if (window.SizeToContent != SizeToContent.Manual)
        {
            root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            root.Arrange(new Rect(root.DesiredSize));
        }
        root.UpdateLayout();
        const double scale = 1.25;
        var bmp = new RenderTargetBitmap((int)(root.ActualWidth * scale), (int)(root.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var dc = background.RenderOpen())
            dc.DrawRectangle((Brush)window.FindResource("Brush.WindowBg"), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        bmp.Render(background);
        bmp.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        await using var fs = File.Create(Path.Combine(dir, name + ".png"));
        encoder.Save(fs);
    }
}
