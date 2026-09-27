using System.Security.Cryptography;
using System.Text.Json;
using PassKeeper.Core.Crypto;
using PassKeeper.Core.Models;

namespace PassKeeper.Core.Storage;

/// <summary>
/// PIN quick-unlock data. The vault key is wrapped with Argon2id(PIN) and the whole record is additionally
/// sealed with Windows DPAPI (current user), so a copied pin file cannot be brute-forced on another
/// machine/account. After <see cref="VaultService.MaxPinAttempts"/> failures the file is destroyed and the
/// master password becomes mandatory.
/// </summary>
internal sealed class PinRecord
{
    public int Version { get; set; } = 1;
    public Guid VaultId { get; set; }
    public KdfParameters Kdf { get; set; } = new();
    public byte[] WrappedKey { get; set; } = [];
    public int FailedAttempts { get; set; }
}

internal static class PinStore
{
    private static readonly byte[] Entropy = "PassKeeper.PinStore.v1"u8.ToArray();

    public static PinRecord Load(string path)
    {
        var sealedBytes = File.ReadAllBytes(path);
        var json = ProtectedData.Unprotect(sealedBytes, Entropy, DataProtectionScope.CurrentUser);
        try
        {
            return JsonSerializer.Deserialize<PinRecord>(json, VaultJson.Options)
                   ?? throw new CryptographicException("PIN record is empty.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(json);
        }
    }

    public static void Save(string path, PinRecord record)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(record, VaultJson.Options);
        try
        {
            var sealedBytes = ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser);
            FileUtil.WriteAtomic(path, sealedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(json);
        }
    }

    public static void Delete(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            // Overwrite before deleting so the old record does not linger in free clusters.
            var len = (int)Math.Min(new FileInfo(path).Length, 1 << 16);
            File.WriteAllBytes(path, RandomNumberGenerator.GetBytes(len));
            File.Delete(path);
        }
        catch (IOException) { File.Delete(path); }
    }
}
