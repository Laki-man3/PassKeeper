using System.Security.Cryptography;
using PassKeeper.Core.Models;
using PassKeeper.Core.Storage;

namespace PassKeeper.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pk-test-" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public string File(string name) => System.IO.Path.Combine(Path, name);
    public void Dispose()
    {
        try { Directory.Delete(Path, true); } catch (IOException) { }
    }
}

public class VaultTests
{
    [Fact]
    public void Create_Lock_UnlockWithMaster()
    {
        using var dir = new TempDir();
        var vault = new VaultService(dir.Path);
        vault.Create("Иван", "Correct-Horse-9");
        vault.Upsert(new VaultEntry { Title = "Mail", Username = "ivan", Password = "p@ss", Url = "https://mail.example.com" });
        vault.Lock();
        Assert.False(vault.IsUnlocked);
        Assert.Throws<InvalidOperationException>(() => vault.Data);

        var reopened = new VaultService(dir.Path);
        Assert.Equal("Иван", reopened.UserName);
        Assert.False(reopened.UnlockWithMaster("wrong-password"));
        Assert.True(reopened.UnlockWithMaster("Correct-Horse-9"));
        var e = Assert.Single(reopened.Data.Entries);
        Assert.Equal("p@ss", e.Password);
    }

    [Fact]
    public void VaultFile_DoesNotContainPlaintext_AndDetectsTampering()
    {
        using var dir = new TempDir();
        var vault = new VaultService(dir.Path);
        vault.Create("user", "Correct-Horse-9");
        vault.Upsert(new VaultEntry { Title = "UniqueTitle123", Password = "UniqueSecret456" });
        var bytes = File.ReadAllBytes(vault.VaultPath);
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("UniqueTitle123", text);
        Assert.DoesNotContain("UniqueSecret456", text);

        // Flip a byte in the encrypted body.
        bytes[^20] ^= 0x55;
        File.WriteAllBytes(vault.VaultPath, bytes);
        var other = new VaultService(dir.Path);
        Assert.ThrowsAny<CryptographicException>(() => other.UnlockWithMaster("Correct-Horse-9"));

        // Tampering with the authenticated header (user name) is detected too.
        File.Copy(vault.VaultPath + ".bak", vault.VaultPath, true);
        bytes = File.ReadAllBytes(vault.VaultPath);
        var idx = System.Text.Encoding.UTF8.GetString(bytes).IndexOf("\"user\"", StringComparison.Ordinal);
        bytes[idx + 1] = (byte)'x';
        File.WriteAllBytes(vault.VaultPath, bytes);
        Assert.ThrowsAny<CryptographicException>(() => new VaultService(dir.Path).UnlockWithMaster("Correct-Horse-9"));
    }

    [Fact]
    public void Pin_UnlocksAndLocksOutAfterFiveFailures()
    {
        using var dir = new TempDir();
        var vault = new VaultService(dir.Path);
        vault.Create("user", "Correct-Horse-9");
        vault.SetPin("2468");
        vault.Lock();

        Assert.Equal(PinUnlockResult.Success, vault.UnlockWithPin("2468"));
        vault.Lock();

        for (var i = 1; i < VaultService.MaxPinAttempts; i++)
        {
            Assert.Equal(PinUnlockResult.WrongPin, vault.UnlockWithPin("0000"));
            Assert.Equal(VaultService.MaxPinAttempts - i, vault.PinAttemptsLeft);
        }
        Assert.Equal(PinUnlockResult.LockedOut, vault.UnlockWithPin("0000"));
        Assert.False(vault.HasPin);
        Assert.Equal(PinUnlockResult.NotConfigured, vault.UnlockWithPin("2468"));
        Assert.True(vault.UnlockWithMaster("Correct-Horse-9"));
    }

    [Fact]
    public void Pin_SuccessResetsFailureCounter_AndSurvivesMasterChange()
    {
        using var dir = new TempDir();
        var vault = new VaultService(dir.Path);
        vault.Create("user", "Correct-Horse-9");
        vault.SetPin("13579");
        vault.Lock();
        Assert.Equal(PinUnlockResult.WrongPin, vault.UnlockWithPin("11111"));
        Assert.Equal(PinUnlockResult.Success, vault.UnlockWithPin("13579"));
        Assert.Equal(VaultService.MaxPinAttempts, vault.PinAttemptsLeft);

        Assert.False(vault.ChangeMaster("bad", "New-Master-77"));
        Assert.True(vault.ChangeMaster("Correct-Horse-9", "New-Master-77"));
        vault.Lock();
        Assert.Equal(PinUnlockResult.Success, vault.UnlockWithPin("13579"));
        vault.Lock();
        Assert.False(vault.UnlockWithMaster("Correct-Horse-9"));
        Assert.True(vault.UnlockWithMaster("New-Master-77"));
    }

    [Fact]
    public void PinFile_IsBoundToVault()
    {
        using var a = new TempDir();
        using var b = new TempDir();
        var va = new VaultService(a.Path);
        va.Create("a", "Correct-Horse-9");
        va.SetPin("1234");
        var vb = new VaultService(b.Path);
        vb.Create("b", "Correct-Horse-9");
        vb.Lock();
        File.Copy(va.PinPath, vb.PinPath, true);
        Assert.Equal(PinUnlockResult.NotConfigured, vb.UnlockWithPin("1234"));
    }

    [Fact]
    public void Trash_Restore_And_Purge()
    {
        using var dir = new TempDir();
        var vault = new VaultService(dir.Path);
        vault.Create("user", "Correct-Horse-9");
        var e = new VaultEntry { Title = "X", Password = "1", Folder = "Work/Mail" };
        vault.Upsert(e);
        Assert.Equal(["Work/Mail"], vault.Folders());
        vault.MoveToTrash(e.Id);
        Assert.Empty(vault.ActiveEntries);
        vault.Restore(e.Id);
        Assert.Single(vault.ActiveEntries);
        vault.MoveToTrash(e.Id);
        Assert.Equal(1, vault.EmptyTrash());
        Assert.Empty(vault.Data.Entries);
    }

    [Fact]
    public void PasswordHistory_IsKept()
    {
        var e = new VaultEntry { Password = "a" };
        e.SetPassword("b");
        e.SetPassword("c");
        Assert.Equal("c", e.Password);
        Assert.Equal(["b", "a"], e.PasswordHistory.Select(h => h.Password));
    }
}
