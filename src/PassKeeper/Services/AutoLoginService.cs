using PassKeeper.Core.AutoType;
using PassKeeper.Core.Matching;
using PassKeeper.Core.Models;
using PassKeeper.Localization;
using PassKeeper.Windows;

namespace PassKeeper.Services;

/// <summary>
/// Automatic sign-in: when a window of a client with an "auto sign-in" entry comes to the foreground and shows its
/// login form, the fields are filled and Enter is pressed. Each window is handled once, and an entry is tried at most
/// <see cref="MaxAttempts"/> times in <see cref="AttemptWindow"/>, so a wrong password cannot lock the account.
/// </summary>
public sealed class AutoLoginService
{
    public const int MaxAttempts = 2;
    public static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Pauses between looks for a window's sign-in form: often at first, then every few seconds for a client that
    /// takes its time to load (about 50 s in all), and only while the window stays in front.
    /// </summary>
    private static readonly int[] FormLookPauses =
        [0, 400, 400, 400, 400, 600, 800, 1000, 1000, 1500, 1500, 2000, 2000, 2000, 3000, 3000, 3000, 3000, 3000, 3000, 3000, 3000, 3000, 3000, 3000, 3000];

    private const uint GaRoot = 2;

    private readonly App _app;
    private readonly Dictionary<IntPtr, DateTime> _done = [];
    private readonly Dictionary<IntPtr, DateTime> _checked = [];
    private readonly HashSet<IntPtr> _unlockAsked = [];
    private readonly SignInAttempts _attempts = new(MaxAttempts, AttemptWindow, TimeSpan.FromSeconds(30));
    private bool _enabled;
    private IntPtr _busyWindow;
    private IntPtr _turnedAway;
    private int _autoEntries;

    public AutoLoginService(App app, ForegroundWatcher foreground)
    {
        _app = app;
        foreground.Changed += hwnd =>
        {
            if (_enabled) _ = TryWindowAsync(hwnd, null);
        };
        // A window filled by the hotkey, a suggestion or after saving an entry is not signed in to again.
        app.AutoType.WindowFilled += hwnd =>
        {
            var root = Native.GetAncestor(hwnd, GaRoot);
            _done[root != IntPtr.Zero ? root : hwnd] = DateTime.UtcNow;
        };
    }

    public void Start() => _enabled = true;

    public void Stop() => _enabled = false;

    /// <summary>
    /// Signs in to the window if an auto sign-in entry covers it. Returns true when the window is being or was
    /// handled here (the autofill suggestion is then not needed).
    /// </summary>
    public async Task<bool> TryWindowAsync(IntPtr hwnd, LoginField? field)
    {
        if (!_app.Settings.AutoLogin || _app.IsExiting || !_app.HasProfile) return false;
        var root = Native.GetAncestor(hwnd, GaRoot);
        if (root != IntPtr.Zero) hwnd = root;
        if (_busyWindow == hwnd || _app.AutoType.IsTyping) return true;
        if (_app.AutoType.IsBusy) return false; // the hotkey is being handled (an entry is being chosen)
        // Nothing to sign in to: return before looking at the window at all.
        if (_app.Vault.IsUnlocked ? _autoEntries == 0 : _app.Settings.AutoLoginTriggers.Count == 0) return false;
        Expire();
        if (_done.ContainsKey(hwnd)) return false;
        if (field == null && _checked.ContainsKey(hwnd)) return false;

        var target = TargetDetector.Capture(hwnd, detectUrl: false);
        if (target.IsBrowser || target.ProcessName.Length == 0) return false;
        if (_busyWindow != IntPtr.Zero)
        {
            // Another window is being looked at (the client's main window, while this is its sign-in window): this
            // one is taken up as soon as that look ends, and is not filled meanwhile by the suggestion either.
            if (!(_app.Vault.IsUnlocked ? Pick(target) != null : IsTrigger(target))) return false;
            if (_turnedAway != hwnd) AutoFillLog.Write($"sign-in window of {target.ProcessName}: taken up once the window before it is done");
            _turnedAway = hwnd;
            return true;
        }

        _busyWindow = hwnd;
        try
        {
            if (!_app.Vault.IsUnlocked)
            {
                // Entries are encrypted while locked: the trigger list tells which clients have auto sign-in.
                if (!IsTrigger(target) || !_unlockAsked.Add(hwnd)) return false;
                if (!await QuickUnlockWindow.ShowAsync(target.Describe())) return true;
                // The PIN was asked for this window: it comes back to the front to be signed in to.
                Native.ForceForeground(hwnd);
            }
            var entry = Pick(target);
            if (entry == null) return false;
            AutoFillLog.Write($"sign-in window of {target.ProcessName}: \"{entry.Title}\"" + (field != null ? ", the cursor is in its form" : ", looking for its form"));
            var form = field;
            if (form == null)
            {
                var (found, left) = await WaitForFormAsync(hwnd);
                if (found == null)
                {
                    // A look cut short (another window came to the front) starts again when this window is back.
                    if (!left) _checked[hwnd] = DateTime.UtcNow;
                    return false;
                }
                form = found;
            }
            if (_app.AutoType.IsTyping || _done.ContainsKey(hwnd)) return true;
            _done[hwnd] = DateTime.UtcNow;
            if (!_attempts.Allowed(entry.Id, DateTime.UtcNow))
            {
                _attempts.AskedAgain(entry.Id, DateTime.UtcNow);
                _app.Tray?.ShowBalloon("PassKeeper", Loc.F("AutoLogin.Paused", entry.Title, MaxAttempts));
                return false;
            }
            Native.ForceForeground(hwnd);
            await Task.Run(() => FieldFinder.Focus(form.Element));
            var result = await _app.AutoType.FillFieldAsync(entry, form, submitAfter: true);
            if (result.Entered) _ = WatchSignInAsync(_attempts.Record(entry.Id, DateTime.UtcNow), hwnd, entry.Title);
            return true;
        }
        finally
        {
            _busyWindow = IntPtr.Zero;
            var next = _turnedAway;
            _turnedAway = IntPtr.Zero;
            if (next != IntPtr.Zero && next != hwnd && _enabled) _ = TryWindowAsync(next, null);
        }
    }

    /// <summary>The auto sign-in entry for the window; several accounts of one client are told apart by the server in the title.</summary>
    private VaultEntry? Pick(TargetWindow target)
    {
        var matches = EntryMatcher.Match(_app.Vault.ActiveEntries.Where(e => e.SignsInToProgram), target.ToContext())
            .Where(m => m.Score >= 95)
            .ToList();
        if (matches.Count == 1) return matches[0].Entry;
        var byServer = matches
            .Where(m => DomainUtil.GetHost(m.Entry.Url) is { } host && target.Title.Contains(host, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return byServer.Count == 1 ? byServer[0].Entry : null;
    }

    /// <summary>
    /// Looks for the window's sign-in form until it appears (a client may show its window long before the fields).
    /// <c>Left</c>: the look was cut short because the window closed, went to the back, or another client window
    /// is waiting.
    /// </summary>
    private async Task<(LoginField? Form, bool Left)> WaitForFormAsync(IntPtr hwnd)
    {
        var started = Environment.TickCount64;
        foreach (var pause in FormLookPauses)
        {
            if (pause > 0) await Task.Delay(pause);
            var why = _app.IsExiting || !Native.IsWindow(hwnd) || !Native.IsWindowVisible(hwnd) ? "the window closed or was hidden"
                : _turnedAway != IntPtr.Zero ? "another sign-in window is waiting"
                : !InFront(hwnd) ? "another window came to the front"
                : null;
            if (why != null)
            {
                AutoFillLog.Write($"  stopped looking after {Environment.TickCount64 - started} ms: {why}");
                return (null, true);
            }
            var field = await Task.Run(() => FieldFinder.FirstSignInField(hwnd));
            if (field == null) continue;
            AutoFillLog.Write($"  sign-in form found after {Environment.TickCount64 - started} ms");
            return (field, false);
        }
        AutoFillLog.Write("  no sign-in form within 50 s");
        return (null, false);
    }

    /// <summary>The window is in front, or PassKeeper's own window is (the PIN prompt for it).</summary>
    private static bool InFront(IntPtr hwnd)
    {
        var front = Native.GetForegroundWindow();
        var root = Native.GetAncestor(front, GaRoot);
        if ((root != IntPtr.Zero ? root : front) == hwnd) return true;
        Native.GetWindowThreadProcessId(front, out var pid);
        return pid == (uint)Environment.ProcessId;
    }

    /// <summary>
    /// After the client was sent its password: the sign-in window closed or hidden, and staying so for a while, means
    /// the sign-in went through and the attempt does not count. Only the window's state is checked (nothing is read
    /// from the client). A client takes longer than a site to refuse, and may hide the window meanwhile: if it asks
    /// again soon after, the attempt counts after all (see SignInAttempts).
    /// </summary>
    private static async Task WatchSignInAsync(SignInAttempt attempt, IntPtr hwnd, string title)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(1);
        DateTime? goneSince = null;
        while (DateTime.UtcNow < deadline && !attempt.Refused)
        {
            await Task.Delay(2000);
            var gone = !Native.IsWindow(hwnd) || !Native.IsWindowVisible(hwnd);
            if (!gone)
            {
                goneSince = null;
                continue;
            }
            goneSince ??= DateTime.UtcNow;
            if (DateTime.UtcNow - goneSince < TimeSpan.FromSeconds(15)) continue;
            if (attempt.MarkSucceeded(DateTime.UtcNow)) AutoFillLog.Write($"signed in to \"{title}\": the sign-in window is gone, the attempt does not count");
            return;
        }
    }

    private void Expire()
    {
        var now = DateTime.UtcNow;
        // A sign-in window that was closed or hidden counts as new when it comes back (clients reuse their dialogs).
        foreach (var hwnd in _done.Where(p => now - p.Value > TimeSpan.FromMinutes(30) || !Native.IsWindowVisible(p.Key)).Select(p => p.Key).ToList()) _done.Remove(hwnd);
        foreach (var hwnd in _checked.Where(p => now - p.Value > TimeSpan.FromSeconds(20)).Select(p => p.Key).ToList()) _checked.Remove(hwnd);
        _unlockAsked.RemoveWhere(h => !Native.IsWindow(h));
    }

    private bool IsTrigger(TargetWindow target)
    {
        var triggers = _app.Settings.AutoLoginTriggers;
        if (triggers.Count == 0) return false;
        if (KnownApps.Match(target.ProcessName, target.Title) is { } app && triggers.Contains(app.Id)) return true;
        return triggers.Contains((target.ProcessName + ".exe").ToLowerInvariant());
    }

    /// <summary>
    /// Keeps the list of clients with auto sign-in (known client ids and executable names — no secrets) so that a
    /// locked PassKeeper still knows when to ask for the PIN.
    /// </summary>
    public void UpdateTriggers()
    {
        if (!_app.Vault.IsUnlocked) return;
        var triggers = new SortedSet<string>(StringComparer.Ordinal);
        _autoEntries = 0;
        // Site entries sign in through autofill (AutoTypeService), not by watching windows.
        foreach (var e in _app.Vault.ActiveEntries.Where(e => e.SignsInToProgram))
        {
            _autoEntries++;
            if (KnownApps.ForPatterns(e.WindowPatterns) is { } app) triggers.Add(app.Id);
            foreach (var p in e.WindowPatterns)
                if (p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !p.Contains('*')) triggers.Add(p.Trim().ToLowerInvariant());
        }
        if (triggers.SequenceEqual(_app.Settings.AutoLoginTriggers)) return;
        _app.Settings.AutoLoginTriggers = [.. triggers];
        _app.Settings.Save();
    }
}
