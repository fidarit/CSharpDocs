namespace CSharpDocs.Data;

/// <summary>Узел структурного дерева: пространство имён, тип или член.</summary>
public sealed class TreeNode
{
    /// <summary>Отображаемое имя.</summary>
    public string Name { get; set; } = "";

    /// <summary>Вид: «namespace», вид типа или вид члена (для бейджа).</summary>
    public string Kind { get; set; } = "";

    /// <summary>Уникальный ключ узла (для операций на клиенте).</summary>
    public string Key { get; set; } = "";

    /// <summary>Квалифицированное имя для поиска (Type.Member, полное имя типа и т. п.).</summary>
    public string Full { get; set; } = "";

    /// <summary>Дополнительный текст для поиска: описание, примечания, сигнатура.</summary>
    public string Search { get; set; } = "";

    /// <summary>URL-слаг (пусто у пространства имён).</summary>
    public string Slug { get; set; } = "";

    /// <summary>Число методов-расширений (только у типов-приёмников).</summary>
    public int Ext { get; set; }

    /// <summary>Порядок сортировки среди соседей.</summary>
    public int Rank { get; set; }

    public List<TreeNode> Children { get; } = [];
}

/// <summary>
/// Строит структурное дерево API: пространства имён → типы (включая вложенные)
/// → члены. Порядок: пространства имён, типы, затем свойства, методы, события, поля
/// (внутри — по имени).
/// </summary>
public static class ApiTree
{
    // Ранги задают порядок узлов среди соседей.
    private const int RankNamespace = 0;
    private const int RankType = 1;
    private const int RankProperty = 2;
    private const int RankMethod = 3;
    private const int RankEvent = 4;
    private const int RankField = 5;

    public static List<TreeNode> Build(ApiModel model)
    {
        var roots = new List<TreeNode>();
        var namespaceNodes = new Dictionary<string, TreeNode>(StringComparer.Ordinal);

        // 1) Все пространства имён по полному пути.
        foreach (var type in model.Types)
        {
            if (type.Namespace.Length > 0)
                EnsureNamespaces(roots, namespaceNodes, type.Namespace);
        }

        // 2) Типы: вложенные — под внешним типом, остальные — под пространством имён.
        var typeNodes = new Dictionary<string, TreeNode>(StringComparer.Ordinal);
        foreach (var type in model.Types)
            AddType(roots, namespaceNodes, typeNodes, type);

        // 3) Члены — под своим типом.
        foreach (var type in model.Types)
        {
            if (!typeNodes.TryGetValue(type.FullName, out var node))
                continue;
            foreach (var member in type.Members)
                if (!member.IsConstructor)
                    node.Children.Add(MemberNode(member, type));
        }

        // 4) Сворачиваем цепочки пространств имён (A → B → C в одну ноду).
        CollapseNamespaceChains(roots);

        SortRecursive(roots);
        return roots;
    }

    private static void EnsureNamespaces(
        List<TreeNode> roots,
        Dictionary<string, TreeNode> nodes,
        string path)
    {
        var full = "";
        var siblings = roots;
        foreach (var segment in path.Split('.'))
        {
            full = full.Length == 0 ? segment : full + "." + segment;
            if (!nodes.TryGetValue(full, out var node))
            {
                node = new TreeNode
                {
                    Name = segment,
                    Full = full,
                    Kind = "namespace",
                    Key = "ns:" + full,
                    Rank = RankNamespace,
                };
                nodes[full] = node;
                siblings.Add(node);
            }
            siblings = node.Children;
        }
    }

    private static void AddType(
        List<TreeNode> roots,
        Dictionary<string, TreeNode> namespaceNodes,
        Dictionary<string, TreeNode> typeNodes,
        ApiType type)
    {
        var node = new TreeNode
        {
            Name = type.Name,
            Full = type.FullName,
            Kind = type.KindLabel,
            Key = "t:" + type.Slug,
            Slug = type.Slug,
            Ext = type.Extensions.Count,
            Rank = RankType,
            Search = Join(type.Summary, type.Remarks),
        };
        typeNodes[type.FullName] = node;

        if (type.EnclosingFullName.Length > 0 &&
            typeNodes.TryGetValue(type.EnclosingFullName, out var enclosing))
        {
            enclosing.Children.Add(node);
        }
        else if (type.Namespace.Length > 0 && namespaceNodes.TryGetValue(type.Namespace, out var ns))
        {
            ns.Children.Add(node);
        }
        else
        {
            roots.Add(node);
        }
    }

    private static TreeNode MemberNode(ApiMember member, ApiType owner) => new()
    {
        Name = member.Name,
        Full = owner.Name + "." + member.Name,
        Kind = MemberKindLabel(member),
        Key = "m:" + owner.Slug + "/" + member.Slug,
        Slug = owner.Slug + "/" + member.Slug,
        Rank = MemberRank(member.Kind),
        Search = Join(member.Signature, member.Summary, member.Remarks, member.Returns),
    };

    /// <summary>Склеивает непустые тексты (без HTML) в одну строку для поиска.</summary>
    private static string Join(params string[] parts) =>
        string.Join(' ', parts.Where(p => p.Length > 0).Select(ModelBuilder.Plain));

    private static int MemberRank(MemberKind kind) => kind switch
    {
        MemberKind.Property => RankProperty,
        MemberKind.Method => RankMethod,
        MemberKind.Event => RankEvent,
        MemberKind.Field => RankField,
        _ => RankField,
    };

    private static string MemberKindLabel(ApiMember member) => member.Kind switch
    {
        MemberKind.Method => "метод",
        MemberKind.Property => "свойство",
        MemberKind.Field => "поле",
        MemberKind.Event => "событие",
        _ => "член",
    };

    /// <summary>Сворачивает пространство имён, у которого единственный ребёнок — тоже пространство имён.</summary>
    private static void CollapseNamespaceChains(List<TreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            while (node.Kind == "namespace" &&
                   node.Children.Count == 1 &&
                   node.Children[0].Kind == "namespace")
            {
                var child = node.Children[0];
                node.Name = node.Name + "." + child.Name;
                node.Full = child.Full;
                node.Key = child.Key;
                node.Children.Clear();
                node.Children.AddRange(child.Children);
            }
            CollapseNamespaceChains(node.Children);
        }
    }

    private static void SortRecursive(List<TreeNode> nodes)
    {
        nodes.Sort((a, b) =>
        {
            var byRank = a.Rank.CompareTo(b.Rank);
            return byRank != 0 ? byRank : string.CompareOrdinal(a.Name, b.Name);
        });
        foreach (var node in nodes)
            SortRecursive(node.Children);
    }
}
