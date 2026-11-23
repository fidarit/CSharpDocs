using System.Text;

namespace CSharpDocs.Data;

/// <summary>Разбор и форматирование идентификаторов документации (doc-id).</summary>
public static class DocId
{
    public static string KindOf(string id) => id.Length >= 2 && id[1] == ':' ? id[..1] : "";
    public static string RestOf(string id) => id.Length >= 2 && id[1] == ':' ? id[2..] : id;

    /// <summary>
    /// Имя члена для показа. У явных реализаций интерфейса компилятор кодирует
    /// точки как «#» (напр. System#Collections#IEnumerable#GetEnumerator);
    /// приводим к читаемому «Интерфейс.Член».
    /// </summary>
    public static string DisplayName(string rawName)
    {
        if (!rawName.Contains('#'))
            return rawName;

        var parts = rawName.Split('#').Where(p => p.Length > 0).ToArray();
        return parts.Length >= 2 ? parts[^2] + "." + parts[^1] : rawName.Replace('#', '.');
    }

    /// <summary>Убирает маркер обобщённости (`1, ``1).</summary>
    public static string StripArity(string name)
    {
        var i = name.IndexOf('`');
        return i >= 0 ? name[..i] : name;
    }

    /// <summary>Разбивает список параметров из doc-id по запятым верхнего уровня.</summary>
    public static List<string> SplitParams(string raw)
    {
        raw = raw.Trim();
        var result = new List<string>();
        if (raw.Length == 0)
            return result;
        var depth = 0;
        var start = 0;
        for (var i = 0; i < raw.Length; i++)
        {
            switch (raw[i])
            {
                case '{' or '(':
                    depth++;
                    break;
                case '}' or ')':
                    depth--;
                    break;
                case ',' when depth == 0:
                    result.Add(raw[start..i].Trim());
                    start = i + 1;
                    break;
            }
        }
        result.Add(raw[start..].Trim());
        return result;
    }

    /// <summary>Разбивает doc-id метода на имя и параметры.</summary>
    public static (string Name, string Params, bool HasParams) SplitMethod(string rest)
    {
        var open = rest.IndexOf('(');
        if (open < 0)
            return (rest, "", false);
        var close = rest.LastIndexOf(')');
        if (close < open)
            return (rest, "", false);
        return (rest[..open], rest[(open + 1)..close], true);
    }

    /// <summary>Превращает тип из doc-id в читаемый C#-подобный вид.</summary>
    public static string FriendlyType(string type)
    {
        type = type.Trim();
        if (type.Length == 0)
            return "";
        var byRef = type.EndsWith('@');
        if (byRef)
            type = type[..^1];
        var result = FriendlyBare(type);
        return byRef ? "ref " + result : result;
    }

    private static string FriendlyBare(string type)
    {
        if (type.EndsWith("[]", StringComparison.Ordinal))
            return FriendlyBare(type[..^2]) + "[]";

        var brace = type.IndexOf('{');
        if (brace >= 0 && type.EndsWith('}'))
        {
            var baseName = FriendlyBare(type[..brace]);
            var args = SplitParams(type[(brace + 1)..^1]);
            return baseName + "<" + string.Join(", ", args.Select(FriendlyBare)) + ">";
        }

        if (type.StartsWith("``", StringComparison.Ordinal))
            return "T" + type[2..];
        if (type.StartsWith('`'))
            return type;

        if (Primitives.TryGetValue(type, out var friendly))
            return friendly;

        var dot = type.LastIndexOf('.');
        return dot >= 0 ? type[(dot + 1)..] : type;
    }

    private static readonly Dictionary<string, string> Primitives = new(StringComparer.Ordinal)
    {
        ["System.Void"] = "void",
        ["System.Boolean"] = "bool",
        ["System.Byte"] = "byte",
        ["System.SByte"] = "sbyte",
        ["System.Char"] = "char",
        ["System.Int16"] = "short",
        ["System.UInt16"] = "ushort",
        ["System.Int32"] = "int",
        ["System.UInt32"] = "uint",
        ["System.Int64"] = "long",
        ["System.UInt64"] = "ulong",
        ["System.Single"] = "float",
        ["System.Double"] = "double",
        ["System.Decimal"] = "decimal",
        ["System.String"] = "string",
        ["System.Object"] = "object",
        ["System.IntPtr"] = "nint",
        ["System.UIntPtr"] = "nuint",
    };

    /// <summary>Строит сигнатуру метода.</summary>
    public static string MethodSignature(string ownerName, string rawName, IReadOnlyList<ParamInfo> parameters)
    {
        var sb = new StringBuilder();
        sb.Append(ownerName).Append('.').Append(StripArity(rawName));
        var ticks = CountDoubleTicks(rawName);
        if (ticks > 0)
            sb.Append('<').Append(string.Join(", ", Enumerable.Range(1, ticks).Select(i => "T" + i))).Append('>');
        sb.Append('(');
        for (var i = 0; i < parameters.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append(parameters[i].Type);
            if (parameters[i].Name.Length > 0)
                sb.Append(' ').Append(parameters[i].Name);
        }
        sb.Append(')');
        return sb.ToString();
    }

    private static int CountDoubleTicks(string name)
    {
        var i = name.IndexOf('`');
        if (i < 0)
            return 0;
        var extra = 0;
        while (i + extra < name.Length && name[i + extra] == '`')
            extra++;
        return extra / 2;
    }
}
