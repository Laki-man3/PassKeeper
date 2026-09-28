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
    /// Describes a focused UI Automation element as an autofill target, or null when it is not a sign-in field.
    /// Besides the accessible name, a text box that has no label but is followed by a password box — typical for
    /// VPN and remote-desktop dialogs — is taken as the login field.
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
public sealed class FocusWatcher : IDisposable
{
    private readonly int _ownPid = Environment.ProcessId;
    private AutomationFocusChangedEventHandler? _handler;
    private int _busy;

    public event Action<LoginField>? LoginFieldFocused;
    public event Action? FocusLeft;

    public bool IsRunning => _handler != null;

    public void Start()
    {
        if (_handler != null) return;
        _handler = OnFocusChanged;
        var handler = _handler;
        Task.Run(() =>
        {
            try { Automation.AddAutomationFocusChangedEventHandler(handler); }
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
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            if (element.Current.ProcessId == _ownPid) return;
            var field = LoginField.From(element, Native.GetForegroundWindow());
            if (field == null) FocusLeft?.Invoke();
            else LoginFieldFocused?.Invoke(field);
        }
        catch (ElementNotAvailableException) { }
        catch (System.Runtime.InteropServices.COMException) { }
        catch (InvalidOperationException) { }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    public void Dispose() => Stop();
}
