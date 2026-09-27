using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace PassKeeper.Services;

/// <summary>
/// Copies secrets excluded from Windows clipboard history/cloud sync and clears them after a timeout
/// (only if the clipboard still holds the copied value).
/// </summary>
public static class ClipboardService
{
    private static DispatcherTimer? _timer;
    private static string? _lastSecret;

    public static bool Copy(string text, int clearAfterSeconds, bool secret = true)
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, text);
        if (secret)
        {
            data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
            data.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
            data.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0)));
        }
        if (!Retry(() => Clipboard.SetDataObject(data, true))) return false;

        _timer?.Stop();
        if (secret && clearAfterSeconds > 0)
        {
            _lastSecret = text;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(clearAfterSeconds) };
            _timer.Tick += (_, _) =>
            {
                _timer?.Stop();
                ClearIfOurs();
            };
            _timer.Start();
        }
        return true;
    }

    /// <summary>Clears the clipboard if it still contains the last copied secret (e.g. on lock/exit).</summary>
    public static void ClearIfOurs()
    {
        var secret = _lastSecret;
        _lastSecret = null;
        if (secret == null) return;
        Retry(() =>
        {
            if (Clipboard.ContainsText() && Clipboard.GetText() == secret) Clipboard.Clear();
        });
    }

    private static bool Retry(Action action)
    {
        for (var i = 0; i < 5; i++)
        {
            try
            {
                action();
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(40);
            }
            catch (ExternalException)
            {
                Thread.Sleep(40);
            }
        }
        return false;
    }
}
