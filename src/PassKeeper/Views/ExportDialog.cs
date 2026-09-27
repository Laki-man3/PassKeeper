using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PassKeeper.Controls;
using PassKeeper.Core.Interop;
using PassKeeper.Localization;

namespace PassKeeper.Views;

/// <summary>Export to every supported format. Always re-confirms the master password.</summary>
public sealed class ExportDialog : DialogBase
{
    private static readonly (ExportFormat Format, string Title, string Description)[] Formats =
    [
        (ExportFormat.PassKeeperEncrypted, "Export.Pkx", "Export.PkxDesc"),
        (ExportFormat.Kdbx, "Export.Kdbx", "Export.KdbxDesc"),
        (ExportFormat.CsvChrome, "Export.CsvChrome", "Export.CsvChromeDesc"),
        (ExportFormat.CsvFirefox, "Export.CsvFirefox", "Export.CsvFirefoxDesc"),
        (ExportFormat.JsonBitwarden, "Export.JsonBitwarden", "Export.JsonBitwardenDesc"),
        (ExportFormat.CsvBitwarden, "Export.CsvBitwarden", "Export.CsvBitwardenDesc"),
        (ExportFormat.CsvOnePassword, "Export.CsvOnePassword", "Export.CsvOnePasswordDesc"),
        (ExportFormat.CsvLastPass, "Export.CsvLastPass", "Export.CsvLastPassDesc"),
        (ExportFormat.CsvKeePassXc, "Export.CsvKeePassXc", "Export.CsvKeePassXcDesc"),
        (ExportFormat.XmlKeePass, "Export.XmlKeePass", "Export.XmlKeePassDesc"),
        (ExportFormat.CsvPassKeeper, "Export.CsvPassKeeper", "Export.CsvPassKeeperDesc"),
        (ExportFormat.JsonPassKeeper, "Export.JsonPassKeeper", "Export.JsonPassKeeperDesc"),
        (ExportFormat.Html, "Export.Html", "Export.HtmlDesc"),
    ];

    private readonly List<(RadioButton Radio, ExportFormat Format)> _radios = [];
    private readonly SecretBox _master = new();
    private readonly Border _plainWarning;
    private readonly TextBlock _error = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly Button _exportButton;

    public ExportDialog()
    {
        DialogWidth = 640;
        var count = App.Instance.Vault.ActiveEntries.Count();
        var dock = new DockPanel();

        var header = new Grid { Margin = new Thickness(26, 20, 18, 0) };
        header.Children.Add(new TextBlock { Text = Loc.T("Export.Title"), Style = (Style)FindResource("Text.H2"), FontSize = 18, VerticalAlignment = VerticalAlignment.Center });
        var close = new Button { Style = (Style)FindResource("Btn.Icon"), Content = "\uE711", HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close(null);
        header.Children.Add(close);
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);

        var subtitle = new TextBlock { Text = Loc.F("Export.Subtitle", Loc.Plural(count, "Plural.Entries")), Style = (Style)FindResource("Text.Secondary"), Margin = new Thickness(26, 6, 26, 14) };
        DockPanel.SetDock(subtitle, Dock.Top);
        dock.Children.Add(subtitle);

        // footer
        var footer = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(26, 14, 26, 14) };
        footer.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        var footerPanel = new StackPanel();
        _plainWarning = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 12) };
        _plainWarning.SetResourceReference(Border.BackgroundProperty, "Brush.DangerSoft");
        var warnText = new TextBlock { Text = Loc.T("Export.PlainWarning"), Style = (Style)FindResource("Text.Secondary"), FontSize = 12.5 };
        _plainWarning.Child = warnText;
        footerPanel.Children.Add(_plainWarning);
        footerPanel.Children.Add(new TextBlock { Text = Loc.T("Export.ConfirmMaster"), Style = (Style)FindResource("Text.Label") });
        _master.Placeholder = Loc.T("Unlock.MasterPlaceholder");
        _master.EnterPressed += async (_, _) => await ExportAsync();
        footerPanel.Children.Add(_master);
        _error.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger");
        footerPanel.Children.Add(_error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = new Button { Style = (Style)FindResource("Btn.Ghost"), Content = Loc.T("Common.Cancel") };
        cancel.Click += (_, _) => Close(null);
        _exportButton = new Button { Style = (Style)FindResource("Btn.Primary"), Content = Loc.T("Export.Button"), Margin = new Thickness(8, 0, 0, 0), MinWidth = 140 };
        UI.SetIcon(_exportButton, "\uEDE1");
        _exportButton.Click += async (_, _) => await ExportAsync();
        buttons.Children.Add(cancel);
        buttons.Children.Add(_exportButton);
        footerPanel.Children.Add(buttons);
        footer.Child = footerPanel;
        DockPanel.SetDock(footer, Dock.Bottom);
        dock.Children.Add(footer);

        // formats
        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(26, 0, 22, 12) };
        foreach (var (format, title, description) in Formats)
        {
            var radio = new RadioButton
            {
                Style = (Style)FindResource("CardRadio"),
                Content = Loc.T(title),
                GroupName = "ExportFormat",
                Margin = new Thickness(0, 0, 8, 8),
            };
            UI.SetDescription(radio, Loc.T(description));
            radio.Checked += (_, _) => UpdateWarning();
            grid.Children.Add(radio);
            _radios.Add((radio, format));
        }
        _radios[0].Radio.IsChecked = true;
        dock.Children.Add(new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false });

        Content = dock;
        UpdateWarning();
        Loaded += (_, _) => _master.FocusInput();
    }

    private ExportFormat Selected => _radios.First(r => r.Radio.IsChecked == true).Format;

    private void UpdateWarning() =>
        _plainWarning.Visibility = _radios.Count > 0 && Exporter.IsEncrypted(Selected) ? Visibility.Collapsed : Visibility.Visible;

    private async Task ExportAsync()
    {
        var master = _master.Value;
        if (master.Length == 0)
        {
            ShowError(Loc.T("Export.NeedMaster"));
            return;
        }
        _exportButton.IsEnabled = false;
        var vault = App.Instance.Vault;
        var ok = await Task.Run(() => vault.VerifyMaster(master));
        _exportButton.IsEnabled = true;
        if (!ok)
        {
            ShowError(Loc.T("Unlock.WrongMaster"));
            _master.Clear();
            return;
        }

        var format = Selected;
        string? password = null;
        if (Exporter.IsEncrypted(format))
        {
            var answer = await App.Instance.Main.ShowDialogAsync(new PasswordPromptDialog(Loc.T("Export.PasswordTitle"), Loc.T("Export.PasswordText"),
                confirm: true, strength: true) { MinLength = 8 });
            if (answer is not ValueTuple<string, byte[]?> (var pw, _)) return;
            password = pw;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = Exporter.DefaultFileName(format),
            Filter = $"{_radios.First(r => r.Format == format).Radio.Content as string}|*{Exporter.Extension(format)}",
            OverwritePrompt = true,
        };
        if (dlg.ShowDialog() != true) return;

        _exportButton.IsEnabled = false;
        try
        {
            var entries = vault.ActiveEntries.ToList();
            var bytes = await Task.Run(() => Exporter.Export(entries, format, password));
            await File.WriteAllBytesAsync(dlg.FileName, bytes);
            App.Instance.Main.ShowToast(Loc.F("Export.Done", Loc.Plural(entries.Count, "Plural.Entries")));
            Close(true);
        }
        catch (Exception ex)
        {
            ShowError(Loc.F("Common.ErrorFormat", ex.Message));
        }
        finally
        {
            _exportButton.IsEnabled = true;
        }
    }

    private void ShowError(string text)
    {
        _error.Text = text;
        _error.Visibility = Visibility.Visible;
    }
}
