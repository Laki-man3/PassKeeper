using System.Security.Cryptography;
using System.Text;

namespace PassKeeper.Core.Security;

public sealed class GeneratorOptions
{
    public int Length { get; set; } = 20;
    public bool Upper { get; set; } = true;
    public bool Lower { get; set; } = true;
    public bool Digits { get; set; } = true;
    public bool Symbols { get; set; } = true;
    public bool ExcludeAmbiguous { get; set; } = true;
}

public static class PasswordGenerator
{
    public const int MinLength = 4;
    public const int MaxLength = 128;

    private const string UpperSet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string LowerSet = "abcdefghijklmnopqrstuvwxyz";
    private const string DigitSet = "0123456789";
    private const string SymbolSet = "!@#$%^&*()-_=+[]{};:,.?/~";
    private const string Ambiguous = "Il1O0o|`'\"";

    public static string Generate(GeneratorOptions options)
    {
        var length = Math.Clamp(options.Length, MinLength, MaxLength);
        var sets = new List<string>();
        if (options.Upper) sets.Add(UpperSet);
        if (options.Lower) sets.Add(LowerSet);
        if (options.Digits) sets.Add(DigitSet);
        if (options.Symbols) sets.Add(SymbolSet);
        if (sets.Count == 0) sets.Add(LowerSet);
        if (options.ExcludeAmbiguous)
            sets = sets.Select(s => new string(s.Where(c => !Ambiguous.Contains(c)).ToArray())).ToList();

        var all = string.Concat(sets);
        var chars = new char[length];
        // Guarantee one character from every selected set, then fill uniformly and shuffle.
        for (var i = 0; i < length; i++)
        {
            var set = i < sets.Count ? sets[i] : all;
            chars[i] = set[RandomNumberGenerator.GetInt32(set.Length)];
        }
        for (var i = length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars);
    }

    public static string GeneratePin(int digits = 6)
    {
        var sb = new StringBuilder(digits);
        for (var i = 0; i < digits; i++) sb.Append((char)('0' + RandomNumberGenerator.GetInt32(10)));
        return sb.ToString();
    }
}
