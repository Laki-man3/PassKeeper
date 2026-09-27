using PassKeeper.Core.Models;
using PassKeeper.Core.Storage;

namespace PassKeeper.Core.Interop;

public enum ImportStatus { New, Duplicate, Conflict }

public enum ConflictAction { UpdatePassword, AddAsNew, Skip }

public sealed class ImportCandidate
{
    public required VaultEntry Entry { get; init; }
    public required string Source { get; init; }
    public ImportStatus Status { get; set; }
    public VaultEntry? Existing { get; set; }
    public bool Selected { get; set; } = true;
}

public static class ImportPlanner
{
    public static List<ImportCandidate> Plan(IEnumerable<VaultEntry> existing, IEnumerable<ImportResult> results)
    {
        var byKey = new Dictionary<string, List<VaultEntry>>();
        foreach (var e in existing.Where(e => !e.IsDeleted))
        {
            var key = ImportHelpers.LoginKey(e);
            if (!byKey.TryGetValue(key, out var list)) byKey[key] = list = [];
            list.Add(e);
        }

        var seenIncoming = new HashSet<string>();
        var plan = new List<ImportCandidate>();
        foreach (var result in results)
        foreach (var e in result.Entries)
        {
            var key = ImportHelpers.LoginKey(e);
            var candidate = new ImportCandidate { Entry = e, Source = result.Source };
            if (!seenIncoming.Add(key + "\n" + e.Password))
            {
                candidate.Status = ImportStatus.Duplicate;
                candidate.Selected = false;
            }
            else if (byKey.TryGetValue(key, out var matches))
            {
                var same = matches.FirstOrDefault(m => m.Password == e.Password);
                if (same != null)
                {
                    candidate.Status = ImportStatus.Duplicate;
                    candidate.Existing = same;
                    candidate.Selected = false;
                }
                else
                {
                    candidate.Status = ImportStatus.Conflict;
                    candidate.Existing = matches[0];
                }
            }
            plan.Add(candidate);
        }
        return plan;
    }

    public static (int Added, int Updated) Apply(VaultService vault, IEnumerable<ImportCandidate> plan, ConflictAction conflictAction,
        string? targetFolder)
    {
        int added = 0, updated = 0;
        targetFolder = targetFolder?.Trim().Trim('/') ?? "";
        foreach (var c in plan.Where(c => c.Selected))
        {
            if (c.Status == ImportStatus.Conflict && c.Existing != null && conflictAction != ConflictAction.AddAsNew)
            {
                if (conflictAction == ConflictAction.Skip) continue;
                c.Existing.SetPassword(c.Entry.Password);
                c.Existing.ModifiedUtc = DateTime.UtcNow;
                updated++;
                continue;
            }
            if (c.Status == ImportStatus.Duplicate && c.Existing != null) continue;

            var e = c.Entry;
            e.Id = Guid.NewGuid();
            e.DeletedUtc = null;
            if (targetFolder.Length > 0) e.Folder = e.Folder.Length > 0 ? targetFolder + "/" + e.Folder : targetFolder;
            vault.Data.Entries.Add(e);
            added++;
        }
        if (added + updated > 0) vault.Save();
        return (added, updated);
    }
}
