using Avalonia.Automation;
using Avalonia.Controls;
using PassKeeper.Core.Storage;

namespace PassKeeper.Desktop;

/// <summary>Sets the PIN for quick sign-in (the vault key wrapped with it, the file sealed by the system's secret store).</summary>
public sealed class PinWindow : DialogWindow
{
    private PinWindow(VaultService vault, bool offer) : base(Loc.T("Pin.Title"), 420)
    {
        var pin = Ui.Secret(id: "NewPin");
        var repeat = Ui.Secret(id: "RepeatPin");
        var error = Ui.ErrorText();
        var save = Ui.Button(Loc.T("Common.Save"), () => { }, accent: true);
        save.IsDefault = true;
        AutomationProperties.SetAutomationId(save, "SavePin");
        save.Click += async (_, _) =>
        {
            var value = pin.Text ?? "";
            var problem = !VaultService.IsValidPin(value) ? Loc.T("Pin.ErrDigits") : value != (repeat.Text ?? "") ? Loc.T("Create.ErrMismatch") : "";
            Ui.ShowError(error, problem);
            if (problem.Length > 0) return;
            save.IsEnabled = false;
            try
            {
                await Task.Run(() => vault.SetPin(value));
                Close(true);
            }
            catch (Exception ex) when (ex is PinSealUnavailableException or System.Security.Cryptography.CryptographicException or IOException)
            {
                Ui.ShowError(error, Loc.F("Pin.Failed", ex.Message));
                save.IsEnabled = true;
            }
        };
        SetBody(Ui.Column(0,
                Ui.Muted(Loc.T("Pin.Intro")),
                Ui.Label(Loc.T("Pin.Pin")), pin,
                Ui.Label(Loc.T("Pin.Repeat")), repeat,
                error),
            Ui.Button(Loc.T(offer ? "Pin.Later" : "Common.Cancel"), () => Close(false)), save);
        Opened += (_, _) => pin.Focus();
    }

    public static Task<bool> SetAsync(Window owner, VaultService vault) => new PinWindow(vault, offer: false).ShowDialog<bool>(owner);

    /// <summary>Right after a new user is made: a PIN saves typing the master password each time (can be done later in Settings).</summary>
    public static async Task OfferAsync(Window owner, VaultService vault)
    {
        if (vault.HasPin) return;
        await new PinWindow(vault, offer: true).ShowDialog<bool>(owner);
    }
}
