using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PassKeeper.Controls;
using PassKeeper.Core.Matching;
using PassKeeper.Core.Models;
using PassKeeper.Core.Storage;
using PassKeeper.Localization;
using PassKeeper.Services;
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
        SidebarColumn.Width = new GridLength(Math.Clamp(App.Instance.Settings.SidebarWidth, SidebarColumn.MinWidth, SidebarMax));
        ListColumn.Width = new GridLength(Math.Clamp(App.Instance.Settings.ListWidth, ListColumn.MinWidth, ListMax));
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
            new() { Kind = NavKind.Header, Label = Loc.T("Nav.Sections").ToUpperInvariant() },
        };
        foreach (var category in EntryCategories.All)
        {
            items.Add(new NavItem
            {
                Kind = NavKind.Category,
                Category = category,
                Icon = CategoryIcon(category),
                Label = CategoryName(category),
                Count = active.Count(e => e.EffectiveCategory == category),
            });
        }
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
        items.Add(new NavItem { Kind = NavKind.Separator });
        items.Add(new NavItem { Kind = NavKind.Trash, Icon = "\uE74D", Label = Loc.T("Nav.Trash"), Count = Vault.Data.Entries.Count(e => e.IsDeleted) });

        _suppressSelection = true;
        _nav.Clear();
        foreach (var i in items) _nav.Add(i);
        var select = _nav.FirstOrDefault(n => n.SameAs(_currentNav)) ?? _nav[0];
        NavList.SelectedItem = select;
        _currentNav = select;
        _suppressSelection = false;
    }

    public static string CategoryName(EntryCategory category) => Loc.T("Category." + category);

    public static string CategoryIcon(EntryCategory category) => category switch
    {
        EntryCategory.Web => "\uE774",
        EntryCategory.Remote => "\uE705",
        EntryCategory.App => "\uE71D",
        _ => "\uE8EC",
    };

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
        NavKind.Category => !e.IsDeleted && e.EffectiveCategory == _currentNav.Category,
        _ => !e.IsDeleted,
    };

    private void RefreshList(Guid? select = null)
    {
        HashSet<Guid> selectedIds = select is { } id ? [id] : EntryList.SelectedItems.Cast<EntryItem>().Select(i => i.Id).ToHashSet();
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
        foreach (var item in result.Where(i => selectedIds.Contains(i.Id))) EntryList.SelectedItems.Add(item);
        _suppressSelection = false;
        UpdateSelectionMarks();
        // The selected entries that left the list (moved to the trash, filtered out) leave the selection too.
        if (DetailHost.Content is BulkActionsView) ShowSelection();

        var isTrash = _currentNav?.Kind == NavKind.Trash;
        ListTitle.Text = _currentNav?.Kind == NavKind.Folder ? _currentNav.Folder.Split('/').Last() : _currentNav?.Label.Trim() ?? "";
        ListCount.Text = result.Count.ToString();
        EmptyTrashButton.Visibility = isTrash && result.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SortBox.Visibility = isTrash ? Visibility.Collapsed : Visibility.Visible;
        ClearSearch.Visibility = query.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ArrangeListHeader();

        EmptyState.Visibility = result.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (result.Count == 0)
        {
            var noEntries = Vault.ActiveEntries.Any() == false && !isTrash && query.Length == 0;
            EmptyIcon.Text = query.Length > 0 ? "\uE721" : isTrash ? "\uE74D" : "\uE8D7";
            EmptyTitle.Text = query.Length > 0 ? Loc.T("Vault.NothingFound") : isTrash ? Loc.T("Vault.TrashEmpty") : noEntries ? Loc.T("Vault.NoEntries") : Loc.T("Vault.EmptyFolder");
            EmptyText.Text = query.Length > 0 ? Loc.T("Vault.NothingFoundHint") : isTrash ? Loc.T("Vault.TrashEmptyHint") : noEntries ? Loc.T("Vault.NoEntriesHint") : "";
            EmptyText.Visibility = EmptyText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyActions.Visibility = noEntries ? Visibility.Visible : Visibility.Collapsed;
            var clients = _currentNav?.Kind == NavKind.Category && _currentNav.Category is EntryCategory.Remote or EntryCategory.App && query.Length == 0;
            FindClientsButton.Visibility = clients ? Visibility.Visible : Visibility.Collapsed;
            if (clients && !noEntries)
            {
                EmptyIcon.Text = CategoryIcon(_currentNav!.Category);
                EmptyTitle.Text = Loc.T("Vault.EmptySection");
                EmptyText.Text = Loc.T(_currentNav.Category == EntryCategory.Remote ? "Vault.EmptyRemoteHint" : "Vault.EmptyAppHint");
                EmptyText.Visibility = Visibility.Visible;
            }
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

    private bool IsMultiple => EntryList.SelectedItems.Count > 1;

    /// <summary>The selected entries in the order of the list.</summary>
    private List<EntryItem> SelectedItems()
    {
        var selected = EntryList.SelectedItems.Cast<EntryItem>().ToHashSet();
        return EntryList.Items.Cast<EntryItem>().Where(selected.Contains).ToList();
    }

    public void ShowEmptyDetails() => DetailHost.Content = new EmptyDetailsView();

    private async Task<bool> ConfirmLeaveEditorAsync()
    {
        if (DetailHost.Content is not EntryEditorView editor || !editor.IsDirty) return true;
        return await ConfirmDialog.AskAsync(Loc.T("Editor.DiscardTitle"), Loc.T("Editor.DiscardText"), Loc.T("Editor.Discard"), danger: true);
    }

    private async void EntryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectionMarks();
        if (_suppressSelection) return;
        if (!await ConfirmLeaveEditorAsync())
        {
            _suppressSelection = true;
            foreach (var added in e.AddedItems) EntryList.SelectedItems.Remove(added);
            foreach (var removed in e.RemovedItems) EntryList.SelectedItems.Add(removed);
            _suppressSelection = false;
            UpdateSelectionMarks();
            return;
        }
        ShowSelection();
    }

    /// <summary>One entry: its card; several: what can be done with all of them; none: the overview.</summary>
    private void ShowSelection()
    {
        var items = SelectedItems();
        if (items.Count > 1) ShowBulk(items);
        else if (items.Count == 1) ShowDetails(items[0].Entry);
        else ShowEmptyDetails();
    }

    /// <summary>
    /// Several selected entries turn the avatars into round checks (a click on one adds or removes that entry) and
    /// the box in the list header shows whether all of the list is selected.
    /// </summary>
    private void UpdateSelectionMarks()
    {
        var count = EntryList.SelectedItems.Count;
        var total = EntryList.Items.Count;
        EntryList.Tag = count > 1 ? "multi" : null;
        SelectAllBox.Visibility = total > 1 ? Visibility.Visible : Visibility.Collapsed;
        _syncingSelectAll = true;
        SelectAllBox.IsChecked = count > 1 && count == total ? true : count > 1 ? null : false;
        _syncingSelectAll = false;
    }

    private bool _syncingSelectAll;

    /// <summary>A click (or a screen reader's toggle) on the box: the whole list is selected, or cleared when it already was.</summary>
    private void SelectAll_Toggled(object sender, RoutedEventArgs e)
    {
        if (_syncingSelectAll) return;
        if (EntryList.Items.Count > 1 && EntryList.SelectedItems.Count == EntryList.Items.Count) ClearSelection();
        else SelectAllEntries();
    }

    private void SelectAllEntries()
    {
        EntryList.SelectAll();
        UpdateSelectionMarks();
    }

    private void ClearSelection()
    {
        EntryList.UnselectAll();
        UpdateSelectionMarks();
    }

    private void SelectMark_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not EntryItem item) return;
        if (EntryList.SelectedItems.Contains(item)) EntryList.SelectedItems.Remove(item);
        else EntryList.SelectedItems.Add(item);
        e.Handled = true;
    }

    private void ShowBulk(List<EntryItem> items)
    {
        var view = new BulkActionsView(items.Select(i => i.Entry).ToList(), EntryList.Items.Count, _currentNav?.Kind == NavKind.Trash);
        view.FavoriteRequested += favorite => SetFavoriteSelected(favorite);
        view.DeleteRequested += DeleteSelected;
        view.RestoreRequested += RestoreSelected;
        view.SelectAllRequested += SelectAllEntries;
        view.ClearRequested += ClearSelection;
        DetailHost.Content = view;
    }

    private void SetFavoriteSelected(bool favorite)
    {
        var n = Vault.SetFavorite(SelectedItems().Select(i => i.Id), favorite);
        App.Instance.Main.ShowToast(Loc.F(favorite ? "Bulk.Favorited" : "Bulk.Unfavorited", Loc.Plural(n, "Plural.Entries")));
    }

    private void RestoreSelected()
    {
        var n = Vault.Restore(SelectedItems().Select(i => i.Id));
        App.Instance.Main.ShowToast(Loc.F("Bulk.Restored", Loc.Plural(n, "Plural.Entries")));
    }

    /// <summary>Several entries go to the trash (or out of it for good) only after a confirmation.</summary>
    private async void DeleteSelected()
    {
        var items = SelectedItems();
        if (items.Count == 0) return;
        if (items.Count == 1)
        {
            DeleteEntry(items[0].Entry);
            return;
        }
        var ids = items.Select(i => i.Id).ToList();
        var what = Loc.Plural(ids.Count, "Plural.Entries");
        if (items.All(i => i.Entry.IsDeleted))
        {
            if (!await ConfirmDialog.AskAsync(Loc.T("Bulk.DeleteForeverTitle"), Loc.F("Bulk.DeleteForeverText", what), Loc.T("Vault.DeleteForever"), danger: true))
                return;
            var n = Vault.DeletePermanently(ids);
            App.Instance.Main.ShowToast(Loc.F("Bulk.DeletedForever", Loc.Plural(n, "Plural.Entries")));
        }
        else
        {
            if (!await ConfirmDialog.AskAsync(Loc.T("Bulk.DeleteTitle"), Loc.F("Bulk.DeleteText", what), Loc.T("Common.Delete"), danger: true))
                return;
            var n = Vault.MoveToTrash(ids);
            App.Instance.Main.ShowToast(Loc.F("Bulk.MovedToTrash", Loc.Plural(n, "Plural.Entries")));
        }
        ShowEmptyDetails();
    }

    private void ShowDetails(VaultEntry entry)
    {
        var view = DetailHost.Content as EntryDetailsView ?? new EntryDetailsView();
        view.Show(entry);
        view.EditRequested -= StartEdit;
        view.EditRequested += StartEdit;
        view.DeleteRequested -= DeleteEntry;
        view.DeleteRequested += DeleteEntry;
        view.DuplicateRequested -= DuplicateEntry;
        view.DuplicateRequested += DuplicateEntry;
        view.UseForClientRequested -= UseForClient;
        view.UseForClientRequested += UseForClient;
        view.UseForSiteRequested -= UseForSite;
        view.UseForSiteRequested += UseForSite;
        DetailHost.Content = view;
    }

    private void StartEdit(VaultEntry entry) => OpenEditor(entry.Clone(), isNew: false);

    private EntryEditorView OpenEditor(VaultEntry entry, bool isNew, Func<VaultEntry, Task>? fillAfterSave = null)
    {
        var oldPassword = isNew ? "" : Vault.Find(entry.Id)?.Password ?? "";
        var editor = new EntryEditorView(entry, isNew, Vault.Folders());
        editor.Saved += async saved =>
        {
            if (isNew && await UseSameAccountAsync(saved) is { } same)
            {
                RefreshList(same.Id);
                ShowDetails(same);
                if (fillAfterSave != null && editor.FillAfterSave) await fillAfterSave(same);
                return;
            }
            Vault.Upsert(saved);
            if (_currentNav?.Kind == NavKind.Trash || (_currentNav?.Kind == NavKind.Folder && !InFolder(saved, _currentNav.Folder)) ||
                (_currentNav?.Kind == NavKind.Favorites && !saved.Favorite) ||
                (_currentNav?.Kind == NavKind.Category && saved.EffectiveCategory != _currentNav.Category))
            {
                _currentNav = _nav.FirstOrDefault(n => n.Kind == NavKind.Category && n.Category == saved.EffectiveCategory) ?? _nav[0];
                RebuildNav();
            }
            Search.Text = Search.Text.Length > 0 && !EntryMatcher.MatchesSearch(saved, Search.Text) ? "" : Search.Text;
            RefreshList(saved.Id);
            ShowDetails(Vault.Find(saved.Id)!);
            App.Instance.Main.ShowToast(Loc.T(isNew ? "Editor.Created" : "Editor.Saved"));
            var fill = fillAfterSave != null && editor.FillAfterSave;
            if (!fill) await OfferPasswordSyncAsync(saved, oldPassword);
            if (fill) await fillAfterSave!(Vault.Find(saved.Id)!);
        };
        editor.Cancelled += () =>
        {
            if (Selected is { } item) ShowDetails(item.Entry);
            else ShowEmptyDetails();
        };
        DetailHost.Content = editor;
        return editor;
    }

    /// <summary>
    /// The same password is often used by a site and a VPN client (a domain account): when it changes in one entry,
    /// offer to change it in the entries that still have the old one.
    /// </summary>
    private async Task OfferPasswordSyncAsync(VaultEntry saved, string oldPassword)
    {
        if (oldPassword.Length == 0 || saved.Password == oldPassword) return;
        var others = Vault.ActiveEntries.Where(e => e.Id != saved.Id && e.Password == oldPassword).ToList();
        if (others.Count == 0) return;
        var names = string.Join(", ", others.Take(4).Select(e => "«" + e.Title + "»")) + (others.Count > 4 ? ", …" : "");
        if (!await ConfirmDialog.AskAsync(Loc.T("Vault.SyncTitle"), Loc.F("Vault.SyncText", names), Loc.T("Vault.SyncButton"))) return;
        foreach (var other in others)
        {
            var copy = other.Clone();
            copy.SetPassword(saved.Password);
            Vault.Upsert(copy);
        }
        App.Instance.Main.ShowToast(Loc.F("Vault.Synced", Loc.Plural(others.Count, "Plural.Entries")));
    }

    private async void DuplicateEntry(VaultEntry entry)
    {
        if (!await ConfirmLeaveEditorAsync()) return;
        OpenEditor(entry.CreateDuplicate(Loc.F("Vault.CopyOf", entry.Title)), isNew: true);
    }

    /// <summary>A copy of a site's login and password for a VPN client or program: the client is chosen first.</summary>
    private async void UseForClient(VaultEntry source)
    {
        if (!await ConfirmLeaveEditorAsync()) return;
        if (await App.Instance.Main.ShowDialogAsync(new ClientPickerDialog()) is not DetectedWindow client) return;
        var entry = new VaultEntry
        {
            Category = client.Category,
            Folder = source.Folder,
            AutoLogin = client.Category == EntryCategory.Remote,
            Notes = Loc.F("Vault.CopiedFromNote", source.Title),
        };
        var editor = OpenEditor(entry, isNew: true);
        editor.ApplyClient(client);
        editor.CopyCredentials(source);
        App.Instance.Main.ShowToast(Loc.F("Editor.TakenFrom", source.Title));
    }

    /// <summary>The other direction: a VPN / program account (often a domain account) used for a website.</summary>
    private async void UseForSite(VaultEntry source)
    {
        if (!await ConfirmLeaveEditorAsync()) return;
        var entry = new VaultEntry { Category = EntryCategory.Web, Folder = source.Folder, Notes = Loc.F("Vault.CopiedFromNote", source.Title) };
        var editor = OpenEditor(entry, isNew: true);
        editor.CopyCredentials(source);
        editor.FocusAddress();
        App.Instance.Main.ShowToast(Loc.F("Editor.TakenFrom", source.Title));
    }

    /// <summary>
    /// The auto-type hotkey found a sign-in window that no entry covers: open a new entry for exactly that client,
    /// with its fields, and sign in once it is saved.
    /// </summary>
    public async void StartEntryForWindow(DetectedWindow window, SignInForm form, Func<VaultEntry, Task> fill)
    {
        if (!await ConfirmLeaveEditorAsync()) return;
        _suppressSelection = true;
        EntryList.SelectedItem = null;
        _suppressSelection = false;
        var entry = new VaultEntry { Category = window.Category, AutoLogin = window.Category == EntryCategory.Remote };
        var editor = OpenEditor(entry, isNew: true, fill);
        editor.ApplyClient(window);
        editor.ShowDetection(window, form);
    }

    /// <summary>
    /// A new site entry with the login and password of an entry that already exists (a domain account of a VPN, for
    /// example): that entry can be used for the site instead of keeping the account twice. Returns it when chosen.
    /// </summary>
    private async Task<VaultEntry?> UseSameAccountAsync(VaultEntry saved)
    {
        if (saved.EffectiveCategory != EntryCategory.Web || DomainUtil.GetHost(saved.Url) is not { } host) return null;
        if (Vault.ActiveEntries.FirstOrDefault(e => e.Id != saved.Id && EntryMatcher.SameAccount(e, saved)) is not { } same) return null;
        var dialog = new ConfirmDialog(Loc.T("Vault.SameAccountTitle"), Loc.F("Vault.SameAccountText", same.Title, host),
            Loc.F("Vault.SameAccountUse", same.Title), cancelText: Loc.T("Vault.SameAccountNew"));
        if (await App.Instance.Main.ShowDialogAsync(dialog) is not true) return null;
        var copy = same.Clone();
        if (!copy.AllUrls().Any(u => DomainUtil.GetHost(u) == host))
        {
            if (copy.Url.Length == 0 && copy.EffectiveCategory != EntryCategory.Remote) copy.Url = saved.Url;
            else copy.ExtraUrls.Add(saved.Url);
        }
        if (!copy.AutoFillHosts.Contains(host, StringComparer.OrdinalIgnoreCase)) copy.AutoFillHosts.Add(host);
        Vault.Upsert(copy);
        App.Instance.Main.ShowToast(Loc.F("Vault.SameAccountDone", copy.Title, host));
        return Vault.Find(copy.Id);
    }

    /// <summary>A sign-in done by hand on a site without an entry, to be saved: the user types the login and password.</summary>
    public async void StartEntryToSave(string host, string address)
    {
        if (!await ConfirmLeaveEditorAsync()) return;
        _suppressSelection = true;
        EntryList.SelectedItem = null;
        _suppressSelection = false;
        var editor = OpenEditor(new VaultEntry { Category = EntryCategory.Web, Title = host, Url = address }, isNew: true);
        editor.ForExistingAccount();
    }

    /// <summary>A new entry for a site that has none (from its sign-in page); the page's form is filled once it is saved.</summary>
    public async void StartEntryForSite(string host, string address, SignInForm form, bool twoSteps, Func<VaultEntry, Task> fill)
    {
        if (!await ConfirmLeaveEditorAsync()) return;
        _suppressSelection = true;
        EntryList.SelectedItem = null;
        _suppressSelection = false;
        var entry = new VaultEntry { Category = EntryCategory.Web, Title = host, Url = address };
        var editor = OpenEditor(entry, isNew: true, fill);
        editor.ShowSiteForm(host, form, twoSteps);
    }

    private async void FindClients_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmLeaveEditorAsync()) return;
        if (await App.Instance.Main.ShowDialogAsync(new ClientPickerDialog()) is not DetectedWindow client) return;
        var editor = OpenEditor(new VaultEntry { Category = client.Category, AutoLogin = client.Category == EntryCategory.Remote }, isNew: true);
        editor.ApplyClient(client);
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmLeaveEditorAsync()) return;
        _suppressSelection = true;
        EntryList.SelectedItem = null;
        _suppressSelection = false;
        var entry = new VaultEntry
        {
            Folder = _currentNav?.Kind == NavKind.Folder ? _currentNav.Folder : "",
            Favorite = _currentNav?.Kind == NavKind.Favorites,
            Category = _currentNav?.Kind == NavKind.Category ? _currentNav.Category : null,
        };
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
        else if (ctrl && e.Key == Key.A && !InTextInput() && !DetailHost.IsKeyboardFocusWithin && EntryList.Items.Count > 0)
        {
            SelectAllEntries();
            e.Handled = true;
        }
        else if (IsMultiple && e.Key == Key.Delete && !InTextInput())
        {
            DeleteSelected();
            e.Handled = true;
        }
        else if (IsMultiple && e.Key == Key.Escape && !InTextInput())
        {
            ClearSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Down && Search.IsKeyboardFocused && EntryList.Items.Count > 0)
        {
            EntryList.SelectedIndex = Math.Max(0, EntryList.SelectedIndex);
            (EntryList.ItemContainerGenerator.ContainerFromIndex(EntryList.SelectedIndex) as ListBoxItem)?.Focus();
            e.Handled = true;
        }
    }

    private static bool InTextInput() => Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase or PasswordBox;

    private void EntryList_KeyDown(object sender, KeyEventArgs e)
    {
        if (IsMultiple || Selected is not { } item) return;
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
        if (!IsMultiple && Selected is { } item && !item.Entry.IsDeleted && item.Entry.Password.Length > 0)
            App.Instance.CopySecret(item.Entry.Password, Loc.T("Toast.PasswordCopied"), item.Entry.Id);
    }

    private void EntryMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item)
        {
            EntryMenu.IsOpen = false;
            return;
        }
        MenuSelectAll.Visibility = MenuSelectAllSeparator.Visibility =
            EntryList.SelectedItems.Count < EntryList.Items.Count ? Visibility.Visible : Visibility.Collapsed;
        if (IsMultiple)
        {
            // Only what applies to all the selected entries at once.
            var items = SelectedItems();
            var trash = items.All(i => i.Entry.IsDeleted);
            foreach (var single in new FrameworkElement[] { MenuCopyUser, MenuCopyPassword, MenuOpenUrl, MenuSeparator, MenuEdit, MenuDuplicate, MenuUseForClient })
                single.Visibility = Visibility.Collapsed;
            MenuFavorite.Visibility = trash ? Visibility.Collapsed : Visibility.Visible;
            MenuFavorite.Header = Loc.T(items.Any(i => !i.Favorite) ? "Vault.Favorite" : "Vault.Unfavorite");
            MenuRestore.Visibility = trash ? Visibility.Visible : Visibility.Collapsed;
            MenuDelete.Header = Loc.F(trash ? "Bulk.DeleteForeverN" : "Bulk.DeleteN", items.Count);
            return;
        }
        MenuCopyUser.Visibility = MenuCopyPassword.Visibility = MenuOpenUrl.Visibility = MenuSeparator.Visibility = Visibility.Visible;
        var deleted = item.Entry.IsDeleted;
        MenuCopyUser.IsEnabled = Login(item.Entry).Length > 0;
        MenuCopyPassword.IsEnabled = item.Entry.Password.Length > 0;
        MenuOpenUrl.IsEnabled = DomainUtil.NormalizeUrlForOpen(item.Entry.Url) != null;
        MenuFavorite.Header = Loc.T(item.Entry.Favorite ? "Vault.Unfavorite" : "Vault.Favorite");
        MenuFavorite.Visibility = MenuEdit.Visibility = MenuDuplicate.Visibility = MenuUseForClient.Visibility = deleted ? Visibility.Collapsed : Visibility.Visible;
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
        if (IsMultiple) SetFavoriteSelected(SelectedItems().Any(i => !i.Favorite));
        else if (Selected is { } item) Vault.ToggleFavorite(item.Id);
    }

    private void MenuEdit_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } item) StartEdit(item.Entry);
    }

    private void MenuDuplicate_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } item) DuplicateEntry(item.Entry);
    }

    private void MenuUseForClient_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } item) UseForClient(item.Entry);
    }

    private void MenuRestore_Click(object sender, RoutedEventArgs e)
    {
        if (IsMultiple)
        {
            RestoreSelected();
            return;
        }
        if (Selected is not { } item) return;
        Vault.Restore(item.Id);
        App.Instance.Main.ShowToast(Loc.T("Vault.Restored"));
    }

    private void MenuDelete_Click(object sender, RoutedEventArgs e)
    {
        if (IsMultiple) DeleteSelected();
        else if (Selected is { } item) DeleteEntry(item.Entry);
    }

    private void MenuSelectAll_Click(object sender, RoutedEventArgs e) => SelectAllEntries();

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

    private void Splitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        App.Instance.Settings.SidebarWidth = Math.Round(SidebarColumn.ActualWidth);
        App.Instance.Settings.ListWidth = Math.Round(ListColumn.ActualWidth);
        App.Instance.Settings.Save();
        FitColumns();
    }

    private const double SidebarMax = 420, ListMax = 760;

    private void Columns_SizeChanged(object sender, SizeChangedEventArgs e) => FitColumns();

    /// <summary>
    /// The panes keep the widths the user gave them as long as the window allows; in a smaller window the list and
    /// then the sections pane give way (the details keep their minimum), and they grow back with the window. The
    /// borders cannot be dragged so far that a pane would be pushed out.
    /// </summary>
    private void FitColumns()
    {
        var total = Columns.ActualWidth;
        if (total <= 0) return;
        var room = total - DetailsColumn.MinWidth;
        var sidebar = Math.Clamp(App.Instance.Settings.SidebarWidth, SidebarColumn.MinWidth, SidebarMax);
        var list = Math.Clamp(App.Instance.Settings.ListWidth, ListColumn.MinWidth, ListMax);
        var over = sidebar + list - room;
        if (over > 0)
        {
            var cut = Math.Min(over, list - ListColumn.MinWidth);
            list -= cut;
            over -= cut;
            sidebar -= Math.Min(over, sidebar - SidebarColumn.MinWidth);
        }
        SidebarColumn.Width = new GridLength(sidebar);
        ListColumn.Width = new GridLength(list);
        SidebarColumn.MaxWidth = Math.Max(SidebarColumn.MinWidth, Math.Min(SidebarMax, room - list));
        ListColumn.MaxWidth = Math.Max(ListColumn.MinWidth, Math.Min(ListMax, room - sidebar));
    }

    private void ListHeader_SizeChanged(object sender, SizeChangedEventArgs e) => ArrangeListHeader();

    /// <summary>
    /// The sorting (or "empty trash") goes under the title when the title, its count and the sorting do not fit in
    /// one line, instead of squeezing the title away; a title too long even for a line of its own is shortened.
    /// </summary>
    private void ArrangeListHeader()
    {
        if (ListHeader.ActualWidth <= 0) return;
        var title = new System.Windows.Media.FormattedText(ListTitle.Text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new System.Windows.Media.Typeface(ListTitle.FontFamily, ListTitle.FontStyle, ListTitle.FontWeight, ListTitle.FontStretch), ListTitle.FontSize,
            System.Windows.Media.Brushes.Black, System.Windows.Media.VisualTreeHelper.GetDpi(ListTitle).PixelsPerDip).WidthIncludingTrailingWhitespace;
        var side = SortBox.Visibility == Visibility.Visible ? SortBox.Width
            : EmptyTrashButton.Visibility == Visibility.Visible ? Math.Max(EmptyTrashButton.ActualWidth, 110) : 0;
        // The "select all" box and the count badge with their margins (as measured; before the first layout, close to it).
        var box = SelectAllBox.Visibility != Visibility.Visible ? 0 : SelectAllBox.DesiredSize.Width > 0 ? SelectAllBox.DesiredSize.Width : 49;
        var badge = ((FrameworkElement)ListCount.Parent).DesiredSize.Width;
        var count = (badge > 0 ? badge : 30) + 10;
        var narrow = side > 0 && ListHeader.ActualWidth < box + title + count + side;
        foreach (var element in new FrameworkElement[] { SortBox, EmptyTrashButton })
        {
            // Under the title, not under the "select all" box.
            Grid.SetRow(element, narrow ? 1 : 0);
            Grid.SetColumn(element, narrow ? 1 : 2);
            Grid.SetColumnSpan(element, narrow ? 2 : 1);
            element.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
            element.Margin = new Thickness(0, narrow ? 8 : 0, 0, 0);
        }
    }

    private async void Help_Click(object sender, RoutedEventArgs e) => await HelpDialog.ShowAsync();

    /// <summary>User menu: lock, sign out, delete the user.</summary>
    private void User_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = UserButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
        MenuItem Item(string icon, string text, Action click)
        {
            var item = new MenuItem { Header = text, Icon = new TextBlock { Text = icon } };
            item.Click += (_, _) => click();
            return item;
        }
        menu.Items.Add(Item("\uE72E", Loc.T("Profile.Lock"), App.Instance.Lock));
        menu.Items.Add(Item("\uE7E8", Loc.T("Profile.SignOut"), async () => await ProfileActions.SignOutAsync()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("\uE74D", Loc.T("Profile.Delete"), async () => await ProfileActions.DeleteAsync()));
        menu.IsOpen = true;
    }
}
