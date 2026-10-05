namespace PassKeeper.Services;

/// <summary>
/// Optional record of what autofill saw and decided (Settings → Autofill → Autofill log), for finding out why a form
/// was or was not filled. Only site names, program names, field kinds and entry titles are written — never logins,
/// passwords or full addresses. The file stays small: past 512 KB it starts over (the previous part is kept as .old).
/// </summary>
public static class AutoFillLog
{
    private static readonly object Sync = new();
    private const long MaxSize = 512 * 1024;

    public static bool Enabled { get; set; }

    public static string FilePath => Path.Combine(App.Instance.DataDirectory, "autofill.log");

    public static void Write(string text)
    {
        if (!Enabled) return;
        try
        {
            lock (Sync)
            {
                var path = FilePath;
                if (File.Exists(path) && new FileInfo(path).Length > MaxSize) File.Move(path, path + ".old", overwrite: true);
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {text}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The site (host name) or program a target is, as written to the log.</summary>
    public static string Describe(TargetWindow target) =>
        target.IsBrowser ? Core.Matching.DomainUtil.GetHost(target.Url) ?? "(address not read) " + target.ProcessName : target.ProcessName;

    public static string Describe(IEnumerable<Core.Matching.EntryMatch> matches)
    {
        var list = matches.Select(m => $"\"{m.Entry.Title}\"={m.Score}").ToList();
        return list.Count == 0 ? "no entries" : string.Join(", ", list);
    }
}
