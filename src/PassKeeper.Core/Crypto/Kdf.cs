using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace PassKeeper.Core.Crypto;

/// <summary>Parameters of a password-based key derivation (Argon2, RFC 9106).</summary>
public sealed class KdfParameters
{
    public string Algorithm { get; set; } = "argon2id";
    public int MemoryKiB { get; set; } = 65536;
    public int Iterations { get; set; } = 3;
    public int Parallelism { get; set; } = 4;
    public byte[] Salt { get; set; } = [];

    /// <summary>RFC 9106 "second recommended option": Argon2id, m=64 MiB, t=3, p=4, 256-bit salt.</summary>
    public static KdfParameters CreateDefault() => new() { Salt = RandomNumberGenerator.GetBytes(32) };

    public void Validate()
    {
        if (Algorithm is not ("argon2id" or "argon2d" or "argon2i"))
            throw new CryptographicException($"Unsupported KDF '{Algorithm}'.");
        if (MemoryKiB < 8 * 1024 || MemoryKiB > 4 * 1024 * 1024)
            throw new CryptographicException("KDF memory parameter is out of range.");
        if (Iterations < 1 || Iterations > 1000)
            throw new CryptographicException("KDF iteration parameter is out of range.");
        if (Parallelism < 1 || Parallelism > 64)
            throw new CryptographicException("KDF parallelism parameter is out of range.");
        if (Salt.Length < 16)
            throw new CryptographicException("KDF salt is too short.");
    }
}

public static class Kdf
{
    /// <summary>Derives a key from a user secret (normalized to NFC, UTF-8 encoded).</summary>
    public static byte[] Derive(string secret, KdfParameters p, int length = 32)
    {
        p.Validate();
        var pw = Encoding.UTF8.GetBytes(secret.Normalize(NormalizationForm.FormC));
        try { return Argon2(pw, p.Algorithm, p.Salt, p.MemoryKiB, p.Iterations, p.Parallelism, length); }
        finally { CryptographicOperations.ZeroMemory(pw); }
    }

    /// <summary>Raw Argon2 (version 0x13) over arbitrary bytes.</summary>
    public static byte[] Argon2(byte[] password, string algorithm, byte[] salt, int memoryKiB, int iterations,
        int parallelism, int length, byte[]? secret = null, byte[]? associatedData = null)
    {
        Argon2 a = algorithm switch
        {
            "argon2id" => new Argon2id(password),
            "argon2d" => new Argon2d(password),
            "argon2i" => new Argon2i(password),
            _ => throw new CryptographicException($"Unsupported KDF '{algorithm}'.")
        };
        try
        {
            a.Salt = salt;
            a.MemorySize = memoryKiB;
            a.Iterations = iterations;
            a.DegreeOfParallelism = parallelism;
            if (secret is { Length: > 0 }) a.KnownSecret = secret;
            if (associatedData is { Length: > 0 }) a.AssociatedData = associatedData;
            return a.GetBytes(length);
        }
        finally
        {
            a.Dispose();
        }
    }
}
