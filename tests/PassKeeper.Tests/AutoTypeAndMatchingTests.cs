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
