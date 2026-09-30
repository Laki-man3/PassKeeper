using System.Security.Cryptography;

namespace PassKeeper.Core.Storage;

/// <summary>A local user: a folder with its own vault, PIN and backups.</summary>
public sealed record ProfileInfo(string Id, string Directory, string UserName);

/// <summary>
/// Local users of one PassKeeper data folder: <c>profiles\&lt;id&gt;\</c> each holding vault.pkv, pin.dat and Backups.
/// The user name is read from the vault header, which is not encrypted, so the list is available before sign-in.
/// </summary>
public sealed class ProfileStore
{
    private static readonly string[] LegacyItems = ["vault.pkv", "vault.pkv.bak", "pin.dat", "Backups"];

    public ProfileStore(string dataDirectory)
    {
        DataDirectory = dataDirectory;
        ProfilesDirectory = Path.Combine(dataDirectory, "profiles");
    }

    public string DataDirectory { get; }
    public string ProfilesDirectory { get; }

    public string DirectoryOf(string id) => Path.Combine(ProfilesDirectory, id);

    public IReadOnlyList<ProfileInfo> List()
    {
        if (!Directory.Exists(ProfilesDirectory)) return [];
        var result = new List<ProfileInfo>();
        foreach (var dir in Directory.EnumerateDirectories(ProfilesDirectory))
        {
            var vault = new VaultService(dir);
            if (!vault.Exists) continue;
            string name;
            try { name = vault.UserName; }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { continue; }
            result.Add(new ProfileInfo(Path.GetFileName(dir), dir, name));
        }
        return result.OrderBy(p => p.UserName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public ProfileInfo? Find(string? id) =>
        string.IsNullOrEmpty(id) ? null : List().FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public ProfileInfo? FindByName(string userName)
    {
        var name = userName.Trim();
        return List().FirstOrDefault(p => p.UserName.Trim().Equals(name, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>Id of a new, not yet created profile (its folder appears when the vault is created).</summary>
    public string NewId()
    {
        string id;
        do id = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        while (Directory.Exists(DirectoryOf(id)));
        return id;
    }

    /// <summary>Removes the user's folder: vault, PIN and backups.</summary>
    public void Delete(string id)
    {
        var dir = DirectoryOf(id);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    /// <summary>
    /// Version 1.2 and older kept a single vault directly in the data folder: it becomes the first profile.
    /// Returns the new profile id, or null when there was nothing to move.
    /// </summary>
    public string? MigrateLegacy()
    {
        if (!File.Exists(Path.Combine(DataDirectory, "vault.pkv"))) return null;
        var id = NewId();
        var target = DirectoryOf(id);
        Directory.CreateDirectory(target);
        foreach (var item in LegacyItems)
        {
            var source = Path.Combine(DataDirectory, item);
            var destination = Path.Combine(target, item);
            if (File.Exists(source)) File.Move(source, destination);
            else if (Directory.Exists(source)) Directory.Move(source, destination);
        }
        return id;
    }
}
