using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;

namespace PassKeeper.Desktop;

/// <summary>Copies a value and takes it off the clipboard after the configured time, unless something else was copied since.</summary>
public static class ClipboardService
{
    private static DispatcherTimer? _timer;
    private static string? _copied;

    /// <returns>Seconds until the clipboard is cleared; 0 when it is not.</returns>
    public static async Task<int> CopyAsync(Visual visual, string text)
    {
        var clipboard = TopLevel.GetTopLevel(visual)?.Clipboard;
        if (clipboard == null) return 0;
        await clipboard.SetTextAsync(text);
        _copied = text;
        _timer?.Stop();
        var seconds = App.Instance.Settings.ClipboardSeconds;
        if (seconds <= 0) return 0;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        _timer.Tick += async (_, _) =>
        {
            _timer?.Stop();
            try
            {
                if (await clipboard.TryGetTextAsync() == _copied) await clipboard.ClearAsync();
            }
            catch (Exception) { /* the clipboard may be held by another program */ }
            _copied = null;
        };
        _timer.Start();
        return seconds;
    }
}
