using PassKeeper.Core.AutoType;
using PassKeeper.Core.Matching;
using PassKeeper.Core.Models;

namespace PassKeeper.Tests;

public class AutoTypeAndMatchingTests
{
    private static readonly VaultEntry Entry = new()
    {
        Title = "Bank", Username = "user", Password = "p{a}ss", Email = "u@x.ru", Phone = "+7",
        SecretKey = "KEY", CustomFields = [new CustomField { Name = "Code", Value = "42" }],
    };

    [Fact]
    public void Obvious_SignInPageOfACompanyDomain()
    {
        // The single sign-on page has its own entry; the company's other services share the domain.
        var sso = new VaultEntry { Title = "SSO", Url = "https://sso.corp.local/auth/realms/x" };
        var jira = new VaultEntry { Title = "Jira", Url = "https://jira.corp.local" };
        var portal = new VaultEntry { Title = "Portal", Url = "https://portal.corp.local" };
        var page = new TargetContext { Url = "https://sso.corp.local/auth/realms/x/protocol/openid-connect/auth?client_id=a", IsBrowser = true };
        Assert.Equal(sso, EntryMatcher.Obvious(EntryMatcher.Match([jira, sso, portal], page), site: true));

        // Without its own entry nothing is filled by itself: the user chooses.
        Assert.Null(EntryMatcher.Obvious(EntryMatcher.Match([jira, portal], page), site: true));
        // The only entry of the domain is the obvious one.
        Assert.Equal(portal, EntryMatcher.Obvious(EntryMatcher.Match([portal], page), site: true));
        // Two accounts saved for the same address: the user chooses.
        var second = new VaultEntry { Title = "SSO 2", Url = "https://sso.corp.local" };
        Assert.Null(EntryMatcher.Obvious(EntryMatcher.Match([sso, second], page), site: true));
    }

    [Fact]
    public void Obvious_ProgramNeedsItsWindows()
    {
        var vpn = new VaultEntry { Title = "VPN", WindowPatterns = ["csc_ui.exe"] };
        var byTitle = new VaultEntry { Title = "Cisco" };
        var window = new TargetContext { WindowTitle = "Cisco Secure Client | vpn", ProcessName = "csc_ui" };
        Assert.Equal(vpn, EntryMatcher.Obvious(EntryMatcher.Match([vpn, byTitle], window), site: false));
        // An entry found only by a word of the title is never filled by itself.
        Assert.Null(EntryMatcher.Obvious(EntryMatcher.Match([byTitle], window), site: false));
    }

    [Fact]
    public void Compile_DefaultSequence()
    {
        var actions = AutoTypeSequence.Compile(AutoTypeSequence.Default, Entry);
        Assert.Equal(
            [new TypeTextAction("user"), new KeyAction("TAB"), new TypeTextAction("p{a}ss"), new KeyAction("ENTER")],
            actions);
    }

    [Fact]
    public void Compile_DelaysRepeatsLiteralsAndFields()
    {
        var actions = AutoTypeSequence.Compile("{DELAY=20}{EMAIL}{TAB 2}{{}x{}}{DELAY 300}{S:Code}{KEY}{PHONE}{bs}", Entry);
        Assert.Equal(
            [
                new KeyDelayAction(20), new TypeTextAction("u@x.ru"), new KeyAction("TAB", 2), new TypeTextAction("{x}"),
                new DelayAction(300), new TypeTextAction("42KEY+7"), new KeyAction("BACKSPACE"),
            ],
            actions);
    }

    [Fact]
    public void Compile_RejectsUnknownPlaceholders()
    {
        Assert.Throws<AutoTypeException>(() => AutoTypeSequence.Compile("{NOPE}", Entry));
        Assert.Throws<AutoTypeException>(() => AutoTypeSequence.Compile("{USERNAME", Entry));
        Assert.NotNull(AutoTypeSequence.Validate("abc}"));
        Assert.Null(AutoTypeSequence.Validate("{USERNAME}{TAB}{PASSWORD}{ENTER}"));
    }

    [Theory]
    [InlineData("https://login.example.com/path", "example.com", 80)]
    [InlineData("https://www.example.com", "https://example.com/login", 100)]
    [InlineData("https://a.example.co.uk", "b.example.co.uk", 70)]
    [InlineData("https://example.com", "example.org", 0)]
    [InlineData("https://почта.рф/", "https://xn--80a1acny.xn--p1ai", 100)]
    public void Matcher_ComparesHosts(string page, string entryUrl, int expected)
    {
        var matches = EntryMatcher.Match([new VaultEntry { Url = entryUrl }], new TargetContext { Url = page, IsBrowser = true });
        Assert.Equal(expected, matches.FirstOrDefault()?.Score ?? 0);
    }

    [Fact]
    public void Matcher_UsesWindowPatternsAndTitles()
    {
        var app = new VaultEntry { Title = "1С", WindowPatterns = ["*1С:Предприятие*"] };
        var rdp = new VaultEntry { Title = "Server", WindowPatterns = ["mstsc.exe"] };
        var site = new VaultEntry { Title = "Portal", Url = "portal.corp.local" };
        var target = new TargetContext { WindowTitle = "Бухгалтерия — 1С:Предприятие", ProcessName = "1cv8" };
        Assert.Equal(app, Assert.Single(EntryMatcher.Match([app, rdp, site], target)).Entry);
        Assert.Equal(rdp, Assert.Single(EntryMatcher.Match([app, rdp, site], new TargetContext { WindowTitle = "Remote", ProcessName = "mstsc" })).Entry);
        Assert.Equal(site, Assert.Single(EntryMatcher.Match([app, rdp, site], new TargetContext { WindowTitle = "portal.corp.local - Chrome", IsBrowser = true })).Entry);
    }

    [Fact]
    public void Search_MatchesAllTerms()
    {
        Assert.True(EntryMatcher.MatchesSearch(Entry, "bank us"));
        Assert.False(EntryMatcher.MatchesSearch(Entry, "bank zzz"));
    }
}
