using System.Text;

namespace CSharpDocs.Data;

/// <summary>Собирает модель API из XML-документации и метаданных сборки.</summary>
public static class ModelBuilder
{
    public static ApiModel Build(Stream xml, Stream dll, string version)
    {
        var docs = XmlDocs.Parse(xml);
        var metadata = MetadataReaderUtil.Read(dll);
        var metaByName = metadata.Types.ToDictionary(m => m.FullName, StringComparer.Ordinal);

        var documentedTypes = DocumentedTypeNames(docs);

        var model = new ApiModel
        {
            Version = version,
            Meta = new AssemblyMeta
            {
                AssemblyName = metadata.Name,
                AssemblyVersion = metadata.Version,
                TargetFramework = TargetTfm(metadata.TargetFramework),
            },
        };

        AddTypes(model, documentedTypes, metaByName, docs);
        AddMembers(model, docs, metadata.ExtensionMethods);

        foreach (var type in model.Types)
            MergePropertyAccessors(type);

        LinkHierarchy(model);
        InheritDoc.Resolve(model, docs);

        model.Types.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
        return model;
    }

    /// <summary>
    /// Из строки вида «.NETCoreApp,Version=v8.0» делает читаемое «net8.0».
    /// </summary>
    private static string TargetTfm(string targetFramework)
    {
        var version = targetFramework.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(p => p.StartsWith("Version=", StringComparison.Ordinal));
        if (version is null)
            return targetFramework;

        var value = version["Version=".Length..].TrimStart('v');
        var framework = targetFramework.StartsWith(".NETCoreApp", StringComparison.Ordinal) ? "net"
            : targetFramework.StartsWith(".NETStandard", StringComparison.Ordinal) ? "netstandard"
            : targetFramework.StartsWith(".NETFramework", StringComparison.Ordinal) ? "net"
            : "";
        return framework.Length > 0 ? framework + value : targetFramework;
    }

    /// <summary>Полные имена типов, для которых есть документация.</summary>
    private static HashSet<string> DocumentedTypeNames(IReadOnlyDictionary<string, DocBody> docs) =>
        docs.Keys
            .Where(id => DocId.KindOf(id) == "T")
            .Select(DocId.RestOf)
            .ToHashSet(StringComparer.Ordinal);

    private static void AddTypes(
        ApiModel model,
        HashSet<string> documentedTypes,
        IReadOnlyDictionary<string, MetaType> metadata,
        IReadOnlyDictionary<string, DocBody> docs)
    {
        foreach (var fullName in documentedTypes)
        {
            // В документацию попадают только публичные типы.
            if (!metadata.TryGetValue(fullName, out var meta) || !meta.IsPublic)
                continue;

            var type = new ApiType
            {
                Name = ShortName(fullName, meta.Namespace),
                FullName = fullName,
                Namespace = meta.Namespace,
                EnclosingFullName = meta.EnclosingFullName,
                DocId = "T:" + fullName,
                Kind = meta.Kind,
                ParentName = meta.BaseName,
                Slug = Slug.ForType(fullName),
            };
            ApplyTypeDocs(type, docs);

            model.Types.Add(type);
            model.ByShort[type.Name] = type;
            model.BySlug[type.Slug] = type;
        }
    }

    private static void AddMembers(
        ApiModel model,
        IReadOnlyDictionary<string, DocBody> docs,
        IReadOnlySet<ExtensionMethodKey> extensionMethods)
    {
        var typesByName = new Dictionary<string, ApiType>(StringComparer.Ordinal);
        foreach (var type in model.Types)
            typesByName[type.FullName] = type;

        var documentedTypeNames = new HashSet<string>(typesByName.Keys, StringComparer.Ordinal);

        var memberIds = docs.Keys
            .Where(id => DocId.KindOf(id) is "M" or "P" or "F" or "E")
            .OrderBy(id => id, StringComparer.Ordinal);

        foreach (var docId in memberIds)
        {
            var rest = DocId.RestOf(docId);
            var (ownerName, memberKey) = SplitOwner(rest, documentedTypeNames);
            if (ownerName is null || !typesByName.TryGetValue(ownerName, out var owner))
                continue;

            var member = BuildMember(owner, DocId.KindOf(docId), docId, memberKey, docs[docId]);
            if (member is null)
                continue;

            if (member is { Kind: MemberKind.Method } && IsExtension(extensionMethods, owner, member, memberKey))
                MarkExtension(model, member);

            owner.Members.Add(member);
        }
    }

    /// <summary>Метод-расширение — если у первого параметра есть модификатор this.</summary>
    private static bool IsExtension(
        IReadOnlySet<ExtensionMethodKey> extensionMethods,
        ApiType owner,
        ApiMember member,
        string memberKey)
    {
        var parameterCount = DocId.SplitMethod(memberKey).HasParams
            ? DocId.SplitParams(DocId.SplitMethod(memberKey).Params).Count
            : 0;
        return extensionMethods.Contains(new ExtensionMethodKey(owner.FullName, member.Name, parameterCount));
    }

    private static void MarkExtension(ApiModel model, ApiMember member)
    {
        var receiver = ReceiverType(member.Params.Count > 0 ? member.Params[0].Type : "");
        if (receiver.Length == 0)
            return;

        member.IsExtension = true;
        member.ExtendType = receiver;
        member.Signature = member.Signature.Replace("(", "(this ", StringComparison.Ordinal);

        if (model.ByShort.TryGetValue(receiver, out var target))
            target.Extensions.Add(member);
    }

    private static ApiMember? BuildMember(ApiType owner, string kind, string docId, string memberKey, DocBody body)
    {
        var parsed = kind switch
        {
            "M" => ParseMethod(owner, memberKey),
            "P" => ParseProperty(owner, memberKey),
            "F" => new MemberBlueprint(MemberKind.Field, memberKey, owner.Name + "." + memberKey),
            "E" => new MemberBlueprint(MemberKind.Event, memberKey, owner.Name + "." + memberKey),
            _ => null,
        };
        if (parsed is null)
            return null;

        return new ApiMember
        {
            Owner = owner,
            Kind = parsed.Kind,
            Name = parsed.Name,
            IsConstructor = parsed.IsConstructor,
            DocId = docId,
            FullName = owner.FullName + "." + memberKey,
            Signature = parsed.Signature,
            Params = parsed.Params,
            Slug = Slug.ForMember(parsed.Kind, owner.FullName + "." + memberKey),
            Summary = body.Summary,
            Remarks = body.Remarks,
            Returns = body.Returns,
            ParamDocs = body.Params.ToList(),
            Exceptions = body.Exceptions.Select(e => new ExceptionDoc
            {
                Cref = ShortCref(e.Cref),
                Summary = e.Summary,
            }).ToList(),
        };
    }

    /// <summary>Разобранный член до заполнения документацией.</summary>
    private sealed record MemberBlueprint(MemberKind Kind, string Name, string Signature)
    {
        public List<ParamInfo> Params { get; init; } = [];
        public bool IsConstructor { get; init; }
    }

    private static MemberBlueprint? ParseMethod(ApiType owner, string memberKey)
    {
        var (rawName, paramsRaw, hasParams) = DocId.SplitMethod(memberKey);

        if (rawName.StartsWith("get_", StringComparison.Ordinal))
            return new MemberBlueprint(MemberKind.Property, rawName[4..], owner.Name + "." + rawName[4..] + " get");
        if (rawName.StartsWith("set_", StringComparison.Ordinal))
            return new MemberBlueprint(MemberKind.Property, rawName[4..], owner.Name + "." + rawName[4..] + " set");
        if (rawName.StartsWith("add_", StringComparison.Ordinal) || rawName.StartsWith("remove_", StringComparison.Ordinal))
            return null;

        var parameters = new List<ParamInfo>();
        if (hasParams)
            parameters.AddRange(DocId.SplitParams(paramsRaw)
                .Select(p => new ParamInfo { Type = DocId.FriendlyType(p) }));

        // Конструктор: имя и сигнатура — это имя типа без префикса владельца
        if (rawName is "#ctor" or "#cctor")
        {
            var ctorParams = string.Join(", ", parameters.Select(p => p.Name.Length > 0 ? $"{p.Type} {p.Name}" : p.Type));
            return new MemberBlueprint(MemberKind.Method, owner.Name, $"{owner.Name}({ctorParams})")
            {
                Params = parameters,
                IsConstructor = true,
            };
        }

        // Явные реализации интерфейсов (компилятор кодирует точки как «#») —
        // показываем как «Интерфейс.Член».
        var explicitName = DocId.DisplayName(DocId.StripArity(rawName));
        return new MemberBlueprint(MemberKind.Method, explicitName,
            DocId.MethodSignature(owner.Name, explicitName, parameters)) { Params = parameters };
    }

    private static MemberBlueprint ParseProperty(ApiType owner, string memberKey)
    {
        var paren = memberKey.IndexOf('(');
        var name = paren >= 0 ? memberKey[..paren] : memberKey;
        return new MemberBlueprint(MemberKind.Property, name, owner.Name + "." + name);
    }

    /// <summary>Объединяет пару get/set в одно свойство.</summary>
    private static void MergePropertyAccessors(ApiType type)
    {
        var byName = new Dictionary<string, ApiMember>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<ApiMember>();

        foreach (var member in type.Members)
        {
            if (member.Kind == MemberKind.Property &&
                byName.TryGetValue(member.Name, out var existing))
            {
                if (existing.Summary.Length == 0)
                    existing.Summary = member.Summary;
                continue;
            }

            if (member.Kind == MemberKind.Property)
                byName[member.Name] = member;
            merged.Add(member);
        }

        merged.Sort(CompareByKindThenName);
        type.Members.Clear();
        type.Members.AddRange(merged);
    }

    private static int CompareByKindThenName(ApiMember a, ApiMember b)
    {
        var byKind = MemberOrder(a.Kind).CompareTo(MemberOrder(b.Kind));
        return byKind != 0 ? byKind : string.CompareOrdinal(a.Name, b.Name);
    }

    private static int MemberOrder(MemberKind kind) => kind switch
    {
        MemberKind.Property => 0,
        MemberKind.Method => 1,
        MemberKind.Event => 2,
        MemberKind.Field => 3,
        _ => 4,
    };

    private static string ReceiverType(string type)
    {
        type = type.Trim();
        type = type.StartsWith("ref ", StringComparison.Ordinal) ? type[4..]
            : type.StartsWith("out ", StringComparison.Ordinal) ? type[4..]
            : type;

        var generic = type.IndexOfAny(['<', '[']);
        if (generic >= 0)
            type = type[..generic];
        type = type.Trim();

        return KnownNonApi.Contains(type) ? "" : type;
    }

    private static readonly HashSet<string> KnownNonApi = new(StringComparer.Ordinal)
    {
        "int", "uint", "long", "ulong", "short", "ushort", "byte", "sbyte",
        "float", "double", "bool", "string", "object", "nint", "nuint", "void",
        "char", "decimal", "Matrix4x4", "Vector2", "Vector3", "Color", "DateTime",
        "Process", "IDisposable", "Action", "Func", "Task", "IEnumerable",
        "IReadOnlyList", "IList", "List", "Dictionary", "Span", "ReadOnlySpan",
    };

    private static void LinkHierarchy(ApiModel model)
    {
        foreach (var type in model.Types)
        {
            if (type.ParentName.Length == 0 || !model.ByShort.TryGetValue(type.ParentName, out var parent))
                continue;
            type.Parent = parent;
            parent.Children.Add(type);
        }

        foreach (var type in model.Types)
            type.Children.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        // Корни дерева — все публичные типы без родителя среди отфильтрованных.
        // Сюда попадают и перечисления/делегаты, иначе они были бы недоступны
        // в дереве (а значит и в поиске).
        foreach (var type in model.Types)
            if (type.Parent is null)
                model.Roots.Add(type);
        model.Roots.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
    }

    private static string KindLabel(ApiMember member) => member.Kind switch
    {
        MemberKind.Method => member.IsExtension ? "extension" : "метод",
        MemberKind.Property => "свойство",
        MemberKind.Field => "поле",
        MemberKind.Event => "событие",
        _ => "член",
    };

    private static void ApplyTypeDocs(ApiType type, IReadOnlyDictionary<string, DocBody> docs)
    {
        if (docs.TryGetValue(type.DocId, out var body))
        {
            type.Summary = body.Summary;
            type.Remarks = body.Remarks;
        }
    }

    /// <summary>Делит doc-id члена на владельца и остаток (имя с параметрами).</summary>
    private static (string? Owner, string Member) SplitOwner(string rest, HashSet<string> typeNames)
    {
        var best = "";
        for (var i = 0; i < rest.Length; i++)
        {
            if (rest[i] is not ('.' or '('))
                continue;
            var prefix = rest[..i];
            if (typeNames.Contains(prefix) && prefix.Length > best.Length)
                best = prefix;
        }
        return best.Length == 0 ? (null, rest) : (best, rest[(best.Length + 1)..]);
    }

    private static string ShortCref(string cref)
    {
        if (cref.StartsWith("T:", StringComparison.Ordinal))
            cref = cref[2..];
        var dot = cref.LastIndexOf('.');
        return dot >= 0 ? cref[(dot + 1)..] : cref;
    }

    /// <summary>
    /// Короткое имя типа (последний сегмент). Иерархия вложенных типов и
    /// пространств имён строится отдельно — здесь нужно только имя узла.
    /// </summary>
    private static string ShortName(string fullName, string ns)
    {
        var dot = fullName.LastIndexOf('.');
        return dot >= 0 ? fullName[(dot + 1)..] : fullName;
    }

    private static string Lower(params string[] parts) =>
        string.Join(' ', parts).ToLowerInvariant();

    /// <summary>Убирает HTML-теги и обрезает текст для сниппетов поиска.</summary>
    public static string Plain(string html)
    {
        if (html.Length == 0)
            return "";

        var text = new StringBuilder(html.Length);
        var inTag = false;
        foreach (var ch in html)
        {
            switch (ch)
            {
                case '<':
                    inTag = true;
                    break;
                case '>':
                    inTag = false;
                    break;
                default:
                    if (!inTag)
                        text.Append(ch);
                    break;
            }
        }

        var normalized = string.Join(' ', text.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length > 200 ? normalized[..200] + "…" : normalized;
    }
}
