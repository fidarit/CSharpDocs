using System.Text.RegularExpressions;
using Markdig;

namespace CSharpDocs.Data;

/// <summary>
/// Рендеринг Markdown в HTML для комментариев.
/// Сырой HTML из входных данных отключён, ссылки с опасными схемами
/// (javascript:, vbscript:, data:) вырезаются — это защищает от XSS.
/// </summary>
public static partial class Markdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAutoLinks()
        .UsePipeTables()
        .UseTaskLists()
        .UseEmphasisExtras()
        .DisableHtml()
        .Build();

    /// <summary>Адреса с опасной схемой в href/src.</summary>
    [GeneratedRegex(@"(?i)\b(href|src)\s*=\s*[""']\s*(?:javascript|vbscript|data)\s*:")]
    private static partial Regex UnsafeLinkRegex();

    public static string ToHtml(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return "";

        var html = Markdig.Markdown.ToHtml(markdown, Pipeline);
        return UnsafeLinkRegex().Replace(html, "$1=\"#");
    }
}
