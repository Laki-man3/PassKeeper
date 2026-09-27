using System.Text;
using PassKeeper.Core.Models;

namespace PassKeeper.Core.Interop;

/// <summary>Kaspersky Password Manager text export ("Website name: …", blocks separated by "---").</summary>
public static class KasperskyImporter
{
    private enum K { Title, Url, Login, Password, Comment, Ignore }

    private static readonly Dictionary<string, K> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Website name"] = K.Title, ["Application"] = K.Title, ["Account name"] = K.Title, ["Name"] = K.Title,
        ["Название сайта"] = K.Title, ["Имя сайта"] = K.Title, ["Приложение"] = K.Title, ["Название учетной записи"] = K.Title,
        ["Название учётной записи"] = K.Title, ["Название"] = K.Title, ["Имя"] = K.Title,
        ["Website URL"] = K.Url, ["URL"] = K.Url, ["Адрес сайта"] = K.Url, ["Адрес"] = K.Url, ["Ссылка"] = K.Url,
        ["Login"] = K.Login, ["Логин"] = K.Login, ["Имя пользователя"] = K.Login,
        ["Password"] = K.Password, ["Пароль"] = K.Password,
        ["Comment"] = K.Comment, ["Text"] = K.Comment, ["Комментарий"] = K.Comment, ["Текст"] = K.Comment,
        ["Login name"] = K.Ignore, ["Имя логина"] = K.Ignore, ["Название логина"] = K.Ignore,
    };

    public static bool LooksLikeKaspersky(string text) =>
        (text.Contains("Website name:", StringComparison.Ordinal) || text.Contains("Application:", StringComparison.Ordinal) ||
         text.Contains("Название сайта:", StringComparison.Ordinal)) && text.Contains("---", StringComparison.Ordinal);

    public static ImportResult Import(string text, string source)
    {
        var result = new ImportResult { Source = source };
        var blocks = text.Replace("\r\n", "\n").Split("\n---\n");
        foreach (var block in blocks)
        {
            var e = new VaultEntry();
            K? last = null;
            var comment = new StringBuilder();
            var any = false;
            foreach (var rawLine in block.Split('\n'))
            {
                var line = rawLine.TrimEnd();
                var colon = line.IndexOf(':');
                if (colon > 0 && Keys.TryGetValue(line[..colon].Trim(), out var k))
                {
                    var value = line[(colon + 1)..].Trim();
                    last = k;
                    any = true;
                    switch (k)
                    {
                        case K.Title: e.Title = value; break;
                        case K.Url: e.Url = value; break;
                        case K.Login: e.Username = value; break;
                        case K.Password: e.Password = value; break;
                        case K.Comment: comment.Append(value); break;
                    }
                }
                else if (last == K.Comment)
                {
                    comment.Append('\n').Append(rawLine);
                }
            }
            if (!any) continue;
            e.Notes = comment.ToString().Trim();
            if (ImportHelpers.IsEmpty(e) && e.Title.Length == 0) continue;
            e.Title = ImportHelpers.MakeTitle(e.Title, e.Url, e.Username);
            result.Entries.Add(e);
        }
        return result;
    }
}
