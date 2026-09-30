using System.Security.Cryptography;
using PassKeeper.Core.Crypto;
using PassKeeper.Core.Models;

namespace PassKeeper.Core.Storage;

public enum PinUnlockResult
{
    Success,
    WrongPin,
    /// <summary>Too many wrong attempts: the PIN was destroyed, master password is required.</summary>
    LockedOut,
    NotConfigured,
}

/// <summary>Owns the vault file, the in-memory decrypted data and the unlock state.</summary>
public sealed class VaultService
{
    public const int MaxPinAttempts = 5;
    public const int MinMasterPasswordLength = 8;
    public const int MinPinLength = 4;
    public const int MaxPinLength = 12;
    private const int DailyBackupsToKeep = 14;

    private readonly object _io = new();
    private byte[]? _key;
    private VaultData? _data;

    /// <summary>The folder is created with the vault (a profile that is only being set up leaves nothing behind).</summary>
    public VaultService(string dataDirectory)
    {
        DataDirectory = dataDirectory;
    }

    public string DataDirectory { get; }
    public string VaultPath => Path.Combine(DataDirectory, "vault.pkv");
    public string PinPath => Path.Combine(DataDirectory, "pin.dat");
    public string BackupDirectory => Path.Combine(DataDirectory, "Backups");

    public bool Exists => File.Exists(VaultPath);
    public bool HasPin => File.Exists(PinPath);
    public bool IsUnlocked => _key != null;
    public VaultHeader? Header { get; private set; }
    public int PinAttemptsLeft { get; private set; } = MaxPinAttempts;

    public VaultData Data => _data ?? throw new InvalidOperationException("The vault is locked.");
    public IEnumerable<VaultEntry> ActiveEntries => Data.Entries.Where(e => !e.IsDeleted);

    /// <summary>Raised after lock/unlock (on the calling thread).</summary>
    public event EventHandler? StateChanged;
    /// <summary>Raised after every successful save.</summary>
    public event EventHandler? DataChanged;

    public string UserName => (Header ?? LoadHeader())?.UserName ?? "";

    public VaultHeader? LoadHeader()
    {
        if (!Exists) return null;
        lock (_io)
        {
            var bytes = File.ReadAllBytes(VaultPath);
            Header = VaultFile.ReadHeader(bytes, out _);
            return Header;
        }
    }

    public void Create(string userName, string masterPassword)
    {
        if (Exists) throw new InvalidOperationException("A vault already exists.");
        Directory.CreateDirectory(DataDirectory);
        if (masterPassword.Length < MinMasterPasswordLength)
            throw new ArgumentException("Master password is too short.", nameof(masterPassword));

        var header = new VaultHeader { UserName = userName.Trim(), Kdf = KdfParameters.CreateDefault() };
        var key = RandomNumberGenerator.GetBytes(Aead.KeySize);
        var kek = Kdf.Derive(masterPassword, header.Kdf);
        try
        {
            header.WrappedKey = Aead.Encrypt(kek, key, VaultFile.KeyWrapAad(header.VaultId));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }

        Header = header;
        _key = key;
        _data = new VaultData();
        Save();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <returns>false when the master password is wrong.</returns>
    public bool UnlockWithMaster(string masterPassword)
    {
        byte[] bytes;
        lock (_io) bytes = File.ReadAllBytes(VaultPath);
        var header = VaultFile.ReadHeader(bytes, out _);
        var key = TryUnwrapWithMaster(header, masterPassword);
        if (key == null) return false;
        var data = VaultFile.DecryptBody(bytes, key);
        SetUnlocked(header, key, data);
        return true;
    }

    public PinUnlockResult UnlockWithPin(string pin)
    {
        if (!HasPin) return PinUnlockResult.NotConfigured;
        byte[] bytes;
        lock (_io) bytes = File.ReadAllBytes(VaultPath);
        var header = VaultFile.ReadHeader(bytes, out _);

        PinRecord record;
        try
        {
            record = PinStore.Load(PinPath);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or System.Text.Json.JsonException)
        {
            PinStore.Delete(PinPath);
            return PinUnlockResult.NotConfigured;
        }

        if (record.VaultId != header.VaultId)
        {
            PinStore.Delete(PinPath);
            return PinUnlockResult.NotConfigured;
        }

        byte[] key;
        var kek = Kdf.Derive(pin, record.Kdf);
        try
        {
            key = Aead.Decrypt(kek, record.WrappedKey, VaultFile.PinWrapAad(header.VaultId));
        }
        catch (CryptographicException)
        {
            record.FailedAttempts++;
            PinAttemptsLeft = Math.Max(0, MaxPinAttempts - record.FailedAttempts);
            if (record.FailedAttempts >= MaxPinAttempts)
            {
                PinStore.Delete(PinPath);
                return PinUnlockResult.LockedOut;
            }
            PinStore.Save(PinPath, record);
            return PinUnlockResult.WrongPin;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }

        if (record.FailedAttempts != 0)
        {
            record.FailedAttempts = 0;
            PinStore.Save(PinPath, record);
        }
        PinAttemptsLeft = MaxPinAttempts;
        var data = VaultFile.DecryptBody(bytes, key);
        SetUnlocked(header, key, data);
        return PinUnlockResult.Success;
    }

    public void SetPin(string pin)
    {
        RequireUnlocked();
        if (!IsValidPin(pin)) throw new ArgumentException("Invalid PIN.", nameof(pin));
        var record = new PinRecord { VaultId = Header!.VaultId, Kdf = KdfParameters.CreateDefault() };
        var kek = Kdf.Derive(pin, record.Kdf);
        try
        {
            record.WrappedKey = Aead.Encrypt(kek, _key!, VaultFile.PinWrapAad(record.VaultId));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }
        PinStore.Save(PinPath, record);
        PinAttemptsLeft = MaxPinAttempts;
    }

    public void RemovePin() => PinStore.Delete(PinPath);

    public static bool IsValidPin(string pin) =>
        pin.Length is >= MinPinLength and <= MaxPinLength && pin.All(char.IsAsciiDigit);

    public bool VerifyMaster(string masterPassword)
    {
        var header = Header ?? LoadHeader() ?? throw new InvalidOperationException("No vault.");
        var key = TryUnwrapWithMaster(header, masterPassword);
        if (key == null) return false;
        CryptographicOperations.ZeroMemory(key);
        return true;
    }

    public bool ChangeMaster(string currentPassword, string newPassword)
    {
        RequireUnlocked();
        if (newPassword.Length < MinMasterPasswordLength)
            throw new ArgumentException("Master password is too short.", nameof(newPassword));
        if (!VerifyMaster(currentPassword)) return false;

        var kdf = KdfParameters.CreateDefault();
        var kek = Kdf.Derive(newPassword, kdf);
        try
        {
            Header!.Kdf = kdf;
            Header.WrappedKey = Aead.Encrypt(kek, _key!, VaultFile.KeyWrapAad(Header.VaultId));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }
        Save();
        return true;
    }

    public void Lock()
    {
        if (_key == null) return;
        CryptographicOperations.ZeroMemory(_key);
        _key = null;
        _data = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Save()
    {
        RequireUnlocked();
        lock (_io)
        {
            var bytes = VaultFile.Serialize(Header!, _key!, _data!);
            MakeDailyBackup();
            FileUtil.WriteAtomic(VaultPath, bytes, VaultPath + ".bak");
        }
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- entry operations -------------------------------------------------------------------------------------

    public VaultEntry? Find(Guid id) => Data.Entries.FirstOrDefault(e => e.Id == id);

    public void Upsert(VaultEntry entry)
    {
        RequireUnlocked();
        entry.ModifiedUtc = DateTime.UtcNow;
        var index = Data.Entries.FindIndex(e => e.Id == entry.Id);
        if (index >= 0) Data.Entries[index] = entry;
        else Data.Entries.Add(entry);
        Save();
    }

    public void AddRange(IEnumerable<VaultEntry> entries)
    {
        RequireUnlocked();
        Data.Entries.AddRange(entries);
        Save();
    }

    public void MoveToTrash(Guid id) => Mutate(id, e => e.DeletedUtc = DateTime.UtcNow);
    public void Restore(Guid id) => Mutate(id, e => e.DeletedUtc = null);
    public void ToggleFavorite(Guid id) => Mutate(id, e => e.Favorite = !e.Favorite);
    public void MarkUsed(Guid id) => Mutate(id, e => e.LastUsedUtc = DateTime.UtcNow, touch: false);

    public void DeletePermanently(Guid id)
    {
        RequireUnlocked();
        Data.Entries.RemoveAll(e => e.Id == id);
        Save();
    }

    public int EmptyTrash()
    {
        RequireUnlocked();
        var n = Data.Entries.RemoveAll(e => e.IsDeleted);
        if (n > 0) Save();
        return n;
    }

    public IReadOnlyList<string> Folders() =>
        ActiveEntries.Select(e => e.Folder.Trim())
            .Where(f => f.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>Copies the current vault file into the backup folder.</summary>
    public string CreateBackup(string? targetPath = null)
    {
        lock (_io)
        {
            Directory.CreateDirectory(BackupDirectory);
            targetPath ??= Path.Combine(BackupDirectory, $"vault-{DateTime.Now:yyyyMMdd-HHmmss}.pkv");
            File.Copy(VaultPath, targetPath, overwrite: true);
            return targetPath;
        }
    }

    private void Mutate(Guid id, Action<VaultEntry> change, bool touch = true)
    {
        RequireUnlocked();
        var entry = Find(id);
        if (entry == null) return;
        change(entry);
        if (touch) entry.ModifiedUtc = DateTime.UtcNow;
        Save();
    }

    private void MakeDailyBackup()
    {
        if (!File.Exists(VaultPath)) return;
        try
        {
            Directory.CreateDirectory(BackupDirectory);
            var today = Path.Combine(BackupDirectory, $"vault-{DateTime.Now:yyyyMMdd}.pkv");
            if (File.Exists(today)) return;
            File.Copy(VaultPath, today);
            var old = new DirectoryInfo(BackupDirectory).GetFiles("vault-????????.pkv")
                .OrderByDescending(f => f.Name).Skip(DailyBackupsToKeep);
            foreach (var f in old) f.Delete();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static byte[]? TryUnwrapWithMaster(VaultHeader header, string masterPassword)
    {
        var kek = Kdf.Derive(masterPassword, header.Kdf);
        try
        {
            return Aead.Decrypt(kek, header.WrappedKey, VaultFile.KeyWrapAad(header.VaultId));
        }
        catch (CryptographicException)
        {
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }
    }

    private void SetUnlocked(VaultHeader header, byte[] key, VaultData data)
    {
        if (_key != null) CryptographicOperations.ZeroMemory(_key);
        Header = header;
        _key = key;
        _data = data;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RequireUnlocked()
    {
        if (_key == null || _data == null || Header == null)
            throw new InvalidOperationException("The vault is locked.");
    }
}
