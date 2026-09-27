using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using PassKeeper.Controls;
using PassKeeper.Core.Storage;
using PassKeeper.Localization;

namespace PassKeeper.Views;

/// <summary>Lock screen: PIN (default) or master password.</summary>
public partial class UnlockView : UserControl
{
    private bool _masterMode;
    private bool _busy;

    public UnlockView(bool compact = false)
    {
        InitializeComponent();
        var vault = App.Instance.Vault;
        _masterMode = !vault.HasPin;
        if (compact)
        {
            Root.Background = Brushes.Transparent;
            Panel.Margin = new Thickness(0);
            Panel.Width = 320;
            Footer.Visibility = Visibility.Collapsed;
        }
        DataObject.AddPastingHandler(Pin, PinSetupView.OnPaste);
        Master.EnterPressed += (_, _) => Unlock_Click(this, new RoutedEventArgs());
        Loaded += (_, _) =>
        {
            Loc.I.LanguageChanged += OnLanguageChanged;
            FocusInput();
        };
        Unloaded += (_, _) => Loc.I.LanguageChanged -= OnLanguageChanged;
        UpdateMode();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => UpdateTexts();

    public event Action? Unlocked;

    public void FocusInput()
    {
        if (_masterMode) Master.FocusInput();
        else Pin.Focus();
    }

    private void UpdateTexts()
    {
        var name = App.Instance.Vault.UserName;
        Initial.Text = Avatar.InitialOf(name);
        Greeting.Text = Loc.F("Unlock.Greeting", name);
        Prompt.Text = Loc.T(_masterMode ? "Unlock.PromptMaster" : "Unlock.PromptPin");
        SwitchMode.Content = Loc.T(_masterMode ? "Unlock.UsePin" : "Unlock.UseMaster");
    }

    private void UpdateMode()
    {
        Pin.Visibility = _masterMode ? Visibility.Collapsed : Visibility.Visible;
        Master.Visibility = _masterMode ? Visibility.Visible : Visibility.Collapsed;
        SwitchMode.Visibility = App.Instance.Vault.HasPin || !_masterMode ? Visibility.Visible : Visibility.Collapsed;
        UpdateTexts();
        ShowError(null);
    }

    private void SwitchMode_Click(object sender, RoutedEventArgs e)
    {
        _masterMode = !_masterMode;
        Pin.Clear();
        Master.Clear();
        UpdateMode();
        FocusInput();
    }

    private void Pin_PreviewTextInput(object sender, TextCompositionEventArgs e) => PinSetupView.OnlyDigits(sender, e);

    private void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Unlock_Click(this, new RoutedEventArgs());
    }

    private async void Unlock_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var vault = App.Instance.Vault;
        if (_masterMode)
        {
            var master = Master.Value;
            if (master.Length == 0) return;
            SetBusy(true);
            try
            {
                var ok = await Task.Run(() => vault.UnlockWithMaster(master));
                if (!ok)
                {
                    Fail(Loc.T("Unlock.WrongMaster"));
                    Master.Clear();
                    return;
                }
                Unlocked?.Invoke();
            }
            catch (Exception ex) when (ex is CryptographicException or VaultFormatException or IOException)
            {
                Fail(Loc.F("Unlock.Corrupted", ex.Message));
            }
            finally
            {
                SetBusy(false);
            }
            return;
        }

        var pin = Pin.Password;
        if (pin.Length == 0) return;
        SetBusy(true);
        try
        {
            var result = await Task.Run(() => vault.UnlockWithPin(pin));
            Pin.Clear();
            switch (result)
            {
                case PinUnlockResult.Success:
                    Unlocked?.Invoke();
                    break;
                case PinUnlockResult.WrongPin:
                    Fail(Loc.F("Unlock.WrongPin", vault.PinAttemptsLeft));
                    break;
                case PinUnlockResult.LockedOut:
                case PinUnlockResult.NotConfigured:
                    _masterMode = true;
                    UpdateMode();
                    Fail(Loc.T(result == PinUnlockResult.LockedOut ? "Unlock.LockedOut" : "Unlock.PinReset"));
                    FocusInput();
                    break;
            }
        }
        catch (Exception ex) when (ex is CryptographicException or VaultFormatException or IOException)
        {
            Fail(Loc.F("Unlock.Corrupted", ex.Message));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void Fail(string message)
    {
        ShowError(message);
        var shake = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(360) };
        foreach (var (t, x) in new[] { (0, 0.0), (60, -10.0), (120, 9.0), (180, -7.0), (240, 5.0), (300, -2.0), (360, 0.0) })
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(x, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(t))));
        var transform = new TranslateTransform();
        Panel.RenderTransform = transform;
        transform.BeginAnimation(TranslateTransform.XProperty, shake);
        FocusInput();
    }

    private void ShowError(string? text)
    {
        Error.Text = text ?? "";
        Error.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        UnlockButton.IsEnabled = !busy;
        Busy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Pin.IsEnabled = !busy;
        Master.IsEnabled = !busy;
        if (!busy) FocusInput();
    }
}
