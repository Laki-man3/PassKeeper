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
    private (IntPtr Window, Rect Bounds) _reported;

    public event Action<LoginField>? LoginFieldFocused;
    public event Action? FocusLeft;
    /// <summary>A browser process got focus for the first time (its page loads can then be watched).</summary>
    public event Action<int>? BrowserSeen;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, bool> _announced = new();

    public bool IsRunning => _handler != null;

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
            if (browser && _announced.TryAdd(info.ProcessId, true)) BrowserSeen?.Invoke(info.ProcessId);
            if (info.ControlType != ControlType.Edit && (info.ControlType == ControlType.Document || info.ControlType == ControlType.Pane) && browser)
            {
                // A page that puts the cursor into its login field by itself (autofocus) gets no focus event for that
                // field from the browser, only for the document: ask the browser a moment later where the cursor is.
                Left();
                ProbeBrowserFocus(sequence, skipReported: false);
                return;
            }
            if (info.ControlType != ControlType.Edit || !info.IsEnabled)
            {
                Left();
                return;
            }
            var kind = FieldClassifier.Classify(info.IsPassword, info.Name, info.AutomationId, info.HelpText);
            var bounds = info.BoundingRectangle;
            if (kind == null || bounds.IsEmpty || bounds.Width < 20)
            {
                Left();
                // The field of a page that is still loading has no name and no place yet: look again a moment later.
                if (browser && bounds.IsEmpty) ProbeBrowserFocus(sequence, skipReported: false);
                return;
            }
            var field = new LoginField { Element = element, Kind = kind.Value, Bounds = bounds, Window = Native.GetForegroundWindow() };
            // Report only if focus stays here for a moment (tabbing through a form produces a burst of events).
            Task.Delay(90).ContinueWith(_ =>
            {
                if (Interlocked.Read(ref _sequence) == sequence && _handler != null) Report(field);
            }, TaskScheduler.Default);
        }
        catch (ElementNotAvailableException) { }
        catch (System.Runtime.InteropServices.COMException) { }
        catch (InvalidOperationException) { }
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

    private void Left()
    {
        _reported = default;
        FocusLeft?.Invoke();
    }

    private void Report(LoginField field)
    {
        _reported = (field.Window, field.Bounds);
        LoginFieldFocused?.Invoke(field);
    }

    /// <summary>
    /// A page was loaded in the active browser window: look where the cursor is. A field already reported (the page
    /// only changed its title while the user types) is not reported again.
    /// </summary>
    public void ProbeNow()
    {
        if (_handler == null) return;
        ProbeBrowserFocus(Interlocked.Increment(ref _sequence), skipReported: true);
    }

    private void ProbeBrowserFocus(long sequence, bool skipReported)
    {
        Task.Run(async () =>
        {
            foreach (var delay in new[] { 400, 600, 1000, 1500, 2500 })
            {
                await Task.Delay(delay);
                if (Interlocked.Read(ref _sequence) != sequence || _handler == null) return;
                try
                {
                    var focused = AutomationElement.FocusedElement?.GetUpdatedCache(Properties);
                    if (focused == null) continue;
                    var info = focused.Cached;
                    if (info.ControlType != ControlType.Edit || !info.IsEnabled || info.ProcessId == _ownPid) continue;
                    var kind = FieldClassifier.Classify(info.IsPassword, info.Name, info.AutomationId, info.HelpText);
                    var bounds = info.BoundingRectangle;
                    // A page still being built shows its field without a name or place at first: ask again later.
                    if (kind == null || bounds.IsEmpty || bounds.Width < 20) continue;
                    if (Interlocked.Read(ref _sequence) != sequence) return;
                    var window = Native.GetForegroundWindow();
                    if (skipReported && _reported == (window, bounds)) return;
                    Report(new LoginField { Element = focused, Kind = kind.Value, Bounds = bounds, Window = window });
                    return;
                }
                catch (ElementNotAvailableException) { }
                catch (System.Runtime.InteropServices.COMException) { }
                catch (InvalidOperationException) { }
            }
        });
    }

    public void Dispose() => Stop();
}
