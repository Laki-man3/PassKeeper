using System.Runtime.InteropServices;
using System.Text;
using PassKeeper.Core.Models;

namespace PassKeeper.Core.Interop;

/// <summary>Reads generic credentials of the current user from the Windows Credential Manager.</summary>
public static class WindowsCredentialImporter
{
    private const uint CredEnumerateAllCredentials = 0x1;
    private const uint CredTypeGeneric = 1;
    private const uint CredTypeDomainVisiblePassword = 4;

    private static readonly string[] SystemPrefixes =
    [
        "virtualapp/didlogical", "WindowsLive:", "MicrosoftAccount:", "MicrosoftOffice", "OneDrive Cached Credential",
        "Microsoft_OC", "msteams_", "SSO_POP_", "XboxLive", "Xbl", "WindowsAzure", "AzureAD", "MSAL",
        "LegacyGeneric:target=MicrosoftOffice", "Adobe App", "vscodevscode.github-authentication",
    ];

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredEnumerateW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredEnumerate(string? filter, uint flags, out uint count, out IntPtr credentials);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    public static ImportResult Import(string source = "Windows Credential Manager")
    {
        var result = new ImportResult { Source = source };
        if (!CredEnumerate(null, CredEnumerateAllCredentials, out var count, out var list))
            return result;
        try
        {
            for (var i = 0; i < count; i++)
            {
                var ptr = Marshal.ReadIntPtr(list, i * IntPtr.Size);
                var c = Marshal.PtrToStructure<Credential>(ptr);
                if (c.Type != CredTypeGeneric && c.Type != CredTypeDomainVisiblePassword) continue;
                var target = Marshal.PtrToStringUni(c.TargetName) ?? "";
                if (SystemPrefixes.Any(p => target.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
                if (c.CredentialBlobSize == 0 || c.CredentialBlob == IntPtr.Zero) continue;

                var blob = new byte[c.CredentialBlobSize];
                Marshal.Copy(c.CredentialBlob, blob, 0, blob.Length);
                var secret = DecodeBlob(blob);
                if (secret == null) continue;

                var name = CleanTarget(target);
                var e = new VaultEntry
                {
                    Title = name,
                    Username = Marshal.PtrToStringUni(c.UserName) ?? "",
                    Password = secret,
                    Notes = Marshal.PtrToStringUni(c.Comment) ?? "",
                    Folder = "Windows",
                };
                if (LooksLikeUrl(name)) e.Url = name.StartsWith("git:", StringComparison.OrdinalIgnoreCase) ? name[4..] : name;
                else e.WindowPatterns.Add(name);
                result.Entries.Add(e);
            }
        }
        finally
        {
            CredFree(list);
        }
        return result;
    }

    private static string CleanTarget(string target)
    {
        foreach (var prefix in new[] { "LegacyGeneric:target=", "Domain:target=", "WindowsLive:target=" })
            if (target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return target[prefix.Length..];
        return target;
    }

    private static bool LooksLikeUrl(string s) =>
        s.Contains("://", StringComparison.Ordinal) || s.StartsWith("git:", StringComparison.OrdinalIgnoreCase);

    /// <summary>Credential blobs are usually UTF-16 strings; binary tokens are skipped.</summary>
    private static string? DecodeBlob(byte[] blob)
    {
        if (blob.Length % 2 == 0)
        {
            var s = Encoding.Unicode.GetString(blob).TrimEnd('\0');
            if (s.Length > 0 && s.All(ch => !char.IsControl(ch) && !char.IsSurrogate(ch) || ch is '\n' or '\r' or '\t')) return s;
        }
        try
        {
            var s = new UTF8Encoding(false, true).GetString(blob).TrimEnd('\0');
            if (s.Length > 0 && s.All(ch => !char.IsControl(ch) || ch is '\n' or '\r' or '\t')) return s;
        }
        catch (DecoderFallbackException) { }
        return null;
    }
}
