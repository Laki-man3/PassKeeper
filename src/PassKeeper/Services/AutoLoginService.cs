using System.Runtime.InteropServices;
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
public sealed class AutoLoginService : IDisposable
{
    public const int MaxAttempts = 2;
    public static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FormWait = TimeSpan.FromSeconds(6);

    private delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventDelegate callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);

    private const uint EventSystemForeground = 0x0003;
    private const uint WinEventOutOfContext = 0x0000;
    private const uint WinEventSkipOwnProcess = 0x0002;
    private const uint GaRoot = 2;

    private readonly App _app;
    private readonly Dictionary<IntPtr, DateTime> _done = [];
    private readonly Dictionary<IntPtr, DateTime> _checked = [];
    private readonly HashSet<IntPtr> _unlockAsked = [];
    private readonly Dictionary<Guid, List<DateTime>> _attempts = [];
    private WinEventDelegate? _callback;
    private IntPtr _hook;
    private IntPtr _busyWindow;

    public AutoLoginService(App app)
    {
        _app = app;
        // A window filled by the hotkey, a suggestion or after saving an entry is not signed in to again.
        app.AutoType.WindowFilled += hwnd =>
        {
            var root = Native.GetAncestor(hwnd, GaRoot);
            _done[root != IntPtr.Zero ? root : hwnd] = DateTime.UtcNow;
        };
    }

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _callback = OnForeground;
        _hook = SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, _callback, 0, 0, WinEventOutOfContext | WinEventSkipOwnProcess);
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
        _callback = null;
    }

    public void Dispose() => Stop();

    private void OnForeground(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || hwnd == IntPtr.Zero) return;
        _app.Dispatcher.BeginInvoke(async () => await TryWindowAsync(hwnd, null));
    }

    /// <summary>
    /// Signs in to the window if an auto sign-in entry covers it. Returns true when the window is being or was
    /// handled here (the autofill suggestion is then not needed).
    /// </summary>
    public async Task<bool> TryWindowAsync(IntPtr hwnd, LoginField? field)
    {
        if (!_app.Settings.AutoLogin || _app.IsExiting || !_app.Vault.Exists) return false;
        var root = Native.GetAncestor(hwnd, GaRoot);
        if (root != IntPtr.Zero) hwnd = root;
        if (_busyWindow == hwnd || _app.AutoType.IsTyping) return true;
        if (_busyWindow != IntPtr.Zero) return false;
        Expire();
        if (_done.ContainsKey(hwnd)) return false;
        if (field == null && _checked.ContainsKey(hwnd)) return false;

        var target = TargetDetector.Capture(hwnd, detectUrl: false);
        if (target.IsBrowser || target.ProcessName.Length == 0) return false;

        _busyWindow = hwnd;
        try
        {
            if (!_app.Vault.IsUnlocked)
            {
                // Entries are encrypted while locked: the trigger list tells which clients have auto sign-in.
                if (!IsTrigger(target) || !_unlockAsked.Add(hwnd)) return false;
                if (!await QuickUnlockWindow.ShowAsync(target.Describe())) return true;
            }
            var entry = Pick(target);
            if (entry == null) return false;
            if (!AllowAttempt(entry))
            {
                _done[hwnd] = DateTime.UtcNow;
                _app.Tray?.ShowBalloon("PassKeeper", Loc.F("AutoLogin.Paused", entry.Title, MaxAttempts));
                return false;
            }
            var form = field ?? await WaitForFormAsync(hwnd);
            if (form == null)
            {
                _checked[hwnd] = DateTime.UtcNow;
                return false;
            }
            if (_app.AutoType.IsTyping || _done.ContainsKey(hwnd)) return true;
            _done[hwnd] = DateTime.UtcNow;
            RecordAttempt(entry);
            Native.ForceForeground(hwnd);
            await Task.Run(() => FieldFinder.Focus(form.Element));
            await _app.AutoType.FillFieldAsync(entry, form, submitAfter: true);
            return true;
        }
        finally
        {
            _busyWindow = IntPtr.Zero;
        }
    }

    /// <summary>The auto sign-in entry for the window; several accounts of one client are told apart by the server in the title.</summary>
    private VaultEntry? Pick(TargetWindow target)
    {
        var matches = EntryMatcher.Match(_app.Vault.ActiveEntries.Where(e => e.AutoLogin), target.ToContext())
            .Where(m => m.Score >= 95)
            .ToList();
        if (matches.Count == 1) return matches[0].Entry;
        var byServer = matches
            .Where(m => DomainUtil.GetHost(m.Entry.Url) is { } host && target.Title.Contains(host, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return byServer.Count == 1 ? byServer[0].Entry : null;
    }

    private async Task<LoginField?> WaitForFormAsync(IntPtr hwnd)
    {
        var deadline = DateTime.UtcNow + FormWait;
        while (DateTime.UtcNow < deadline && Native.IsWindow(hwnd))
        {
            var field = await Task.Run(() => FieldFinder.FirstSignInField(hwnd));
            if (field != null) return field;
            await Task.Delay(400);
        }
        return null;
    }

    private bool AllowAttempt(VaultEntry entry) =>
        !_attempts.TryGetValue(entry.Id, out var times) || times.Count(t => DateTime.UtcNow - t < AttemptWindow) < MaxAttempts;

    private void RecordAttempt(VaultEntry entry)
    {
        if (!_attempts.TryGetValue(entry.Id, out var times)) _attempts[entry.Id] = times = [];
        times.RemoveAll(t => DateTime.UtcNow - t >= AttemptWindow);
        times.Add(DateTime.UtcNow);
    }

    private void Expire()
    {
        var now = DateTime.UtcNow;
        foreach (var hwnd in _done.Where(p => now - p.Value > TimeSpan.FromMinutes(30) || !Native.IsWindow(p.Key)).Select(p => p.Key).ToList()) _done.Remove(hwnd);
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
        foreach (var e in _app.Vault.ActiveEntries.Where(e => e.AutoLogin))
        {
            if (KnownApps.ForPatterns(e.WindowPatterns) is { } app) triggers.Add(app.Id);
            foreach (var p in e.WindowPatterns)
                if (p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !p.Contains('*')) triggers.Add(p.Trim().ToLowerInvariant());
        }
        if (triggers.SequenceEqual(_app.Settings.AutoLoginTriggers)) return;
        _app.Settings.AutoLoginTriggers = [.. triggers];
        _app.Settings.Save();
    }
}
