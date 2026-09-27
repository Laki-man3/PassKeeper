using System.Security.Cryptography;

namespace PassKeeper.Core.Crypto;

/// <summary>AES-256-GCM helper. Output layout: nonce(12) || ciphertext || tag(16).</summary>
public static class Aead
{
    public const int KeySize = 32;
    public const int NonceSize = 12;
    public const int TagSize = 16;

    public static byte[] Encrypt(byte[] key, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData)
    {
        if (key.Length != KeySize) throw new ArgumentException("Key must be 256 bits.", nameof(key));
        var output = new byte[NonceSize + plaintext.Length + TagSize];
        var span = output.AsSpan();
        RandomNumberGenerator.Fill(span[..NonceSize]);
        using var gcm = new AesGcm(key, TagSize);
        gcm.Encrypt(span[..NonceSize], plaintext, span.Slice(NonceSize, plaintext.Length),
            span[^TagSize..], associatedData);
        return output;
    }

    /// <exception cref="CryptographicException">Wrong key or tampered data.</exception>
    public static byte[] Decrypt(byte[] key, ReadOnlySpan<byte> blob, ReadOnlySpan<byte> associatedData)
    {
        if (key.Length != KeySize) throw new ArgumentException("Key must be 256 bits.", nameof(key));
        if (blob.Length < NonceSize + TagSize) throw new CryptographicException("Encrypted data is truncated.");
        var ctLength = blob.Length - NonceSize - TagSize;
        var plaintext = new byte[ctLength];
        using var gcm = new AesGcm(key, TagSize);
        gcm.Decrypt(blob[..NonceSize], blob.Slice(NonceSize, ctLength), blob[^TagSize..], plaintext, associatedData);
        return plaintext;
    }
}
