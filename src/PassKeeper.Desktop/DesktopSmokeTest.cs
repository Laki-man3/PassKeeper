using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using PassKeeper.Core.Storage;

namespace PassKeeper.Desktop;

/// <summary>
/// Exercises the actual native window and UI event handlers from an extracted self-contained package.
/// Only enabled by --smoke-test; Program gives it a disposable data directory.
/// </summary>
internal static class DesktopSmokeTest
{
    public static async Task RunAsync(MainWindow main, IClassicDesktopStyleApplicationLifetime desktop, string output)
    {
        var exitCode = 1;
        var lines = new List<string>();
        var pinRequired = Environment.GetEnvironmentVariable("PASSKEEPER_SMOKE_PIN") == "1";
        VaultService? vault = null;
        try
        {
            await WaitAsync(() => main.IsVisible && Find<TextBox>(main, "Name") != null);
            Capture(main, Path.Combine(output, "01-create.png"));
            if (OperatingSystem.IsLinux())
            {
                if (main.AutoType.HotkeyError != null) throw new InvalidOperationException(main.AutoType.HotkeyError);
                // A second X11 connection must report a collision, not kill the process in the global error callback.
                using var duplicate = Platform.DesktopPlatform.Current.RegisterHotkey(Platform.Hotkey.Default, () => { }, out var error);
                if (duplicate != null || error == null) throw new InvalidOperationException("A duplicate hotkey grab was not rejected.");
                main.AutoType.ApplySettings();
                if (main.AutoType.HotkeyError != null) throw new InvalidOperationException("Could not re-register the hotkey.");
                lines.Add("X11 hotkey collision and re-registration: PASS");
            }
            Set(main, "Name", "Package smoke test");
            Set(main, "NewMaster", "Smoke-test-only-7439!");
            Set(main, "RepeatMaster", "Smoke-test-only-7439!");
            Click(main, "Create");
            await WaitAsync(() => main.Vault is { IsUnlocked: true });
            vault = main.Vault!;
            var pin = await WaitWindowAsync<PinWindow>(main);
            if (pinRequired)
            {
                Set(pin, "NewPin", "743921");
                Set(pin, "RepeatPin", "743921");
                Click(pin, "SavePin");
                await WaitAsync(() => vault.HasPin && !pin.IsVisible);
                lines.Add("Native keyring PIN creation: PASS");
            }
            else
            {
                pin.Close();
                lines.Add("Native keyring PIN: SKIPPED (set PASSKEEPER_SMOKE_PIN=1 to require it)");
            }

            Click(main, "NewEntry");
            var editor = await WaitWindowAsync<EntryEditorWindow>(main);
            Set(editor, "EditTitle", "Package smoke entry");
            Set(editor, "EditLogin", "smoke-user");
            Set(editor, "EditPassword", "Test-entry-secret-7439");
            Click(editor, "SaveEntry");
            await WaitAsync(() => !editor.IsVisible && vault.ActiveEntries.Any(e => e.Title == "Package smoke entry"));
            var list = Find<ListBox>(main, "EntryList") ?? throw new InvalidOperationException("Entry list is missing.");
            list.SelectedIndex = 0;
            Capture(main, Path.Combine(output, "02-vault.png"));
            lines.Add("Create user, save entry, render vault: PASS");

            // Rebuild used to replace the native tray menu, crashing macOS after a language change.
            App.Instance.Settings.Language = "ru";
            Loc.Use("ru");
            main.Rebuild();
            App.Instance.Settings.Language = "en";
            Loc.Use("en");
            main.Rebuild();
            await WaitAsync(() => Find<Button>(main, "Settings") != null);
            Click(main, "Settings");
            var settings = await WaitWindowAsync<SettingsWindow>(main);
            Click(settings, "SaveSettings");
            await WaitAsync(() => !settings.IsVisible);
            lines.Add("Rebuild tray menu and save settings: PASS");

            Click(main, "Lock");
            await WaitAsync(() => Find<TextBox>(main, pinRequired ? "Pin" : "Master") != null);
            Set(main, pinRequired ? "Pin" : "Master", pinRequired ? "743921" : "Smoke-test-only-7439!");
            Click(main, "SignIn");
            await WaitAsync(() => main.Vault is { IsUnlocked: true });
            vault = main.Vault!;
            var entry = vault.ActiveEntries.Single(e => e.Title == "Package smoke entry");
            if (entry.Username != "smoke-user" || entry.Password != "Test-entry-secret-7439")
                throw new InvalidDataException("Saved entry did not survive locking and reopening the vault.");
            Capture(main, Path.Combine(output, "03-reopened.png"));
            lines.Add("Lock, reopen encrypted vault, verify saved entry: PASS");
            if (pinRequired)
            {
                vault.RemovePin();
                if (vault.HasPin) throw new InvalidOperationException("PIN cleanup failed.");
            }
            lines.Add($"PASS: {App.SystemName} {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
            exitCode = 0;
        }
        catch (Exception error)
        {
            lines.Add(error.ToString());
            try { Capture(main, Path.Combine(output, "failure.png")); } catch { /* Keep the original failure. */ }
        }
        finally
        {
            // Remove only the keyring item belonging to this temporary vault, even after a failed test.
            try
            {
                if (vault?.HasPin == true) vault.RemovePin();
            }
            catch (Exception error)
            {
                lines.Add("PIN cleanup failed: " + error.Message);
                exitCode = 1;
            }
            File.WriteAllLines(Path.Combine(output, "result.txt"), lines);
            if (exitCode == 0) File.WriteAllText(Path.Combine(output, "passed"), "PASS");
            foreach (var line in lines) Console.WriteLine(line);
            desktop.Shutdown(exitCode);
        }
    }

    private static T? Find<T>(Visual root, string id) where T : Control => root.GetVisualDescendants()
        .OfType<T>().FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == id);

    private static void Set(Window window, string id, string value) =>
        (Find<TextBox>(window, id) ?? throw new InvalidOperationException("Missing field: " + id)).Text = value;

    private static void Click(Window window, string id)
    {
        var button = Find<Button>(window, id) ?? throw new InvalidOperationException("Missing button: " + id);
        if (!button.IsEnabled) throw new InvalidOperationException("Disabled button: " + id);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static async Task<T> WaitWindowAsync<T>(Window owner) where T : Window
    {
        await WaitAsync(() => owner.OwnedWindows.OfType<T>().Any(window => window.IsVisible));
        return owner.OwnedWindows.OfType<T>().First(window => window.IsVisible);
    }

    private static async Task WaitAsync(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!ready())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Timed out waiting for the native UI.");
            await Task.Delay(100);
        }
        // Yield to layout/render work before looking up the next screen's visual descendants.
        await Task.Delay(200);
    }

    private static void Capture(Window window, string path)
    {
        var size = window.ClientSize;
        if (size.Width < 1 || size.Height < 1) throw new InvalidOperationException("Window has no rendered area.");
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height)));
        bitmap.Render(window);
        bitmap.Save(path);
    }
}
