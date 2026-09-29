using System.Text.Json;
using PassKeeper.Core.AutoType;
using PassKeeper.Core.Models;

namespace PassKeeper.Tests;

public class EntryModelTests
{
    [Fact]
    public void SectionIsDetectedForEntriesWithoutOne()
    {
        Assert.Equal(EntryCategory.Web, new VaultEntry { Url = "https://mail.example.com" }.EffectiveCategory);
        Assert.Equal(EntryCategory.Remote, new VaultEntry { WindowPatterns = ["csc_ui.exe"] }.EffectiveCategory);
        Assert.Equal(EntryCategory.Remote, new VaultEntry { WindowPatterns = ["mstsc"] }.EffectiveCategory);
        Assert.Equal(EntryCategory.App, new VaultEntry { WindowPatterns = ["*1С:Предприятие*"] }.EffectiveCategory);
        Assert.Equal(EntryCategory.App, new VaultEntry { WindowPatterns = ["notepad++.exe"], Url = "https://example.com" }.EffectiveCategory);
        Assert.Equal(EntryCategory.Other, new VaultEntry { Title = "Wi-Fi", Password = "x" }.EffectiveCategory);
        Assert.Equal(EntryCategory.Other, new VaultEntry { Url = "https://example.com", Category = EntryCategory.Other }.EffectiveCategory);
    }

    [Fact]
    public void KnownClientIsFoundFromPatterns()
    {
        Assert.Equal("cisco", KnownApps.ForPatterns(["Cisco Secure Client*"])?.Id);
        Assert.Equal("checkpoint", KnownApps.ForPatterns(["TrGUI"])?.Id);
        Assert.Null(KnownApps.ForPatterns(["notepad.exe", "*Report*"]));
        Assert.Contains("csc_ui", KnownApps.Find("cisco")!.ProcessNames);
        Assert.True(KnownApps.Find("rutoken")!.UsesPin);
    }

    [Fact]
    public void PinIsStoredInOneProtectedField()
    {
        var entry = new VaultEntry { Password = "secret" };
        entry.SetPinCode("1234");
        entry.SetPinCode("5678");
        var field = Assert.Single(entry.CustomFields);
        Assert.True(field.Protected);
        Assert.Equal("5678", entry.StoredPin());
        Assert.Equal("5678", entry.PinCode());
        entry.SetPinCode("");
        Assert.Empty(entry.CustomFields);
        Assert.Equal("secret", entry.PinCode());
    }

    [Fact]
    public void DuplicateIsIndependent()
    {
        var entry = new VaultEntry { Title = "VPN", Password = "new", CustomFields = [new CustomField { Name = "PIN", Value = "1" }] };
        entry.PasswordHistory.Add(new PasswordHistoryItem { Password = "old" });
        entry.LastUsedUtc = DateTime.UtcNow;
        var copy = entry.CreateDuplicate("VPN (copy)");
        Assert.NotEqual(entry.Id, copy.Id);
        Assert.Equal("VPN (copy)", copy.Title);
        Assert.Equal("new", copy.Password);
        Assert.Empty(copy.PasswordHistory);
        Assert.Null(copy.LastUsedUtc);
        copy.CustomFields[0].Value = "2";
        Assert.Equal("1", entry.CustomFields[0].Value);
    }

    [Fact]
    public void SectionAndAutoLoginSurviveSerialization()
    {
        var entry = new VaultEntry { Category = EntryCategory.Remote, AutoLogin = true };
        var json = JsonSerializer.Serialize(entry, VaultJson.Options);
        Assert.Contains("\"category\":\"Remote\"", json);
        Assert.DoesNotContain("effectiveCategory", json);
        var back = JsonSerializer.Deserialize<VaultEntry>(json, VaultJson.Options)!;
        Assert.Equal(EntryCategory.Remote, back.Category);
        Assert.True(back.AutoLogin);
        var old = JsonSerializer.Deserialize<VaultEntry>("{\"title\":\"x\",\"url\":\"https://a.example\"}", VaultJson.Options)!;
        Assert.Null(old.Category);
        Assert.Equal(EntryCategory.Web, old.EffectiveCategory);
    }
}
