using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PassKeeper.Core.Storage;
using PassKeeper.Localization;

namespace PassKeeper.Views;

/// <summary>PIN creation: mandatory right after registration (and after a PIN lock-out), optional from settings.</summary>
public partial class PinSetupView : UserControl
{
    private readonly bool _mandatory;
    private bool _busy;

    public PinSetupView(bool mandatory)
    {
        _mandatory = mandatory;
        InitializeComponent();
        CancelButton.Visibility = mandatory ? Visibility.Collapsed : Visibility.Visible;
        Pin.MaxLength = VaultService.MaxPinLength;
        Confirm.MaxLength = VaultService.MaxPinLength;
        DataObject.AddPastingHandler(Pin, OnPaste);
        DataObject.AddPastingHandler(Confirm, OnPaste);
        Pin.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Confirm.Focus(); e.Handled = true; }
        };
        Loaded += (_, _) => Pin.Focus();
    }

    public event Action? Done;

    internal static void OnlyDigits(object sender, TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(char.IsAsciiDigit);

    private void Digits_PreviewTextInput(object sender, TextCompositionEventArgs e) => OnlyDigits(sender, e);

    internal static void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetData(DataFormats.UnicodeText) is not string s || !s.All(char.IsAsciiDigit)) e.CancelCommand();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var pin = Pin.Password;
        string? error = null;
        if (!VaultService.IsValidPin(pin)) error = Loc.F("Pin.ErrLength", VaultService.MinPinLength, VaultService.MaxPinLength);
        else if (pin != Confirm.Password) error = Loc.T("Pin.ErrMismatch");
        else if (pin.Distinct().Count() == 1 || "0123456789012".Contains(pin) || "9876543210987".Contains(pin)) error = Loc.T("Pin.ErrSimple");
        Error.Text = error ?? "";
        Error.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
        if (error != null) return;

        _busy = true;
        SaveButton.IsEnabled = false;
        Busy.Visibility = Visibility.Visible;
        try
        {
            var vault = App.Instance.Vault;
            await Task.Run(() => vault.SetPin(pin));
            Pin.Clear();
            Confirm.Clear();
            if (Done != null) Done();
            else App.Instance.ShowStartPage();
            if (!_mandatory) App.Instance.Main.ShowToast(Loc.T("Pin.Changed"));
        }
        catch (Exception ex)
        {
            Error.Text = Loc.F("Common.ErrorFormat", ex.Message);
            Error.Visibility = Visibility.Visible;
        }
        finally
        {
            _busy = false;
            SaveButton.IsEnabled = true;
            Busy.Visibility = Visibility.Collapsed;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (Done != null) Done();
        else App.Instance.ShowStartPage();
    }
}
