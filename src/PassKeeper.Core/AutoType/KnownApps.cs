using PassKeeper.Core.Matching;

namespace PassKeeper.Core.AutoType;

/// <summary>
/// A desktop sign-in client (VPN, VDI, remote desktop, token PIN prompt): window patterns for an entry and, where the
/// login form cannot be filled field by field (terminal, PIN prompt), the auto-type sequence to use. Patterns follow
/// <see cref="EntryMatcher.WildcardMatch"/>: "name.exe" matches the process, '*' patterns match the whole window
/// title, other plain text matches a part of the title.
/// </summary>
public sealed record KnownApp(string Id, string Name, string[] Patterns, string? Sequence = null);

public static class KnownApps
{
    private const string PinOnly = "{PIN}{ENTER}";

    public static readonly IReadOnlyList<KnownApp> All =
    [
        // VPN
        new("cisco", "Cisco Secure Client (AnyConnect)", ["csc_ui.exe", "vpnui.exe", "Cisco Secure Client*", "Cisco AnyConnect*"]),
        new("checkpoint", "Check Point Endpoint Security VPN", ["TrGUI.exe", "Check Point*"]),
        new("forticlient", "FortiClient VPN", ["FortiClient.exe", "FortiTray.exe", "FortiClient*"]),
        new("globalprotect", "Palo Alto GlobalProtect", ["PanGPA.exe", "GlobalProtect*"]),
        new("openvpn", "OpenVPN GUI / OpenVPN Connect", ["openvpn-gui.exe", "OpenVPNConnect.exe", "OpenVPN*"]),
        new("continent", "Континент-АП / Continent", ["*Континент*", "*Continent*"]),
        new("vipnet", "ViPNet Client", ["ViPNet*"]),
        // VDI / remote desktop
        new("basis", "Базис.WorkPlace (ВРМ)", ["*Базис*", "*Basis*", "*WorkPlace*"]),
        new("citrix", "Citrix Workspace", ["SelfService.exe", "Citrix*"]),
        new("horizon", "Omnissa / VMware Horizon Client", ["vmware-view.exe", "horizon-client.exe", "*Horizon Client*"]),
        new("termidesk", "Termidesk", ["termidesk*.exe", "Termidesk*"]),
        new("rdp", "Remote Desktop / Windows Security", ["mstsc.exe", "CredentialUIBroker.exe", "Безопасность Windows", "Windows Security"]),
        // Tokens and smart cards
        new("rutoken", "Рутокен / Rutoken PIN", ["*Рутокен*", "*Rutoken*"], PinOnly),
        new("cryptopro", "КриптоПро CSP PIN", ["*КриптоПро CSP*", "*CryptoPro CSP*"], PinOnly),
        // Business applications and terminals
        new("1c", "1С:Предприятие", ["1cv8.exe", "1cv8c.exe", "*1С:Предприятие*"]),
        new("sap", "SAP GUI", ["saplogon.exe", "sapgui.exe"]),
        new("putty", "PuTTY / KiTTY (SSH)", ["putty.exe", "kitty.exe"], "{USERNAME}{ENTER}{DELAY 800}{PASSWORD}{ENTER}"),
        new("winscp", "WinSCP", ["WinSCP.exe"]),
    ];

    public static KnownApp? Find(string id) => All.FirstOrDefault(a => a.Id == id);

    /// <summary>The known client that owns this window, if any.</summary>
    public static KnownApp? Match(string? processName, string? windowTitle)
    {
        var exe = string.IsNullOrEmpty(processName) ? null : processName + ".exe";
        return All.FirstOrDefault(app => app.Patterns.Any(p =>
            EntryMatcher.WildcardMatch(exe, p) || EntryMatcher.WildcardMatch(windowTitle, p)));
    }
}
