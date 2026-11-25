using System.Text;

namespace CSharpDocs.Data;

/// <summary>Собирает модель API из XML-документации и метаданных сборки.</summary>
public static class ModelBuilder
{
    public static ApiModel Build(Stream xml, Stream dll, string version)
    {
        var docs = XmlDocs.Parse(xml);
        using var bufferedDll = dll.CanSeek ? null : new MemoryStream();
        if (bufferedDll is not null)
            dll.CopyTo(bufferedDll);
        var metadataStream = (Stream?)bufferedDll ?? dll;

        if (metadataStream.CanSeek)
            metadataStream.Position = 0;
        var metadata = MetadataReaderUtil.Read(metadataStream);
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
        AddMembers(model, docs, metadata.ExtensionMethods, metadata.MemberSignatures);

        foreach (var type in model.Types)
            MergePropertyAccessors(type);

        LinkHierarchy(model);
        InheritDoc.Resolve(model, docs);
        docs.Clear();

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
        IReadOnlySet<ExtensionMethodKey> extensionMethods,
        IReadOnlyDictionary<string, MetaMemberSignature> memberSignatures)
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

            var member = BuildMember(owner, DocId.KindOf(docId), docId, memberKey, docs[docId], memberSignatures);
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

    private static ApiMember? BuildMember(ApiType owner, string kind, string docId, string memberKey, DocBody body,
        IReadOnlyDictionary<string, MetaMemberSignature> memberSignatures)
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

        var signature = parsed.Signature;
        var returnType = parsed.ReturnType;
        var memberName = memberKey.Split('(')[0];
        var lookupName = memberName;
        if ((parsed.Kind is MemberKind.Method or MemberKind.Property) && !parsed.IsConstructor &&
            TryGetMemberSignature(owner, lookupName, MemberParameterTypes(memberKey), memberSignatures, out var memberSignature))
        {
            var methodTypeNames = memberSignature.MethodTypeParameters.ToList();
            var typeNames = memberSignature.DeclaringTypeParameters;
            for (var i = 0; i < methodTypeNames.Count; i++)
            {
                var documented = body.TypeParams.FirstOrDefault(p => p.Name == methodTypeNames[i]);
                if (!string.IsNullOrWhiteSpace(documented?.Name))
                    methodTypeNames[i] = documented.Name;
            }
            parsed.TypeParams.AddRange(methodTypeNames);
            returnType = FormatMemberType(memberSignature.ReturnType, methodTypeNames, typeNames);
            signature = FormatMethodTypeParameters(signature, methodTypeNames);
            foreach (var parameter in parsed.Params)
                parameter.Type = FormatMemberType(parameter.Type, methodTypeNames, typeNames);
        }

        for (var i = 0; body.Params.Count == parsed.Params.Count && i < parsed.Params.Count; i++)
        {
            parsed.Params[i].Name = body.Params[i].Name;
        }
        if (returnType.Length > 0 && (parsed.Kind is MemberKind.Method or MemberKind.Property))
            signature = returnType + " " + signature;
        if ((parsed.Kind is MemberKind.Method or MemberKind.Property) && parsed.Params.Count > 0)
        {
            var open = signature.IndexOf('(');
            var close = signature.LastIndexOf(')');
            if (open >= 0 && close > open)
                signature = signature[..(open + 1)]
                    + string.Join(", ", parsed.Params.Select(p =>
                    {
                        var parameterType = p.Type;
                        return p.Name.Length > 0 ? parameterType + " " + p.Name : parameterType;
                    }))
                    + signature[close..];
        }

        return new ApiMember
        {
            Owner = owner,
            Kind = parsed.Kind,
            Name = parsed.Name,
            IsConstructor = parsed.IsConstructor,
            DocId = docId,
            FullName = owner.FullName + "." + memberKey,
            Signature = signature,
            ReturnType = returnType,
            TypeParams = parsed.TypeParams,
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

    private static IReadOnlyList<string> MemberParameterTypes(string memberKey)
    {
        var (_, rawParams, hasParams) = DocId.SplitMethod(memberKey);
        return hasParams ? DocId.SplitParams(rawParams) : [];
    }

    private static bool TryGetMemberSignature(ApiType owner, string name, IReadOnlyList<string> parameterTypes,
        IReadOnlyDictionary<string, MetaMemberSignature> memberSignatures, out MetaMemberSignature memberSignature)
    {
        var normalizedTypes = string.Join(",", parameterTypes.Select(NormalizeDocIdTypeForLookup));
        return memberSignatures.TryGetValue($"{owner.FullName}.{name}({normalizedTypes})", out memberSignature!);
    }

    private static string FormatMemberType(string type, IReadOnlyList<string> methodTypeNames, IReadOnlyList<string> typeNames)
    {
        type = ReplaceGenericMarkers(type, "!!", methodTypeNames);
        type = ReplaceGenericMarkers(type, "!", typeNames);
        return FormatDisplayType(type);
    }

    private static string FormatDisplayType(string type)
    {
        if (type.StartsWith("ref ", StringComparison.Ordinal))
            return "ref " + FormatDisplayType(type[4..]);
        if (type.EndsWith(']'))
        {
            var bracket = type.LastIndexOf('[');
            if (bracket >= 0 && type[(bracket + 1)..^1].All(c => c == ','))
                return FormatDisplayType(type[..bracket]) + type[bracket..];
        }
        if (type.EndsWith('*'))
            return FormatDisplayType(type[..^1]) + "*";

        var nested = type.IndexOf('+');
        var generic = type.IndexOf('<');
        if (nested >= 0 && (generic < 0 || nested < generic))
            return FormatDisplayType(type[..nested]) + "." + FormatDisplayType(type[(nested + 1)..]);

        if (generic >= 0)
        {
            var depth = 0;
            var close = -1;
            for (var i = generic; i < type.Length; i++)
            {
                if (type[i] == '<') depth++;
                else if (type[i] == '>' && --depth == 0)
                {
                    close = i;
                    break;
                }
            }
            if (close >= 0)
            {
                var arguments = SplitTypeArguments(type[(generic + 1)..close]);
                var suffix = type[(close + 1)..];
                return ShortTypeName(type[..generic]) + "<" + string.Join(", ", arguments.Select(FormatDisplayType)) + ">"
                    + (suffix.Length > 0 ? "." + FormatDisplayType(suffix.TrimStart('+')) : "");
            }
        }

        return ShortTypeName(type);
    }

    private static string ShortTypeName(string type)
    {
        var dot = type.LastIndexOf('.');
        return dot >= 0 ? type[(dot + 1)..] : type;
    }

    private static List<string> SplitTypeArguments(string arguments)
    {
        var result = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < arguments.Length; i++)
        {
            if (arguments[i] == '<') depth++;
            else if (arguments[i] == '>') depth--;
            else if (arguments[i] == ',' && depth == 0)
            {
                result.Add(arguments[start..i].Trim());
                start = i + 1;
            }
        }
        result.Add(arguments[start..].Trim());
        return result;
    }

    private static string ReplaceGenericMarkers(string text, string marker, IReadOnlyList<string> names)
    {
        var result = new System.Text.StringBuilder(text.Length);
        for (var i = 0; i < text.Length;)
        {
            if (text.AsSpan(i).StartsWith(marker, StringComparison.Ordinal))
            {
                var digitStart = i + marker.Length;
                var digitEnd = digitStart;
                while (digitEnd < text.Length && char.IsAsciiDigit(text[digitEnd]))
                    digitEnd++;
                if (digitEnd > digitStart && int.TryParse(text.AsSpan(digitStart, digitEnd - digitStart), out var index)
                    && index < names.Count)
                {
                    result.Append(names[index]);
                    i = digitEnd;
                }
                else
                {
                    result.Append(text[i++]);
                }
            }
            else
            {
                result.Append(text[i++]);
            }
        }
        return result.ToString();
    }

    private static string FormatMethodTypeParameters(string signature, IReadOnlyList<string> typeParameters)
    {
        if (typeParameters.Count == 0)
            return signature;
        var open = signature.IndexOf('(');
        return open < 0
            ? signature
            : signature[..open] + "<" + string.Join(", ", typeParameters) + ">" + signature[open..];
    }

    private static string NormalizeDocIdTypeForLookup(string type)
    {
        if (type.EndsWith('@'))
            return "ref " + NormalizeDocIdTypeForLookup(type[..^1]);
        if (type.EndsWith(']'))
        {
            var bracket = type.LastIndexOf('[');
            if (bracket >= 0)
            {
                var rank = type[(bracket + 1)..^1];
                if (rank.Length == 0)
                    return NormalizeDocIdTypeForLookup(type[..bracket]) + "[]";
                if (rank.Split(',').All(dimension => dimension.Length == 0 || dimension == "0:"))
                    return NormalizeDocIdTypeForLookup(type[..bracket]) + "[" + new string(',', rank.Count(c => c == ',')) + "]";
            }
        }
        var brace = type.IndexOf('{');
        if (brace >= 0 && type.EndsWith('}'))
            return NormalizeDocIdTypeForLookup(type[..brace]) + "<" + string.Join(",", DocId.SplitParams(type[(brace + 1)..^1]).Select(NormalizeDocIdTypeForLookup)) + ">";
        if (type.StartsWith("``", StringComparison.Ordinal))
            return "!!" + type[2..];
        if (type.StartsWith('`'))
            return "!" + type[1..];
        if (type.StartsWith("!!", StringComparison.Ordinal))
            return "!!" + type[2..];
        if (type.StartsWith('!'))
            return "!" + type[1..];
        if (type.StartsWith("System.", StringComparison.Ordinal))
            return MetadataReaderUtil.NormalizeTypeForLookup(type);
        return type;
    }

    /// <summary>Разобранный член до заполнения документацией.</summary>
    private sealed record MemberBlueprint(MemberKind Kind, string Name, string Signature)
    {
        public List<ParamInfo> Params { get; init; } = [];
        public List<string> TypeParams { get; } = [];
        public bool IsConstructor { get; init; }
        public string ReturnType { get; init; } = "";
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
        var signatureName = DocId.DisplayName(rawName);
        var displayName = DocId.DisplayName(DocId.StripArity(rawName));
        return new MemberBlueprint(MemberKind.Method, displayName,
            DocId.MethodSignature(owner.Name, signatureName, parameters)) { Params = parameters };
    }

    private static MemberBlueprint ParseProperty(ApiType owner, string memberKey)
    {
        var paren = memberKey.IndexOf('(');
        var name = paren >= 0 ? memberKey[..paren] : memberKey;
        var parameters = paren >= 0
            ? DocId.SplitParams(memberKey[(paren + 1)..^1]).Select(p => new ParamInfo { Type = DocId.FriendlyType(p) }).ToList()
            : [];
        var signature = owner.Name + "." + name;
        if (parameters.Count > 0)
            signature += "(" + string.Join(", ", parameters.Select(parameter => parameter.Type)) + ")";
        return new MemberBlueprint(MemberKind.Property, name, signature) { Params = parameters };
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
