using System.Text.RegularExpressions;
using PassKeeper.Core.Models;

namespace PassKeeper.Core.Matching;

/// <summary>What is currently in front of the user: a browser tab or a desktop application window.</summary>
public sealed class TargetContext
{
    public string WindowTitle { get; init; } = "";
    public string ProcessName { get; init; } = "";
    public string? Url { get; init; }
    public bool IsBrowser { get; init; }
}

public sealed record EntryMatch(VaultEntry Entry, int Score);

public static class EntryMatcher
{
    public static List<EntryMatch> Match(IEnumerable<VaultEntry> entries, TargetContext target)
    {
        var pageHost = DomainUtil.GetHost(target.Url);
        var title = target.WindowTitle ?? "";
        var result = new List<EntryMatch>();

        foreach (var e in entries)
        {
            if (e.IsDeleted) continue;
            var score = 0;

            foreach (var pattern in e.WindowPatterns)
            {
                if (string.IsNullOrWhiteSpace(pattern)) continue;
                if (WildcardMatch(title, pattern) || WildcardMatch(target.ProcessName, pattern) ||
                    WildcardMatch(target.ProcessName + ".exe", pattern))
                    score = Math.Max(score, 95);
            }

            foreach (var url in e.AllUrls())
            {
                var entryHost = DomainUtil.GetHost(url);
                if (entryHost == null) continue;
                if (pageHost != null)
                    score = Math.Max(score, DomainUtil.CompareHosts(pageHost, entryHost));
                // Browser URL unknown (or a desktop client): fall back to the window title.
                if (pageHost == null && entryHost.Length >= 4 &&
                    title.Contains(entryHost, StringComparison.OrdinalIgnoreCase))
                    score = Math.Max(score, 60);
            }

            if (score == 0 && !target.IsBrowser && e.Title.Trim().Length >= 3 &&
                title.Contains(e.Title.Trim(), StringComparison.OrdinalIgnoreCase))
                score = 50;
            if (score == 0 && target.IsBrowser && pageHost == null && e.Title.Trim().Length >= 3 &&
                title.Contains(e.Title.Trim(), StringComparison.OrdinalIgnoreCase))
                score = 40;

            if (score > 0) result.Add(new EntryMatch(e, score));
        }

        return result
            .OrderByDescending(m => m.Score)
            .ThenByDescending(m => m.Entry.LastUsedUtc ?? DateTime.MinValue)
            .ThenBy(m => m.Entry.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Case-insensitive wildcard match supporting '*' and '?'. Plain text matches as a substring.</summary>
    public static bool WildcardMatch(string? input, string pattern)
    {
        if (string.IsNullOrEmpty(input)) return false;
        pattern = pattern.Trim();
        if (pattern.Length == 0) return false;
        if (!pattern.Contains('*') && !pattern.Contains('?'))
            return input.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(input, regex, RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromMilliseconds(200));
    }

    /// <summary>Full-text filter used by the search box.</summary>
    public static bool MatchesSearch(VaultEntry e, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        foreach (var term in query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var hit = Contains(e.Title, term) || Contains(e.Username, term) || Contains(e.Url, term) ||
                      Contains(e.Email, term) || Contains(e.Phone, term) || Contains(e.Notes, term) ||
                      Contains(e.Folder, term) || e.Tags.Any(t => Contains(t, term)) ||
                      e.ExtraUrls.Any(u => Contains(u, term)) ||
                      e.CustomFields.Any(f => Contains(f.Name, term) || (!f.Protected && Contains(f.Value, term)));
            if (!hit) return false;
        }
        return true;

        static bool Contains(string? s, string t) => s != null && s.Contains(t, StringComparison.CurrentCultureIgnoreCase);
    }
}
