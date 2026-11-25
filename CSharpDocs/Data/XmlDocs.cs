using System.Text;
using System.Xml;
using System.Xml.XPath;

namespace CSharpDocs.Data;

/// <summary>Разобранное содержимое одного &lt;member&gt;.</summary>
public sealed class DocBody
{
    public string Summary { get; init; } = "";
    public string Remarks { get; init; } = "";
    public string Returns { get; init; } = "";
    public List<ParamDoc> Params { get; init; } = [];
    public List<ParamDoc> TypeParams { get; init; } = [];
    public List<ExceptionDoc> Exceptions { get; init; } = [];

    /// <summary>Есть тег &lt;inheritdoc&gt;.</summary>
    public bool HasInheritDoc { get; init; }

    /// <summary>Значение cref у &lt;inheritdoc cref="..."/&gt;, иначе пусто.</summary>
    public string InheritCref { get; init; } = "";

    /// <summary>Тело без собственных описаний (нужно наследовать).</summary>
    public bool IsEmpty => Summary.Length == 0 && Remarks.Length == 0 && Returns.Length == 0
        && Params.Count == 0 && TypeParams.Count == 0 && Exceptions.Count == 0;
}

/// <summary>
/// Чтение XML-документации компилятора через System.Xml.XPath (first-party,
/// совместимо с Native AOT). Навигация выполняется XPath-запросами, а не ручным
/// обходом дерева.
/// </summary>
public static class XmlDocs
{
    public static Dictionary<string, DocBody> Parse(Stream stream)
    {
        var document = new XPathDocument(stream);
        var navigator = document.CreateNavigator();

        var result = new Dictionary<string, DocBody>(StringComparer.Ordinal);
        var members = navigator.Select("/doc/members/member");
        while (members.MoveNext())
        {
            var member = members.Current;
            if (member is null)
                continue;

            var id = member.GetAttribute("name", "");
            if (id.Length == 0)
                continue;

            result[id] = ReadMember(member);
        }
        return result;
    }

    private static DocBody ReadMember(XPathNavigator member)
    {
        var inherit = member.SelectSingleNode("inheritdoc");
        return new DocBody
        {
            Summary = Render(member.SelectSingleNode("summary")),
            Remarks = Render(member.SelectSingleNode("remarks")),
            Returns = Render(member.SelectSingleNode("returns")),
            HasInheritDoc = inherit is not null,
            InheritCref = inherit?.GetAttribute("cref", "") ?? "",
            Params = ReadNamedElements(member, "param", p => new ParamDoc
            {
                Name = p.GetAttribute("name", ""),
                Summary = Render(p),
            }),
            TypeParams = ReadNamedElements(member, "typeparam", p => new ParamDoc
            {
                Name = p.GetAttribute("name", ""),
                Summary = Render(p),
            }),
            Exceptions = ReadNamedElements(member, "exception", e => new ExceptionDoc
            {
                Cref = e.GetAttribute("cref", ""),
                Summary = Render(e),
            }),
        };
    }

    private static List<T> ReadNamedElements<T>(XPathNavigator parent, string name, Func<XPathNavigator, T> map)
    {
        var list = new List<T>();
        var children = parent.SelectChildren(name, "");
        while (children.MoveNext())
            if (children.Current is { } current)
                list.Add(map(current));
        return list;
    }

    /// <summary>Собирает безопасный HTML из фрагмента XML-документации.</summary>
    public static string Render(XPathNavigator? element)
    {
        if (element is null)
            return "";

        var sb = new StringBuilder();
        RenderChildren(element, sb);
        return sb.ToString().Trim();
    }

    private static void RenderChildren(XPathNavigator element, StringBuilder sb)
    {
        var node = element.Clone();
        if (!node.MoveToFirstChild())
        {
            // Лист: значение берём через Value (корректно раскрывает сущности).
            AppendText(element.Value, sb);
            return;
        }

        do
        {
            switch (node.NodeType)
            {
                case XPathNodeType.Text:
                case XPathNodeType.SignificantWhitespace:
                case XPathNodeType.Whitespace:
                    AppendText(node.Value, sb);
                    break;
                case XPathNodeType.Element:
                    RenderElement(node, sb);
                    break;
            }
        }
        while (node.MoveToNext());
    }

    private static void RenderElement(XPathNavigator element, StringBuilder sb)
    {
        switch (element.LocalName)
        {
            case "see":
            case "seealso":
                sb.Append(CodeSpan(element.GetAttribute("cref", "") is { Length: > 0 } cref
                    ? cref
                    : element.GetAttribute("langword", "")));
                break;
            case "paramref":
            case "typeparamref":
                sb.Append(CodeSpan(element.GetAttribute("name", "")));
                break;
            case "para":
                sb.Append("<br/>");
                RenderChildren(element, sb);
                break;
            case "br":
                sb.Append("<br/>");
                break;
            case "inheritdoc":
                break;
            default:
                RenderChildren(element, sb);
                break;
        }
    }

    private static void AppendText(string text, StringBuilder sb)
    {
        if (text.Length > 0)
            sb.Append(HtmlEscape(text));
    }

    private static string CodeSpan(string cref)
    {
        if (cref.Length == 0)
            return "";

        var cut = cref.LastIndexOfAny([':', '.']);
        if (cut >= 0)
            cref = cref[(cut + 1)..];

        var tick = cref.IndexOf('`');
        if (tick >= 0)
            cref = cref[..tick];

        return "<code>" + HtmlEscape(cref) + "</code>";
    }

    public static string HtmlEscape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
