using PassKeeper.Core.Matching;
using PassKeeper.Core.Models;

namespace PassKeeper.Core.AutoType;

/// <summary>
/// A desktop sign-in client (VPN, VDI, remote desktop, token PIN prompt): window patterns for an entry and, where the
/// login form cannot be filled field by field (terminal, PIN prompt), the auto-type sequence to use. Patterns follow
/// <see cref="EntryMatcher.WildcardMatch"/>: "name.exe" matches the process, '*' patterns match the whole window
/// title, other plain text matches a part of the title.
/// </summary>
public sealed record KnownApp(string Id, string Name, EntryCategory Category, string[] Patterns, string? Sequence = null)
{
    /// <summary>Executable names from the patterns ("csc_ui.exe"), used to find the client among running processes.</summary>
    public IEnumerable<string> ProcessNames =>
        Patterns.Where(p => p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !p.Contains('*')).Select(p => p[..^4]);

    /// <summary>The client asks for a token / smart-card PIN rather than a password.</summary>
    public bool UsesPin => Sequence?.Contains("{PIN}", StringComparison.OrdinalIgnoreCase) == true;
}

public static class KnownApps
{
    private const string PinOnly = "{PIN}{ENTER}";

    public static readonly IReadOnlyList<KnownApp> All =
    [
        // VPN
        new("cisco", "Cisco Secure Client (AnyConnect)", EntryCategory.Remote, ["csc_ui.exe", "vpnui.exe", "Cisco Secure Client*", "Cisco AnyConnect*"]),
        new("checkpoint", "Check Point Endpoint Security VPN", EntryCategory.Remote, ["TrGUI.exe", "Check Point*"]),
        new("forticlient", "FortiClient VPN", EntryCategory.Remote, ["FortiClient.exe", "FortiTray.exe", "FortiClient*"]),
        new("globalprotect", "Palo Alto GlobalProtect", EntryCategory.Remote, ["PanGPA.exe", "GlobalProtect*"]),
        new("openvpn", "OpenVPN GUI / OpenVPN Connect", EntryCategory.Remote, ["openvpn-gui.exe", "OpenVPNConnect.exe", "OpenVPN*"]),
        new("continent", "Континент-АП / Continent", EntryCategory.Remote, ["*Континент*", "*Continent*"]),
        new("vipnet", "ViPNet Client", EntryCategory.Remote, ["ViPNet*"]),
        // VDI / remote desktop
        new("basis", "Базис.WorkPlace (ВРМ)", EntryCategory.Remote, ["*Базис*", "*Basis*", "*WorkPlace*"]),
        new("citrix", "Citrix Workspace", EntryCategory.Remote, ["SelfService.exe", "Citrix*"]),
        new("horizon", "Omnissa / VMware Horizon Client", EntryCategory.Remote, ["vmware-view.exe", "horizon-client.exe", "*Horizon Client*"]),
        new("termidesk", "Termidesk", EntryCategory.Remote, ["termidesk*.exe", "Termidesk*"]),
        new("rdp", "Remote Desktop / Windows Security", EntryCategory.Remote, ["mstsc.exe", "CredentialUIBroker.exe", "Безопасность Windows", "Windows Security"]),
        // Tokens and smart cards
        new("rutoken", "Рутокен / Rutoken PIN", EntryCategory.App, ["*Рутокен*", "*Rutoken*"], PinOnly),
        new("cryptopro", "КриптоПро CSP PIN", EntryCategory.App, ["*КриптоПро CSP*", "*CryptoPro CSP*"], PinOnly),
        // Business applications and terminals
        new("1c", "1С:Предприятие", EntryCategory.App, ["1cv8.exe", "1cv8c.exe", "*1С:Предприятие*"]),
        new("sap", "SAP GUI", EntryCategory.App, ["saplogon.exe", "sapgui.exe"]),
        new("putty", "PuTTY / KiTTY (SSH)", EntryCategory.App, ["putty.exe", "kitty.exe"], "{USERNAME}{ENTER}{DELAY 800}{PASSWORD}{ENTER}"),
        new("winscp", "WinSCP", EntryCategory.App, ["WinSCP.exe"]),
    ];

    public static KnownApp? Find(string id) => All.FirstOrDefault(a => a.Id == id);

    /// <summary>The known client whose patterns an entry uses (added from the catalog or typed by hand).</summary>
    public static KnownApp? ForPatterns(IEnumerable<string> patterns)
    {
        foreach (var raw in patterns)
        {
            var p = raw.Trim();
            if (p.Length == 0) continue;
            var app = All.FirstOrDefault(a => a.Patterns.Contains(p, StringComparer.OrdinalIgnoreCase));
            if (app != null) return app;
            var process = p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p[..^4] : p;
            if (!p.Contains('*') && Match(process, null) is { } byProcess) return byProcess;
        }
        return null;
    }

    /// <summary>The known client that owns this window, if any.</summary>
    public static KnownApp? Match(string? processName, string? windowTitle)
    {
        var exe = string.IsNullOrEmpty(processName) ? null : processName + ".exe";
        return All.FirstOrDefault(app => app.Patterns.Any(p =>
            EntryMatcher.WildcardMatch(exe, p) || EntryMatcher.WildcardMatch(windowTitle, p)));
    }
}
