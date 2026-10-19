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
    public void Sequence_SplitsIntoElementsAndJoinsBack()
    {
        var sequence = "{USERNAME}{TAB}abc {{}x{}}{DELAY 500}{PASSWORD}{ENTER}";
        var tokens = AutoTypeSequence.Tokenize(sequence);
        Assert.Equal(["{USERNAME}", "{TAB}", "abc {{}x{}}", "{DELAY 500}", "{PASSWORD}", "{ENTER}"], tokens);
        Assert.Equal(sequence, string.Concat(tokens));
        // Removing one element (the cross on it in the editor) leaves a valid sequence.
        tokens.RemoveAt(0);
        Assert.Null(AutoTypeSequence.Validate(string.Concat(tokens)));
        Assert.Equal(["broken {TAB"], AutoTypeSequence.Tokenize("broken {TAB"));

        // A removed field takes its separator with it; keystroke sequences lose just the element.
        Assert.Equal("{PASSWORD}{ENTER}", AutoTypeSequence.Remove("{USERNAME}{TAB}{PASSWORD}{ENTER}", 0));
        Assert.Equal("{USERNAME}{ENTER}", AutoTypeSequence.Remove("{USERNAME}{TAB}{PASSWORD}{ENTER}", 2));
        Assert.Equal("{USERNAME}{TAB}{PASSWORD}", AutoTypeSequence.Remove("{USERNAME}{TAB}{PASSWORD}{ENTER}", 3));
        Assert.Equal("{PASSWORD}{ENTER}", AutoTypeSequence.Remove("{USERNAME}{ENTER}{PASSWORD}{ENTER}", 0));
        Assert.Equal("{USERNAME}{ENTER}{DELAY 800}{ENTER}", AutoTypeSequence.Remove("{USERNAME}{ENTER}{DELAY 800}{PASSWORD}{ENTER}", 3));
    }

    [Fact]
    public void Sequence_MadeFromTheFormFields()
    {
        Assert.Equal("{USERNAME}{TAB}{PASSWORD}{ENTER}", AutoTypeSequence.ForForm(login: true, password: true, submit: true));
        Assert.Equal("{USERNAME}{TAB}{PASSWORD}", AutoTypeSequence.ForForm(login: true, password: true));
        // Login first, password on the next page: Enter after each page with automatic sign-in only.
        Assert.Equal("{USERNAME}{ENTER}{PASSWORD}{ENTER}", AutoTypeSequence.ForForm(login: true, password: true, twoSteps: true, submit: true));
        Assert.Equal("{USERNAME}{TAB}{PASSWORD}", AutoTypeSequence.ForForm(login: true, password: true, twoSteps: true));
        Assert.Equal("{USERNAME}{TAB}{PASSWORD}{TAB}{PIN}{ENTER}", AutoTypeSequence.ForForm(login: true, password: true, pin: true, submit: true));
        Assert.Equal("{PIN}{ENTER}", AutoTypeSequence.ForForm(login: false, password: false, pin: true, submit: true));
        Assert.Equal("", AutoTypeSequence.ForForm(login: false, password: false));
        Assert.Equal("{USERNAME}{TAB}{PASSWORD}", AutoTypeSequence.WithoutEnter("{USERNAME}{ENTER}{PASSWORD}{ENTER}"));

        var entry = new VaultEntry { FormSequence = "{USERNAME}{ENTER}{PASSWORD}{ENTER}", AutoLogin = true };
        Assert.Equal("{USERNAME}{ENTER}{PASSWORD}{ENTER}", AutoTypeSequence.Generated(entry));
        entry.AutoLogin = false;
        Assert.Equal("{USERNAME}{TAB}{PASSWORD}", AutoTypeSequence.Generated(entry));
        // A second account of the same site has the same form.
        Assert.Equal(entry.FormSequence, entry.CreateDuplicate("copy").FormSequence);
    }

    [Fact]
    public void SameAccount_LoginWithOrWithoutTheDomain()
    {
        var vpn = new VaultEntry { Title = "VPN", Username = @"CORP\jdoe", Password = "S3cret!" };
        Assert.True(EntryMatcher.SameAccount(vpn, new VaultEntry { Username = "jdoe", Password = "S3cret!" }));
        Assert.True(EntryMatcher.SameAccount(vpn, new VaultEntry { Email = "JDoe@corp.example", Password = "S3cret!" }));
        Assert.False(EntryMatcher.SameAccount(vpn, new VaultEntry { Username = "jdoe", Password = "other" }));
        Assert.False(EntryMatcher.SameAccount(vpn, new VaultEntry { Username = "asmith", Password = "S3cret!" }));
        Assert.False(EntryMatcher.SameAccount(new VaultEntry { Username = "a" }, new VaultEntry { Username = "a" }));
    }

    [Fact]
    public void FieldPlan_OnlyForSequencesOfFields()
    {
        var plan = FieldPlan.Parse("{USERNAME}{ENTER}{PASSWORD}{ENTER}")!;
        Assert.True(plan.Includes(FieldKind.Login));
        Assert.True(plan.Includes(FieldKind.Email)); // the login field, whatever it asks for
        Assert.True(plan.EnterAfter(FieldKind.Login));
        Assert.True(plan.EnterAfter(FieldKind.Password));
        Assert.False(plan.Includes(FieldKind.Pin));

        // The user removed the login: only the password is filled, and Enter after it.
        var passwordOnly = FieldPlan.Parse("{PASSWORD}{ENTER}")!;
        Assert.False(passwordOnly.Includes(FieldKind.Login));
        Assert.True(passwordOnly.EnterAfter(FieldKind.Password));
        Assert.False(FieldPlan.Parse("{USERNAME}{TAB}{PASSWORD}")!.HasEnter);

        // Keystrokes, delays and text are typed as they are, not filled by fields.
        Assert.Null(FieldPlan.Parse("{USERNAME}{ENTER}{DELAY 800}{PASSWORD}{ENTER}"));
        Assert.Null(FieldPlan.Parse("{PASSWORD}x"));
        Assert.Null(FieldPlan.Parse("{ENTER}{PASSWORD}"));
        Assert.Null(FieldPlan.Parse(""));

        var entry = new VaultEntry { FormSequence = "{USERNAME}{TAB}{PASSWORD}{ENTER}", AutoLogin = true };
        Assert.True(FieldPlan.For(entry)!.Generated);
        entry.AutoTypeSequence = "{PASSWORD}";
        Assert.False(FieldPlan.For(entry)!.Generated);
        Assert.False(FieldPlan.For(entry)!.Includes(FieldKind.Login));
    }

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
    public void Obvious_ChosenAccountAmongDuplicates()
    {
        // Two accounts saved for one sign-in page (e.g. imported twice): the one the user chose is filled in next time,
        // whatever the address of the page this time (a fresh one-time link on every sign-in).
        var first = new VaultEntry { Title = "A", Url = "https://sso.corp.local/auth?state=1" };
        var second = new VaultEntry { Title = "B", Url = "https://sso.corp.local/auth?state=2", AutoFillHosts = ["sso.corp.local"] };
        var page = new TargetContext { Url = "https://sso.corp.local/auth/realms/x/protocol/openid-connect/auth?state=9f3c&code_challenge=Zx", IsBrowser = true };
        var matches = EntryMatcher.Match([first, second], page);
        Assert.Equal(MatchScore.Chosen, matches[0].Score);
        Assert.Equal(second, EntryMatcher.Obvious(matches, site: true));
        // Without a choice the user is asked.
        Assert.Null(EntryMatcher.Obvious(EntryMatcher.Match([first, new VaultEntry { Url = "https://sso.corp.local" }], page), site: true));
    }

    [Fact]
    public void ChosenAccount_NotCopiedByDuplicate_AndDroppedWithItsAddress()
    {
        var entry = new VaultEntry { Title = "A", Url = "https://sso.corp.local/auth", ExtraUrls = ["https://mail.corp.local"], AutoFillHosts = ["sso.corp.local", "mail.corp.local"] };
        // A duplicate is a second account: it must not claim the sites of the original.
        var copy = entry.CreateDuplicate("A (copy)");
        Assert.Empty(copy.AutoFillHosts);
        Assert.Equal(2, entry.AutoFillHosts.Count);
        // The user removed one address in the editor: its choice goes with it.
        var before = entry.AllUrls().ToList();
        entry.ExtraUrls.Clear();
        entry.ForgetChoicesOfRemovedAddresses(before);
        Assert.Equal(["sso.corp.local"], entry.AutoFillHosts);
        // A choice whose address an earlier version dropped (a VPN entry chosen on a site, then edited) stays.
        var vpn = new VaultEntry { Title = "VPN", Category = EntryCategory.Remote, Url = "vpn.corp.local", AutoFillHosts = ["sso.corp.local"] };
        vpn.ForgetChoicesOfRemovedAddresses(vpn.AllUrls().ToList());
        Assert.Equal(["sso.corp.local"], vpn.AutoFillHosts);
    }

    [Fact]
    public void AutoLogin_SiteAndProgramAreSeparate()
    {
        var site = new VaultEntry { Title = "Portal", Url = "https://portal.example.org", AutoLogin = true };
        Assert.True(site.SignsInToSite);
        Assert.False(site.SignsInToProgram);
        // A VPN entry used on the company portal: its switch is about the client, the portal is only filled in.
        var vpn = new VaultEntry { Title = "VPN", Category = EntryCategory.Remote, WindowPatterns = ["vpnui.exe"], ExtraUrls = ["https://portal.example.org"], AutoLogin = true };
        Assert.True(vpn.SignsInToProgram);
        Assert.False(vpn.SignsInToSite);
        Assert.False(new VaultEntry { Url = "https://portal.example.org" }.SignsInToSite);
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
