using System.Security.Cryptography;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PassKeeper.Core.Interop;
using PassKeeper.Core.Interop.KeePass;
using PassKeeper.Core.Storage;

namespace PassKeeper.Desktop;

/// <summary>Moving entries between computers: an encrypted .pkx (or another manager's file) in, the vault out.</summary>
public static class ImportExport
{
    public static async Task ImportAsync(Window owner, VaultService vault, Action<Guid?> refresh)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.T("Import.Title"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Loc.T("Import.Files")) { Patterns = ["*.pkx", "*.kdbx", "*.csv", "*.json", "*.xml", "*.1pux", "*.txt"] },
                FilePickerFileTypes.All,
            ],
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path == null) return;
        await ImportFileAsync(owner, vault, path, refresh);
    }

    public static async Task ImportFileAsync(Window owner, VaultService vault, string path, Action<Guid?> refresh)
    {
        var name = Path.GetFileName(path);
        ImportResult result;
        try
        {
            var data = await File.ReadAllBytesAsync(path);
            var format = FileImporter.Detect(path, data);
            string? password = null;
            if (FileImporter.NeedsPassword(format))
            {
                password = await Dialogs.AskPassword(owner, Loc.T("Import.Title"), Loc.F("Import.PasswordFor", name));
                if (password == null) return;
            }
            result = await Task.Run(() => FileImporter.Import(path, format, password));
        }
        catch (Exception ex) when (ex is CryptographicException or KdbxInvalidKeyException or ImportPasswordRequiredException)
        {
            await Dialogs.Message(owner, Loc.T("Import.Wrong"));
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FormatException
            or System.Text.Json.JsonException or System.Xml.XmlException or KdbxException or NotSupportedException)
        {
            await Dialogs.Message(owner, Loc.F("Import.Failed", ex.Message));
            return;
        }

        var plan = ImportPlanner.Plan(vault.ActiveEntries, [result]);
        var fresh = plan.Count(c => c.Status == ImportStatus.New);
        var same = plan.Count(c => c.Status == ImportStatus.Duplicate);
        var conflicts = plan.Count(c => c.Status == ImportStatus.Conflict);
        if (fresh + conflicts == 0)
        {
            await Dialogs.Message(owner, Loc.T("Import.Nothing"));
            return;
        }

        var w = new DialogWindow(Loc.T("Import.Title"), 480);
        var action = new ComboBox { ItemsSource = new[] { Loc.T("Import.Update"), Loc.T("Import.AddNew"), Loc.T("Import.Skip") }, SelectedIndex = 0, Margin = new Thickness(0, 4, 0, 0) };
        var body = Ui.Column(6, new TextBlock { Text = Loc.F("Import.Summary", name, fresh, same, conflicts), TextWrapping = TextWrapping.Wrap });
        if (conflicts > 0)
        {
            body.Children.Add(Ui.Label(Loc.T("Import.Conflict")));
            body.Children.Add(action);
        }
        var go = Ui.Button(Loc.T("Import.Button"), () => w.Close(true), accent: true);
        go.IsDefault = true;
        AutomationProperties.SetAutomationId(go, "DoImport");
        w.SetBody(body, Ui.Button(Loc.T("Common.Cancel"), () => w.Close(false)), go);
        if (!await w.ShowDialog<bool>(owner)) return;

        var conflictAction = action.SelectedIndex switch { 1 => ConflictAction.AddAsNew, 2 => ConflictAction.Skip, _ => ConflictAction.UpdatePassword };
        var (added, updated) = ImportPlanner.Apply(vault, plan, conflictAction, null);
        refresh(null);
        await Dialogs.Message(owner, Loc.F("Import.Done", added, updated));
    }

    public static async Task ExportAsync(Window owner, VaultService vault)
    {
        var w = new DialogWindow(Loc.T("Export.Title"), 500);
        var formats = new[] { ExportFormat.PassKeeperEncrypted, ExportFormat.Kdbx, ExportFormat.CsvPassKeeper };
        var radios = new[] { Loc.T("Export.Pkx"), Loc.T("Export.Kdbx"), Loc.T("Export.Csv") }
            .Select((text, i) => new RadioButton { Content = text, GroupName = "ExportFormat", IsChecked = i == 0 }).ToList();
        var warning = new TextBlock { Text = Loc.T("Export.PlainWarning"), Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        var master = Ui.Secret(id: "ExportMaster");
        var filePassword = Ui.Secret(id: "ExportPassword");
        var repeat = Ui.Secret(id: "ExportRepeat");
        var filePasswordPanel = Ui.Column(0, Ui.Label(Loc.T("Export.FilePassword")), filePassword, Ui.Label(Loc.T("Export.FileRepeat")), repeat);
        var error = Ui.ErrorText();
        ExportFormat Selected() => formats[radios.FindIndex(r => r.IsChecked == true)];
        foreach (var radio in radios)
            radio.IsCheckedChanged += (_, _) =>
            {
                var encrypted = Exporter.IsEncrypted(Selected());
                warning.IsVisible = !encrypted;
                filePasswordPanel.IsVisible = encrypted;
            };

        var go = Ui.Button(Loc.T("Export.Button"), () => { }, accent: true);
        go.IsDefault = true;
        AutomationProperties.SetAutomationId(go, "DoExport");
        go.Click += async (_, _) =>
        {
            var format = Selected();
            var password = filePassword.Text ?? "";
            if (Exporter.IsEncrypted(format))
            {
                var problem = password.Length < 8 ? Loc.F("Create.ErrShort", 8) : password != (repeat.Text ?? "") ? Loc.T("Create.ErrMismatch") : "";
                Ui.ShowError(error, problem);
                if (problem.Length > 0) return;
            }
            go.IsEnabled = false;
            var confirmed = await Task.Run(() => vault.VerifyMaster(master.Text ?? ""));
            go.IsEnabled = true;
            if (!confirmed)
            {
                Ui.ShowError(error, Loc.T("Unlock.Wrong"));
                master.Text = "";
                return;
            }
            var file = await w.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Loc.T("Export.Title"),
                SuggestedFileName = Exporter.DefaultFileName(format),
                DefaultExtension = Exporter.Extension(format).TrimStart('.'),
                ShowOverwritePrompt = true,
            });
            if (file == null) return;
            var entries = vault.ActiveEntries.ToList();
            var bytes = await Task.Run(() => Exporter.Export(entries, format, Exporter.IsEncrypted(format) ? password : null));
            await using (var stream = await file.OpenWriteAsync())
            {
                stream.SetLength(0);
                await stream.WriteAsync(bytes);
            }
            w.Close();
            await Dialogs.Message(owner, Loc.F("Export.Done", entries.Count));
        };

        var body = Ui.Column(6, Ui.Muted(Loc.T("Export.Intro")));
        foreach (var radio in radios) body.Children.Add(radio);
        body.Children.Add(warning);
        body.Children.Add(Ui.Label(Loc.T("Export.Master")));
        body.Children.Add(master);
        body.Children.Add(filePasswordPanel);
        body.Children.Add(error);
        w.SetBody(body, Ui.Button(Loc.T("Common.Cancel"), () => w.Close()), go);
        w.Opened += (_, _) => master.Focus();
        await w.ShowDialog(owner);
    }
}
