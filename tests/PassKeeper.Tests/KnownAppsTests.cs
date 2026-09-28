using PassKeeper.Core.AutoType;
using PassKeeper.Core.Matching;
using PassKeeper.Core.Models;

namespace PassKeeper.Tests;

public class KnownAppsTests
{
    [Theory]
    [InlineData("csc_ui", "Cisco Secure Client | vpn.example.com", "cisco")]
    [InlineData("vpnui", "Cisco AnyConnect Secure Mobility Client", "cisco")]
    [InlineData("TrGUI", "Check Point Endpoint Security", "checkpoint")]
    [InlineData("SomeClient", "Базис.WorkPlace — вход", "basis")]
    [InlineData("mstsc", "Remote Desktop Connection", "rdp")]
    [InlineData("CredentialUIBroker", "Безопасность Windows", "rdp")]
    [InlineData("rtAdmin", "Рутокен: введите PIN-код", "rutoken")]
    [InlineData("putty", "192.168.1.10 - PuTTY", "putty")]
    public void RecognisesClients(string process, string title, string id) =>
        Assert.Equal(id, KnownApps.Match(process, title)?.Id);

    [Theory]
    [InlineData("chrome", "Hello kitty wallpapers - Google Chrome")]
    [InlineData("notepad", "notes.txt - Notepad")]
    public void IgnoresOtherWindows(string process, string title) => Assert.Null(KnownApps.Match(process, title));

    [Fact]
    public void EntryWithClientPatternsMatchesItsWindow()
    {
        var entry = new VaultEntry { Title = "VPN", Username = "u", Password = "p", WindowPatterns = [.. KnownApps.Find("cisco")!.Patterns] };
        var matches = EntryMatcher.Match([entry], new TargetContext { ProcessName = "csc_ui", WindowTitle = "Cisco Secure Client | vpn.example.com" });
        Assert.Single(matches);
        Assert.Equal(95, matches[0].Score);
        Assert.Empty(EntryMatcher.Match([entry], new TargetContext { ProcessName = "chrome", WindowTitle = "Cisco blog", IsBrowser = true }));
    }

    [Fact]
    public void PinComesFromCustomFieldOrPassword()
    {
        var entry = new VaultEntry { Password = "secret" };
        Assert.Equal("secret", entry.PinCode());
        entry.CustomFields.Add(new CustomField { Name = "PIN‑код", Value = "123456", Protected = true });
        Assert.Equal("123456", entry.PinCode());
        Assert.Equal("123456\n", string.Concat(AutoTypeSequence.Compile("{PIN}{ENTER}", entry).Select(a => a switch
        {
            TypeTextAction t => t.Text,
            KeyAction { Key: "ENTER" } => "\n",
            _ => "?",
        })));
    }
}
