using System.Security.Cryptography;
using System.Text;
using PassKeeper.Core.Interop;
using PassKeeper.Core.Interop.KeePass;
using PassKeeper.Core.Models;

namespace PassKeeper.Tests;

public class InteropTests
{
    private static List<VaultEntry> Sample() =>
    [
        new()
        {
            Title = "Почта", Username = "ivan@example.ru", Password = "Pa;ss,\"word\"\n2", Url = "https://mail.example.ru/login",
            Notes = "многострочная\nзаметка", Folder = "Работа/Почта", Favorite = true, Email = "ivan@example.ru", Phone = "+7 900 000-00-00",
            Totp = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", Tags = ["mail", "work"], ExtraUrls = ["https://m.example.ru"],
            CustomFields = [new CustomField { Name = "PIN", Value = "1234", Protected = true }],
            WindowPatterns = ["*Outlook*"], AutoTypeSequence = "{USERNAME}{TAB}{PASSWORD}",
        },
        new() { Title = "GitHub", Username = "octo", Password = "gh-secret", Url = "github.com", SecretKey = "ghp_token" },
        new() { Title = "Note only", Notes = "just text" },
    ];

    [Theory]
    [InlineData(ExportFormat.CsvChrome)]
    [InlineData(ExportFormat.CsvFirefox)]
    [InlineData(ExportFormat.CsvBitwarden)]
    [InlineData(ExportFormat.CsvKeePassXc)]
    [InlineData(ExportFormat.CsvOnePassword)]
    [InlineData(ExportFormat.CsvLastPass)]
    [InlineData(ExportFormat.CsvPassKeeper)]
    [InlineData(ExportFormat.JsonBitwarden)]
    [InlineData(ExportFormat.JsonPassKeeper)]
    [InlineData(ExportFormat.XmlKeePass)]
    public void Export_Then_Import_PreservesLogins(ExportFormat format)
    {
        using var dir = new TempDir();
        var path = dir.File("export" + Exporter.Extension(format));
        File.WriteAllBytes(path, Exporter.Export(Sample(), format));
        var result = FileImporter.Import(path);
        var mail = result.Entries.Single(e => e.Password == "Pa;ss,\"word\"\n2");
        Assert.Equal("ivan@example.ru", mail.Username);
        Assert.Contains("mail.example.ru", mail.Url);
        var gh = result.Entries.Single(e => e.Password == "gh-secret");
        Assert.Equal("octo", gh.Username);

        if (format is ExportFormat.CsvPassKeeper or ExportFormat.JsonPassKeeper or ExportFormat.XmlKeePass or ExportFormat.JsonBitwarden)
        {
            Assert.Equal("Почта", mail.Title);
            Assert.Equal("многострочная\nзаметка", mail.Notes);
            Assert.Equal("Работа/Почта", mail.Folder);
            Assert.True(mail.Favorite);
            Assert.Equal("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", Core.Security.Totp.Parse(mail.Totp) is { } t ? Core.Security.Base32.Encode(t.Secret) : null);
        }
    }

    [Fact]
    public void Kdbx_RoundTrip_Argon2_Aes_KeyFile()
    {
        foreach (var options in new[]
                 {
                     new KdbxWriteOptions { Kdf = "argon2d", Argon2MemoryKiB = 8192, Argon2Iterations = 2 },
                     new KdbxWriteOptions { Kdf = "argon2id", Argon2MemoryKiB = 8192, Argon2Iterations = 2 },
                     new KdbxWriteOptions { Kdf = "aes", AesRounds = 1000 },
                 })
        {
            var bytes = Exporter.Export(Sample(), ExportFormat.Kdbx, "db-pass", options);
            Assert.True(Kdbx.IsKdbx(bytes));
            Assert.Throws<KdbxInvalidKeyException>(() => Kdbx.Decrypt(bytes, "wrong", null));
            var result = KeePassXml.ToEntries(Kdbx.Decrypt(bytes, "db-pass", null), "t");
            Assert.Equal(3, result.Entries.Count);
            var mail = result.Entries.Single(e => e.Title == "Почта");
            Assert.Equal("Pa;ss,\"word\"\n2", mail.Password);
            Assert.Equal("Работа/Почта", mail.Folder);
            Assert.Equal("+7 900 000-00-00", mail.Phone);
            Assert.Equal(["*Outlook*"], mail.WindowPatterns);
            Assert.Equal("{USERNAME}{TAB}{PASSWORD}", mail.AutoTypeSequence);
            Assert.True(mail.Favorite);
            var pin = Assert.Single(mail.CustomFields);
            Assert.True(pin.Protected);
            Assert.Equal("1234", pin.Value);
            Assert.Equal("ghp_token", result.Entries.Single(e => e.Title == "GitHub").SecretKey);
        }

        // Key file (XML v2 + raw 32 bytes) combined with password.
        var raw = RandomNumberGenerator.GetBytes(32);
        var xmlKey = Encoding.UTF8.GetBytes($"""
            <?xml version="1.0" encoding="utf-8"?>
            <KeyFile><Meta><Version>2.0</Version></Meta><Key><Data Hash="00000000">{Convert.ToHexString(raw)[..32]} {Convert.ToHexString(raw)[32..]}</Data></Key></KeyFile>
            """);
        Assert.Equal(Kdbx.KeyFileData(raw), Kdbx.KeyFileData(xmlKey));
        var doc = KeePassXml.FromEntries(Sample(), kdbx4: true);
        var withKey = Kdbx.Encrypt(doc, "pw", raw, new KdbxWriteOptions { Kdf = "aes", AesRounds = 10 });
        Assert.Throws<KdbxInvalidKeyException>(() => Kdbx.Decrypt(withKey, "pw", null));
        Assert.Equal(3, KeePassXml.ToEntries(Kdbx.Decrypt(withKey, "pw", xmlKey), "t").Entries.Count);
    }

    [Fact]
    public void PassKeeperEncrypted_RoundTrip()
    {
        var bytes = Exporter.Export(Sample(), ExportFormat.PassKeeperEncrypted, "export-pass");
        Assert.True(PassKeeperFormat.IsEncrypted(bytes));
        Assert.DoesNotContain("gh-secret", Encoding.UTF8.GetString(bytes));
        Assert.ThrowsAny<CryptographicException>(() => PassKeeperFormat.Decrypt(bytes, "nope", "x"));
        var r = PassKeeperFormat.Decrypt(bytes, "export-pass", "x");
        Assert.Equal(3, r.Entries.Count);
        Assert.Equal(["*Outlook*"], r.Entries[0].WindowPatterns);
    }

    [Fact]
    public void Csv_RecognizesThirdPartyLayouts()
    {
        var lastPass = CsvImporter.Import("url,username,password,totp,extra,name,grouping,fav\nhttps://a.com,u,p,,note,A,Social,1\nhttp://sn,,,,secret note,N,,0\n", "lp");
        Assert.Equal("Social", lastPass.Entries[0].Folder);
        Assert.True(lastPass.Entries[0].Favorite);
        Assert.Equal("", lastPass.Entries[1].Url);
        Assert.Equal("secret note", lastPass.Entries[1].Notes);

        var onePassword = CsvImporter.Import("Title,Url,Username,Password,OTPAuth,Favorite,Archived,Tags,Notes\nB,https://b.com,bu,bp,,true,false,\"x,y\",n\n", "1p");
        Assert.Equal(["x", "y"], onePassword.Entries[0].Tags);

        var keepassxc = CsvImporter.Import("\"Group\",\"Title\",\"Username\",\"Password\",\"URL\",\"Notes\"\n\"Root/Dev\",\"C\",\"cu\",\"cp\",\"c.com\",\"\"\n", "kx");
        Assert.Equal("Dev", keepassxc.Entries[0].Folder);

        var dashlane = CsvImporter.Import("username,username2,username3,title,password,note,url,category,otpSecret\nd@x.com,,,Dash,dp,,https://d.com,Work,\n", "dl");
        Assert.Equal("Work", dashlane.Entries[0].Folder);
        Assert.Equal("d@x.com", dashlane.Entries[0].Username);

        var proton = CsvImporter.Import("type,name,url,email,username,password,note,totp,createTime,modifyTime,vault\nlogin,P,https://p.me,e@p.me,,pp,,,1,2,Personal\n", "pp");
        Assert.Equal("e@p.me", proton.Entries[0].Username);
        Assert.Equal("Personal", proton.Entries[0].Folder);

        var nord = CsvImporter.Import("name,url,additional_urls,username,password,note,folder,type\nFolderRow,,,,,,,folder\nN,https://n.com,\"https://n2.com,https://n3.com\",nu,np,,F,password\n", "np");
        var n = Assert.Single(nord.Entries);
        Assert.Equal(["https://n2.com", "https://n3.com"], n.ExtraUrls);

        var keeper = CsvImporter.Import("Folder1,Site,user1,pass1,https://k.com,note1\n", "keeper");
        Assert.Equal("pass1", keeper.Entries[0].Password);
        Assert.Equal("Folder1", keeper.Entries[0].Folder);
    }

    [Fact]
    public void Csv_RussianHeaders_Semicolon_Cp1251()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding(1251).GetBytes("Название;Логин;Пароль;Сайт;Заметки\r\nБанк;клиент;пароль;bank.ru;\"a;b\"\r\n");
        var result = CsvImporter.Import(TextDecoding.Decode(bytes), "ru");
        var e = Assert.Single(result.Entries);
        Assert.Equal("Банк", e.Title);
        Assert.Equal("клиент", e.Username);
        Assert.Equal("a;b", e.Notes);
    }

    [Fact]
    public void Kaspersky_TextExport()
    {
        const string text = """
            Websites

            Website name: Example
            Website URL: https://example.com
            Login name:
            Login: kuser
            Password: kpass
            Comment: first line
            second line

            ---

            Applications

            Application: Skype
            Login: sk
            Password: skp
            Comment:

            ---
            """;
        Assert.True(KasperskyImporter.LooksLikeKaspersky(text));
        var r = KasperskyImporter.Import(text, "kpm");
        Assert.Equal(2, r.Entries.Count);
        Assert.Equal("kuser", r.Entries[0].Username);
        Assert.Equal("first line\nsecond line", r.Entries[0].Notes);
        Assert.Equal("Skype", r.Entries[1].Title);
    }

    [Fact]
    public void ImportPlanner_FlagsDuplicatesAndConflicts()
    {
        var existing = new List<VaultEntry>
        {
            new() { Title = "A", Url = "https://a.com", Username = "u", Password = "same" },
            new() { Title = "B", Url = "https://b.com", Username = "u", Password = "old" },
        };
        var incoming = new ImportResult { Source = "x" };
        incoming.Entries.Add(new VaultEntry { Url = "https://www.a.com/login", Username = "U", Password = "same" });
        incoming.Entries.Add(new VaultEntry { Url = "https://b.com", Username = "u", Password = "new" });
        incoming.Entries.Add(new VaultEntry { Url = "https://c.com", Username = "u", Password = "c" });
        incoming.Entries.Add(new VaultEntry { Url = "https://c.com", Username = "u", Password = "c" });
        var plan = ImportPlanner.Plan(existing, [incoming]);
        Assert.Equal([ImportStatus.Duplicate, ImportStatus.Conflict, ImportStatus.New, ImportStatus.Duplicate], plan.Select(p => p.Status));
        Assert.Equal([false, true, true, false], plan.Select(p => p.Selected));
    }
}
