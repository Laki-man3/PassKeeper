using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PassKeeper.Controls;
using PassKeeper.Core.Matching;
using PassKeeper.Core.Models;
using PassKeeper.Core.Storage;
using PassKeeper.Localization;
using PassKeeper.ViewModels;

namespace PassKeeper.Views;

public partial class VaultView : UserControl
{
    private readonly ObservableCollection<NavItem> _nav = [];
    private readonly Dictionary<Guid, EntryItem> _items = [];
    private NavItem? _currentNav;
    private bool _suppressSelection;
    private string _sort;

    private static VaultService Vault => App.Instance.Vault;

    public VaultView()
    {
        InitializeComponent();
        NavList.ItemsSource = _nav;
        _sort = App.Instance.Settings.SortOrder;
        BuildSortBox();
        UpdateUser();

        Loaded += (_, _) =>
        {
            Vault.DataChanged += OnDataChanged;
            Loc.I.LanguageChanged += OnLanguageChanged;
        };
        Unloaded += (_, _) =>
        {
            Vault.DataChanged -= OnDataChanged;
            Loc.I.LanguageChanged -= OnLanguageChanged;
        };
        PreviewKeyDown += OnPreviewKeyDown;
        Reload();
        ShowEmptyDetails();
    }

    // ------------------------------------------------------------------ data

    private void OnDataChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnDataChanged(sender, e));
            return;
        }
        if (Vault.IsUnlocked) Reload();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        BuildSortBox();
        Reload();
        if (DetailHost.Content is EmptyDetailsView) ShowEmptyDetails();
    }

    public void Reload()
    {
        var entries = Vault.Data.Entries;
        var alive = new HashSet<Guid>();
        foreach (var e in entries)
        {
            alive.Add(e.Id);
            if (_items.TryGetValue(e.Id, out var item)) item.Refresh(e);
            else _items[e.Id] = new EntryItem(e);
        }
        foreach (var id in _items.Keys.Where(id => !alive.Contains(id)).ToList()) _items.Remove(id);
        RebuildNav();
        RefreshList();
        if (DetailHost.Content is EntryDetailsView details)
        {
            var current = Vault.Find(details.EntryId);
            if (current == null) ShowEmptyDetails();
            else details.Show(current);
        }
    }

    private void RebuildNav()
    {
        var active = Vault.ActiveEntries.ToList();
        var folders = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var f in active.Select(e => e.Folder.Trim().Trim('/')).Where(f => f.Length > 0))
        {
            var parts = f.Split('/');
            for (var i = 1; i <= parts.Length; i++) folders.Add(string.Join("/", parts.Take(i)));
        }

        var items = new List<NavItem>
        {
            new() { Kind = NavKind.All, Icon = "\uE8A9", Label = Loc.T("Nav.All"), Count = active.Count },
            new() { Kind = NavKind.Favorites, Icon = "\uE734", Label = Loc.T("Nav.Favorites"), Count = active.Count(e => e.Favorite) },
        };
        if (folders.Count > 0)
        {
            items.Add(new NavItem { Kind = NavKind.Header, Label = Loc.T("Nav.Folders").ToUpperInvariant() });
            foreach (var f in folders)
            {
                var depth = f.Count(ch => ch == '/');
                items.Add(new NavItem
                {
                    Kind = NavKind.Folder,
                    Folder = f,
                    Icon = "\uE8B7",
                    Depth = depth,
                    Label = new string(' ', depth * 3) + f[(f.LastIndexOf('/') + 1)..],
                    Count = active.Count(e => InFolder(e, f)),
                });
            }
        }
        items.Add(new NavItem { Kind = NavKind.Header, Label = "" });
        items.Add(new NavItem { Kind = NavKind.Trash, Icon = "\uE74D", Label = Loc.T("Nav.Trash"), Count = Vault.Data.Entries.Count(e => e.IsDeleted) });

        _suppressSelection = true;
        _nav.Clear();
        foreach (var i in items) _nav.Add(i);
        var select = _nav.FirstOrDefault(n => n.SameAs(_currentNav)) ?? _nav[0];
        NavList.SelectedItem = select;
        _currentNav = select;
        _suppressSelection = false;
    }

    private static bool InFolder(VaultEntry e, string folder)
    {
        var f = e.Folder.Trim().Trim('/');
        return f.Equals(folder, StringComparison.CurrentCultureIgnoreCase) ||
               f.StartsWith(folder + "/", StringComparison.CurrentCultureIgnoreCase);
    }

    private bool MatchesNav(VaultEntry e) => _currentNav?.Kind switch
    {
        NavKind.Trash => e.IsDeleted,
        NavKind.Favorites => !e.IsDeleted && e.Favorite,
        NavKind.Folder => !e.IsDeleted && InFolder(e, _currentNav.Folder),
        _ => !e.IsDeleted,
    };

    private void RefreshList(Guid? select = null)
    {
        var selectedId = select ?? (EntryList.SelectedItem as EntryItem)?.Id;
        var query = Search.Text.Trim();
        var list = _items.Values.Where(i => MatchesNav(i.Entry) && EntryMatcher.MatchesSearch(i.Entry, query));
        list = _sort switch
        {
            "modified" => list.OrderByDescending(i => i.Entry.ModifiedUtc),
            "used" => list.OrderByDescending(i => i.Entry.LastUsedUtc ?? DateTime.MinValue).ThenByDescending(i => i.Entry.ModifiedUtc),
            _ => list.OrderByDescending(i => i.Entry.Favorite && _currentNav?.Kind != NavKind.Favorites)
                .ThenBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase),
        };
        var result = list.ToList();

        _suppressSelection = true;
        EntryList.ItemsSource = result;
        EntryList.SelectedItem = selectedId.HasValue ? result.FirstOrDefault(i => i.Id == selectedId) : null;
        _suppressSelection = false;

        var isTrash = _currentNav?.Kind == NavKind.Trash;
        ListTitle.Text = _currentNav?.Kind == NavKind.Folder ? _currentNav.Folder.Split('/').Last() : _currentNav?.Label.Trim() ?? "";
        ListCount.Text = result.Count.ToString();
        EmptyTrashButton.Visibility = isTrash && result.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SortBox.Visibility = isTrash ? Visibility.Collapsed : Visibility.Visible;
        ClearSearch.Visibility = query.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        EmptyState.Visibility = result.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (result.Count == 0)
        {
            var noEntries = Vault.ActiveEntries.Any() == false && !isTrash && query.Length == 0;
            EmptyIcon.Text = query.Length > 0 ? "\uE721" : isTrash ? "\uE74D" : "\uE8D7";
            EmptyTitle.Text = query.Length > 0 ? Loc.T("Vault.NothingFound") : isTrash ? Loc.T("Vault.TrashEmpty") : noEntries ? Loc.T("Vault.NoEntries") : Loc.T("Vault.EmptyFolder");
            EmptyText.Text = query.Length > 0 ? Loc.T("Vault.NothingFoundHint") : isTrash ? Loc.T("Vault.TrashEmptyHint") : noEntries ? Loc.T("Vault.NoEntriesHint") : "";
            EmptyText.Visibility = EmptyText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyActions.Visibility = noEntries ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void UpdateUser()
    {
        UserNameText.Text = Vault.UserName;
        UserInitial.Text = Avatar.InitialOf(Vault.UserName);
    }

    private void BuildSortBox()
    {
        var sort = _sort;
        _suppressSelection = true;
        SortBox.Items.Clear();
        foreach (var (key, label) in new[] { ("name", "Sort.Name"), ("modified", "Sort.Modified"), ("used", "Sort.Used") })
            SortBox.Items.Add(new ComboBoxItem { Content = Loc.T(label), Tag = key });
        SortBox.SelectedItem = SortBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == sort) ?? SortBox.Items[0];
        _suppressSelection = false;
    }

    // ------------------------------------------------------------------ details pane

    private EntryItem? Selected => EntryList.SelectedItem as EntryItem;

    public void ShowEmptyDetails() => DetailHost.Content = new EmptyDetailsView();

    private async Task<bool> ConfirmLeaveEditorAsync()
    {
        if (DetailHost.Content is not EntryEditorView editor || !editor.IsDirty) return true;
        return await ConfirmDialog.AskAsync(Loc.T("Editor.DiscardTitle"), Loc.T("Editor.DiscardText"), Loc.T("Editor.Discard"), danger: true);
    }

    private async void EntryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection) return;
        if (!await ConfirmLeaveEditorAsync())
        {
            _suppressSelection = true;
            EntryList.SelectedItem = e.RemovedItems.Count > 0 ? e.RemovedItems[0] : null;
            _suppressSelection = false;
            return;
        }
        if (Selected is { } item) ShowDetails(item.Entry);
        else ShowEmptyDetails();
    }

    private void ShowDetails(VaultEntry entry)
    {
        var view = DetailHost.Content as EntryDetailsView ?? new EntryDetailsView();
        view.Show(entry);
        view.EditRequested -= StartEdit;
        view.EditRequested += StartEdit;
        view.DeleteRequested -= DeleteEntry;
        view.DeleteRequested += DeleteEntry;
        DetailHost.Content = view;
    }

    private void StartEdit(VaultEntry entry) => OpenEditor(entry.Clone(), isNew: false);

    private void OpenEditor(VaultEntry entry, bool isNew)
    {
        var editor = new EntryEditorView(entry, isNew, Vault.Folders());
        editor.Saved += saved =>
        {
            Vault.Upsert(saved);
            if (_currentNav?.Kind == NavKind.Trash || (_currentNav?.Kind == NavKind.Folder && !InFolder(saved, _currentNav.Folder)) ||
                (_currentNav?.Kind == NavKind.Favorites && !saved.Favorite))
            {
                _currentNav = _nav[0];
                RebuildNav();
            }
            Search.Text = Search.Text.Length > 0 && !EntryMatcher.MatchesSearch(saved, Search.Text) ? "" : Search.Text;
            RefreshList(saved.Id);
            ShowDetails(Vault.Find(saved.Id)!);
            App.Instance.Main.ShowToast(Loc.T(isNew ? "Editor.Created" : "Editor.Saved"));
        };
        editor.Cancelled += () =>
        {
            if (Selected is { } item) ShowDetails(item.Entry);
            else ShowEmptyDetails();
        };
        DetailHost.Content = editor;
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmLeaveEditorAsync()) return;
        _suppressSelection = true;
        EntryList.SelectedItem = null;
        _suppressSelection = false;
        var entry = new VaultEntry { Folder = _currentNav?.Kind == NavKind.Folder ? _currentNav.Folder : "", Favorite = _currentNav?.Kind == NavKind.Favorites };
        OpenEditor(entry, isNew: true);
    }

    private async void DeleteEntry(VaultEntry entry)
    {
        if (entry.IsDeleted)
        {
            if (!await ConfirmDialog.AskAsync(Loc.T("Vault.DeleteForeverTitle"), Loc.F("Vault.DeleteForeverText", entry.Title), Loc.T("Vault.DeleteForever"), danger: true))
                return;
            Vault.DeletePermanently(entry.Id);
            App.Instance.Main.ShowToast(Loc.T("Vault.DeletedForever"));
        }
        else
        {
            Vault.MoveToTrash(entry.Id);
            App.Instance.Main.ShowToast(Loc.T("Vault.MovedToTrash"));
        }
        ShowEmptyDetails();
    }

    // ------------------------------------------------------------------ navigation & search

    private async void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection || NavList.SelectedItem is not NavItem nav) return;
        if (nav.IsHeader)
        {
            _suppressSelection = true;
            NavList.SelectedItem = _currentNav;
            _suppressSelection = false;
            return;
        }
        if (!await ConfirmLeaveEditorAsync())
        {
            _suppressSelection = true;
            NavList.SelectedItem = _currentNav;
            _suppressSelection = false;
            return;
        }
        _currentNav = nav;
        RefreshList();
        if (Selected == null) ShowEmptyDetails();
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => RefreshList();

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        Search.Clear();
        Search.Focus();
    }

    private void Sort_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection || SortBox.SelectedItem is not ComboBoxItem { Tag: string key }) return;
        _sort = key;
        App.Instance.Settings.SortOrder = key;
        App.Instance.Settings.Save();
        RefreshList();
    }

    private async void EmptyTrash_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmDialog.AskAsync(Loc.T("Vault.EmptyTrashTitle"), Loc.T("Vault.EmptyTrashText"), Loc.T("Vault.EmptyTrash"), danger: true))
            return;
        var n = Vault.EmptyTrash();
        ShowEmptyDetails();
        App.Instance.Main.ShowToast(Loc.F("Vault.TrashEmptied", n));
    }

    // ------------------------------------------------------------------ keyboard & context menu

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (App.Instance.Main.IsDialogOpen) return;
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        if (ctrl && e.Key == Key.F)
        {
            Search.Focus();
            Search.SelectAll();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.N)
        {
            Add_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.L)
        {
            App.Instance.Lock();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && Search.IsKeyboardFocused && Search.Text.Length > 0)
        {
            Search.Clear();
            e.Handled = true;
        }
        else if (e.Key == Key.Down && Search.IsKeyboardFocused && EntryList.Items.Count > 0)
        {
            EntryList.SelectedIndex = Math.Max(0, EntryList.SelectedIndex);
            (EntryList.ItemContainerGenerator.ContainerFromIndex(EntryList.SelectedIndex) as ListBoxItem)?.Focus();
            e.Handled = true;
        }
    }

    private void EntryList_KeyDown(object sender, KeyEventArgs e)
    {
        if (Selected is not { } item) return;
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        if (ctrl && e.Key == Key.C) App.Instance.CopySecret(item.Entry.Password, Loc.T("Toast.PasswordCopied"), item.Entry.Id);
        else if (ctrl && e.Key == Key.B) App.Instance.CopyPlain(Login(item.Entry), Loc.T("Toast.LoginCopied"));
        else if (ctrl && e.Key == Key.E && !item.Entry.IsDeleted) StartEdit(item.Entry);
        else if (e.Key == Key.Delete) DeleteEntry(item.Entry);
        else return;
        e.Handled = true;
    }

    private static string Login(VaultEntry e) => e.Username.Length > 0 ? e.Username : e.Email;

    private void EntryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Selected is { } item && !item.Entry.IsDeleted && item.Entry.Password.Length > 0)
            App.Instance.CopySecret(item.Entry.Password, Loc.T("Toast.PasswordCopied"), item.Entry.Id);
    }

    private void EntryMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item)
        {
            EntryMenu.IsOpen = false;
            return;
        }
        var deleted = item.Entry.IsDeleted;
        MenuCopyUser.IsEnabled = Login(item.Entry).Length > 0;
        MenuCopyPassword.IsEnabled = item.Entry.Password.Length > 0;
        MenuOpenUrl.IsEnabled = DomainUtil.NormalizeUrlForOpen(item.Entry.Url) != null;
        MenuFavorite.Header = Loc.T(item.Entry.Favorite ? "Vault.Unfavorite" : "Vault.Favorite");
        MenuFavorite.Visibility = MenuEdit.Visibility = MenuDuplicate.Visibility = deleted ? Visibility.Collapsed : Visibility.Visible;
        MenuRestore.Visibility = deleted ? Visibility.Visible : Visibility.Collapsed;
        MenuDelete.Header = Loc.T(deleted ? "Vault.DeleteForever" : "Common.Delete");
    }

    private void MenuCopyUser_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } item) App.Instance.CopyPlain(Login(item.Entry), Loc.T("Toast.LoginCopied"));
    }

    private void MenuCopyPassword_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } item) App.Instance.CopySecret(item.Entry.Password, Loc.T("Toast.PasswordCopied"), item.Entry.Id);
    }

    private void MenuOpenUrl_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } item) App.Instance.OpenUrl(item.Entry.Url);
    }

    private void MenuFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } item) Vault.ToggleFavorite(item.Id);
    }

    private void MenuEdit_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } item) StartEdit(item.Entry);
    }

    private void MenuDuplicate_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item) return;
        var copy = item.Entry.Clone();
        copy.Id = Guid.NewGuid();
        copy.Title = Loc.F("Vault.CopyOf", copy.Title);
        copy.CreatedUtc = DateTime.UtcNow;
        copy.PasswordHistory.Clear();
        OpenEditor(copy, isNew: true);
    }

    private void MenuRestore_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item) return;
        Vault.Restore(item.Id);
        App.Instance.Main.ShowToast(Loc.T("Vault.Restored"));
    }

    private void MenuDelete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } item) DeleteEntry(item.Entry);
    }

    // ------------------------------------------------------------------ sidebar buttons

    private async void Generator_Click(object sender, RoutedEventArgs e) =>
        await App.Instance.Main.ShowDialogAsync(new GeneratorDialog(pickMode: false));

    private async void Import_Click(object sender, RoutedEventArgs e) =>
        await App.Instance.Main.ShowDialogAsync(new ImportDialog());

    private async void Export_Click(object sender, RoutedEventArgs e) =>
        await App.Instance.Main.ShowDialogAsync(new ExportDialog());

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        await App.Instance.Main.ShowDialogAsync(new SettingsDialog());
        UpdateUser();
    }

    private void Lock_Click(object sender, RoutedEventArgs e) => App.Instance.Lock();
}
