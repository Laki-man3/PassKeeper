using System.Windows;
using System.Windows.Automation;
using PassKeeper.Core.AutoType;

namespace PassKeeper.Services;

public sealed class LoginField
{
    public required AutomationElement Element { get; init; }
    public required FieldKind Kind { get; init; }
    public required Rect Bounds { get; init; }
    public required IntPtr Window { get; init; }
    public bool IsPassword => Kind == FieldKind.Password;

    /// <summary>
    /// Describes a UI Automation element as an autofill target, or null when it is not a sign-in field. Besides the
    /// accessible name, a text box that has no label but is followed by a password box — typical for VPN and
    /// remote-desktop dialogs — is taken as the login field (this searches the window, so it is used on explicit
    /// requests only, not for every focus change).
    /// </summary>
    public static LoginField? From(AutomationElement element, IntPtr window)
    {
        var info = element.Current;
        if (info.ControlType != ControlType.Edit || !info.IsEnabled) return null;
        var kind = FieldClassifier.Classify(info.IsPassword, info.Name, info.AutomationId, info.HelpText);
        if (kind == null && !info.IsPassword && string.IsNullOrWhiteSpace(info.Name) && IsSignInWindow(window) &&
            FieldFinder.FindPasswordAfter(element) != null)
            kind = FieldKind.Login;
        if (kind == null) return null;
        var bounds = info.BoundingRectangle;
        if (bounds.IsEmpty || bounds.Width < 20) return null;
        return new LoginField { Element = element, Kind = kind.Value, Bounds = bounds, Window = window };
    }

    /// <summary>A dialog-sized window or a known sign-in client (the extra UI Automation search is cheap there).</summary>
    private static bool IsSignInWindow(IntPtr hwnd)
    {
        if (Native.GetWindowClass(hwnd) == "#32770") return true;
        if (Native.GetWindowRect(hwnd, out var r) && r.Right - r.Left < 1100 && r.Bottom - r.Top < 900) return true;
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            return KnownApps.Match(p.ProcessName, Native.GetWindowTitle(hwnd)) != null;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }
}

/// <summary>A browser page in front of the user without the cursor in any of its fields.</summary>
public sealed record BrowserPage(AutomationElement Document, IntPtr Window, long Sequence);

/// <summary>
/// Watches keyboard focus in all applications (UI Automation) and reports when a login, password, e-mail, phone,
/// one-time-code, token PIN or key field receives focus — the trigger for autofill suggestions in browsers and desktop apps.
/// </summary>
/// <remarks>
/// Kept deliberately light, since it runs for every focus change in the session: the properties it needs arrive
/// with the event in one batch (cache request), nothing else of the other application is queried, and a burst of
/// focus changes is reported once. Deeper inspection of a window happens only on an explicit request (hotkey,
/// suggestion click, automatic sign-in).
/// </remarks>
public sealed class FocusWatcher : IDisposable
{
    private static readonly CacheRequest Properties = CreateCacheRequest();
    private readonly int _ownPid = Environment.ProcessId;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, bool> _browsers = new();
    private AutomationFocusChangedEventHandler? _handler;
    private long _sequence;

    public event Action<LoginField>? LoginFieldFocused;
    public event Action? FocusLeft;
    /// <summary>
    /// A browser page came to the front (loaded, or the user switched to it) and the cursor is in none of its fields:
    /// reported once, a moment after the page got focus, so that its sign-in form can be looked for.
    /// </summary>
    public event Action<BrowserPage>? PageShown;

    /// <summary>Focus has not moved since the event with this sequence number.</summary>
    public bool IsCurrent(long sequence) => _handler != null && Interlocked.Read(ref _sequence) == sequence;

    private static CacheRequest CreateCacheRequest()
    {
        var request = new CacheRequest { TreeScope = TreeScope.Element, AutomationElementMode = AutomationElementMode.Full };
        request.Add(AutomationElement.ProcessIdProperty);
        request.Add(AutomationElement.ControlTypeProperty);
        request.Add(AutomationElement.IsEnabledProperty);
        request.Add(AutomationElement.IsPasswordProperty);
        request.Add(AutomationElement.NameProperty);
        request.Add(AutomationElement.AutomationIdProperty);
        request.Add(AutomationElement.HelpTextProperty);
        request.Add(AutomationElement.BoundingRectangleProperty);
        return request;
    }

    public void Start()
    {
        if (_handler != null) return;
        _handler = OnFocusChanged;
        var handler = _handler;
        Task.Run(() =>
        {
            try
            {
                using (Properties.Activate()) Automation.AddAutomationFocusChangedEventHandler(handler);
            }
            catch (Exception) { /* UIA unavailable (e.g. restricted session) */ }
        });
    }

    public void Stop()
    {
        var handler = _handler;
        _handler = null;
        if (handler == null) return;
        Task.Run(() =>
        {
            try { Automation.RemoveAutomationFocusChangedEventHandler(handler); }
            catch (Exception) { }
        });
    }

    private void OnFocusChanged(object sender, AutomationFocusChangedEventArgs e)
    {
        if (_handler == null || sender is not AutomationElement element) return;
        var sequence = Interlocked.Increment(ref _sequence);
        try
        {
            AutomationElement.AutomationElementInformation info;
            try { info = element.Cached; }
            catch (InvalidOperationException) { info = element.Current; } // cache not supported by the provider
            if (info.ProcessId == _ownPid) return;
            var browser = IsBrowser(info.ProcessId);
            if (browser && (info.ControlType == ControlType.Document || info.ControlType == ControlType.Pane || info.ControlType == ControlType.Window))
            {
                // A page that puts the cursor into its login field by itself (autofocus) gets no focus event for that
                // field from the browser, only for the document: ask the browser a moment later where the cursor is.
                FocusLeft?.Invoke();
                ProbeBrowserFocus(sequence);
                return;
            }
            if (info.ControlType != ControlType.Edit || !info.IsEnabled)
            {
                FocusLeft?.Invoke();
                return;
            }
            var kind = FieldClassifier.Classify(info.IsPassword, info.Name, info.AutomationId, info.HelpText);
            var bounds = info.BoundingRectangle;
            // VPN clients often leave the login box without a label: in a known client's window it is the login.
            if (kind == null && !browser && !info.IsPassword && string.IsNullOrWhiteSpace(info.Name) && IsKnownClient(info.ProcessId))
                kind = FieldKind.Login;
            if (kind == null || bounds.IsEmpty || bounds.Width < 20)
            {
                FocusLeft?.Invoke();
                // The field of a page that is still loading has no name and no place yet: look again a moment later.
                if (browser && bounds.IsEmpty) ProbeBrowserFocus(sequence);
                return;
            }
            // The field must belong to the window in front: a closing sign-in dialog may report its field after another
            // window has already come forward.
            var window = Native.GetForegroundWindow();
            Native.GetWindowThreadProcessId(window, out var windowPid);
            if (windowPid != info.ProcessId)
            {
                FocusLeft?.Invoke();
                return;
            }
            var field = new LoginField { Element = element, Kind = kind.Value, Bounds = bounds, Window = window };
            // Report only if focus stays here for a moment (tabbing through a form produces a burst of events).
            Task.Delay(90).ContinueWith(_ =>
            {
                if (IsCurrent(sequence)) LoginFieldFocused?.Invoke(field);
            }, TaskScheduler.Default);
        }
        catch (ElementNotAvailableException) { }
        catch (TimeoutException) { } // the program does not answer UI Automation in time
        catch (System.Runtime.InteropServices.COMException) { }
        catch (InvalidOperationException) { }
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, string> _processNames = new();

    /// <summary>The window in front belongs to a known sign-in client (VPN, remote access, token PIN prompt).</summary>
    private bool IsKnownClient(int pid)
    {
        var name = _processNames.GetOrAdd(pid, id =>
        {
            try
            {
                using var p = System.Diagnostics.Process.GetProcessById(id);
                return p.ProcessName;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return "";
            }
        });
        return name.Length > 0 && KnownApps.Match(name, Native.GetWindowTitle(Native.GetForegroundWindow())) != null;
    }

    private bool IsBrowser(int pid) => _browsers.GetOrAdd(pid, id =>
    {
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(id);
            return TargetDetector.IsBrowserProcess(p.ProcessName);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    });

    private static readonly int[] ProbeDelays = [400, 600, 1000, 1500, 2500];

    /// <summary>
    /// The active browser window changed its page (or came to the front): look where the cursor is. Needed for
    /// browsers that send no focus events from their pages.
    /// </summary>
    public void ProbePage()
    {
        if (_handler != null) ProbeBrowserFocus(Interlocked.Increment(ref _sequence));
    }

    private void ProbeBrowserFocus(long sequence)
    {
        Task.Run(async () =>
        {
            var pageReported = false;
            foreach (var delay in ProbeDelays)
            {
                await Task.Delay(delay);
                if (!IsCurrent(sequence)) return;
                try
                {
                    var focused = AutomationElement.FocusedElement?.GetUpdatedCache(Properties);
                    if (focused == null) continue;
                    var info = focused.Cached;
                    // A browser window that is just opening may not have taken the focus yet: look again later.
                    if (info.ProcessId == _ownPid || !IsBrowser(info.ProcessId)) continue;
                    if (info.ControlType == ControlType.Document || info.ControlType == ControlType.Window || info.ControlType == ControlType.Pane)
                    {
                        // The page shows no cursor in a field: once it has had a moment to appear, report the page.
                        // Some browsers keep the focus on the window: its page is then found below it.
                        if (!pageReported && delay >= 600)
                        {
                            var window = Native.GetForegroundWindow();
                            var page = info.ControlType == ControlType.Document ? focused : FieldFinder.PageOf(window);
                            if (page != null && IsCurrent(sequence))
                            {
                                pageReported = true;
                                PageShown?.Invoke(new BrowserPage(page, window, sequence));
                            }
                        }
                        continue;
                    }
                    if (info.ControlType != ControlType.Edit || !info.IsEnabled) continue;
                    var kind = FieldClassifier.Classify(info.IsPassword, info.Name, info.AutomationId, info.HelpText);
                    var bounds = info.BoundingRectangle;
                    // A page still being built shows its field without a name or place at first: ask again later.
                    if (kind == null || bounds.IsEmpty || bounds.Width < 20) continue;
                    if (!IsCurrent(sequence)) return;
                    var front = Native.GetForegroundWindow();
                    Native.GetWindowThreadProcessId(front, out var frontPid);
                    if (frontPid != info.ProcessId) return;
                    LoginFieldFocused?.Invoke(new LoginField { Element = focused, Kind = kind.Value, Bounds = bounds, Window = front });
                    return;
                }
                catch (ElementNotAvailableException) { }
                catch (TimeoutException) { } // the program does not answer UI Automation in time
                catch (System.Runtime.InteropServices.COMException) { }
                catch (InvalidOperationException) { }
            }
        });
    }

    public void Dispose() => Stop();
}
