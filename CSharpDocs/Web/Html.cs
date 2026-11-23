using System.Text;

namespace CSharpDocs.Web;

/// <summary>Помощники для сборки HTML без шаблонизатора (совместимо с Native AOT).</summary>
public static class Html
{
    public static string Esc(string? s) => s is null ? "" : s
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);

    /// <summary>Ссылка с экранированными адресом и текстом.</summary>
    public static string Link(string href, string text) =>
        $"<a href=\"{Esc(href)}\">{Esc(text)}</a>";

    /// <summary>Экранирует и подсвечивает C#-сигнатуру.</summary>
    public static string Highlight(string signature)
    {
        var sb = new StringBuilder(signature.Length * 2);
        var i = 0;
        while (i < signature.Length)
        {
            var c = signature[i];
            if (c == '"')
            {
                var j = i + 1;
                while (j < signature.Length && signature[j] != '"')
                    j++;
                if (j < signature.Length)
                    j++;
                sb.Append("<span class=\"hl-str\">").Append(Esc(signature[i..j])).Append("</span>");
                i = j;
            }
            else if (IsWordStart(c))
            {
                var j = i + 1;
                while (j < signature.Length && IsWordPart(signature[j]))
                    j++;
                var word = signature[i..j];
                if (Keywords.Contains(word))
                    sb.Append("<span class=\"hl-kw\">").Append(word).Append("</span>");
                else if (char.IsUpper(word[0]))
                    sb.Append("<span class=\"hl-type\">").Append(word).Append("</span>");
                else
                    sb.Append(Esc(word));
                i = j;
            }
            else
            {
                sb.Append(Esc(c.ToString()));
                i++;
            }
        }
        return sb.ToString();
    }

    private static bool IsWordStart(char c) => c == '_' || char.IsAsciiLetter(c);
    private static bool IsWordPart(char c) => IsWordStart(c) || char.IsAsciiDigit(c);

    private static readonly HashSet<string> Keywords =
    [
        "public", "private", "protected", "internal", "static", "readonly", "const",
        "class", "interface", "struct", "enum", "void", "new", "override", "virtual",
        "abstract", "sealed", "this", "ref", "out", "in", "return", "get", "set",
        "async", "await", "null", "true", "false", "var",
    ];
}
