using System.ComponentModel;
using System.Runtime.CompilerServices;
using PassKeeper.Core.Matching;
using PassKeeper.Core.Models;

namespace PassKeeper.ViewModels;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

/// <summary>Row of the entry list.</summary>
public sealed class EntryItem(VaultEntry entry) : Observable
{
    public VaultEntry Entry { get; private set; } = entry;
    public Guid Id => Entry.Id;
    public string Title => Entry.Title.Length > 0 ? Entry.Title : "—";
    public string AvatarSource => Entry.Title.Length > 0 ? Entry.Title : Entry.Url;
    public bool Favorite => Entry.Favorite;

    public string Subtitle
    {
        get
        {
            if (Entry.Username.Length > 0) return Entry.Username;
            if (Entry.Email.Length > 0) return Entry.Email;
            var host = DomainUtil.GetHost(Entry.Url);
            if (host != null) return host;
            return Entry.Notes.Length > 0 ? Entry.Notes.Split('\n')[0] : "";
        }
    }

    public void Refresh(VaultEntry entry)
    {
        Entry = entry;
        OnPropertyChanged(null);
    }
}

public enum NavKind { All, Favorites, Folder, Trash, Header }

/// <summary>Sidebar navigation row.</summary>
public sealed class NavItem : Observable
{
    private int _count;
    private string _label = "";

    public required NavKind Kind { get; init; }
    public string Folder { get; init; } = "";
    public string Icon { get; init; } = "";
    public bool IsHeader => Kind == NavKind.Header;
    public int Depth { get; init; }

    public string Label
    {
        get => _label;
        set => Set(ref _label, value);
    }

    public int Count
    {
        get => _count;
        set
        {
            if (Set(ref _count, value)) OnPropertyChanged(nameof(CountText));
        }
    }

    public string CountText => Count > 0 ? Count.ToString() : "";

    public bool SameAs(NavItem? other) => other != null && other.Kind == Kind && other.Folder == Folder;
}
