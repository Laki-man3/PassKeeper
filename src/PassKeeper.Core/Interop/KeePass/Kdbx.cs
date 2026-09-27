using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using PassKeeper.Core.Crypto;

namespace PassKeeper.Core.Interop.KeePass;

public class KdbxException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class KdbxInvalidKeyException() : KdbxException("The password or key file is wrong.");

public sealed class KdbxWriteOptions
{
    /// <summary>"argon2d", "argon2id" or "aes".</summary>
    public string Kdf { get; set; } = "argon2d";
    public int Argon2MemoryKiB { get; set; } = 65536;
    public int Argon2Iterations { get; set; } = 3;
    public int Argon2Parallelism { get; set; } = 2;
    public ulong AesRounds { get; set; } = 600_000;
}

/// <summary>KeePass 2.x database format (KDBX 3.1 and 4.x). Read and write, password and/or key file.</summary>
public static class Kdbx
{
    private const uint Signature1 = 0x9AA2D903;
    private const uint Signature2 = 0xB54BFB67;
    private const uint Signature2Kdb = 0xB54BFB65;

    private static readonly byte[] CipherAes256 = Convert.FromHexString("31C1F2E6BF714350BE5805216AFC5AFF");
    private static readonly byte[] CipherChaCha20 = Convert.FromHexString("D6038A2B8B6F4CB5A524339A31DBB59A");
    private static readonly byte[] CipherTwofish = Convert.FromHexString("AD68F29F576F4BB9A36AD47AF965346C");
    private static readonly byte[] KdfAes = Convert.FromHexString("C9D9F39A628A4460BF740D08C18A4FEA");
    private static readonly byte[] KdfAesKdbx4 = Convert.FromHexString("7C02BB8279A74AC0927D114A00648238");
    private static readonly byte[] KdfArgon2d = Convert.FromHexString("EF636DDF8C29444B91F7A9A403E30A0C");
    private static readonly byte[] KdfArgon2id = Convert.FromHexString("9E298B1956DB4773B23DFC3EC6F0A1E6");
    private static readonly byte[] Salsa20Nonce = [0xE8, 0x30, 0x09, 0x4B, 0x97, 0x20, 0x5D, 0x2A];

    private enum H : byte
    {
        End = 0, Comment = 1, CipherId = 2, Compression = 3, MasterSeed = 4, TransformSeed = 5, TransformRounds = 6,
        EncryptionIv = 7, ProtectedStreamKey = 8, StreamStartBytes = 9, InnerRandomStreamId = 10, KdfParameters = 11,
        PublicCustomData = 12,
    }

    public static bool IsKdbx(ReadOnlySpan<byte> data) =>
        data.Length >= 12 && BinaryPrimitives.ReadUInt32LittleEndian(data) == Signature1 &&
        BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) is Signature2 or Signature2Kdb;

    // ------------------------------------------------------------------------------------------------ reading

    public static XDocument Decrypt(byte[] file, string? password, byte[]? keyFile)
    {
        using var ms = new MemoryStream(file, writable: false);
        using var r = new BinaryReader(ms);
        try
        {
            if (r.ReadUInt32() != Signature1) throw new KdbxException("Not a KeePass database.");
            var sig2 = r.ReadUInt32();
            if (sig2 == Signature2Kdb) throw new KdbxException("KeePass 1.x (.kdb) databases are not supported. Convert the file with KeePass 2.");
            if (sig2 != Signature2) throw new KdbxException("Not a KeePass database.");
            var version = r.ReadUInt32();
            var major = (int)(version >> 16);
            if (major is < 3 or > 4) throw new KdbxException($"Unsupported KDBX version {major}.{version & 0xFFFF}.");

            var fields = new Dictionary<H, byte[]>();
            while (true)
            {
                var id = (H)r.ReadByte();
                var size = major >= 4 ? r.ReadInt32() : r.ReadUInt16();
                if (size < 0) throw new KdbxException("Corrupted header.");
                var data = r.ReadBytes(size);
                if (data.Length != size) throw new KdbxException("Truncated header.");
                if (id == H.End) break;
                fields[id] = data;
            }
            var headerLength = (int)ms.Position;
            var headerBytes = file.AsSpan(0, headerLength);

            var cipher = Required(fields, H.CipherId);
            if (cipher.AsSpan().SequenceEqual(CipherTwofish))
                throw new KdbxException("Twofish-encrypted databases are not supported. Change the cipher to AES or ChaCha20 in KeePass.");
            if (!cipher.AsSpan().SequenceEqual(CipherAes256) && !cipher.AsSpan().SequenceEqual(CipherChaCha20))
                throw new KdbxException("Unknown database cipher.");
            var compressed = fields.TryGetValue(H.Compression, out var comp) && BinaryPrimitives.ReadUInt32LittleEndian(comp) == 1;
            var masterSeed = Required(fields, H.MasterSeed);
            var iv = Required(fields, H.EncryptionIv);
            var composite = CompositeKey(password, keyFile);

            byte[] xml;
            IKeystream? inner;
            if (major >= 4)
            {
                var storedHash = r.ReadBytes(32);
                if (!SHA256.HashData(headerBytes).AsSpan().SequenceEqual(storedHash))
                    throw new KdbxException("The database header is corrupted.");
                var storedHmac = r.ReadBytes(32);

                var kdf = VariantDictionary.Read(Required(fields, H.KdfParameters));
                var transformed = TransformKey(composite, kdf);
                var masterKey = SHA256.HashData([.. masterSeed, .. transformed]);
                var hmacBase = SHA512.HashData([.. masterSeed, .. transformed, 0x01]);

                var headerHmac = HMACSHA256.HashData(BlockHmacKey(ulong.MaxValue, hmacBase), headerBytes);
                if (!CryptographicOperations.FixedTimeEquals(headerHmac, storedHmac)) throw new KdbxInvalidKeyException();

                var payload = ReadHmacBlocks(r, hmacBase);
                var decrypted = DecryptPayload(cipher, masterKey, iv, payload);
                var content = compressed ? Gunzip(decrypted) : decrypted;
                (inner, xml) = ReadInnerHeader(content);
            }
            else
            {
                var transformSeed = Required(fields, H.TransformSeed);
                var rounds = BinaryPrimitives.ReadUInt64LittleEndian(Required(fields, H.TransformRounds));
                var transformed = AesKdf(composite, transformSeed, rounds);
                var masterKey = SHA256.HashData([.. masterSeed, .. transformed]);

                var encrypted = file.AsSpan(headerLength).ToArray();
                byte[] decrypted;
                try
                {
                    decrypted = DecryptPayload(cipher, masterKey, iv, encrypted);
                }
                catch (CryptographicException)
                {
                    throw new KdbxInvalidKeyException();
                }
                var startBytes = Required(fields, H.StreamStartBytes);
                if (decrypted.Length < startBytes.Length || !decrypted.AsSpan(0, startBytes.Length).SequenceEqual(startBytes))
                    throw new KdbxInvalidKeyException();
                var blocks = ReadHashedBlocks(decrypted.AsSpan(startBytes.Length));
                xml = compressed ? Gunzip(blocks) : blocks;
                var streamId = fields.TryGetValue(H.InnerRandomStreamId, out var sid) ? BinaryPrimitives.ReadUInt32LittleEndian(sid) : 0;
                inner = CreateInnerStream(streamId, fields.GetValueOrDefault(H.ProtectedStreamKey) ?? []);
            }

            var doc = ParseXml(xml);
            if (inner != null) ApplyProtection(doc, inner, decrypt: true);
            return doc;
        }
        catch (EndOfStreamException ex)
        {
            throw new KdbxException("The database file is truncated.", ex);
        }
    }

    private static byte[] Required(Dictionary<H, byte[]> fields, H id) =>
        fields.TryGetValue(id, out var v) ? v : throw new KdbxException($"Header field {id} is missing.");

    private static byte[] ReadHmacBlocks(BinaryReader r, byte[] hmacBase)
    {
        using var payload = new MemoryStream();
        for (ulong index = 0; ; index++)
        {
            var storedHmac = r.ReadBytes(32);
            var size = r.ReadInt32();
            if (size < 0) throw new KdbxException("Corrupted data block.");
            var data = r.ReadBytes(size);
            if (data.Length != size || storedHmac.Length != 32) throw new KdbxException("The database file is truncated.");

            var msg = new byte[12 + size];
            BinaryPrimitives.WriteUInt64LittleEndian(msg, index);
            BinaryPrimitives.WriteInt32LittleEndian(msg.AsSpan(8), size);
            data.CopyTo(msg, 12);
            var computed = HMACSHA256.HashData(BlockHmacKey(index, hmacBase), msg);
            if (!CryptographicOperations.FixedTimeEquals(computed, storedHmac))
                throw new KdbxException("The database is corrupted (block authentication failed).");
            if (size == 0) break;
            payload.Write(data);
        }
        return payload.ToArray();
    }

    private static byte[] ReadHashedBlocks(ReadOnlySpan<byte> data)
    {
        using var output = new MemoryStream();
        var pos = 0;
        while (true)
        {
            if (pos + 40 > data.Length) throw new KdbxException("The database file is truncated.");
            pos += 4; // block index
            var hash = data.Slice(pos, 32);
            pos += 32;
            var size = BinaryPrimitives.ReadInt32LittleEndian(data[pos..]);
            pos += 4;
            if (size == 0) break;
            if (size < 0 || pos + size > data.Length) throw new KdbxException("Corrupted data block.");
            var block = data.Slice(pos, size);
            if (!SHA256.HashData(block).AsSpan().SequenceEqual(hash))
                throw new KdbxException("The database is corrupted (block hash mismatch).");
            output.Write(block);
            pos += size;
        }
        return output.ToArray();
    }

    private static (IKeystream?, byte[]) ReadInnerHeader(byte[] content)
    {
        var pos = 0;
        uint streamId = 0;
        byte[] key = [];
        while (true)
        {
            if (pos + 5 > content.Length) throw new KdbxException("Corrupted inner header.");
            var id = content[pos];
            var size = BinaryPrimitives.ReadInt32LittleEndian(content.AsSpan(pos + 1));
            pos += 5;
            if (size < 0 || pos + size > content.Length) throw new KdbxException("Corrupted inner header.");
            var data = content.AsSpan(pos, size);
            pos += size;
            if (id == 0) break;
            if (id == 1) streamId = BinaryPrimitives.ReadUInt32LittleEndian(data);
            else if (id == 2) key = data.ToArray();
            // id 3 = attachments: not imported.
        }
        return (CreateInnerStream(streamId, key), content[pos..]);
    }

    private static IKeystream? CreateInnerStream(uint id, byte[] key) => id switch
    {
        0 => null,
        2 => new Salsa20(SHA256.HashData(key), Salsa20Nonce),
        3 => CreateChaChaInner(key),
        _ => throw new KdbxException("Unsupported inner stream cipher."),
    };

    private static ChaCha20 CreateChaChaInner(byte[] key)
    {
        var h = SHA512.HashData(key);
        return new ChaCha20(h.AsSpan(0, 32), h.AsSpan(32, 12));
    }

    private static byte[] DecryptPayload(byte[] cipher, byte[] key, byte[] iv, byte[] data)
    {
        if (cipher.AsSpan().SequenceEqual(CipherChaCha20))
        {
            var copy = (byte[])data.Clone();
            new ChaCha20(key, iv).Xor(copy);
            return copy;
        }
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.DecryptCbc(data, iv, PaddingMode.PKCS7);
    }

    private static byte[] TransformKey(byte[] composite, Dictionary<string, object> kdf)
    {
        if (kdf.GetValueOrDefault("$UUID") is not byte[] uuid) throw new KdbxException("KDF is not specified.");
        if (uuid.AsSpan().SequenceEqual(KdfAes) || uuid.AsSpan().SequenceEqual(KdfAesKdbx4))
        {
            var seed = kdf.GetValueOrDefault("S") as byte[] ?? throw new KdbxException("AES-KDF seed missing.");
            var rounds = Convert.ToUInt64(kdf.GetValueOrDefault("R") ?? 0UL);
            return AesKdf(composite, seed, rounds);
        }
        var isD = uuid.AsSpan().SequenceEqual(KdfArgon2d);
        if (!isD && !uuid.AsSpan().SequenceEqual(KdfArgon2id)) throw new KdbxException("Unknown KDF.");

        var salt = kdf.GetValueOrDefault("S") as byte[] ?? throw new KdbxException("Argon2 salt missing.");
        var parallelism = Convert.ToInt32(kdf.GetValueOrDefault("P") ?? 1u);
        var memoryBytes = Convert.ToUInt64(kdf.GetValueOrDefault("M") ?? 0UL);
        var iterations = Convert.ToUInt64(kdf.GetValueOrDefault("I") ?? 0UL);
        var argonVersion = Convert.ToUInt32(kdf.GetValueOrDefault("V") ?? 0x13u);
        if (argonVersion != 0x13) throw new KdbxException("Only Argon2 version 1.3 is supported.");
        if (memoryBytes is < 8 * 1024 or > 4UL * 1024 * 1024 * 1024 || iterations is 0 or > 100_000 || parallelism is < 1 or > 256)
            throw new KdbxException("Argon2 parameters are out of range.");
        return Kdf.Argon2(composite, isD ? "argon2d" : "argon2id", salt, (int)(memoryBytes / 1024), (int)iterations,
            parallelism, 32, kdf.GetValueOrDefault("K") as byte[], kdf.GetValueOrDefault("A") as byte[]);
    }

    internal static byte[] AesKdf(byte[] composite, byte[] seed, ulong rounds)
    {
        using var aes = Aes.Create();
        aes.Key = seed;
        var a = (byte[])composite.Clone();
        var b = new byte[32];
        for (ulong i = 0; i < rounds; i++)
        {
            aes.EncryptEcb(a, b, PaddingMode.None);
            (a, b) = (b, a);
        }
        return SHA256.HashData(a);
    }

    private static byte[] BlockHmacKey(ulong index, byte[] hmacBase)
    {
        var buf = new byte[8 + 64];
        BinaryPrimitives.WriteUInt64LittleEndian(buf, index);
        hmacBase.CopyTo(buf, 8);
        return SHA512.HashData(buf);
    }

    public static byte[] CompositeKey(string? password, byte[]? keyFile)
    {
        using var ms = new MemoryStream();
        var hasKeyFile = keyFile is { Length: > 0 };
        if (password != null && (password.Length > 0 || !hasKeyFile))
            ms.Write(SHA256.HashData(Encoding.UTF8.GetBytes(password)));
        if (hasKeyFile) ms.Write(KeyFileData(keyFile!));
        return SHA256.HashData(ms.ToArray());
    }

    /// <summary>KeePass key file rules: XML v1/v2, 32 raw bytes, 64 hex chars, otherwise SHA-256 of the file.</summary>
    internal static byte[] KeyFileData(byte[] file)
    {
        try
        {
            var text = TextDecoding.Decode(file).TrimStart();
            if (text.StartsWith('<'))
            {
                var doc = ParseXml(Encoding.UTF8.GetBytes(text));
                var version = doc.Root?.Element("Meta")?.Element("Version")?.Value.Trim();
                var data = doc.Root?.Element("Key")?.Element("Data")?.Value;
                if (data != null)
                {
                    if (version != null && version.StartsWith("2.", StringComparison.Ordinal))
                        return Convert.FromHexString(new string(data.Where(Uri.IsHexDigit).ToArray()));
                    return Convert.FromBase64String(data.Trim());
                }
            }
        }
        catch (Exception ex) when (ex is XmlException or FormatException or KdbxException) { }

        if (file.Length == 32) return file;
        if (file.Length == 64 && file.All(b => Uri.IsHexDigit((char)b)))
            return Convert.FromHexString(Encoding.ASCII.GetString(file));
        return SHA256.HashData(file);
    }

    // ------------------------------------------------------------------------------------------------ writing

    /// <summary>Writes a KDBX 4.0 database (AES-256, gzip, ChaCha20 inner stream). Elements carrying
    /// Protected="True" must hold plaintext; they are encrypted here.</summary>
    public static byte[] Encrypt(XDocument doc, string password, byte[]? keyFile, KdbxWriteOptions? options = null)
    {
        options ??= new KdbxWriteOptions();
        var masterSeed = RandomNumberGenerator.GetBytes(32);
        var iv = RandomNumberGenerator.GetBytes(16);
        var innerKey = RandomNumberGenerator.GetBytes(64);

        var kdf = new List<(string, object)>();
        if (options.Kdf == "aes")
        {
            kdf.Add(("$UUID", KdfAes));
            kdf.Add(("R", options.AesRounds));
            kdf.Add(("S", RandomNumberGenerator.GetBytes(32)));
        }
        else
        {
            kdf.Add(("$UUID", options.Kdf == "argon2id" ? KdfArgon2id : KdfArgon2d));
            kdf.Add(("S", RandomNumberGenerator.GetBytes(32)));
            kdf.Add(("P", (uint)options.Argon2Parallelism));
            kdf.Add(("M", (ulong)options.Argon2MemoryKiB * 1024));
            kdf.Add(("I", (ulong)options.Argon2Iterations));
            kdf.Add(("V", 0x13u));
        }
        var kdfBytes = VariantDictionary.Write(kdf);

        using var header = new MemoryStream();
        using (var w = new BinaryWriter(header, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Signature1);
            w.Write(Signature2);
            w.Write(0x00040000u);
            void Field(H id, byte[] data)
            {
                w.Write((byte)id);
                w.Write(data.Length);
                w.Write(data);
            }
            Field(H.CipherId, CipherAes256);
            Field(H.Compression, BitConverter.GetBytes(1u));
            Field(H.MasterSeed, masterSeed);
            Field(H.EncryptionIv, iv);
            Field(H.KdfParameters, kdfBytes);
            Field(H.End, "\r\n\r\n"u8.ToArray());
        }
        var headerBytes = header.ToArray();

        var composite = CompositeKey(password, keyFile);
        var transformed = TransformKey(composite, VariantDictionary.Read(kdfBytes));
        var masterKey = SHA256.HashData([.. masterSeed, .. transformed]);
        var hmacBase = SHA512.HashData([.. masterSeed, .. transformed, 0x01]);

        // Inner content: inner header + XML with protected values encrypted by ChaCha20.
        var protectedDoc = new XDocument(doc);
        ApplyProtection(protectedDoc, CreateChaChaInner(innerKey), decrypt: false);
        using var inner = new MemoryStream();
        using (var w = new BinaryWriter(inner, Encoding.UTF8, leaveOpen: true))
        {
            w.Write((byte)1);
            w.Write(4);
            w.Write(3u);
            w.Write((byte)2);
            w.Write(innerKey.Length);
            w.Write(innerKey);
            w.Write((byte)0);
            w.Write(0);
        }
        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true };
        using (var xw = XmlWriter.Create(inner, settings)) protectedDoc.Save(xw);

        var compressed = Gzip(inner.ToArray());
        byte[] encrypted;
        using (var aes = Aes.Create())
        {
            aes.Key = masterKey;
            encrypted = aes.EncryptCbc(compressed, iv, PaddingMode.PKCS7);
        }

        using var output = new MemoryStream();
        output.Write(headerBytes);
        output.Write(SHA256.HashData(headerBytes));
        output.Write(HMACSHA256.HashData(BlockHmacKey(ulong.MaxValue, hmacBase), headerBytes));

        // HMAC block stream: 1 MiB blocks followed by an empty terminating block.
        const int blockSize = 1 << 20;
        ulong index = 0;
        var offset = 0;
        while (true)
        {
            var size = Math.Min(blockSize, encrypted.Length - offset);
            var msg = new byte[12 + size];
            BinaryPrimitives.WriteUInt64LittleEndian(msg, index);
            BinaryPrimitives.WriteInt32LittleEndian(msg.AsSpan(8), size);
            encrypted.AsSpan(offset, size).CopyTo(msg.AsSpan(12));
            output.Write(HMACSHA256.HashData(BlockHmacKey(index, hmacBase), msg));
            output.Write(BitConverter.GetBytes(size));
            output.Write(encrypted.AsSpan(offset, size));
            if (size == 0) break;
            offset += size;
            index++;
        }
        return output.ToArray();
    }

    // ------------------------------------------------------------------------------------------------ helpers

    private static void ApplyProtection(XDocument doc, IKeystream stream, bool decrypt)
    {
        foreach (var el in doc.Descendants().ToList())
        {
            var attr = el.Attribute("Protected");
            if (attr == null || !attr.Value.Equals("True", StringComparison.OrdinalIgnoreCase)) continue;
            if (decrypt)
            {
                byte[] raw;
                try
                {
                    raw = Convert.FromBase64String(el.Value);
                }
                catch (FormatException ex)
                {
                    throw new KdbxException("Corrupted protected value.", ex);
                }
                stream.Xor(raw);
                // The attribute stays so the mapper knows the field is secret; the value is now plaintext.
                el.Value = el.Name.LocalName == "Value" ? Encoding.UTF8.GetString(raw) : Convert.ToBase64String(raw);
            }
            else
            {
                var raw = Encoding.UTF8.GetBytes(el.Value);
                stream.Xor(raw);
                el.Value = Convert.ToBase64String(raw);
            }
        }
    }

    internal static XDocument ParseXml(byte[] xml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreWhitespace = false,
            CheckCharacters = false,
        };
        using var ms = new MemoryStream(xml);
        using var reader = XmlReader.Create(ms, settings);
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    private static byte[] Gunzip(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var gz = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gz.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] Gzip(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gz = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true)) gz.Write(data);
        return output.ToArray();
    }
}

/// <summary>KDBX 4 VariantDictionary serialization.</summary>
internal static class VariantDictionary
{
    public static Dictionary<string, object> Read(byte[] data)
    {
        var result = new Dictionary<string, object>();
        using var r = new BinaryReader(new MemoryStream(data));
        var version = r.ReadUInt16();
        if ((version & 0xFF00) > 0x0100) throw new KdbxException("Unsupported KDF parameter format.");
        while (true)
        {
            var type = r.ReadByte();
            if (type == 0) break;
            var name = Encoding.UTF8.GetString(r.ReadBytes(r.ReadInt32()));
            var value = r.ReadBytes(r.ReadInt32());
            result[name] = type switch
            {
                0x04 => BinaryPrimitives.ReadUInt32LittleEndian(value),
                0x05 => BinaryPrimitives.ReadUInt64LittleEndian(value),
                0x08 => value.Length > 0 && value[0] != 0,
                0x0C => BinaryPrimitives.ReadInt32LittleEndian(value),
                0x0D => BinaryPrimitives.ReadInt64LittleEndian(value),
                0x18 => Encoding.UTF8.GetString(value),
                0x42 => value,
                _ => value,
            };
        }
        return result;
    }

    public static byte[] Write(IEnumerable<(string Name, object Value)> items)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((ushort)0x0100);
        foreach (var (name, value) in items)
        {
            var (type, bytes) = value switch
            {
                uint u => ((byte)0x04, BitConverter.GetBytes(u)),
                ulong u => ((byte)0x05, BitConverter.GetBytes(u)),
                bool b => ((byte)0x08, new[] { (byte)(b ? 1 : 0) }),
                int i => ((byte)0x0C, BitConverter.GetBytes(i)),
                long l => ((byte)0x0D, BitConverter.GetBytes(l)),
                string s => ((byte)0x18, Encoding.UTF8.GetBytes(s)),
                byte[] b => ((byte)0x42, b),
                _ => throw new ArgumentException("Unsupported variant type."),
            };
            var nameBytes = Encoding.UTF8.GetBytes(name);
            w.Write(type);
            w.Write(nameBytes.Length);
            w.Write(nameBytes);
            w.Write(bytes.Length);
            w.Write(bytes);
        }
        w.Write((byte)0);
        return ms.ToArray();
    }
}
