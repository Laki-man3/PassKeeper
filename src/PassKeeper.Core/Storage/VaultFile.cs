using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PassKeeper.Core.Crypto;
using PassKeeper.Core.Models;

namespace PassKeeper.Core.Storage;

/// <summary>Plain-text (but authenticated) header of the vault file.</summary>
public sealed class VaultHeader
{
    public int FormatVersion { get; set; } = 1;
    public Guid VaultId { get; set; } = Guid.NewGuid();
    public string UserName { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    /// <summary>KDF used to turn the master password into the key-encryption key.</summary>
    public KdfParameters Kdf { get; set; } = new();
    /// <summary>Random 256-bit vault key encrypted with the master key (AES-256-GCM).</summary>
    public byte[] WrappedKey { get; set; } = [];
}

public sealed class VaultFormatException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Binary vault layout:
/// <code>
/// "PKVAULT\0" (8) | format version u16 LE | header length u32 LE | header JSON (UTF-8) | body
/// body = nonce(12) || AES-256-GCM(vault key, JSON(VaultData)) || tag(16),
/// AAD(body) = every byte that precedes the body (magic, version, header).
/// </code>
/// </summary>
public static class VaultFile
{
    private static readonly byte[] Magic = "PKVAULT\0"u8.ToArray();
    private const ushort FormatVersion = 1;
    private const int MaxHeaderLength = 1 << 20;
    private const int PrefixLength = 8 + 2 + 4;

    public static byte[] KeyWrapAad(Guid vaultId) => Concat("PassKeeper.KeyWrap.v1"u8, vaultId.ToByteArray());
    public static byte[] PinWrapAad(Guid vaultId) => Concat("PassKeeper.PinWrap.v1"u8, vaultId.ToByteArray());

    public static byte[] Serialize(VaultHeader header, byte[] key, VaultData data)
    {
        var headerBytes = JsonSerializer.SerializeToUtf8Bytes(header, VaultJson.Options);
        var prefix = new byte[PrefixLength + headerBytes.Length];
        Magic.CopyTo(prefix, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(prefix.AsSpan(8), FormatVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(prefix.AsSpan(10), (uint)headerBytes.Length);
        headerBytes.CopyTo(prefix, PrefixLength);

        var plain = JsonSerializer.SerializeToUtf8Bytes(data, VaultJson.Options);
        try
        {
            var body = Aead.Encrypt(key, plain, prefix);
            var result = new byte[prefix.Length + body.Length];
            prefix.CopyTo(result, 0);
            body.CopyTo(result, prefix.Length);
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public static VaultHeader ReadHeader(ReadOnlySpan<byte> file, out int bodyOffset)
    {
        if (file.Length < PrefixLength || !file[..8].SequenceEqual(Magic))
            throw new VaultFormatException("Not a PassKeeper vault file.");
        var version = BinaryPrimitives.ReadUInt16LittleEndian(file[8..]);
        if (version != FormatVersion)
            throw new VaultFormatException($"Unsupported vault format version {version}.");
        var headerLength = BinaryPrimitives.ReadUInt32LittleEndian(file[10..]);
        if (headerLength > MaxHeaderLength || PrefixLength + headerLength > file.Length)
            throw new VaultFormatException("Vault header is corrupted.");
        bodyOffset = PrefixLength + (int)headerLength;
        try
        {
            var header = JsonSerializer.Deserialize<VaultHeader>(file[PrefixLength..bodyOffset], VaultJson.Options)
                         ?? throw new VaultFormatException("Vault header is empty.");
            header.Kdf.Validate();
            return header;
        }
        catch (JsonException ex)
        {
            throw new VaultFormatException("Vault header is corrupted.", ex);
        }
    }

    /// <exception cref="CryptographicException">Wrong key or the file was modified.</exception>
    public static VaultData DecryptBody(ReadOnlySpan<byte> file, byte[] key)
    {
        ReadHeader(file, out var bodyOffset);
        var plain = Aead.Decrypt(key, file[bodyOffset..], file[..bodyOffset]);
        try
        {
            return JsonSerializer.Deserialize<VaultData>(plain, VaultJson.Options)
                   ?? throw new VaultFormatException("Vault body is empty.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static byte[] Concat(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        var r = new byte[a.Length + b.Length];
        a.CopyTo(r);
        b.CopyTo(r.AsSpan(a.Length));
        return r;
    }

    internal static string Describe(VaultHeader h) =>
        Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(h, VaultJson.Indented));
}
