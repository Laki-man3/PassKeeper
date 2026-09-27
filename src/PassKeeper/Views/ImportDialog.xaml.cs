using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PassKeeper.Core.Interop;
using PassKeeper.Core.Interop.Browsers;
using PassKeeper.Core.Interop.KeePass;
using PassKeeper.Localization;
using PassKeeper.ViewModels;

namespace PassKeeper.Views;

public sealed class CandidateItem(ImportCandidate candidate) : Observable
{
    public ImportCandidate Candidate { get; } = candidate;
    public string Title => Candidate.Entry.Title;
    public string Subtitle => string.Join(" · ", new[] { Candidate.Entry.Username, Candidate.Source }.Where(s => !string.IsNullOrEmpty(s)));

    public bool Selected
    {
        get => Candidate.Selected;
        set
        {
            if (Candidate.Selected == value) return;
            Candidate.Selected = value;
            OnPropertyChanged();
            SelectionChanged?.Invoke();
        }
    }

    public Action? SelectionChanged { get; set; }

    public string StatusText => Candidate.Status switch
    {
        ImportStatus.Duplicate => Loc.T("Import.StatusDuplicate"),
        ImportStatus.Conflict => Loc.T("Import.StatusConflict"),
        _ => Loc.T("Import.StatusNew"),
    };

    public Brush StatusBackground => (Brush)Application.Current.FindResource(Candidate.Status switch
    {
        ImportStatus.Duplicate => "Brush.SurfaceAlt",
        ImportStatus.Conflict => "Brush.WarningSoft",
        _ => "Brush.SuccessSoft",
    });

    public Brush StatusForeground => (Brush)Application.Current.FindResource(Candidate.Status switch
    {
        ImportStatus.Duplicate => "Brush.TextSecondary",
        ImportStatus.Conflict => "Brush.Warning",
        _ => "Brush.Success",
    });
}

public partial class ImportDialog : DialogBase
{
    private readonly List<(CheckBox Box, BrowserProfile Profile)> _browsers = [];
    private List<CandidateItem> _candidates = [];

    public ImportDialog()
    {
        InitializeComponent();
        DialogWidth = 640;
        DetectBrowsers();
        ConflictMode.Items.Add(new ComboBoxItem { Content = Loc.T("Import.ConflictUpdate"), Tag = ConflictAction.UpdatePassword });
        ConflictMode.Items.Add(new ComboBoxItem { Content = Loc.T("Import.ConflictAdd"), Tag = ConflictAction.AddAsNew });
        ConflictMode.Items.Add(new ComboBoxItem { Content = Loc.T("Import.ConflictSkip"), Tag = ConflictAction.Skip });
        ConflictMode.SelectedIndex = 0;
        foreach (var f in App.Instance.Vault.Folders()) TargetFolder.Items.Add(f);
    }

    private void DetectBrowsers()
    {
        BrowserList.Children.Clear();
        _browsers.Clear();
        List<BrowserProfile> profiles;
        try { profiles = BrowserDetector.Detect(); }
        catch (Exception) { profiles = []; }
        foreach (var p in profiles)
        {
            var box = new CheckBox { Content = p.DisplayName, IsChecked = true, Margin = new Thickness(0, 4, 0, 4) };
            BrowserList.Children.Add(box);
            _browsers.Add((box, p));
        }
        NoBrowsers.Visibility = profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ImportBrowsersButton.Visibility = profiles.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => DetectBrowsers();

    private void SetBusy(string? text)
    {
        BusyLayer.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
        BusyText.Text = text ?? "";
    }

    private async void ImportBrowsers_Click(object sender, RoutedEventArgs e)
    {
        var selected = _browsers.Where(b => b.Box.IsChecked == true).Select(b => b.Profile).ToList();
        if (selected.Count == 0) return;
        SetBusy(Loc.T("Import.Reading"));
        var results = new List<ImportResult>();
        foreach (var profile in selected)
        {
            var result = await Task.Run(() => BrowserDetector.Read(profile));
            if (profile.Kind == BrowserKind.Firefox && result.Warnings.Any(w => w.Code == WarningCodes.PrimaryPassword))
            {
                SetBusy(null);
                var answer = await App.Instance.Main.ShowDialogAsync(new PasswordPromptDialog(
                    Loc.T("Import.FirefoxPrimaryTitle"), Loc.F("Import.FirefoxPrimaryText", profile.DisplayName)));
                SetBusy(Loc.T("Import.Reading"));
                if (answer is ValueTuple<string, byte[]?> (var primary, _))
                    result = await Task.Run(() => BrowserDetector.Read(profile, primary));
            }
            results.Add(result);
        }
        SetBusy(null);
        ShowPreview(results);
    }

    private async void ChooseFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("Import.ChooseFile"),
            Filter = $"{Loc.T("Import.Supported")}|{FileImporter.FileFilter}|CSV|*.csv|KeePass (*.kdbx, *.xml)|*.kdbx;*.xml|JSON|*.json|1Password (*.1pux)|*.1pux|PassKeeper (*.pkx)|*.pkx|{Loc.T("Import.AllFiles")}|*.*",
        };
        if (dlg.ShowDialog() != true) return;
        var path = dlg.FileName;

        ImportFileFormat format;
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            format = FileImporter.Detect(path, bytes);
        }
        catch (Exception ex)
        {
            await ConfirmDialog.InfoAsync(Loc.T("Import.ErrorTitle"), ex.Message);
            return;
        }

        string? password = null;
        byte[]? keyFile = null;
        while (true)
        {
            if (FileImporter.NeedsPassword(format))
            {
                var isKdbx = format == ImportFileFormat.Kdbx;
                var answer = await App.Instance.Main.ShowDialogAsync(new PasswordPromptDialog(
                    Loc.T(isKdbx ? "Import.KdbxPasswordTitle" : "Import.PkxPasswordTitle"),
                    Loc.F("Import.FilePasswordText", Path.GetFileName(path)), allowKeyFile: isKdbx));
                if (answer is not ValueTuple<string, byte[]?> (var pw, var kf)) return;
                password = pw;
                keyFile = kf;
            }

            SetBusy(Loc.T("Import.Reading"));
            try
            {
                var result = await Task.Run(() => FileImporter.Import(path, format, password, keyFile));
                SetBusy(null);
                ShowPreview([result]);
                return;
            }
            catch (Exception ex) when (ex is KdbxInvalidKeyException or CryptographicException && FileImporter.NeedsPassword(format))
            {
                SetBusy(null);
                await ConfirmDialog.InfoAsync(Loc.T("Import.ErrorTitle"), Loc.T("Import.WrongPassword"));
            }
            catch (Exception ex)
            {
                SetBusy(null);
                await ConfirmDialog.InfoAsync(Loc.T("Import.ErrorTitle"), Loc.F("Import.ErrorText", ex.Message));
                return;
            }
        }
    }

    private async void WindowsCredentials_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(Loc.T("Import.Reading"));
        var result = await Task.Run(() => WindowsCredentialImporter.Import(Loc.T("Import.WindowsCredentials")));
        SetBusy(null);
        ShowPreview([result]);
    }

    // ------------------------------------------------------------------ preview

    private void ShowPreview(List<ImportResult> results)
    {
        var plan = ImportPlanner.Plan(App.Instance.Vault.Data.Entries, results);
        _candidates = plan.Select(c => new CandidateItem(c) { SelectionChanged = UpdateApply }).ToList();

        WarningsPanel.Children.Clear();
        foreach (var w in results.SelectMany(r => r.Warnings)) WarningsPanel.Children.Add(WarningCard(w));

        PreviewTitle.Text = Loc.F("Import.Found", Loc.Plural(plan.Count, "Plural.Entries"));
        NewCount.Text = Loc.F("Import.CountNew", plan.Count(c => c.Status == ImportStatus.New));
        ConflictCount.Text = Loc.F("Import.CountConflict", plan.Count(c => c.Status == ImportStatus.Conflict));
        DuplicateCount.Text = Loc.F("Import.CountDuplicate", plan.Count(c => c.Status == ImportStatus.Duplicate));
        ConflictCount.Parent.SetValue(VisibilityProperty, plan.Any(c => c.Status == ImportStatus.Conflict) ? Visibility.Visible : Visibility.Collapsed);
        DuplicateCount.Parent.SetValue(VisibilityProperty, plan.Any(c => c.Status == ImportStatus.Duplicate) ? Visibility.Visible : Visibility.Collapsed);

        Filter.Text = "";
        CandidateList.ItemsSource = _candidates;
        SourceStep.Visibility = Visibility.Collapsed;
        PreviewStep.Visibility = Visibility.Visible;
        UpdateApply();
    }

    private Border WarningCard(ImportWarning w)
    {
        var text = w.Code switch
        {
            WarningCodes.AppBound => Loc.F("Warn.AppBound", w.Args),
            WarningCodes.DecryptFailed => Loc.F("Warn.DecryptFailed", w.Args),
            WarningCodes.PrimaryPassword => Loc.F("Warn.PrimaryPassword", w.Args),
            WarningCodes.YandexMasterPassword => Loc.F("Warn.YandexMaster", w.Args),
            WarningCodes.SkippedRows => Loc.F("Warn.SkippedRows", w.Args),
            WarningCodes.Empty => Loc.F("Warn.Empty", w.Args),
            _ => Loc.F("Warn.Error", w.Args.Length >= 2 ? w.Args : [w.Args.FirstOrDefault() ?? "", ""]),
        };
        var border = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 6) };
        border.SetResourceReference(Border.BackgroundProperty, "Brush.WarningSoft");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var icon = new TextBlock { Text = "\uE7BA", Style = (Style)FindResource("Icon"), FontSize = 14, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 10, 0) };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Warning");
        var tb = new TextBlock { Text = text, Style = (Style)FindResource("Text.Secondary"), FontSize = 12.5 };
        Grid.SetColumn(tb, 1);
        grid.Children.Add(icon);
        grid.Children.Add(tb);
        border.Child = grid;
        return border;
    }

    private void UpdateApply()
    {
        var n = _candidates.Count(c => c.Selected);
        ApplyButton.Content = Loc.F("Import.Apply", n);
        ApplyButton.IsEnabled = n > 0;
        SelectAll.IsChecked = n == 0 ? false : n == _candidates.Count ? true : null;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        var select = SelectAll.IsChecked == true;
        foreach (var c in VisibleCandidates()) c.Selected = select;
        UpdateApply();
    }

    private IEnumerable<CandidateItem> VisibleCandidates() => CandidateList.ItemsSource as IEnumerable<CandidateItem> ?? [];

    private void Filter_TextChanged(object sender, TextChangedEventArgs e)
    {
        var q = Filter.Text.Trim();
        CandidateList.ItemsSource = q.Length == 0
            ? _candidates
            : _candidates.Where(c => Core.Matching.EntryMatcher.MatchesSearch(c.Candidate.Entry, q) || c.Candidate.Source.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var action = ConflictMode.SelectedItem is ComboBoxItem { Tag: ConflictAction a } ? a : ConflictAction.UpdatePassword;
        try
        {
            var (added, updated) = ImportPlanner.Apply(App.Instance.Vault, _candidates.Select(c => c.Candidate), action, TargetFolder.Text);
            App.Instance.Main.ShowToast(Loc.F("Import.Done", added, updated));
            Close(true);
        }
        catch (Exception ex)
        {
            App.Instance.Main.ShowToast(Loc.F("Common.ErrorFormat", ex.Message), error: true);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        PreviewStep.Visibility = Visibility.Collapsed;
        SourceStep.Visibility = Visibility.Visible;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close(null);
}
