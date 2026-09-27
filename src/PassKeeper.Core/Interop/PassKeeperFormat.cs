using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using PassKeeper.Core.Crypto;
using PassKeeper.Core.Models;

namespace PassKeeper.Core.Interop;

public sealed class PassKeeperExport
{
    public string Format { get; set; } = "PassKeeper";
    public int Version { get; set; } = 1;
    public DateTime ExportedUtc { get; set; } = DateTime.UtcNow;
    public List<VaultEntry> Entries { get; set; } = [];
}

/// <summary>
/// PassKeeper native exchange formats: plain JSON and the encrypted backup (.pkx):
/// "PKEXPORT" | u16 version | u32 header length | header JSON {kdf} | AES-256-GCM(JSON), AAD = preceding bytes.
/// </summary>
public static class PassKeeperFormat
{
    private static readonly byte[] Magic = "PKEXPORT"u8.ToArray();

    private sealed class Header
    {
        public KdfParameters Kdf { get; set; } = new();
        public DateTime CreatedUtc { get; set; }
    }

    public static string ToJson(IEnumerable<VaultEntry> entries) =>
        JsonSerializer.Serialize(new PassKeeperExport { Entries = entries.Where(e => !e.IsDeleted).ToList() }, VaultJson.Indented);

    public static bool IsJson(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("format", out var f) &&
        f.ValueKind == JsonValueKind.String && f.GetString() == "PassKeeper";

    public static ImportResult FromJson(string json, string source)
    {
        var export = JsonSerializer.Deserialize<PassKeeperExport>(json, VaultJson.Options) ?? new PassKeeperExport();
        var result = new ImportResult { Source = source };
        foreach (var e in export.Entries)
        {
            e.Id = Guid.NewGuid();
            e.DeletedUtc = null;
            result.Entries.Add(e);
        }
        return result;
    }

    public static bool IsEncrypted(ReadOnlySpan<byte> data) => data.Length > 14 && data[..8].SequenceEqual(Magic);

    public static byte[] Encrypt(IEnumerable<VaultEntry> entries, string password)
    {
        var header = new Header { Kdf = KdfParameters.CreateDefault(), CreatedUtc = DateTime.UtcNow };
        var headerBytes = JsonSerializer.SerializeToUtf8Bytes(header, VaultJson.Options);
        var prefix = new byte[14 + headerBytes.Length];
        Magic.CopyTo(prefix, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(prefix.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(prefix.AsSpan(10), (uint)headerBytes.Length);
        headerBytes.CopyTo(prefix, 14);

        var key = Kdf.Derive(password, header.Kdf);
        var plain = JsonSerializer.SerializeToUtf8Bytes(new PassKeeperExport { Entries = entries.Where(e => !e.IsDeleted).ToList() }, VaultJson.Options);
        try
        {
            return [.. prefix, .. Aead.Encrypt(key, plain, prefix)];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <exception cref="CryptographicException">Wrong password.</exception>
    public static ImportResult Decrypt(byte[] data, string password, string source)
    {
        if (!IsEncrypted(data)) throw new InvalidDataException("Not a PassKeeper backup.");
        var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(10));
        if (length <= 0 || 14 + length > data.Length) throw new InvalidDataException("Corrupted PassKeeper backup.");
        var header = JsonSerializer.Deserialize<Header>(data.AsSpan(14, length), VaultJson.Options)
                     ?? throw new InvalidDataException("Corrupted PassKeeper backup.");
        header.Kdf.Validate();
        var key = Kdf.Derive(password, header.Kdf);
        try
        {
            var plain = Aead.Decrypt(key, data.AsSpan(14 + length), data.AsSpan(0, 14 + length));
            try
            {
                return FromJson(System.Text.Encoding.UTF8.GetString(plain), source);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }
}
