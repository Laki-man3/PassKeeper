using System.Security.Cryptography;

namespace PassKeeper.Core.Storage;

/// <summary>
/// Seals the PIN file to the user on this computer, so a copy of it cannot be tried elsewhere: Windows uses DPAPI,
/// the macOS and Linux app a key kept in the system's secret store (Keychain, Secret Service).
/// </summary>
public interface IPinSealer
{
    byte[] Seal(string path, byte[] data);

    /// <exception cref="CryptographicException">Sealed elsewhere, or damaged.</exception>
    /// <exception cref="PinSealUnavailableException">The secret store cannot be reached now (the PIN stays).</exception>
    byte[] Open(string path, byte[] sealedData);

    /// <summary>The PIN file is gone: whatever was kept for it goes too.</summary>
    void Forget(string path);
}

/// <summary>The system's secret store cannot be used right now (locked, missing or refused).</summary>
public sealed class PinSealUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Windows: DPAPI, bound to the user's account.</summary>
public sealed class DpapiPinSealer : IPinSealer
{
    private static readonly byte[] Entropy = "PassKeeper.PinStore.v1"u8.ToArray();

    public byte[] Seal(string path, byte[] data) => ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Open(string path, byte[] sealedData) => ProtectedData.Unprotect(sealedData, Entropy, DataProtectionScope.CurrentUser);

    public void Forget(string path) { }
}

public static class PinSealing
{
    /// <summary>Chosen once at start by the application; DPAPI unless it says otherwise.</summary>
    public static IPinSealer Current { get; set; } = new DpapiPinSealer();
}
