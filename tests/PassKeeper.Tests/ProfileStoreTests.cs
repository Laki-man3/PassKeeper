using PassKeeper.Core.Storage;

namespace PassKeeper.Tests;

public class ProfileStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pk-profiles-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (DirectoryNotFoundException) { }
    }

    [Fact]
    public void LegacyVaultBecomesFirstProfile()
    {
        var legacy = new VaultService(_root);
        legacy.Create("Анна", "Correct-Horse-9");
        legacy.SetPin("482913");
        legacy.CreateBackup();
        legacy.Lock();
        File.WriteAllText(Path.Combine(_root, "settings.json"), "{}");

        var store = new ProfileStore(_root);
        var id = store.MigrateLegacy();
        Assert.NotNull(id);
        Assert.False(File.Exists(Path.Combine(_root, "vault.pkv")));
        Assert.True(File.Exists(Path.Combine(_root, "settings.json")));
        var profile = Assert.Single(store.List());
        Assert.Equal("Анна", profile.UserName);
        var vault = new VaultService(profile.Directory);
        Assert.True(vault.HasPin);
        Assert.True(Directory.Exists(vault.BackupDirectory));
        Assert.True(vault.UnlockWithMaster("Correct-Horse-9"));
        Assert.Null(store.MigrateLegacy());
    }

    [Fact]
    public void UsersAreFoundByNameAndDeletedWithTheirData()
    {
        var store = new ProfileStore(_root);
        foreach (var name in new[] { "Alex", "Анна" })
        {
            var id = store.NewId();
            Assert.False(Directory.Exists(store.DirectoryOf(id)));
            var vault = new VaultService(store.DirectoryOf(id));
            vault.Create(name, "Correct-Horse-9");
            vault.Lock();
        }
        Assert.Equal(2, store.List().Count);
        var anna = store.FindByName(" анна ");
        Assert.NotNull(anna);
        Assert.Null(store.FindByName("Boris"));
        store.Delete(anna!.Id);
        Assert.False(Directory.Exists(anna.Directory));
        Assert.Equal("Alex", Assert.Single(store.List()).UserName);
    }
}
