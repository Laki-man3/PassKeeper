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
}

/// <summary>
/// Watches keyboard focus in all applications (UI Automation) and reports when a login, password, e-mail, phone,
/// one-time-code or key field receives focus — the trigger for autofill suggestions in browsers and desktop apps.
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
            var info = element.Current;
            if (info.ProcessId == _ownPid) return;
            if (info.ControlType != ControlType.Edit || !info.IsEnabled)
            {
                FocusLeft?.Invoke();
                return;
            }
            var kind = FieldClassifier.Classify(info.IsPassword, info.Name, info.AutomationId, info.HelpText);
            if (kind == null)
            {
                FocusLeft?.Invoke();
                return;
            }
            var bounds = info.BoundingRectangle;
            if (bounds.IsEmpty || bounds.Width < 20) return;
            LoginFieldFocused?.Invoke(new LoginField
            {
                Element = element,
                Kind = kind.Value,
                Bounds = bounds,
                Window = Native.GetForegroundWindow(),
            });
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
