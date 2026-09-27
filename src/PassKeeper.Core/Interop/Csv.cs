using System.Text;

namespace PassKeeper.Core.Interop;

/// <summary>RFC 4180 CSV reader/writer with delimiter auto-detection.</summary>
public static class Csv
{
    public static List<string[]> Parse(string text, char? delimiter = null)
    {
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        var sep = delimiter ?? DetectDelimiter(text);
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldStarted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else inQuotes = false;
                }
                else field.Append(c);
                continue;
            }

            if (c == '"' && !fieldStarted)
            {
                inQuotes = true;
                fieldStarted = true;
            }
            else if (c == sep)
            {
                row.Add(field.ToString());
                field.Clear();
                fieldStarted = false;
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString());
                field.Clear();
                fieldStarted = false;
                if (row.Count > 1 || row[0].Length > 0) rows.Add(row.ToArray());
                row.Clear();
            }
            else
            {
                field.Append(c);
                fieldStarted = true;
            }
        }
        if (field.Length > 0 || row.Count > 0 || fieldStarted)
        {
            row.Add(field.ToString());
            if (row.Count > 1 || row[0].Length > 0) rows.Add(row.ToArray());
        }
        return rows;
    }

    public static char DetectDelimiter(string text)
    {
        // Count candidate delimiters on the first line outside of quotes.
        int comma = 0, semicolon = 0, tab = 0;
        var inQuotes = false;
        foreach (var c in text)
        {
            if (c == '"') inQuotes = !inQuotes;
            else if (!inQuotes)
            {
                if (c is '\r' or '\n') break;
                if (c == ',') comma++;
                else if (c == ';') semicolon++;
                else if (c == '\t') tab++;
            }
        }
        if (tab > comma && tab > semicolon) return '\t';
        return semicolon > comma ? ';' : ',';
    }

    public static string Escape(string? value, char delimiter = ',')
    {
        value ??= "";
        var needsQuotes = value.IndexOfAny([delimiter, '"', '\r', '\n']) >= 0 || value.StartsWith(' ') || value.EndsWith(' ');
        return needsQuotes ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    public static string Write(IEnumerable<IEnumerable<string?>> rows, char delimiter = ',', bool quoteAll = false)
    {
        var sb = new StringBuilder();
        foreach (var r in rows)
        {
            var first = true;
            foreach (var v in r)
            {
                if (!first) sb.Append(delimiter);
                first = false;
                sb.Append(quoteAll ? "\"" + (v ?? "").Replace("\"", "\"\"") + "\"" : Escape(v, delimiter));
            }
            sb.Append("\r\n");
        }
        return sb.ToString();
    }
}

public static class TextDecoding
{
    static TextDecoding() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Reads text honoring BOMs; falls back to Windows-1251 when the file is not valid UTF-8.</summary>
    public static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1251).GetString(bytes);
        }
    }

    public static string ReadFile(string path) => Decode(File.ReadAllBytes(path));
}
