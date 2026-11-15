using System.Security.Cryptography;
using PassKeeper.Core.Storage;

namespace PassKeeper.Tests;

[Collection("Pin")]
public class PinSealingTests
{
    /// <summary>Stands in for the system's secret store of the macOS / Linux app.</summary>
    private sealed class FakeSealer : IPinSealer
    {
        public bool Unavailable { get; set; }
        public int Forgotten { get; private set; }

        public byte[] Seal(string path, byte[] data) => [0x5A, .. data.Select(b => (byte)(b ^ 0x5A))];

        public byte[] Open(string path, byte[] sealedData)
        {
            if (Unavailable) throw new PinSealUnavailableException("locked");
            if (sealedData.Length == 0 || sealedData[0] != 0x5A) throw new CryptographicException("not ours");
            return sealedData[1..].Select(b => (byte)(b ^ 0x5A)).ToArray();
        }

        public void Forget(string path) => Forgotten++;
    }

    [Fact]
    public void Pin_WorksWithAnotherSealer_AndSurvivesALockedSecretStore()
    {
        var previous = PinSealing.Current;
        var sealer = new FakeSealer();
        PinSealing.Current = sealer;
        try
        {
            using var dir = new TempDir();
            var vault = new VaultService(dir.Path);
            vault.Create("Анна", "Correct-Horse-9");
            vault.SetPin("482913");
            vault.Lock();
            Assert.Equal(PinUnlockResult.Success, vault.UnlockWithPin("482913"));
            vault.Lock();

            // The secret store cannot be reached: the error goes up and the PIN stays for next time.
            sealer.Unavailable = true;
            Assert.Throws<PinSealUnavailableException>(() => vault.UnlockWithPin("482913"));
            Assert.True(vault.HasPin);
            sealer.Unavailable = false;
            Assert.Equal(PinUnlockResult.Success, vault.UnlockWithPin("482913"));

            // Removing the PIN also lets the secret store drop its key.
            vault.RemovePin();
            Assert.False(vault.HasPin);
            Assert.Equal(1, sealer.Forgotten);
        }
        finally
        {
            PinSealing.Current = previous;
        }
    }
}
