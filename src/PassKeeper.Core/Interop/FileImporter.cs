using System.Text.Json;
using PassKeeper.Core.Interop.KeePass;

namespace PassKeeper.Core.Interop;

public enum ImportFileFormat
{
    Auto,
    Csv,
    Kdbx,
    KeePassXml,
    BitwardenJson,
    OnePassword1Pux,
    Kaspersky,
    PassKeeperJson,
    PassKeeperEncrypted,
}

public sealed class ImportPasswordRequiredException() : Exception("A password is required to open this file.");

public static class FileImporter
{
    public const string FileFilter = "*.csv;*.txt;*.json;*.xml;*.kdbx;*.1pux;*.pkx";

    public static ImportFileFormat Detect(string path, byte[] data)
    {
        if (Kdbx.IsKdbx(data)) return ImportFileFormat.Kdbx;
        if (PassKeeperFormat.IsEncrypted(data)) return ImportFileFormat.PassKeeperEncrypted;
        if (data.Length > 4 && data[0] == 'P' && data[1] == 'K' && data[2] == 3 && data[3] == 4)
            return ImportFileFormat.OnePassword1Pux;

        var text = TextDecoding.Decode(data).TrimStart('﻿', ' ', '\r', '\n', '\t');
        if (text.StartsWith('<') && text.Contains("<KeePassFile", StringComparison.Ordinal)) return ImportFileFormat.KeePassXml;
        if (text.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true });
                if (PassKeeperFormat.IsJson(doc.RootElement)) return ImportFileFormat.PassKeeperJson;
                if (JsonImporters.LooksLikeBitwarden(doc.RootElement)) return ImportFileFormat.BitwardenJson;
            }
            catch (JsonException) { }
        }
        if (KasperskyImporter.LooksLikeKaspersky(text)) return ImportFileFormat.Kaspersky;
        return ImportFileFormat.Csv;
    }

    public static bool NeedsPassword(ImportFileFormat format) =>
        format is ImportFileFormat.Kdbx or ImportFileFormat.PassKeeperEncrypted;

    /// <exception cref="ImportPasswordRequiredException">The file is encrypted and no password was given.</exception>
    /// <exception cref="KdbxInvalidKeyException">Wrong KeePass password/key file.</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">Wrong PassKeeper backup password.</exception>
    public static ImportResult Import(string path, ImportFileFormat format = ImportFileFormat.Auto, string? password = null,
        byte[]? keyFile = null)
    {
        var data = File.ReadAllBytes(path);
        var name = Path.GetFileName(path);
        if (format == ImportFileFormat.Auto) format = Detect(path, data);
        if (NeedsPassword(format) && password == null && keyFile == null) throw new ImportPasswordRequiredException();

        return format switch
        {
            ImportFileFormat.Kdbx => KeePassXml.ToEntries(Kdbx.Decrypt(data, password, keyFile), name),
            ImportFileFormat.PassKeeperEncrypted => PassKeeperFormat.Decrypt(data, password!, name),
            ImportFileFormat.KeePassXml => KeePassXml.ToEntries(Kdbx.ParseXml(data), name),
            ImportFileFormat.OnePassword1Pux => JsonImporters.Import1Pux(data, name),
            ImportFileFormat.BitwardenJson => JsonImporters.ImportBitwarden(TextDecoding.Decode(data), name),
            ImportFileFormat.PassKeeperJson => PassKeeperFormat.FromJson(TextDecoding.Decode(data), name),
            ImportFileFormat.Kaspersky => KasperskyImporter.Import(TextDecoding.Decode(data), name),
            _ => CsvImporter.Import(TextDecoding.Decode(data), name),
        };
    }
}
