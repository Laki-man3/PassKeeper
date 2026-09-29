using System.Text.Json.Serialization;
using PassKeeper.Core.AutoType;
using PassKeeper.Core.Matching;

namespace PassKeeper.Core.Models;

/// <summary>Section of the vault: sites, VPN / remote access clients, other desktop programs, everything else.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EntryCategory>))]
public enum EntryCategory { Web, Remote, App, Other }

public static class EntryCategories
{
    public static readonly EntryCategory[] All = [EntryCategory.Web, EntryCategory.Remote, EntryCategory.App, EntryCategory.Other];

    /// <summary>
    /// Section of an entry that has none set (entries of older versions, imports): a known client in the window
    /// patterns decides, then any window pattern (program), then a web address (site).
    /// </summary>
    public static EntryCategory Detect(VaultEntry e)
    {
        if (KnownApps.ForPatterns(e.WindowPatterns) is { } app) return app.Category;
        if (e.WindowPatterns.Any(p => !string.IsNullOrWhiteSpace(p))) return EntryCategory.App;
        if (e.AllUrls().Any(u => DomainUtil.GetHost(u) != null)) return EntryCategory.Web;
        return EntryCategory.Other;
    }
}
